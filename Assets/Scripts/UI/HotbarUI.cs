using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 快捷栏 UGUI 控制器
/// 始终可见，左侧竖排。订阅 EquipmentManager.OnSlotChanged 自动刷新。
/// </summary>
public class HotbarUI : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private EquipmentManager equipmentManager;
    [SerializeField] private Transform         slotContainer;
    [SerializeField] private GameObject        slotPrefab;
    [SerializeField] private Sprite            defaultItemIcon;

    private List<ItemSlotUI> _slots = new List<ItemSlotUI>();

    void Start()
    {
        if (equipmentManager == null)
            equipmentManager = FindObjectOfType<EquipmentManager>();

        BuildSlots();
        SubscribeEvents();
        RefreshAll();
    }

    void OnDestroy()
    {
        if (equipmentManager != null)
            equipmentManager.OnSlotChanged -= OnSlotChanged;
    }

    // ── 构建格子 ─────────────────────────────────────────────────────────

    // 与 InventoryUI 保持一致的格子规格（修改此处需同步修改 InventoryUI）
    private const float SlotSize    = 60f;
    private const float SlotSpacing = 8f;

    /// <summary>
    /// 构建快捷栏格子
    /// </summary>
    private void BuildSlots()
    {
        foreach (Transform child in slotContainer)
            Destroy(child.gameObject);
        _slots.Clear();

        int size = equipmentManager != null ? equipmentManager.HotbarSize : 8;
        for (int i = 0; i < size; i++)
        {
            var go   = Instantiate(slotPrefab, slotContainer);
            var slot = go.GetComponent<ItemSlotUI>();
            slot.SlotIndex   = i;
            slot.IsHotbarSlot = true;
            slot.OnSlotClicked       += OnSlotClicked; // 订阅格子点击事件
            slot.OnSlotDoubleClicked += OnSlotDoubleClicked; // 订阅格子双击事件
            if (DragDropController.Instance != null)
            {
                slot.OnBeginDragSlot += DragDropController.Instance.BeginDrag; // 订阅格子拖拽开始事件
                slot.OnEndDragSlot   += DragDropController.Instance.EndDrag; // 订阅格子拖拽结束事件
            }
            _slots.Add(slot);
        }
    }

    private void SubscribeEvents()
    {
        if (equipmentManager != null) // 如果 EquipmentManager 存在，则订阅快捷栏格子变化事件
            equipmentManager.OnSlotChanged += OnSlotChanged; // 订阅快捷栏格子变化事件
    }

    // ── 刷新显示 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 刷新所有格子
    /// </summary>
    public void RefreshAll()
    {
        //if (equipmentManager == null) return;
        for (int i = 0; i < _slots.Count; i++)
            RefreshSlot(i);
        UpdateSelectionHighlight();
    }

    /// <summary>
    /// 刷新格子
    /// </summary>
    /// <param name="index">格子索引</param>
    private void RefreshSlot(int index)
    {
        // if (index < 0 || index >= _slots.Count) return;
        var hotbarSlot = equipmentManager.GetSlot(index); // 获取格子数据
        if (hotbarSlot == null || hotbarSlot.IsEmpty)
        {
            _slots[index].SetEmpty();
        }
        else
        {
            var item = hotbarSlot.itemData;
            var icon = (item.Icon != null) ? item.Icon : defaultItemIcon;
            _slots[index].SetItem(item, hotbarSlot.quantity);
        }
    }

    /// <summary>
    /// 更新选中高亮
    /// </summary>
    private void UpdateSelectionHighlight()
    {
        //if (equipmentManager == null) return; 不需要检查，因为 _slots 已经初始化
        int cur = equipmentManager.CurrentSlotIndex;
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].IsSelected = (i == cur);
    }

    // ── 事件回调 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 快捷栏格子变化事件
    /// </summary>
    /// <param name="index">格子索引</param>
    /// <param name="slot">格子数据</param>
    private void OnSlotChanged(int index, HotbarSlot slot)
    {
        RefreshSlot(index);
        UpdateSelectionHighlight();
    }

    private void OnSlotClicked(ItemSlotUI slot)
    {
        equipmentManager?.SelectSlot(slot.SlotIndex);
        UpdateSelectionHighlight();
    }

    private void OnSlotDoubleClicked(ItemSlotUI slot)
    {
        // Hotbar 格双击：移回仓库
        if (!slot.IsHotbarSlot || equipmentManager == null) return;
        equipmentManager.MoveToInventory(slot.SlotIndex);
    }
}
