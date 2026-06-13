using System.Collections.Generic;
using UnityEngine;

// ══════════════════════════════════════════════════════════════════════════════
// MissionTypes.cs
// 任务系统的所有枚举和通用数据结构
// 被 MissionData、MissionPool、MissionManager、ConditionEvaluator 共同引用
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// AI 行为模式
/// 决定 ActionLibrary 查表的 key，定义任务下 AI 的基础行为风格
/// 不描述任务的目标，只描述 AI 的行动方式
/// </summary>
/// <summary>
/// ObjectiveInteractExecutor 的交互模式
/// 写入 Blackboard["objectiveInteractMode"]，执行器据此选择调用哪个 WorldItem 方法
/// </summary>
public enum ObjectiveInteractMode
{
    Destroy,   // TriggerComplete()  — ObjectiveDestroy：Enemy 破坏/完成物体
    Activate,  // TriggerActivate()  — ObjectiveActivate：任务开始时 AI 开启物体
    Repair,    // TriggerActivate()  — ObjectiveRepair：物体被打断后 AI 修复（逻辑同 Activate）
}

public enum MissionBehaviourProfile
{
    Aggressive,    // 主动搜索和攻击，高机动，优先消灭威胁
    Defensive,     // 守住当前区域，低机动，等待威胁进入再还击
    Mobile,        // 持续向目标位置移动（护送/运输/占领），途中遇到威胁优先规避
    Investigative, // 搜索和调查，中等机动，对声音和线索高度敏感
    Stealth,       // 低噪音行动，避免暴露位置，尽量不触发战斗
    Patrol,        // 在指定区域内持续巡逻（配合 targetZoneId 使用）；不填 targetZoneId 则全图巡逻

    ObjectiveGuard,        // 守护目标物体持续运转（WorldItem.CurrentState==Active）
                           // 物体被玩家打断 → WorldItem.State=Interrupted → 配合 failCondition 触发任务失败
    ObjectiveDestroy,      // 移动到目标物体位置并交互破坏它
                           // Enemy 到位后执行交互 → WorldItem.State=Completed → 配合 successCondition 触发成功
    ObjectiveActivate,     // 任务开始时 AI 移动到物体并开启它（Inactive → Active）
                           // 成功条件：StoryObjectState=Active；完成后通常衔接 ObjectiveGuardWithPatrol
    ObjectiveGuardWithPatrol, // 守护物体同时在 targetZoneId 内巡逻
                              // item 被打断时自动切换到 Repair 行为（weight_repair 权重），修复后继续巡逻
}

/// <summary>
/// 遭遇玩家时的战斗响应模式
/// 由 MissionData.combatResponse 定义
/// 具体战术（Aggressive / Defensive / CallSupport 等）由战斗决策层根据战况动态选择
/// </summary>
public enum CombatResponse
{
    Chase,        // 发现玩家立刻追击，转为战斗优先
    Defend,       // 防守反击，不主动追击，维持任务执行
    HoldPosition, // 不离开当前区域，原地应战
}

/// <summary>
/// Condition 比较运算符
/// </summary>
public enum ConditionOperator
{
    Equals,
    NotEquals,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
}

/// <summary>
/// 条件组的逻辑运算方式
/// </summary>
public enum ConditionLogic
{
    AND, // 全部满足才成立（默认）
    OR,  // 任一满足即成立
}

/// <summary>
/// Condition 检查目标类型
/// </summary>
public enum ConditionTarget
{
    Unit,         // 场景中某个具体单位（targetId 指定，如 E03 / VIP）
    Player,       // 玩家单位
    Mission,      // 当前任务自身的状态
    StoryFlag,    // 全局故事标记字典
    PlanProgress, // 敌人计划进度值（0-100）
    Zone,         // 区域相关状态
    Turn,         // 回合计数（任务回合数 TurnCount / 全局回合数 GlobalTurnNumber）
    WorldObject,  // 场景中指定物体的状态
                  //   WorldItem       → check=StoryObjectState, objectId=WorldItem.objectId
                  //   SceneItemInstance → check=SceneItemIsOpen/SceneItemIsDestroyed, objectId=SceneItemData.sceneObjectId

