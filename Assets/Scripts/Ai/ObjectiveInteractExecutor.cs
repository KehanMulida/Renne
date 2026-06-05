using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ObjectiveInteractExecutor — ObjectiveDestroy 任务的最后一步
///
/// 前提：Enemy 已经到达 targetZoneId 区域（由 MoveToZoneExecutor 负责）
/// 行为：
///   1. 从 Blackboard 读取 targetObjectId
///   2. 检查自身是否在 targetZoneId 内（不在则立即 yield break，BT 回落到 MoveToZone）
///   3. 在场景内找到对应 WorldItem
///   4. 调用 worldItem.TriggerComplete(gameObject) 触发 Completed 状态
///      → 写入 WorldItemRegistry → ConditionEvaluator 下一轮轮询时检测到成功条件满足
///
/// BT 排布：
///   在 MissionLayer Selector 中放在 Mission_Mobile_MoveToZone 之前，
///   这样"在区域内"时交互优先执行，"未到达"时自然回落到 MoveToZone 推进。
/// </summary>
public class ObjectiveInteractExecutor : IActionExecutor
{
    private Transform    owner;
    private UnitMovement unitMovement;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner       = owner;
        this.unitMovement = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        // ── 1. 读取目标物体 ID ─────────────────────────────────────────────
        if (!context.Blackboard.TryGetValue("targetObjectId", out var objIdObj) ||
            string.IsNullOrEmpty(objIdObj?.ToString()))
        {
            Debug.LogWarning("[ObjectiveInteractExecutor] Blackboard 里没有 targetObjectId，跳过");
            yield break;
        }
        string targetObjectId = objIdObj.ToString();

        // ── 2. 检查是否在目标区域内（不在则回落到 MoveToZone）──────────────
        if (context.Blackboard.TryGetValue("targetZoneId", out var zoneIdObj) &&
            !string.IsNullOrEmpty(zoneIdObj?.ToString()))
        {
            string zoneId = zoneIdObj.ToString();
            if (ZoneManager.Instance != null &&
                !ZoneManager.Instance.IsUnitInZone(
                    zoneId, unitMovement.CurrentGridPosition, unitMovement.CurrentFloor))
            {
                // 还没到区域，让 MoveToZoneExecutor 继续推进
                yield break;
            }
        }

        // ── 3. 在场景内查找 WorldItem ────────────────────────────────────
        WorldItem target = FindWorldItemById(targetObjectId);
        if (target == null)
        {
            // 物体不在场景内：可能已被销毁（交互完成或被玩家取走）
            // 注册表里若已有 Completed 记录则条件会自然满足，无需再交互
            Debug.Log($"[ObjectiveInteractExecutor] WorldItem [{targetObjectId}] 不在场景，" +
                      "可能已完成或已被取走，跳过交互");
            yield break;
        }

        if (target.CurrentState != WorldItemState.Active)
        {
            // 已经被干预（Interrupted）或已完成（Completed），不重复交互
            Debug.Log($"[ObjectiveInteractExecutor] WorldItem [{targetObjectId}] " +
                      $"状态为 {target.CurrentState}，无需再次交互");
            yield break;
        }

        // ── 4. 执行交互 ──────────────────────────────────────────────────
        Debug.Log($"[{owner.name}] ObjectiveInteract: 与 [{targetObjectId}] 交互 → Completed");

        // 短暂"操作"停顿（模拟交互动画，可根据需要调整或改为播放 Animator）
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        target.TriggerComplete(owner.gameObject);

        // 消耗一点 AP 表示此行动有代价（避免同回合无限交互）
        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        if (turnUnit != null)
            turnUnit.ConsumeAP(1);

        Debug.Log($"[{owner.name}] ObjectiveInteract 完成，等待 MissionManager 条件轮询确认");
    }

    private static WorldItem FindWorldItemById(string objectId)
    {
        foreach (var item in Object.FindObjectsOfType<WorldItem>())
            if (string.Equals(item.ObjectId, objectId, System.StringComparison.OrdinalIgnoreCase))
                return item;
        return null;
    }
}
