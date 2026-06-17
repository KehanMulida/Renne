using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 背包物品槽
/// 存储背包中的单个物品及其数量
/// </summary>
[System.Serializable]
public class InventorySlot
{
    public ItemData itemData;
    public int quantity;

    public InventorySlot(ItemData data, int qty)
    {
        itemData = data;
        quantity = qty;
    }

    public bool IsFull()
    {
        return itemData != null && quantity >= itemData.maxStack;
    }

    public bool CanStack(ItemData data)
    {
        return itemData != null && 
               itemData.ID == data.ID && 
               itemData.maxStack > 1 && 
               quantity < itemData.maxStack;
    }
}

/// <summary>
/// 背包系统
/// 职责：
/// 1. 管理玩家的物品列表
/// 2. 添加/移除物品
/// 3. 物品堆叠管理
/// 4. 背包容量限制
/// 特点：
/// - 低耦合：不依赖具体的UI
/// - 事件驱动：通知UI更新
/// - 支持堆叠和容量限制
/// </summary>
public class Inventory : MonoBehaviour
{
    [Header("背包配置")]
    [SerializeField] private int maxSlots = 20;             // 最大槽位数
    [SerializeField] private float maxWeight = 100f;        // 最大负重
    [SerializeField] private bool unlimitedCapacity = false; // 无限容量

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    public void SetDebugEnabled(bool v) { enableDebugLog = v; }

    // 运行时数据
    private List<InventorySlot> slots = new List<InventorySlot>();

    // ============ 公开属性 ============

    public int SlotCount => slots.Count;
    public int MaxSlots => maxSlots;
    public float CurrentWeight => CalculateTotalWeight();
    public float MaxWeight => maxWeight;
    public bool IsFull => !unlimitedCapacity && slots.Count >= maxSlots;

    // ============ 事件系统 ============

    /// <summary>物品添加事件</summary>
    public event System.Action<ItemData, int> OnItemAdded;

    /// <summary>物品移除事件</summary>
    public event System.Action<ItemData, int> OnItemRemoved;

    /// <summary>背包变化事件</summary>
    public event System.Action OnInventoryChanged;

    // ============ 添加物品 ============

