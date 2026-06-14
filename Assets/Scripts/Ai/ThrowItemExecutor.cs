using System.Collections;
using UnityEngine;

/// <summary>
/// ThrowItemExecutor — 投掷道具执行器
///
/// BT 触发条件（由 EnemyInventory.EvaluateAndWriteBlackboard 写入）：
///   Blackboard["item_should_throw"] == true
///   Blackboard["item_throw_target"] == ItemData（isThrowable=true）
///   context.TargetPosition          ← 来自 lastSeenPosition（玩家最后已知位置）
///
/// 执行流程：
///   1. 读取投掷道具 + 目标坐标
///   2. 射程检查：目标超出 throwRange 时选最远可投位置
///   3. 调用 ThrowableProjectile.Launch() 发射
///   4. 消耗 AP，清除 Blackboard 标记
/// </summary>
public class ThrowItemExecutor : IActionExecutor
{
    private Transform      owner;
    private UnitMovement   unitMovement;
    private EnemyInventory enemyInventory;
    private TurnBasedUnit  turnUnit;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner     = owner;
        unitMovement   = owner.GetComponent<UnitMovement>();
        enemyInventory = owner.GetComponent<EnemyInventory>();
        turnUnit       = owner.GetComponent<TurnBasedUnit>();
    }

    public bool CanExecute() => enemyInventory != null && unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        // ── 读取道具 ──
        if (!context.Blackboard.TryGetValue("item_throw_target", out var itemObj) ||
            !(itemObj is ItemData throwItem) ||
            !throwItem.isThrowable || throwItem.throwableConfig == null)
        {
            Debug.LogWarning($"[ThrowItemExecutor:{owner.name}] item_throw_target 无效，跳过");
            yield break;
        }

        if (!enemyInventory.HasItem(throwItem))
        {
            Debug.LogWarning($"[ThrowItemExecutor:{owner.name}] 背包里没有 {throwItem.Name}，跳过");
            context.Blackboard.Remove("item_should_throw");
            context.Blackboard.Remove("item_throw_target");
            yield break;
        }

        // ── 确定目标位置 ──
        Vector3 targetPos = context.TargetPosition;
        if (targetPos == Vector3.zero)
        {
            Debug.LogWarning($"[ThrowItemExecutor:{owner.name}] 目标位置为零，跳过投掷");
            yield break;
        }

        // ── 射程限制（将目标夹在最大射程内）──
        ThrowableConfig cfg = throwItem.throwableConfig;
        float cellSize      = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        float maxWorldDist  = cfg.throwRange * cellSize;

        Vector3 startPos = owner.position;
        Vector3 dir      = (targetPos - startPos);
        dir.y = 0f;
        float dist = dir.magnitude;

        if (dist > maxWorldDist)
            targetPos = startPos + dir.normalized * maxWorldDist;

        // ── 投掷延迟 ──
        float elapsed = 0f;
        while (elapsed < 0.25f) { elapsed += Time.deltaTime; yield return null; }

        // ── 发射 ──
        ThrowableProjectile.Launch(throwItem, cfg, startPos, targetPos, owner.gameObject);
        Debug.Log($"[{owner.name}] ThrowItem: {throwItem.Name} → {targetPos} (maxDist={maxWorldDist:F1})");

        // ── 消耗 AP + 道具 ──
        int apCost = throwItem.UseCost > 0 ? throwItem.UseCost : 1;
        turnUnit?.ConsumeAP(apCost);
        enemyInventory.ConsumeItem(throwItem);

        // ── 清除标记 ──
        context.Blackboard.Remove("item_should_throw");
        context.Blackboard.Remove("item_throw_target");
    }
}