    // ⚠ 新增 target 同样必须追加到末尾
    ActionPoints, // 行动点相关（check=APConsumed：assignedEnemies 累计消耗 AP 总量）
}

/// <summary>
/// Condition 检查属性
/// </summary>
public enum ConditionCheck
{
    // ── Unit / Player ──────────────────────────────
    HP,              // 当前血量（绝对值）
    HPRatio,         // 当前血量百分比（0-1）
    IsAlive,         // 是否存活
    IsDefeated,      // 是否被击败
    Floor,           // 当前所在楼层编号

    // ── Mission ─────────────────────────────────────
    RepeatsDone,     // 该任务已成功完成的次数
    MissionPhase,    // 任务当前内部阶段标记（字符串比较）

    // ── StoryFlag ────────────────────────────────────
    FlagValue,       // StoryFlag 字典中某个 key 的布尔值

    // ── PlanProgress ─────────────────────────────────
    ProgressValue,   // PlanProgress 当前数值（0-100）

    // ── Zone ─────────────────────────────────────────
    UnitInZone,              // 指定单位是否在指定 Zone（targetId + zoneId）
    PlayerInZone,            // 玩家是否在指定 Zone（zoneId）
    AnyEnemyInZone,          // 指定 Zone 内是否有任意敌人（zoneId）
    AllEnemiesDefeatedInZone, // 指定 Zone 内所有敌人是否已被击败（zoneId）
    UnitCountInZone,         // 指定 Zone 内存活单位数量（zoneId，用 numericValue 作阈值）
    UnitInZoneType,          // 指定单位是否在指定类型的任意 Zone（targetId + zoneType）
    PlayerInZoneType,        // 玩家是否在指定类型的任意 Zone（zoneType）

    // ── 任务执行者状态 ─────────────────────────────────
    // 用于 Eliminate / Patrol 等任务的失败判断：执行任务的 Enemy 自己被击败
    AssignedEnemyDefeated, // 16 某个指定参与 Enemy 是否被击败（targetId）
    AllAssignedDefeated,   // 17 任务所有参与 Enemy 是否全部被击败

    // ── 时间条件 ──────────────────────────────────────────
    TurnCount,             // 18 任务激活后经过的回合数（target=Turn 或 Mission，numericValue 为阈值）
    GlobalTurnNumber,      // 19 全局回合数（TurnSystem.GlobalTurnNumber，target=Turn）

    // ── 区域驻留回合 ──────────────────────────────────────
    // 任意参与 Enemy 在 zoneId 内累计驻留的回合数（由 MissionManager 每敌人回合末更新）
    // 用法：target=Zone, check=TurnsInZone, zoneId="patrol_zone", op=GreaterThanOrEqual, numericValue=3
    // 含义：至少一个参与 Enemy 在该区域累积停留了 3 回合 → 触发（成功或失败）
    TurnsInZone,           // 20

    // ── 物品状态 ───────────────────────────────────────
    // 用于放置/运输任务
    ItemPlacedInZone,   // 21 指定物品是否已被放置在指定区域（itemId + zoneId）
    ItemPickedUp,       // 22 指定物品是否已被某个单位拾取（itemId + targetId 指定拾取者）
    ItemInPossession,   // 23 指定单位是否持有指定物品（targetId + itemId）

    // ── 场景物体状态 ────────────────────────────────────────
    // 检查场景中指定 WorldItem 的运行状态（Active / Interrupted / Completed）
    // 用法：target=WorldObject, check=StoryObjectState, objectId="generator_b1"
    //   stringValue="Interrupted"  → 物体被打断（配合 failCondition 使用）
    //   stringValue="Completed"    → 物体交互完成（配合 successCondition 使用）
    //   boolValue=true             → 物体处于 Active 状态（正常运转中）
    StoryObjectState,      // 24

