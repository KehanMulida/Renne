using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ConditionEvaluator — 静态条件求值器
/// 被 MissionManager 和 EndingResolver 共同调用，无状态纯静态
/// </summary>
public static class ConditionEvaluator
{
    // AND 逻辑：全部满足才返回 true
    public static bool EvaluateAll(List<MissionCondition> conditions,
                                   MissionRuntimeState missionState = null)
    {
        if (conditions == null || conditions.Count == 0) return true;
        foreach (var c in conditions)
            if (!Evaluate(c, missionState)) return false;
        return true;
    }

    // OR 逻辑：任一满足就返回 true（用于 failConditions）
    public static bool EvaluateAny(List<MissionCondition> conditions,
                                   MissionRuntimeState missionState = null)
    {
        if (conditions == null || conditions.Count == 0) return false;
        foreach (var c in conditions)
            if (Evaluate(c, missionState)) return true;
        return false;
    }

    public static bool Evaluate(MissionCondition c, MissionRuntimeState missionState = null)
    {
        return c.target switch
        {
            ConditionTarget.Unit         => EvaluateUnit(c),
            ConditionTarget.Player       => EvaluatePlayer(c),
            ConditionTarget.Mission      => EvaluateMission(c, missionState),
            ConditionTarget.StoryFlag    => EvaluateStoryFlag(c),
            ConditionTarget.PlanProgress => EvaluatePlanProgress(c),
            ConditionTarget.Zone         => EvaluateZone(c, missionState),
            ConditionTarget.Turn         => EvaluateTurn(c, missionState),
            ConditionTarget.WorldObject  => EvaluateWorldObject(c),
            ConditionTarget.ActionPoints => EvaluateActionPoints(c, missionState),
            _ => false
        };
    }

    // ── Unit ─────────────────────────────────────────────

    private static bool EvaluateUnit(MissionCondition c)
    {
        if (string.IsNullOrEmpty(c.targetId))
        {
            Debug.LogWarning("[ConditionEvaluator] Unit 条件缺少 targetId");
            return false;
        }

        var enemy = FindEnemyById(c.targetId);
        if (enemy == null)
        {
            Debug.LogWarning($"[ConditionEvaluator] 找不到单位 [{c.targetId}]，条件返回 false");
            return false;
        }

        var mov = enemy.GetComponent<UnitMovement>();

        switch (c.check)
        {
            case ConditionCheck.IsAlive:
                return CompareBool(enemy.IsAlive, c.boolValue, c.op);
            case ConditionCheck.IsDefeated:
                return CompareBool(!enemy.IsAlive, c.boolValue, c.op);
            case ConditionCheck.HP:
                return CompareFloat(enemy.CurrentHp, c.numericValue, c.op);
            case ConditionCheck.HPRatio:
                float ratio = enemy.MaxHp > 0 ? (float)enemy.CurrentHp / enemy.MaxHp : 0f;
                return CompareFloat(ratio, c.numericValue, c.op);
            case ConditionCheck.Floor:
                return CompareFloat(mov != null ? mov.CurrentFloor : -1, c.numericValue, c.op);
            case ConditionCheck.UnitInZone:
                if (mov == null || ZoneManager.Instance == null) return false;
                return ZoneManager.Instance.IsUnitInZone(c.zoneId,
                    mov.CurrentGridPosition, mov.CurrentFloor);
            case ConditionCheck.UnitInZoneType:
                if (mov == null || ZoneManager.Instance == null) return false;
                return ZoneManager.Instance.IsUnitInAnyZoneOfType(c.zoneType,
                    mov.CurrentGridPosition, mov.CurrentFloor);
            default:
                Debug.LogWarning($"[ConditionEvaluator] Unit 不支持的 check: {c.check}");
                return false;
        }
    }

    // ── Player ───────────────────────────────────────────