    /// <summary>
    /// 添加物品到背包
    /// 返回：是否成功添加
    /// </summary>
    public bool AddItem(ItemData itemData, int quantity = 1)
    {
        if (itemData == null)
        {
            Debug.LogWarning("[Inventory] Cannot add null item");
            return false;
        }

        // 检查负重
        if (!unlimitedCapacity && CurrentWeight + (itemData.weight * quantity) > maxWeight)
        {
            DebugLog($"Cannot add {itemData.Name}: would exceed weight limit");
            return false;
        }

        // 尝试堆叠到现有槽位
        if (itemData.maxStack > 1)
        {
            foreach (var slot in slots)
            {
                if (slot.CanStack(itemData))
                {
                    int spaceLeft = itemData.maxStack - slot.quantity;
                    int toAdd = Mathf.Min(quantity, spaceLeft);
                    
                    slot.quantity += toAdd;
                    quantity -= toAdd;

                    DebugLog($"Stacked {toAdd} {itemData.Name} to existing slot. Now: {slot.quantity}");

                    if (quantity <= 0)
                    {
                        OnItemAdded?.Invoke(itemData, toAdd);
                        OnInventoryChanged?.Invoke();
                        return true;
                    }
                }
            }
        }

        // 创建新槽位
        while (quantity > 0)
        {
            if (!unlimitedCapacity && slots.Count >= maxSlots)
            {
                DebugLog($"Cannot add {itemData.Name}: inventory full");
                return false;
            }

            int toAdd = itemData.maxStack > 1 ? Mathf.Min(quantity, itemData.maxStack) : quantity;
            InventorySlot newSlot = new InventorySlot(itemData, toAdd);
            slots.Add(newSlot);
            quantity -= toAdd;

            DebugLog($"Added new slot: {itemData.Name} x{toAdd}");
        }

        OnItemAdded?.Invoke(itemData, quantity);
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 移除物品
    /// 返回：实际移除的数量
    /// </summary>
    public int RemoveItem(ItemData itemData, int quantity = 1)
    {
        int removedTotal = 0;

        for (int i = slots.Count - 1; i >= 0 && quantity > 0; i--)
        {
            if (slots[i].itemData.ID == itemData.ID)
            {
                int toRemove = Mathf.Min(quantity, slots[i].quantity);
                slots[i].quantity -= toRemove;
                quantity -= toRemove;
                removedTotal += toRemove;

                // 如果槽位空了，移除
                if (slots[i].quantity <= 0)
                {
                    slots.RemoveAt(i);
                }
            }
        }

        if (removedTotal > 0)
        {
            DebugLog($"Removed {removedTotal} {itemData.Name}");
            OnItemRemoved?.Invoke(itemData, removedTotal);
            OnInventoryChanged?.Invoke();
        }

        return removedTotal;
    }

    // ============ 查询接口 ============

    /// <summary>
    /// 检查是否有指定物品
    /// </summary>
    public bool HasItem(ItemData itemData, int requiredQuantity = 1)
    {
        int totalCount = GetItemCount(itemData);
        return totalCount >= requiredQuantity;
    }

    /// <summary>
    /// 获取物品数量
    /// </summary>
    public int GetItemCount(ItemData itemData)
    {
        int count = 0;
        foreach (var slot in slots)
        {
            if (slot.itemData.ID == itemData.ID)
            {
                count += slot.quantity;
            }
        }
        return count;
    }

    /// <summary>
    /// 获取所有物品列表
    /// </summary>
    public List<InventorySlot> GetAllItems()
    {
        return new List<InventorySlot>(slots);
    }

    /// <summary>
    /// 获取指定类型的物品
    /// </summary>
    public List<InventorySlot> GetItemsByType(ItemType type)
    {
        return slots.Where(s => s.itemData.Type == type).ToList();
    }

    /// <summary>
    /// 计算总重量
    /// </summary>
    private float CalculateTotalWeight()
    {
        float total = 0f;
        foreach (var slot in slots)
        {
            total += slot.itemData.weight * slot.quantity;
        }
        return total;
    }

    /// <summary>
    /// 清空背包
    /// </summary>
    public void Clear()
    {
        slots.Clear();
        OnInventoryChanged?.Invoke();
        DebugLog("Inventory cleared");
    }

    // ============ 使用物品 ============

    /// <summary>
    /// 使用物品
    /// </summary>
    public bool UseItem(ItemData itemData)
    {
        if (!HasItem(itemData))
        {
            DebugLog($"Don't have {itemData.Name}");
            return false;
        }

        // 检查UseCost（0=即时使用，不消耗回合）
        if (itemData.UseCost > 0)
        {
            // 需要消耗回合的物品（如武器攻击）
            // 这里暂时只是标记，实际消耗在战斗系统中处理
            DebugLog($"{itemData.Name} requires {itemData.UseCost} action point(s) to use");
        }

        // 应用物品效果
        ApplyItemEffect(itemData);

        // 如果是消耗品，使用后移除
        if (itemData.Type == ItemType.Consumable)
        {
            RemoveItem(itemData, 1);
        }

        DebugLog($"Used {itemData.Name}");
        return true;
    }

    /// <summary>
    /// 应用物品效果
    /// </summary>
    private void ApplyItemEffect(ItemData itemData)
    {
        PlayerController playerCtrl = GetComponent<PlayerController>();

        if (itemData.Type == ItemType.Consumable)
        {
            if (playerCtrl != null)
            {
                if (itemData.healAmount > 0)
                    playerCtrl.Heal(itemData.healAmount);

                if (itemData.staminaAmount > 0)
                    playerCtrl.RestoreStamina(itemData.staminaAmount);

                if (itemData.sanityAmount > 0)
                    playerCtrl.RestoreSanity(itemData.sanityAmount);
            }

            if (itemData.meleeDamage > 0)
            {
                float worldRadius = itemData.meleeRange *
                    (GridManager.Instance != null ? GridManager.Instance.CellSize : 1f);

                Collider[] hits = Physics.OverlapSphere(
                    transform.position, worldRadius, itemData.meleeLayer);

                HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();
                foreach (var hit in hits)
                {
                    IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                    if (damageable == null) continue;
                    if (alreadyHit.Contains(damageable)) continue;
                    alreadyHit.Add(damageable);

                    if (damageable.IsAlive)
                    {
                        damageable.TakeDamage(itemData.meleeDamage, gameObject);
                        DebugLog($"{itemData.Name} dealt {itemData.meleeDamage} damage to {hit.transform.root.name}");
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(itemData.Sound))
            DebugLog($"Play sound: {itemData.Sound}");
    }

    private void DebugLog(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[Inventory] {message}");
        }
    }

    // ============ 调试可视化 ============

    void OnGUI()
    {
        if (!enableDebugLog) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperLeft;

        string info = $"[Inventory]\n" +
                     $"Slots: {SlotCount}/{MaxSlots}\n" +
                     $"Weight: {CurrentWeight:F1}/{MaxWeight}\n";

        // 显示前3个物品
        int displayCount = Mathf.Min(3, slots.Count);
        for (int i = 0; i < displayCount; i++)
        {
            info += $"• {slots[i].itemData.Name} x{slots[i].quantity}\n";
        }

        if (slots.Count > 3)
        {
            info += $"... +{slots.Count - 3} more";
        }

        GUI.Box(new Rect(10, 400, 200, 120), info, style);
    }
}