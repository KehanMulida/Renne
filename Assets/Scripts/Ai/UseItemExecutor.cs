using System.Collections;
using UnityEngine;

/// <summary>
/// UseItemExecutor — 使用消耗品执行器
///
/// 职责：决定"何时停止"和"消耗道具"，具体效果委托给 EnemyInventory。
/// 执行器不知道任何 stat 字段名（healAmount / staminaAmount 等），
/// 通过 EnemyInventory 的三个方法路由所有副作用：
///   FireStatEffects()  — 广播自身数值变化事件（hp/stamina/sanity/...）
///   ApplyMeleeEffect() — AOE 伤害
///   ConsumeItem()      — 统一移除道具
///
/// 扩展新效果：在 EnemyInventory.FireStatEffects() 里加一行，此处无需改动。
/// </summary>
public class UseItemExecutor : IActionExecutor
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
        // ── 读取目标道具 ──
        if (!context.Blackboard.TryGetValue("item_selected", out var itemObj) ||
            !(itemObj is ItemData consumable) || consumable.Type != ItemType.Consumable)
        {
            Debug.LogWarning($"[UseItemExecutor:{owner.name}] item_selected 不存在或非 Consumable 类型");
            yield break;
        }

        if (!enemyInventory.HasItem(consumable))
        {
            Debug.LogWarning($"[UseItemExecutor:{owner.name}] 背包里没有 {consumable.Name}，跳过");
            context.Blackboard.Remove("item_should_heal");
            context.Blackboard.Remove("item_selected");
            yield break;
        }

        // ── 使用动画延迟 ──
        float elapsed = 0f;
        while (elapsed < 0.3f) { elapsed += Time.deltaTime; yield return null; }

        // ── 应用自身数值效果（通过事件传到 EnemyAIController.HandleStatEffect）──
        enemyInventory.FireStatEffects(consumable);

        // ── 应用近战/AOE 效果（如有）──
        enemyInventory.ApplyMeleeEffect(consumable, owner);

        // ── 统一消耗道具（副作用方法不再负责移除）──
        enemyInventory.ConsumeItem(consumable);

        Debug.Log($"[{owner.name}] UseItem: {consumable.Name}");

        // ── 消耗 AP ──
        int apCost = consumable.UseCost > 0 ? consumable.UseCost : 1;
        turnUnit?.ConsumeAP(apCost);

        // ── 清除标记 ──
        context.Blackboard.Remove("item_should_heal");
        context.Blackboard.Remove("item_selected");
    }
}
