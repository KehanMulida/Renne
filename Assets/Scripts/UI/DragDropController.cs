using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 统一拖拽控制器（单例）
/// 职责：
///   - 管理拖拽时跟随鼠标的 DragProxy 图标
///   - 落点判断：Hotbar / 仓库 / 范围外（丢弃到地面）
/// </summary>
public class DragDropController : MonoBehaviour
{
    public static DragDropController Instance { get; private set; }

    [Header("引用")]
    [SerializeField] private RectTransform hotbarPanel;
    [SerializeField] private RectTransform inventoryPanel;
    [SerializeField] private RectTransform dragProxy;       // 跟随鼠标的浮动图标容器
    [SerializeField] private Image         dragProxyIcon;

    [Header("丢弃设置")]
    [SerializeField] private GameObject worldItemPrefab;    // 地面物品预制体
    [SerializeField] private Transform  playerTransform;    // 丢弃时生成在玩家脚下

    private EquipmentManager _equipmentManager;
    private Inventory         _inventory;
    private ItemSlotUI        _draggingSlot;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _equipmentManager = FindObjectOfType<EquipmentManager>();
        _inventory        = FindObjectOfType<Inventory>();
        if (dragProxy != null) dragProxy.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_draggingSlot != null && dragProxy != null)
            dragProxy.position = Input.mousePosition;
    }

    // ── 拖拽开始 ─────────────────────────────────────────────────────────

    public void BeginDrag(ItemSlotUI slot)
    {
        if (slot.Item == null) return;
        _draggingSlot = slot;

        if (dragProxy != null)
        {
            dragProxy.gameObject.SetActive(true);
            if (dragProxyIcon != null)
                dragProxyIcon.sprite = slot.Item.Icon;
        }
    }

    // ── 拖拽结束 ─────────────────────────────────────────────────────────

    public void EndDrag(ItemSlotUI slot, Vector2 screenPos)
    {
        if (_draggingSlot == null) return;

        bool inHotbar    = hotbarPanel != null &&
                           RectTransformUtility.RectangleContainsScreenPoint(hotbarPanel, screenPos);
        bool inInventory = inventoryPanel != null &&
                           inventoryPanel.gameObject.activeSelf &&
                           RectTransformUtility.RectangleContainsScreenPoint(inventoryPanel, screenPos);

        if (inHotbar)
        {
            // 落入 Hotbar：找目标槽位（通过射线找到 ItemSlotUI）
            ItemSlotUI targetSlot = FindSlotUnderPoint(screenPos, true);
            if (targetSlot != null && targetSlot != _draggingSlot)
            {
                if (_draggingSlot.IsHotbarSlot && targetSlot.IsHotbarSlot)
                {
                    _equipmentManager?.SwapHotbarSlots(_draggingSlot.SlotIndex, targetSlot.SlotIndex);
                }
                else if (!_draggingSlot.IsHotbarSlot && targetSlot.IsHotbarSlot)
                {
                    // 仓库格 → 指定 Hotbar 空槽
                    if (_equipmentManager != null && _draggingSlot.Item != null)
                    {
                        int qty = _draggingSlot.Quantity;
                        if (_equipmentManager.DragToHotbarSlot(_draggingSlot.Item, qty, targetSlot.SlotIndex))
                            _inventory?.RemoveItem(_draggingSlot.Item, qty);
                    }
                }
            }
            else if (targetSlot == null && !_draggingSlot.IsHotbarSlot)
            {
                _equipmentManager?.MoveToHotbar(_draggingSlot.Item);
            }
        }
        else if (inInventory)
        {
            if (_draggingSlot.IsHotbarSlot)
                _equipmentManager?.MoveToInventory(_draggingSlot.SlotIndex);
            // 仓库内部换位暂不支持（后续扩展）
        }
        else
        {
            // 范围外 → 丢弃到地面
            DropToWorld(_draggingSlot);
        }

        _draggingSlot = null;
        if (dragProxy != null) dragProxy.gameObject.SetActive(false);
    }

    // ── 丢弃到地面 ───────────────────────────────────────────────────────

    private void DropToWorld(ItemSlotUI slot)
    {
        if (slot.Item == null) return;

        // 从数据层移除
        if (slot.IsHotbarSlot)
            _equipmentManager?.MoveToInventory(slot.SlotIndex);

        _inventory?.RemoveItem(slot.Item, slot.Quantity);

        // 在玩家附近生成 WorldItem
        if (worldItemPrefab != null && playerTransform != null)
        {
            Vector3 dropPos = playerTransform.position
                + new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
            var go = Instantiate(worldItemPrefab, dropPos, Quaternion.identity);
            var wi = go.GetComponent<WorldItem>();
            if (wi != null) wi.Initialize(slot.Item, slot.Quantity);
        }
    }

    // ── 工具方法 ─────────────────────────────────────────────────────────

    private ItemSlotUI FindSlotUnderPoint(Vector2 screenPos, bool hotbarOnly)
    {
        var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        var ped     = new UnityEngine.EventSystems.PointerEventData(
                          UnityEngine.EventSystems.EventSystem.current) { position = screenPos };
        UnityEngine.EventSystems.EventSystem.current.RaycastAll(ped, results);

        foreach (var r in results)
        {
            var s = r.gameObject.GetComponent<ItemSlotUI>();
            if (s == null) s = r.gameObject.GetComponentInParent<ItemSlotUI>();
            if (s != null && (!hotbarOnly || s.IsHotbarSlot)) return s;
        }
        return null;
    }
}
