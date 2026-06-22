using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 仓库背包 UGUI 控制器
/// 行列数在 Inspector 里配置，Start 时预创建全部格子，Refresh 只更新内容。
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private Inventory        inventory;
    [SerializeField] private EquipmentManager equipmentManager;
    [SerializeField] private Transform        gridContainer;
    [SerializeField] private GameObject       slotPrefab;
    [SerializeField] private Sprite           defaultItemIcon;

    [Header("格子配置")]
    [SerializeField] private int columns = 3;
    [SerializeField] private int rows    = 8;

    private List<ItemSlotUI> _slots = new List<ItemSlotUI>();

    void Start()
    {
        if (inventory == null)
            inventory = FindObjectOfType<Inventory>();
        if (equipmentManager == null)
            equipmentManager = FindObjectOfType<EquipmentManager>();

        BuildSlots();

        if (inventory != null)
            inventory.OnInventoryChanged += Refresh;
    }

    void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= Refresh;
    }

    // ── 初始化 ──────────────────────────────────────────────────────────

    private void BuildSlots()
    {
        if (slotPrefab == null || gridContainer == null) return;

        int total = rows * columns;
        for (int i = 0; i < total; i++)
        {
            var go   = Instantiate(slotPrefab, gridContainer);
            var slot = go.GetComponent<ItemSlotUI>();
            slot.SlotIndex    = i;
            slot.IsHotbarSlot = false;
            slot.OnSlotClicked       += OnSlotClicked;
            slot.OnSlotDoubleClicked += OnSlotDoubleClicked;
            if (DragDropController.Instance != null)
            {
                slot.OnBeginDragSlot += DragDropController.Instance.BeginDrag;
                slot.OnEndDragSlot   += DragDropController.Instance.EndDrag;
            }
            _slots.Add(slot);
        }
    }

    // ── 刷新显示 ─────────────────────────────────────────────────────────

    public void Refresh()
    {
        if (inventory == null || _slots.Count == 0) return;

        var items = inventory.GetAllItems();
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] == null) continue;
            if (i < items.Count)
                _slots[i].SetItem(items[i].itemData, items[i].quantity);
            else
                _slots[i].SetEmpty();
        }
    }

    // ── 事件回调 ─────────────────────────────────────────────────────────

    private void OnSlotDoubleClicked(ItemSlotUI slot)
    {
        if (slot.Item == null || equipmentManager == null) return;
        equipmentManager.MoveToHotbar(slot.Item);
    }

    private void OnSlotClicked(ItemSlotUI slot)
    {
        if (slot.Item == null || equipmentManager == null) return;
        equipmentManager.MoveSingleToHotbar(slot.Item);
    }
}