    private static bool EvaluatePlayer(MissionCondition c)
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null)
        {
            Debug.LogWarning("[ConditionEvaluator] 找不到 Player");
            return false;
        }

        var mov = playerObj.GetComponent<UnitMovement>();
        var ctrl = playerObj.GetComponent<PlayerController>();

        switch (c.check)
        {
            case ConditionCheck.IsAlive:
                return CompareBool(ctrl == null || ctrl.CurrentHp > 0, c.boolValue, c.op);
            case ConditionCheck.IsDefeated:
                // HP 归零即为 defeated（ctrl 为 null 时无法判断，视为未击败）
                return CompareBool(ctrl != null && ctrl.CurrentHp <= 0, c.boolValue, c.op);
            case ConditionCheck.HP:
                return CompareFloat(ctrl != null ? ctrl.CurrentHp : 0, c.numericValue, c.op);
            case ConditionCheck.HPRatio:
                if (ctrl == null) return false;
                float r = ctrl.MaxHp > 0 ? (float)ctrl.CurrentHp / ctrl.MaxHp : 0f;
                return CompareFloat(r, c.numericValue, c.op);
            case ConditionCheck.Floor:
                return CompareFloat(mov != null ? mov.CurrentFloor : -1, c.numericValue, c.op);
            case ConditionCheck.PlayerInZone:
                if (mov == null || ZoneManager.Instance == null) return false;
                return ZoneManager.Instance.IsUnitInZone(c.zoneId,
                    mov.CurrentGridPosition, mov.CurrentFloor);
            case ConditionCheck.PlayerInZoneType:
                if (mov == null || ZoneManager.Instance == null) return false;
                return ZoneManager.Instance.IsUnitInAnyZoneOfType(c.zoneType,
                    mov.CurrentGridPosition, mov.CurrentFloor);
            default:
                Debug.LogWarning($"[ConditionEvaluator] Player 不支持的 check: {c.check}");
                return false;
        }
    }

    // ── Mission ──────────────────────────────────────────

    private static bool EvaluateMission(MissionCondition c, MissionRuntimeState state)
    {
        if (state == null)
        {
            Debug.LogWarning("[ConditionEvaluator] Mission 条件需要传入 MissionRuntimeState");
            return false;
        }

        switch (c.check)
        {
            case ConditionCheck.RepeatsDone:
                return CompareFloat(state.repeatsDone, c.numericValue, c.op);
            case ConditionCheck.MissionPhase:
                return state.currentPhase == c.stringValue;
            case ConditionCheck.AssignedEnemyDefeated:
                // targetId 为空无法定位单位，返回 false 而非 true
                if (string.IsNullOrEmpty(c.targetId))
                {
                    Debug.LogWarning("[ConditionEvaluator] AssignedEnemyDefeated：targetId 为空，条件返回 false");
                    return false;
                }
                var enemy = FindEnemyById(c.targetId);
                return enemy == null || !enemy.IsAlive;

            case ConditionCheck.AllAssignedDefeated:
                // assignedEnemies 为 null 或空列表时不能认为"全部被击败"
                // 空集合的"全部满足"是逻辑上的假命题，应返回 false
                if (state.data.assignedEnemies == null || state.data.assignedEnemies.Count == 0)
                {
                    Debug.LogWarning("[ConditionEvaluator] AllAssignedDefeated：assignedEnemies 为空，条件返回 false\n" +
                                     "提示：若要用此条件，请在 MissionData.assignedEnemies 里填写要检查的 Enemy ID");
                    return false;
                }
                foreach (var id in state.data.assignedEnemies)
                {
                    var e = FindEnemyById(id);
                    if (e != null && e.IsAlive) return false;
                }
                return true;
            case ConditionCheck.TurnCount:
                return CompareFloat(state.turnCount, c.numericValue, c.op);
            default:
                Debug.LogWarning($"[ConditionEvaluator] Mission 不支持的 check: {c.check}");
                return false;
        }
    }

    // ── StoryFlag ────────────────────────────────────────

    private static bool EvaluateStoryFlag(MissionCondition c)
    {
        if (StoryManager.Instance == null)
        {
            Debug.LogWarning("[ConditionEvaluator] StoryManager 未找到");
            return false;
        }
        return CompareBool(StoryManager.Instance.GetFlag(c.flagKey), c.boolValue, c.op);
    }

    // ── PlanProgress ─────────────────────────────────────

    private static bool EvaluatePlanProgress(MissionCondition c)
    {
        if (MissionManager.Instance == null) return false;
        return CompareFloat(MissionManager.Instance.PlanProgress, c.numericValue, c.op);
    }

    // ── Turn ─────────────────────────────────────────────

    /// <summary>
    /// 回合计数条件
    ///
    /// TurnCount       — 任务激活后经过的回合数（相对计数，任务开始时从 0 起算）
    ///                   例：封锁 5 回合 → check=TurnCount, op=GreaterThanOrEqual, numericValue=5
    ///
    /// GlobalTurnNumber — 全局回合数（TurnSystem.GlobalTurnNumber）
    ///                   例：第 10 回合触发 → check=GlobalTurnNumber, op=Equals, numericValue=10
    /// </summary>
    private static bool EvaluateTurn(MissionCondition c, MissionRuntimeState state)
    {
        switch (c.check)
        {
            case ConditionCheck.TurnCount:
                if (state == null)
                {
                    Debug.LogWarning("[ConditionEvaluator] TurnCount 需要传入 MissionRuntimeState");
                    return false;
                }
                return CompareFloat(state.turnCount, c.numericValue, c.op);

            case ConditionCheck.GlobalTurnNumber:
                if (TurnSystem.Instance == null)
                {
                    Debug.LogWarning("[ConditionEvaluator] TurnSystem 未找到");
                    return false;
                }
                return CompareFloat(TurnSystem.Instance.GlobalTurnNumber, c.numericValue, c.op);

            default:
                Debug.LogWarning($"[ConditionEvaluator] Turn 不支持的 check: {c.check}");
                return false;
        }
    }

    // ── Zone ─────────────────────────────────────────────

    private static bool EvaluateZone(MissionCondition c, MissionRuntimeState state = null)
    {
        if (ZoneManager.Instance == null)
        {
            Debug.LogWarning("[ConditionEvaluator] ZoneManager 未找到");
            return false;
        }

        switch (c.check)
        {
            case ConditionCheck.TurnsInZone:
                if (state == null)
                {
                    Debug.LogWarning("[ConditionEvaluator] TurnsInZone 需要传入 MissionRuntimeState");
                    return false;
                }
                if (string.IsNullOrEmpty(c.zoneId))
                {
                    Debug.LogWarning("[ConditionEvaluator] TurnsInZone：zoneId 为空，条件返回 false");
                    return false;
                }
                int accumulated = state.turnsInZone.TryGetValue(c.zoneId, out int tz) ? tz : 0;
                return CompareFloat(accumulated, c.numericValue, c.op);

            case ConditionCheck.AnyEnemyInZone:
                foreach (var e in Object.FindObjectsOfType<EnemyAIController>())
                {
                    if (!e.IsAlive) continue;
                    var mov = e.GetComponent<UnitMovement>();
                    if (mov != null && ZoneManager.Instance.IsUnitInZone(
                        c.zoneId, mov.CurrentGridPosition, mov.CurrentFloor))
                        return true;
                }
                return false;

            case ConditionCheck.AllEnemiesDefeatedInZone:
                // zoneId 为空时区域不存在，无法判断"区域内全部击败"，返回 false
                if (string.IsNullOrEmpty(c.zoneId))
                {
                    Debug.LogWarning("[ConditionEvaluator] AllEnemiesDefeatedInZone：zoneId 为空，条件返回 false");
                    return false;
                }
                foreach (var e in Object.FindObjectsOfType<EnemyAIController>())
                {
                    if (!e.IsAlive) continue;
                    var mov = e.GetComponent<UnitMovement>();
                    if (mov != null && ZoneManager.Instance.IsUnitInZone(
                        c.zoneId, mov.CurrentGridPosition, mov.CurrentFloor))
                        return false;
                }
                return true;

            case ConditionCheck.UnitCountInZone:
                int cnt = 0;
                foreach (var e in Object.FindObjectsOfType<EnemyAIController>())
                {
                    if (!e.IsAlive) continue;
                    var mov = e.GetComponent<UnitMovement>();
                    if (mov != null && ZoneManager.Instance.IsUnitInZone(
                        c.zoneId, mov.CurrentGridPosition, mov.CurrentFloor))
                        cnt++;
                }
                return CompareFloat(cnt, c.numericValue, c.op);

            default:
                Debug.LogWarning($"[ConditionEvaluator] Zone 不支持的 check: {c.check}");
                return false;
        }
    }

    // ── 工具方法 ──────────────────────────────────────────

    private static bool CompareFloat(float actual, float target, ConditionOperator op)
    {
        return op switch
        {
            ConditionOperator.Equals             => Mathf.Approximately(actual, target),
            ConditionOperator.NotEquals          => !Mathf.Approximately(actual, target),
            ConditionOperator.LessThan           => actual < target,
            ConditionOperator.GreaterThan        => actual > target,
            ConditionOperator.LessThanOrEqual    => actual <= target,
            ConditionOperator.GreaterThanOrEqual => actual >= target,
            _ => false
        };
    }

    private static bool CompareBool(bool actual, bool target, ConditionOperator op)
    {
        return op switch
        {
            ConditionOperator.Equals    => actual == target,
            ConditionOperator.NotEquals => actual != target,
            _ => actual == target
        };
    }

    private static EnemyAIController FindEnemyById(string enemyId)
    {
        foreach (var e in Object.FindObjectsOfType<EnemyAIController>())
            if (e.EnemyId == enemyId) return e;
        return null;
    }

    // ── ActionPoints ─────────────────────────────────────

    /// <summary>
    /// AP 消耗条件：统计任务 assignedEnemies 累计消耗的 AP 总量
    /// 多个 Enemy 的消耗量求和（由 MissionRuntimeState.totalAPConsumed 追踪）
    /// 配置示例：target=ActionPoints, check=APConsumed, op=GreaterThanOrEqual, numericValue=20
    /// </summary>
    private static bool EvaluateActionPoints(MissionCondition c, MissionRuntimeState state)
    {
        if (state == null)
        {
            Debug.LogWarning("[ConditionEvaluator] ActionPoints 条件需要传入 MissionRuntimeState");
            return false;
        }

        switch (c.check)
        {
            case ConditionCheck.APConsumed:
                return CompareFloat(state.totalAPConsumed, c.numericValue, c.op);
            default:
                Debug.LogWarning($"[ConditionEvaluator] ActionPoints 不支持的 check: {c.check}");
                return false;
        }
    }

    // ── WorldObject ───────────────────────────────────────

    /// <summary>
    /// 检查场景中指定物体的状态。
    /// 支持两类物体，通过 check 类型区分：
    ///
    /// ── WorldItem（check=StoryObjectState）────────────────────────────────
    ///   物体销毁后从 WorldItemRegistry 读取最后已知状态。
    ///   objectId = WorldItem.objectId
    ///
    ///   stringValue 可选值（不区分大小写）：
    ///     "Active"              → 物体正常运转（未被干预）
    ///     "Interrupted"         → 物体被打断/关闭/玩家拿走
    ///     "Completed"           → 物体被 Enemy 完成交互
    ///     "PickedUpByPlayer"    → 玩家拾取了此物体
    ///     "PickedUpByEnemy"     → 敌人拾取了此物体
    ///     "InteractedByPlayer"  → 玩家与此物体发生了任意交互
    ///     "InteractedByEnemy"   → 敌人与此物体发生了任意交互
    ///   boolValue：true = Active，false = 已被干预或完成
    ///
    /// ── SceneItemInstance（check=SceneItemIsOpen / SceneItemIsDestroyed）──
    ///   直接读取运行时状态，objectId = SceneItemData.sceneObjectId
    ///
    ///   SceneItemIsOpen：boolValue=true → 当前开启，false → 当前关闭
    ///   SceneItemIsDestroyed：boolValue=true → 已被破坏，false → 完好
    ///
    ///   check=StoryObjectState 时也可用以下 stringValue 走 SceneItemInstance 路径：
    ///     "IsOpen"         → SceneItemInstance.IsOpen == true
    ///     "IsClosed"       → SceneItemInstance.IsOpen == false
    ///     "IsDestroyed"    → SceneItemInstance.IsDestroyed == true
    ///     "IsNotDestroyed" → SceneItemInstance.IsDestroyed == false
    ///     "IsToppled"      → SceneItemInstance.IsToppled == true
    /// </summary>
    private static bool EvaluateWorldObject(MissionCondition c)
    {
        if (string.IsNullOrEmpty(c.objectId))
        {
            Debug.LogWarning("[ConditionEvaluator] WorldObject 条件：objectId 为空，条件返回 false");
            return false;
        }

        // ── SceneItemInstance 专属 check（直接读运行时状态）─────────────────
        if (c.check == ConditionCheck.SceneItemIsOpen ||
            c.check == ConditionCheck.SceneItemIsDestroyed)
        {
            return EvaluateSceneItem(c);
        }

        if (c.check != ConditionCheck.StoryObjectState)
        {
            Debug.LogWarning($"[ConditionEvaluator] WorldObject 不支持的 check: {c.check}");
            return false;
        }

        // ── StoryObjectState + SceneItemInstance 专属 stringValue 关键字 ────
        // 如果 stringValue 是 SceneItemInstance 特有的关键字，优先走 SceneItem 路径
        if (!string.IsNullOrEmpty(c.stringValue))
        {
            var sv = c.stringValue.ToLowerInvariant();
            if (sv == "isopen" || sv == "isclosed" ||
                sv == "isdestroyed" || sv == "isnotdestroyed" || sv == "istoppled")
            {
                return EvaluateSceneItem(c);
            }
        }

        // ── 1. 先在场景中查找 WorldItem（物体仍存在时优先读取实时状态）─────────
        var item = FindWorldItemById(c.objectId);

        WorldItemState state;
        string         faction;

        if (item != null)
        {
            state   = item.CurrentState;
            faction = item.InteractedByFaction;
        }
        else if (WorldItemRegistry.TryGetRecord(c.objectId, out var record))
        {
            // ── 2. 物体已销毁，从注册表读取最后已知状态
            state   = record.state;
            faction = record.interactedByFaction;
            Debug.Log($"[ConditionEvaluator] WorldItem [{c.objectId}] 已销毁，" +
                      $"从注册表读取：state={state} faction={faction}");
        }
        else
        {
            // ── 3. 既不在场景也无注册记录：物体从未存在或从未被操作过
            Debug.LogWarning($"[ConditionEvaluator] 找不到 WorldItem [{c.objectId}]（场景内不存在且无注册记录），条件返回 false");
            return false;
        }

        // ── stringValue 比较（优先） ─────────────────────────────────────────
        if (!string.IsNullOrEmpty(c.stringValue))
        {
            return c.stringValue.ToLowerInvariant() switch
            {
                "active"             => state == WorldItemState.Active,
                "interrupted"        => state == WorldItemState.Interrupted,
                "completed"          => state == WorldItemState.Completed,

                "pickedupbyplayer"   => state != WorldItemState.Active
                                        && string.Equals(faction, "Player", System.StringComparison.OrdinalIgnoreCase),
                "pickedupbyenemy"    => state != WorldItemState.Active
                                        && string.Equals(faction, "Enemy",  System.StringComparison.OrdinalIgnoreCase),

                "interactedbyplayer" => state != WorldItemState.Active
                                        && string.Equals(faction, "Player", System.StringComparison.OrdinalIgnoreCase),
                "interactedbyenemy"  => state != WorldItemState.Active
                                        && string.Equals(faction, "Enemy",  System.StringComparison.OrdinalIgnoreCase),

                _ => string.Equals(state.ToString(), c.stringValue, System.StringComparison.OrdinalIgnoreCase),
            };
        }

        // ── boolValue 比较（true = Active） ──────────────────────────────────
        return CompareBool(state == WorldItemState.Active, c.boolValue, c.op);
    }

    /// <summary>
    /// 求值 SceneItemInstance 的运行时状态
    /// 被 SceneItemIsOpen / SceneItemIsDestroyed check 和 StoryObjectState + SceneItem 关键字调用
    /// </summary>
    private static bool EvaluateSceneItem(MissionCondition c)
    {
        var sceneItem = FindSceneItemById(c.objectId);
        if (sceneItem == null)
        {
            Debug.LogWarning($"[ConditionEvaluator] 找不到 SceneItem [{c.objectId}]" +
                             $"（check={c.check}，请确认 SceneItemData.sceneObjectId 与 objectId 一致），条件返回 false");
            return false;
        }

        // SceneItemIsOpen / SceneItemIsDestroyed check
        if (c.check == ConditionCheck.SceneItemIsOpen)
            return CompareBool(sceneItem.IsOpen, c.boolValue, c.op);

        if (c.check == ConditionCheck.SceneItemIsDestroyed)
            return CompareBool(sceneItem.IsDestroyed, c.boolValue, c.op);

        // StoryObjectState + SceneItem 专属 stringValue 关键字
        if (!string.IsNullOrEmpty(c.stringValue))
        {
            return c.stringValue.ToLowerInvariant() switch
            {
                "isopen"         => sceneItem.IsOpen,
                "isclosed"       => !sceneItem.IsOpen,
                "isdestroyed"    => sceneItem.IsDestroyed,
                "isnotdestroyed" => !sceneItem.IsDestroyed,
                "istoppled"      => sceneItem.IsToppled,
                _ => false,
            };
        }

        // boolValue 兜底（true = IsOpen，适用于 toggle 类物品）
        return CompareBool(sceneItem.IsOpen, c.boolValue, c.op);
    }

    private static WorldItem FindWorldItemById(string objectId)
    {
        foreach (var item in Object.FindObjectsOfType<WorldItem>())
            if (string.Equals(item.ObjectId, objectId, System.StringComparison.OrdinalIgnoreCase))
                return item;
        return null;
    }

    private static SceneItemInstance FindSceneItemById(string objectId)
    {
        foreach (var item in Object.FindObjectsOfType<SceneItemInstance>())
        {
            if (item.Data != null &&
                string.Equals(item.Data.sceneObjectId, objectId, System.StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }
}