    // ── AP 消耗 ────────────────────────────────────────────
    // ⚠ 新增 check 必须追加到此处末尾，禁止插入到已有 check 中间（会导致 .asset 枚举值错位）
    // 用法：target=ActionPoints, check=APConsumed, op=GreaterThanOrEqual, numericValue=20
    APConsumed,            // 25 任务 assignedEnemies 累计消耗的 AP 总量（多 Enemy 求和）

    // ── 物体持续运转回合 ───────────────────────────────────────────────────────
    // 指定 WorldItem 保持 Active 状态的累计回合数（被 Interrupted 时暂停计数，修复后继续）
    // 用法：target=WorldObject, check=ObjectActiveTurns, objectId="generator_b1",
    //       op=GreaterThanOrEqual, numericValue=5
    ObjectActiveTurns,     // 28

    // SceneItemInstance 实时状态（target=WorldObject, objectId = SceneItemData.sceneObjectId）
    // 直接读取运行时状态，不经过 WorldItemRegistry
    //
    // 成功条件示例（物品被开启）：
    //   target=WorldObject, check=SceneItemIsOpen, objectId="generator_b1", boolValue=true
    // 失败条件示例（物品被破坏）：
    //   target=WorldObject, check=SceneItemIsDestroyed, objectId="relay_box_01", boolValue=true
    SceneItemIsOpen,       // 26 开/关切换状态（SceneItemInstance.IsOpen）
    SceneItemIsDestroyed,  // 27 是否已被破坏（SceneItemInstance.IsDestroyed）
}

// ─────────────────────────────────────────────────────────────────────────────
// MissionCondition
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 单个条件
///
/// 同一 conditions[] 列表内所有条件为 AND 逻辑（全部满足才成立）
/// 需要 OR 效果时，在 failConditions 里添加多条独立记录
///
/// 常用配置示例：
///   护送成功 → target=Unit, check=UnitInZone, targetId="VIP", zoneId="floor3_safeRoom"
///   护送失败 → target=Unit, check=IsDefeated, targetId="VIP", boolValue=true
///   消灭成功 → check=AllAssignedDefeated
///   消灭失败 → check=AssignedEnemyDefeated, targetId="E03"（执行者被玩家击败）
///   封锁失败 → check=PlayerInZone, zoneId="floor2_storage"
/// </summary>
[System.Serializable]
public class MissionCondition
{
    public ConditionTarget   target;
    public ConditionCheck    check;
    public ConditionOperator op;

    [Header("目标指定")]
    [Tooltip("单位 ID（target=Unit 或 check=UnitInZone / AssignedEnemyDefeated 时填写）\n格式：E01 / E07 / VIP")]
    public string targetId;

    [Tooltip("StoryFlag 字典的 key（check=FlagValue 时填写）")]
    public string flagKey;

    [Tooltip("区域 ID（check=UnitInZone / PlayerInZone 等时填写）\n对应 ZoneMarker.zoneId，如 convenience_b1")]
    public string zoneId;

    [Tooltip("区域类型（check=UnitInZoneType / PlayerInZoneType 时填写）")]
    public ZoneType zoneType;

    [Header("比较值")]
    [Tooltip("数值比较目标（HP、进度值、单位数量等）")]
    public float numericValue;

    [Tooltip("布尔比较目标（IsAlive / IsDefeated / FlagValue 等）")]
    public bool boolValue;

    [Tooltip("字符串比较目标（MissionPhase 等）")]
    public string stringValue;

    [Tooltip("物品 ID（check=ItemPlacedInZone / ItemPickedUp / ItemInPossession 时填写）\n" +
             "对应 ItemData.ID 字段，如 weapon_case_01")]
    public string itemId;

    [Tooltip("场景物体唯一 ID（target=WorldObject 时填写）\n" +
             "WorldItem        → 对应 WorldItem.objectId 字段\n" +
             "SceneItemInstance → 对应 SceneItemData.sceneObjectId 字段\n" +
             "例：generator_b1 / relay_switch_02 / fuel_tank_01")]
    public string objectId;
}

