using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EnemyInventory — AI 物品栏
///
/// 职责：
///   1. 持有并初始化一个 Inventory 组件（与玩家共用同一套 ItemData 体系）
///   2. 每回合开始时评估当前物品库存 + 战场状态，将决策结果写入 Blackboard
///   3. 为执行器提供副作用方法（治疗、近战、投掷效果）—— 独立于玩家的 ApplyItemEffect
///
/// 挂载方式：
///   与 EnemyAIController 挂在同一 GameObject 上。
///   Inspector 里在 startingItems 列表中配置初始持有物品。
///
/// Blackboard Keys（每回合写入，执行器只读）：
///   item_should_heal   (bool)       → 应该使用治疗道具
///   item_should_throw  (bool)       → 应该投掷道具（需要视野接触）
///   item_selected      (ItemData)   → useItem 执行器要使用的道具
///   item_throw_target  (ItemData)   → throwItem 执行器要投掷的道具
/// </summary>
public class EnemyInventory : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────

    [Header("初始物品")]
    [SerializeField] private List<ItemEntry> startingItems = new List<ItemEntry>();

    [Header("治疗条件")]
    [Tooltip("HP 低于此比例时自动使用治疗道具（0=禁用）")]
    [SerializeField] [Range(0f, 1f)] public float healThreshold = 0.5f;

    [Header("投掷条件")]
    [Tooltip("开启后 AI 在有视野且 HP 高于 throwMinHpRatio 时投掷")]
    [SerializeField] public bool allowThrowInCombat = true;
    [Tooltip("HP 低于此值时不投掷，优先自保")]
    [SerializeField] [Range(0f, 1f)] public float throwMinHpRatio = 0.4f;

    [Header("搜刮（玩家）")]
    [Tooltip("玩家与尸体的最大搜刮距离（世界单位）")]
    [SerializeField] public float lootRange = 1.8f;

    [Header("拾取行为（AI 非战斗）")]
    [Tooltip("每次评估时触发拾取的概率（0=禁用）")]
    [SerializeField] [Range(0f, 1f)] private float pickupChance = 0.4f;
    [Tooltip("拾取成功后需等待的回合数（防止每回合都拾取）")]
    [SerializeField] private int pickupCooldownTurns = 3;
    [Tooltip("检测周围 WorldItem 的世界坐标距离")]
    [SerializeField] private float pickupDetectionRange = 3.5f;

    // ── 事件 ───────────────────────────────────────────────────────

    public event System.Action<EnemyStatEffect> OnStatEffectRequested;

    // ── 内部 ───────────────────────────────────────────────────────

    private Inventory          inventory;
    private EnemyAIController  enemyAI;
    private int                _pickupCooldown;

    /// <summary>敌人已死亡且背包非空时可被搜刮</summary>
    public bool IsSearchable => enemyAI != null && !enemyAI.IsAlive && inventory.SlotCount > 0;

    /// <summary>供 PickupItemExecutor 检查超界用</summary>
    public float PickupDetectionRange => pickupDetectionRange;

    // ── Unity ──────────────────────────────────────────────────────

    private void Awake()
    {
        enemyAI   = GetComponent<EnemyAIController>();
        inventory = GetComponent<Inventory>();
        if (inventory == null)
            inventory = gameObject.AddComponent<Inventory>();

        // 禁用 Inventory 自带的 debug GUI，避免显示在玩家物品栏旁边
        inventory.SetDebugEnabled(false);
    }

    private void Start()
    {
        foreach (var entry in startingItems)
        {
            if (entry?.item != null)
                inventory.AddItem(entry.item, entry.quantity);
        }
    }

    // ── 决策：每回合由 EnemyAIController.OnMyTurnStart 调用 ─────────

    /// <summary>
    /// 根据当前物品库存和战况，将决策写入 Blackboard。
    /// 优先级：治疗 > 投掷（二者互斥，一回合只做一件）
    /// </summary>
    public void EvaluateAndWriteBlackboard(
        Dictionary<string, object> bb,
        float hpRatio)
    {
        // 清除上一回合的标记，避免残留
        bb.Remove("item_should_heal");
        bb.Remove("item_should_throw");
        bb.Remove("item_selected");
        bb.Remove("item_throw_target");

        // ── 治疗优先 ──
        if (healThreshold > 0f && hpRatio < healThreshold)
        {
            ItemData healItem = FindBestHealItem();
            if (healItem != null)
            {
                bb["item_selected"]    = healItem;
                bb["item_should_heal"] = true;
                return;
            }
        }

        // 投掷不在此处自动触发，由外部事件调用 RequestThrow() 写入标记
    }

    // ── 非战斗拾取评估：每回合由 EnemyAIController.OnMyTurnStart 单独调用 ─

    /// <summary>
    /// 评估是否应在本回合拾取附近的 WorldItem，结果写入 Blackboard。
    /// 与 EvaluateAndWriteBlackboard 分开调用，保证只在非战斗回合执行。
    /// </summary>
    public void EvaluatePickup(Dictionary<string, object> bb)
    {
        bb.Remove("item_can_pickup");
        bb.Remove("item_pickup_target");

        if (_pickupCooldown > 0) return;
        if (pickupChance <= 0f) return;
        if (UnityEngine.Random.value > pickupChance) return;

        WorldItem target = FindNearbyWorldItem();
        if (target == null) return;

        bb["item_can_pickup"]    = true;
        bb["item_pickup_target"] = target;
    }

    /// <summary>每回合开始时由 EnemyAIController 调用，递减冷却计数</summary>
    public void TickPickupCooldown()
    {
        if (_pickupCooldown > 0) _pickupCooldown--;
    }

    /// <summary>拾取成功后由 PickupItemExecutor 调用</summary>
    public void ResetPickupCooldown() => _pickupCooldown = pickupCooldownTurns;

    /// <summary>在 pickupDetectionRange 内随机选取一个可拾取 WorldItem</summary>
    private WorldItem FindNearbyWorldItem()
    {
        var candidates = new System.Collections.Generic.List<WorldItem>();
        foreach (var w in Object.FindObjectsOfType<WorldItem>())
        {
            if (w == null || w.IsPickedUp) continue;
            if (w.ItemData == null || w.ItemData.Type == ItemType.SceneItem) continue;
            if (Vector3.Distance(transform.position, w.transform.position) <= pickupDetectionRange)
                candidates.Add(w);
        }
        return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    /// <summary>
    /// 外部事件（如 MissionManager、战术脚本）调用，请求 AI 本回合投掷。
    /// 调用后 BT 在下一次 Tick 检测到 item_should_throw=true 时执行。
    /// </summary>
    public void RequestThrow(Dictionary<string, object> bb)
    {
        if (!allowThrowInCombat) return;
        ItemData throwItem = FindBestThrowable();
        if (throwItem == null) return;
        bb["item_throw_target"] = throwItem;
        bb["item_should_throw"] = true;
    }

    // ── 查询 API（供执行器调用）────────────────────────────────────

    /// <summary>选出 healAmount 最高的可消耗治疗道具</summary>
    public ItemData FindBestHealItem()
    {
        ItemData best    = null;
        int      bestVal = -1;
        foreach (var slot in inventory.GetAllItems())
        {
            if (slot.quantity <= 0) continue;
            if (slot.itemData.Type == ItemType.Consumable && slot.itemData.healAmount > bestVal)
            {
                best    = slot.itemData;
                bestVal = slot.itemData.healAmount;
            }
        }
        return best;
    }

    /// <summary>选出 throwRange 最大（最有威胁）的投掷道具</summary>
    public ItemData FindBestThrowable()
    {
        ItemData best    = null;
        float    bestRng = -1f;
        foreach (var slot in inventory.GetAllItems())
        {
            if (slot.quantity <= 0) continue;
            var item = slot.itemData;
            if (!item.isThrowable || item.throwableConfig == null) continue;
            float rng = item.throwableConfig.throwRange;
            if (rng > bestRng) { best = item; bestRng = rng; }
        }
        return best;
    }

    public bool HasItem(ItemData item, int qty = 1) => inventory.HasItem(item, qty);
    public List<InventorySlot> GetAllItems()         => inventory.GetAllItems();
    public List<InventorySlot> GetByType(ItemType t) => inventory.GetItemsByType(t);
    public Inventory RawInventory                    => inventory;

    // ── 副作用（由执行器调用）──────────────────────────────────────

    /// <summary>
    /// 将消耗品的自身数值效果转为 EnemyStatEffect 事件并广播。
    /// 不移除道具，消耗由调用方（UseItemExecutor）统一在最后调用 ConsumeItem()。
    ///
    /// 扩展新 stat：在 ConsumableData 里加字段后，在此处加一行即可。
    /// </summary>
    public void FireStatEffects(ItemData consumable)
    {
        if (consumable.healAmount > 0)
            OnStatEffectRequested?.Invoke(new EnemyStatEffect(EnemyStatEffect.HP, consumable.healAmount));
        if (consumable.staminaAmount > 0)
            OnStatEffectRequested?.Invoke(new EnemyStatEffect(EnemyStatEffect.Stamina, consumable.staminaAmount));
        if (consumable.sanityAmount > 0)
            OnStatEffectRequested?.Invoke(new EnemyStatEffect(EnemyStatEffect.Sanity, consumable.sanityAmount));
        // 新 stat 示例：
        // if (consumable.moraleAmount > 0)
        //     OnStatEffectRequested?.Invoke(new EnemyStatEffect(EnemyStatEffect.Morale, consumable.moraleAmount));
    }

    /// <summary>返回该消耗品是否有任何自身数值效果</summary>
    public bool HasAnySelfEffect(ItemData consumable)
        => consumable.healAmount > 0 || consumable.staminaAmount > 0 || consumable.sanityAmount > 0;

    /// <summary>
    /// AOE 近战效果：对 origin 周围单位造成伤害。
    /// 不移除道具，消耗由调用方统一处理。
    /// </summary>
    public void ApplyMeleeEffect(ItemData consumable, Transform origin)
    {
        if (consumable.meleeDamage <= 0) return;

        float cellSize   = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        float worldRadius = consumable.meleeRange * cellSize;

        Collider[] hits = Physics.OverlapSphere(origin.position, worldRadius, consumable.meleeLayer);
        var alreadyHit  = new HashSet<IDamageable>();

        foreach (var hit in hits)
        {
            var dmg = hit.GetComponentInParent<IDamageable>();
            if (dmg == null || alreadyHit.Contains(dmg)) continue;
            alreadyHit.Add(dmg);
            dmg.TakeDamage(consumable.meleeDamage, origin.gameObject);
        }
    }

    /// <summary>仅移除道具（投掷类由 ThrowableProjectile 处理效果）</summary>
    public void ConsumeItem(ItemData item) => inventory.RemoveItem(item, 1);

    // ── 搜刮 API（供 LootUI 调用）────────────────────────────────────────

    /// <summary>将指定物品（全部数量）转移给调用方；返回实际移除数量</summary>
    public int TakeItem(ItemData item, int qty) => inventory.RemoveItem(item, qty);

    /// <summary>清空背包（全部拿走时调用）</summary>
    public void TakeAll() => inventory.Clear();

    // ── 内部类 ─────────────────────────────────────────────────────

    [System.Serializable]
    public class ItemEntry
    {
        public ItemData item;
        [Min(1)] public int quantity = 1;
    }
}
