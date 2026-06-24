using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包 Canvas 总控
/// 职责：Tab 控制 InventoryPanel 的 SetActive，Hotbar 始终常驻。
/// 启动时将 InventoryPanel 的 Y 与 HotbarPanel 对齐，并同步两侧格子间距。
/// 开启/关闭时播放从左向右滑入 + 淡入动画，关闭反向。
/// </summary>
public class InventoryCanvas : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject  inventoryPanel;
    [SerializeField] private InventoryUI inventoryUI;

    [Header("对齐引用（可选，不填则自动查找）")]
    [SerializeField] private RectTransform hotbarPanelRect;      // HotbarPanel RectTransform
    [SerializeField] private Transform     hotbarSlotContainer;  // Hotbar SlotContainer
    [SerializeField] private Transform     inventoryGridContainer;// Inventory GridContainer

    [Header("动画配置")]
    [SerializeField] private float animDuration = 0.18f;
    [SerializeField] private float slideOffsetX = -60f; // 负值 = 从左侧滑入

    // 运行时引用（由 inventoryPanel 自动取得）
    private RectTransform _inventoryPanelRect;
    private CanvasGroup   _canvasGroup;
    private float         _targetAnchoredX;

    private bool      _isOpen = false;
    private Coroutine _animCoroutine;

    private PlayerInputController inputController;

    void Awake()
    {
        if (inventoryPanel == null) return;

        // 自动获取，无需 Inspector 额外连线
        _inventoryPanelRect = inventoryPanel.GetComponent<RectTransform>();

        _canvasGroup = inventoryPanel.GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = inventoryPanel.AddComponent<CanvasGroup>();

        inventoryPanel.SetActive(false);
    }

    void Start()
    {
        inputController = FindObjectOfType<PlayerInputController>();
        PlayerInputController.OnToggleInventory += ToggleInventory;

        SyncSpacingAndAlignment();

        // 对齐结束后记录目标 X（此时 panel 已定位）
        if (_inventoryPanelRect != null)
            _targetAnchoredX = _inventoryPanelRect.anchoredPosition.x;
    }

    void OnDestroy()
    {
        PlayerInputController.OnToggleInventory -= ToggleInventory;
    }

    // ── 对齐与间距同步 ───────────────────────────────────────────────────

    private void SyncSpacingAndAlignment()
    {
        // 1. 同步间距：Hotbar VLG spacing → Inventory GLG spacing
        float spacing = 8f;
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

        // 2. Y 对齐：InventoryPanel 顶部与 HotbarPanel 顶部齐平
        if (hotbarPanelRect != null && _inventoryPanelRect != null)
        {
            var pos = _inventoryPanelRect.anchoredPosition;
            pos.y = hotbarPanelRect.anchoredPosition.y;
            _inventoryPanelRect.anchoredPosition = pos;
        }
    }

    // ── 开关控制 ─────────────────────────────────────────────────────────

    public void OpenInventory()
    {
        if (_isOpen) return;
        _isOpen = true;
        inventoryUI?.Refresh();
        inputController?.SetInventoryOpen(true);

        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        inventoryPanel?.SetActive(true);
        _animCoroutine = StartCoroutine(AnimatePanel(true));
    }

    public void CloseInventory()
    {
        if (!_isOpen) return;
        _isOpen = false;
        inputController?.SetInventoryOpen(false);

        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimatePanel(false));
    }

    public void ToggleInventory()
    {
        if (_isOpen) CloseInventory();
        else OpenInventory();
    }

    // ── 动画协程 ─────────────────────────────────────────────────────────

    private IEnumerator AnimatePanel(bool opening)
    {
        // 无法做动画时直接设置终态，保证 SetActive 必定执行
        if (_inventoryPanelRect == null || _canvasGroup == null)
        {
            if (!opening) inventoryPanel?.SetActive(false);
            yield break;
        }

        float startX     = opening ? _targetAnchoredX + slideOffsetX : _targetAnchoredX;
        float endX       = opening ? _targetAnchoredX                 : _targetAnchoredX + slideOffsetX;
        float startAlpha = opening ? 0f : 1f;
        float endAlpha   = opening ? 1f : 0f;

        float elapsed = 0f;
        while (elapsed < animDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / animDuration));

            var pos = _inventoryPanelRect.anchoredPosition;
            pos.x = Mathf.Lerp(startX, endX, t);
            _inventoryPanelRect.anchoredPosition = pos;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);

            yield return null;
        }

        // 精确对齐终点
        var finalPos = _inventoryPanelRect.anchoredPosition;
        finalPos.x = endX;
        _inventoryPanelRect.anchoredPosition = finalPos;
        _canvasGroup.alpha = endAlpha;

        if (!opening)
            inventoryPanel.SetActive(false);
    }
}