// ─────────────────────────────────────────────────────────────────────────────
// ActionWeight / EnemyActionProfile
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class ActionWeight
{
    [Tooltip("行为 key，写入 Blackboard 时格式为 weight_{actionKey}\n" +
             "有效值：patrol / follow / defend / investigate\n" +
             "待补充：chase / retreat（BT 节点尚未实现，填了无效）\n\n" +
             "⚠ weight 值是开关而非优先级：≥阈值=激活，<阈值=跳过\n" +
             "  选哪个分支由 BT 节点排列顺序决定，不是 weight 数值大小\n" +
             "  若同时满足多个条件，BT 取第一个成功的（follow > defend > investigate > patrol）")]
    public string actionKey;

    [Range(0f, 1f)]
    [Tooltip("权重 0-1，越高越优先被 BTRunner 选择")]
    public float weight;
}

[System.Serializable]
public class EnemyActionProfile
{
    [Tooltip("Enemy 编号，如 E01。与 EnemyAIController.enemyId 对应")]
    public string enemyId;

    [Tooltip("该 Enemy 在此任务下的专属行为权重，覆盖默认权重")]
    public List<ActionWeight> actionWeights;
}

// ─────────────────────────────────────────────────────────────────────────────
// MissionContext — 推送给 Enemy 的轻量任务感知副本
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 推送给 Enemy 的轻量任务感知副本
/// MissionManager 在任务激活或状态变化时重新生成并推送
/// Enemy 不直接持有 MissionData
/// </summary>
public class MissionContext
{
    public string                 missionId;
    public MissionBehaviourProfile behaviourProfile;
    public CombatResponse         combatResponse;
    public List<ActionWeight>     actionWeights;
    public string                 missionPhase;   // 如 "initial" / "alerted" / "escorting"
    public int                    repeatsDone;
    public string                 targetZoneId;   // 任务目标区域，AI 导航的终点

    // 优先级（来自 MissionEntry.priority，决定多任务共存时的执行顺序）
    public int priority = 0;

    // 编队（leaderId 为空时不写入 Blackboard，BT 的 exists 检查自然 false）
    public string leaderId         = "";
    public int    formationDistance = 2;  // 跟随者与 Leader 的最大允许距离
    public bool   moveInGroup      = false; // 并排模式：true=紧贴相邻格并排移动，false=独立行动

    // 状态覆盖（默认 1f = 不变，任务结束推新 context 时自动还原）
    public float apMultiplier    = 1f;
    public float speedMultiplier = 1f;

    // 接收此 Context 的 Enemy 编号，用于防止 Leader 把自己写进 Blackboard 跟随自己
    private readonly string ownerEnemyId;

    // ObjectiveGuard / ObjectiveDestroy / ObjectiveActivate 目标物体 ID
    // 对应 WorldItem.objectId，写入 Blackboard 供 ObjectiveInteractExecutor 使用
    public string targetObjectId = "";

    // ObjectiveInteractExecutor 的交互模式（Destroy / Activate / Repair）
    public ObjectiveInteractMode interactMode = ObjectiveInteractMode.Destroy;

    public MissionContext(MissionData data, string enemyId, int priority = 0)
    {
        ownerEnemyId     = enemyId;
        missionId        = data.missionId;
        behaviourProfile = data.behaviourProfile;
        combatResponse   = data.combatResponse;
        missionPhase     = "initial";
        repeatsDone      = 0;
        targetZoneId     = data.targetZoneId;
        targetObjectId   = data.targetObjectId ?? "";
        interactMode     = data.interactMode;
        apMultiplier     = data.apMultiplier;
        speedMultiplier  = data.speedMultiplier;
        this.priority    = priority;
        leaderId          = data.leaderId ?? "";
        formationDistance = data.formationDistance;
        moveInGroup       = data.moveInGroup;

        // 并排模式：强制编队距离为 1（跟随者必须紧贴 Leader 相邻格）
        if (moveInGroup) formationDistance = 1;

        // ── 权重生成：合并自动基准 + 手动覆盖 ─────────────────────────────
        // 1. BuildProfileWeights：根据 behaviourProfile 生成合理的基准权重
        // 2. ResolveWeights：读取 defaultActionWeights（或 enemyActionProfiles 专属配置）
        // 3. MergeWeights：手填的 key 覆盖自动生成的同名 key；新 key 追加
        //    → 设计者只需填写想要微调的 key，其余由 Profile 保底生成
        var profileWeights = BuildProfileWeights(data.behaviourProfile, !string.IsNullOrEmpty(data.targetZoneId));
        var manualWeights  = ResolveWeights(data, enemyId);
        actionWeights      = MergeWeights(profileWeights, manualWeights);
    }

