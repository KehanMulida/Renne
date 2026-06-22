using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包 Canvas 总控
/// 职责：Tab 控制 InventoryPanel 的 SetActive，Hotbar 始终常驻。
/// 启动时将 InventoryPanel 的 Y 与 HotbarPanel 对齐，并同步两侧格子间距。
/// </summary>
public class InventoryCanvas : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject  inventoryPanel;
    [SerializeField] private InventoryUI inventoryUI;

    [Header("对齐引用")]
    [SerializeField] private RectTransform hotbarPanelRect;    // HotbarPanel 的 RectTransform
    [SerializeField] private RectTransform inventoryPanelRect; // InventoryPanel 的 RectTransform
    [SerializeField] private Transform     hotbarSlotContainer;   // Hotbar 的 SlotContainer
    [SerializeField] private Transform     inventoryGridContainer; // Inventory 的 GridContainer

    private bool _isOpen = false;
    private PlayerInputController inputController;

    void Awake()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
    }

    void Start()
    {
        inputController = FindObjectOfType<PlayerInputController>();
        PlayerInputController.OnToggleInventory += ToggleInventory;

        SyncSpacingAndAlignment();
    }

    // 同步格子间距，并将 InventoryPanel 的 Y 与 HotbarPanel 对齐
    private void SyncSpacingAndAlignment()
    {
        // 1. 同步间距
        float spacing = 8f; // 默认回退值
        if (hotbarSlotContainer != null)
        {
            var vlg = hotbarSlotContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) spacing = vlg.spacing;
        }
        if (inventoryGridContainer != null)
        {
            var glg = inventoryGridContainer.GetComponent<GridLayoutGroup>();
            if (glg != null) glg.spacing = new Vector2(spacing, spacing);
        }

        // 2. Y 轴对齐：让 InventoryPanel 顶部与 HotbarPanel 顶部齐平
        if (hotbarPanelRect != null && inventoryPanelRect != null)
        {
            var pos = inventoryPanelRect.anchoredPosition;
            pos.y = hotbarPanelRect.anchoredPosition.y;
            inventoryPanelRect.anchoredPosition = pos;
        }
    }

    void OnDestroy()
    {
        PlayerInputController.OnToggleInventory -= ToggleInventory;
    }

    public void OpenInventory()
    {
        if (_isOpen) return;
        _isOpen = true;
        inventoryPanel?.SetActive(true);
        inventoryUI?.Refresh();
        inputController?.SetInventoryOpen(true);
    }

    public void CloseInventory()
    {
        if (!_isOpen) return;
        _isOpen = false;
        inventoryPanel?.SetActive(false);
        inputController?.SetInventoryOpen(false);
    }

    public void ToggleInventory()
    {
        if (_isOpen) CloseInventory();
        else OpenInventory();
    }
}
