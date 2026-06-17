using System.Collections;
using UnityEngine;

/// <summary>
/// PickupItemExecutor — 非战斗拾取物品执行器
/// 位置：Assets/Scripts/Ai/PickupItemExecutor.cs
///
/// 触发条件（由 EnemyInventory.EvaluatePickup 写入 Blackboard）：
///   item_can_pickup   (bool)      → 本回合允许拾取
///   item_pickup_target (WorldItem) → 目标物品
///
/// 拾取成功后重置冷却，使该行为至少 N 回合内不再触发。
/// </summary>
public class PickupItemExecutor : IActionExecutor
{
    private Transform      owner;
    private EnemyInventory enemyInventory;
    private TurnBasedUnit  turnUnit;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner     = owner;
        enemyInventory = owner.GetComponent<EnemyInventory>();
        turnUnit       = owner.GetComponent<TurnBasedUnit>();
    }

    public bool CanExecute() => enemyInventory != null;

    public IEnumerator Execute(ActionContext context)
    {
        // ── 读取目标 ──────────────────────────────────────────────────
        if (!context.Blackboard.TryGetValue("item_pickup_target", out var obj)
            || !(obj is WorldItem worldItem) || worldItem == null || worldItem.IsPickedUp)
        {
            ClearBlackboard(context);
            yield break;
        }

        // ── 检查距离（执行时目标可能已被移走）────────────────────────
        float dist = Vector3.Distance(owner.position, worldItem.transform.position);
        if (dist > enemyInventory.PickupDetectionRange + 0.5f)
        {
            ClearBlackboard(context);
            yield break;
        }

        // ── 拾取动作延迟（模拟弯腰）──────────────────────────────────
        float t = 0f;
        while (t < 0.25f) { t += Time.deltaTime; yield return null; }

        // ── 加入背包，然后销毁 WorldItem ─────────────────────────────
        bool added = enemyInventory.RawInventory.AddItem(worldItem.ItemData, worldItem.Quantity);
        if (added)
        {
            Debug.Log($"[{owner.name}] 拾取: {worldItem.ItemData.Name} x{worldItem.Quantity}");
            worldItem.Pickup(owner.gameObject); // 更新任务状态 + Destroy
        }
        else
        {
            Debug.Log($"[{owner.name}] 背包已满，无法拾取 {worldItem.ItemData?.Name}");
        }

        // ── 消耗 AP + 重置拾取冷却 ───────────────────────────────────
        turnUnit?.ConsumeAP(1);
        enemyInventory.ResetPickupCooldown();

        ClearBlackboard(context);
    }

    private static void ClearBlackboard(ActionContext ctx)
    {
        ctx.Blackboard.Remove("item_can_pickup");
        ctx.Blackboard.Remove("item_pickup_target");
    }
}