    /// <summary>
    /// 把行为权重和任务信息写入 Enemy 的 Blackboard
    /// 由 EnemyAIController.ApplyTopContext() 调用
    /// </summary>
    public void ApplyToBlackboard(Dictionary<string, object> blackboard)
    {
        if (blackboard == null) return;

        // 行为权重
        if (actionWeights != null)
        {
            foreach (var w in actionWeights)
                blackboard[$"weight_{w.actionKey}"] = w.weight;
        }

        // 任务元数据，供行为树条件节点读取
        blackboard["missionId"]      = missionId;
        blackboard["missionPhase"]   = missionPhase;
        blackboard["combatResponse"] = (int)combatResponse;

        // targetZoneId / targetObjectId 非空才写入
        // 空字符串写入后 exists 检查会通过，但执行器找不到对象会立刻 yield break
        if (!string.IsNullOrEmpty(targetZoneId))
            blackboard["targetZoneId"] = targetZoneId;

        if (!string.IsNullOrEmpty(targetObjectId))
        {
            blackboard["targetObjectId"]        = targetObjectId;
            blackboard["objectiveInteractMode"] = (int)interactMode;
        }

        // 编队：leaderId 非空且不等于自身才写入
        // leaderId == ownerEnemyId 时表示 Leader 自己收到了自己的编队指令（自我跟随死循环），跳过
        if (!string.IsNullOrEmpty(leaderId) && leaderId != ownerEnemyId)
        {
            blackboard["leaderId"]          = leaderId;
            blackboard["formationDistance"] = formationDistance;
            // 并排模式标记（FollowLeaderExecutor 读取，限定为紧邻格）
            if (moveInGroup)
                blackboard["moveInGroup"] = true;
        }
    }

    /// <summary>
    /// 从 Blackboard 中移除此 Context 写入的任务相关 key
    /// 任务出队时调用，防止旧值残留影响下一个任务
    /// 注意：weight_* 权重 key 由 ApplyTopContext 统一清除，不在此处处理
    /// </summary>
    public void RemoveFromBlackboard(Dictionary<string, object> blackboard)
        => ClearMissionBlackboard(blackboard);

    /// <summary>
    /// 清除 Blackboard 中所有任务相关 key（静态，无需实例）
    /// ApplyTopContext 在写入新任务前调用，确保旧任务值不残留
    /// </summary>
    public static void ClearMissionBlackboard(Dictionary<string, object> blackboard)
    {
        if (blackboard == null) return;
        blackboard.Remove("missionId");
        blackboard.Remove("missionPhase");
        blackboard.Remove("combatResponse");
        blackboard.Remove("targetZoneId");
        blackboard.Remove("targetObjectId");
        blackboard.Remove("objectiveInteractMode");
        blackboard.Remove("leaderId");
        blackboard.Remove("formationDistance");
        blackboard.Remove("moveInGroup");
    }

    private List<ActionWeight> ResolveWeights(MissionData data, string enemyId)
    {
        if (data.enemyActionProfiles != null)
        {
            foreach (var profile in data.enemyActionProfiles)
            {
                if (profile.enemyId == enemyId &&
                    profile.actionWeights != null &&
                    profile.actionWeights.Count > 0)
                    return new List<ActionWeight>(profile.actionWeights);
            }
        }
        return data.defaultActionWeights != null
            ? new List<ActionWeight>(data.defaultActionWeights)
            : new List<ActionWeight>();
    }

