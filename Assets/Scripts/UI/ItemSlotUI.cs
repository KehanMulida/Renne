using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections;

/// <summary>
/// 单个物品格子（Hotbar 和仓库通用）
///
/// 视觉规则：
///   - bgImage 永远可见，颜色固定，透明度不因物品有无而改变
///   - iconImage / quantityText 跟随物品数据显隐
///   - 高亮（选中）通过 transform.localScale 放大实现，不依赖任何额外 sprite
///   - Hover 通过 bgImage 颜色微调实现
/// </summary>
public class ItemSlotUI : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("子节点引用")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image bgImage;
    [SerializeField] private Text  quantityText;

    [Header("颜色配置")]
    [SerializeField] private Color colorNormal   = new Color(0.75f, 0.75f, 0.75f, 1f);
    [SerializeField] private Color colorHover    = new Color(0.90f, 0.90f, 0.90f, 1f);
    [SerializeField] private Color colorSelected = new Color(1.00f, 1.00f, 1.00f, 1f);

    [Header("高亮缩放")]
    [SerializeField] private float scaleSelected = 1.08f;
    [SerializeField] private float scaleNormal   = 1.00f;

    // ── 运行时数据 ──────────────────────────────────────────────────────
    public ItemData Item     { get; private set; }
    public int      Quantity { get; private set; }
    public int      SlotIndex    { get; set; }
    public bool     IsHotbarSlot { get; set; }

    // ── 事件 ────────────────────────────────────────────────────────────
    public event Action<ItemSlotUI>          OnSlotClicked;
    public event Action<ItemSlotUI>          OnSlotDoubleClicked;
    public event Action<ItemSlotUI>          OnBeginDragSlot;
    public event Action<ItemSlotUI, Vector2> OnEndDragSlot;

    private float _lastClickTime = -1f;
    private const float DoubleClickThreshold = 0.3f;
    private Coroutine _pendingSingleClick;  // 挂起的单击（确认不是双击后才触发）

    private bool _selected;
    private bool _hovered;

    public bool IsSelected
    {
        get => _selected;
        set { _selected = value; RefreshVisual(); }
    }

    // ── 生命周期 ─────────────────────────────────────────────────────────

    void Awake()
    {
        // bgImage 未在 Inspector 连接时自动取根节点 Image
        if (bgImage == null)
            bgImage = GetComponent<Image>();

        if (bgImage != null)
            bgImage.color = colorNormal;

        if (iconImage != null)
        {
            iconImage.enabled = false;
            iconImage.color   = Color.white;
        }

        if (quantityText != null)
            quantityText.enabled = false;
    }

    // ── 公开方法 ─────────────────────────────────────────────────────────

    public void SetItem(ItemData item, int quantity)
    {
        Item     = item;
        Quantity = quantity;
        RefreshContent();
    }

    public void SetEmpty()
    {
        Item     = null;
        Quantity = 0;
        RefreshContent();
    }

    // ── 内容刷新（只管图标和数量，不碰格子本体可见性） ─────────────────

    private void RefreshContent()
    {
        // 武器即使弹药为 0 也保留图标（占据槽位、显示为空弹夹）；其它物品数量为 0 视为空
        bool isWeapon = Item != null && Item.Type == ItemType.Weapon;
        bool hasItem  = Item != null && (Quantity > 0 || isWeapon);
        bool emptyGun = isWeapon && Quantity <= 0;

        if (iconImage != null)
        {
            iconImage.enabled = hasItem;
            if (hasItem && Item.Icon != null)
                iconImage.sprite = Item.Icon;
            // 弹药耗尽的武器：图标变暗，直观提示"没有子弹"
            if (hasItem)
                iconImage.color = emptyGun ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
        }

        if (quantityText != null)
        {
            // 武器始终显示弹药数（含 0）；其它物品数量 > 1 才显示
            bool showQty = hasItem && (isWeapon || Quantity > 1);
            quantityText.enabled = showQty;
            if (showQty) quantityText.text = Quantity.ToString();
        }
    }

    // ── 视觉刷新（颜色 + 缩放，格子本体始终可见） ───────────────────────

    private void RefreshVisual()
    {
        if (bgImage != null)
        {
            bgImage.color = _selected ? colorSelected
                          : _hovered  ? colorHover
                                      : colorNormal;
        }

        float target = _selected ? scaleSelected : scaleNormal;
        transform.localScale = Vector3.one * target;
    }

    // ── 指针事件 ─────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        float now = Time.unscaledTime;
        if (now - _lastClickTime <= DoubleClickThreshold)
        {
            // 双击：取消挂起的单击，只执行双击。否则单击会先移 1 个、双击再移剩余，
            // 堆叠被拆成 1 + (n-1)，多出一个在堆叠外。
            if (_pendingSingleClick != null) { StopCoroutine(_pendingSingleClick); _pendingSingleClick = null; }
            OnSlotDoubleClicked?.Invoke(this);
            _lastClickTime = -1f;
        }
        else
        {
            _lastClickTime = now;
            // 延迟单击：等一个双击阈值，没有第二击才真正触发（与双击互斥）
            if (_pendingSingleClick != null) StopCoroutine(_pendingSingleClick);
            _pendingSingleClick = StartCoroutine(FireSingleClickDelayed());
        }
    }

    // 用 Realtime 计时，避免仓库打开时若 timeScale=0 导致单击不触发
    private IEnumerator FireSingleClickDelayed()
    {
        yield return new WaitForSecondsRealtime(DoubleClickThreshold);
        _pendingSingleClick = null;
        OnSlotClicked?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        if (!_selected) RefreshVisual();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        if (!_selected) RefreshVisual();
    }

    // ── 拖拽事件 ─────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Item == null) return;
        OnBeginDragSlot?.Invoke(this);
    }

    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        OnEndDragSlot?.Invoke(this, eventData.position);
    }
}