    /// <summary>
    /// 合并权重列表：baseWeights 为 Profile 自动生成的基准，overrides 为手动配置的覆盖值。
    /// - overrides 中与 baseWeights 同名的 key → 覆盖（手填优先）
    /// - overrides 中全新的 key              → 追加
    /// - 两者都没有权重时                    → 返回空列表（BT 走 DefaultPatrol）
    /// </summary>
    private static List<ActionWeight> MergeWeights(
        List<ActionWeight> baseWeights, List<ActionWeight> overrides)
    {
        if (overrides.Count == 0) return baseWeights;
        var result = new List<ActionWeight>(baseWeights);
        foreach (var ow in overrides)
        {
            int idx = result.FindIndex(w => w.actionKey == ow.actionKey);
            if (idx >= 0)
                result[idx] = ow;  // 手填值覆盖自动生成值
            else
                result.Add(ow);    // 新 key 追加到列表
        }
        return result;
    }

    /// <summary>
    /// 没有显式配置权重时，根据 behaviourProfile 自动生成合理的默认权重。
    /// 设计者只需选择 Profile，无需手动填写 ActionWeights。
    ///
    /// 权重 key 与 EnemyBehaviorTree.json 中的 WeightCondition.key 对应：
    ///   follow      → Mission_Mobile_MoveToZone（向目标区域移动）
    ///   defend      → Mission_Defensive_HoldZone（守住目标区域）
    ///   investigate → Mission_Investigative（搜索目标区域）
    ///   patrol      → Mission_PatrolInZone / DefaultPatrol（巡逻）
    /// </summary>
    private static List<ActionWeight> BuildProfileWeights(
        MissionBehaviourProfile profile, bool hasTargetZone)
    {
        var list = new List<ActionWeight>();
        switch (profile)
        {
            case MissionBehaviourProfile.Aggressive:
                // 主动进攻：有目标区域则向它冲，否则全图随机游走
                if (hasTargetZone)
                    list.Add(new ActionWeight { actionKey = "follow", weight = 0.7f });
                // 无 zone：不写权重，直接走 DefaultPatrol
                break;

            case MissionBehaviourProfile.Defensive:
                // 防守：有目标区域则守住它，否则就地巡逻
                if (hasTargetZone)
                    list.Add(new ActionWeight { actionKey = "defend", weight = 0.8f });
                else
                    list.Add(new ActionWeight { actionKey = "patrol", weight = 0.5f });
                break;

            case MissionBehaviourProfile.Mobile:
                // 移动/护送：向目标区域推进
                if (hasTargetZone)
                    list.Add(new ActionWeight { actionKey = "follow", weight = 0.8f });
                break;

            case MissionBehaviourProfile.Investigative:
                // 调查：前往目标区域调查
                if (hasTargetZone)
                    list.Add(new ActionWeight { actionKey = "investigate", weight = 0.7f });
                else
                    list.Add(new ActionWeight { actionKey = "patrol", weight = 0.4f });
                break;

            case MissionBehaviourProfile.Stealth:
                // 潜行：低噪声巡逻，不主动暴露
                list.Add(new ActionWeight { actionKey = "patrol", weight = 0.4f });
                break;

            case MissionBehaviourProfile.Patrol:
                // 巡逻：在 targetZoneId 区域内持续巡逻；无 zone 则 DefaultPatrol
                // weight_patrol 同时触发 Mission_PatrolInZone（需 zone）和兜底
                list.Add(new ActionWeight { actionKey = "patrol", weight = 0.8f });
                break;

            case MissionBehaviourProfile.ObjectiveGuard:
                // 守护目标物体：在 targetZoneId 区域内低烈度巡逻来回走动
                // 无 zone 时全图低强度巡逻
                if (hasTargetZone)
                    list.Add(new ActionWeight { actionKey = "patrol", weight = 0.6f });
                else
                    list.Add(new ActionWeight { actionKey = "patrol", weight = 0.4f });
                break;

            case MissionBehaviourProfile.ObjectiveDestroy:
                // 破坏目标物体：向 targetZoneId 推进，到位后执行交互（TriggerComplete）
                // interact 权重最高，ObjectiveInteractExecutor 内部检查"已在区域内"，
                // 不在区域时立即 yield break → BT 回落到 follow 权重推进移动
                if (hasTargetZone)
                {
                    list.Add(new ActionWeight { actionKey = "interact", weight = 0.9f });
                    list.Add(new ActionWeight { actionKey = "follow",   weight = 0.85f });
                }
                break;

            case MissionBehaviourProfile.ObjectiveActivate:
                // 开启目标物体（Inactive→Active）
                // interact 权重无论有无 zone 都生成：无 zone 时 ObjectiveInteractExecutor
                // 会写入 targetGridPosition，由 MoveToPositionExecutor 导航到 item 旁边
                list.Add(new ActionWeight { actionKey = "interact", weight = 0.9f });
                if (hasTargetZone)
                    list.Add(new ActionWeight { actionKey = "follow", weight = 0.85f });
                break;

            case MissionBehaviourProfile.ObjectiveGuardWithPatrol:
                // 守护并巡逻：在 targetZoneId 内巡逻，同时监视物体
                // repair 权重最高：ObjectiveInteractExecutor 内部检查 item.State==Interrupted，
                // 不满足时立即 yield break → 回落到 patrol 正常巡逻
                if (hasTargetZone)
                {
                    list.Add(new ActionWeight { actionKey = "interact", weight = 0.95f }); // repair
                    list.Add(new ActionWeight { actionKey = "patrol",   weight = 0.7f });
                }
                else
                {
                    list.Add(new ActionWeight { actionKey = "patrol", weight = 0.5f });
                }
                break;
        }
        return list;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// MissionRuntimeState — 任务运行时状态（MissionManager 内部跟踪）
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 任务运行时状态，MissionManager 为每个激活的任务维护一个此对象
/// 纯运行时数据，不存储在 ScriptableObject 里
/// </summary>
public class MissionRuntimeState
{
    public MissionData data;
    public int    repeatsDone;
    public string currentPhase;
    public bool   isActive;
    public float  activatedTime;
    public int    turnCount;       // 任务激活后经过的回合数（有 targetZoneId 时从抵达区域起算）
    public int    priority;        // 来自 MissionEntry.priority，用于推送带优先级的 Context
    public bool   exclusive;       // 来自 MissionEntry.allowParallel=false；推送时清除 Enemy 队列中低优先级任务

    /// <summary>
    /// 各区域的驻留回合计数（key = zoneId，value = 累计回合数）
    /// 每敌人回合末由 MissionManager.UpdateZoneTurnCounters() 更新：
    /// 若至少一个参与 Enemy 在该区域内，则 +1。
    /// 供 ConditionCheck.TurnsInZone 条件读取。
    /// </summary>
    public Dictionary<string, int> turnsInZone = new Dictionary<string, int>();

    /// <summary>
    /// 物体持续运转回合计数（key = objectId，value = 累计 Active 回合数）
    /// 每敌人回合末由 MissionManager.UpdateObjectActiveTurnCounters() 更新：
    /// WorldItem.CurrentState == Active 则 +1，Interrupted 时暂停（不清零），修复后继续累积。
    /// 供 ConditionCheck.ObjectActiveTurns 条件读取。
    /// </summary>
    public Dictionary<string, int> objectActiveTurns = new Dictionary<string, int>();

    /// <summary>
    /// 任务激活后 assignedEnemies 累计消耗的 AP 总量
    /// 由 MissionManager 订阅每个参与 Enemy 的 TurnBasedUnit.OnAPConsumed 事件累加
    /// </summary>
    public int totalAPConsumed = 0;

    public MissionRuntimeState(MissionData data, int priority = 0)
    {
        this.data     = data;
        this.priority = priority;
        repeatsDone   = 0;
        currentPhase  = "initial";
        isActive      = true;
        activatedTime = Time.time;
        turnCount     = 0;
    }

    public bool IsCompleted => repeatsDone >= data.repeatCount;
}