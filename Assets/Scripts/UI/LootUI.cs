using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LootUI — 搜刮面板，跟随尸体世界坐标显示在旁边，风格参考生存类游戏。
/// 挂到玩家 GameObject 上，由 PlayerInputController 调用 Open/Close。
/// </summary>
public class LootUI : MonoBehaviour
{
    [Header("美术资源（可选）")]
    [Tooltip("由 SRPG → LootUI 美术配置 窗口生成并拖入，留空使用纯色占位")]
    [SerializeField] public LootUIAssets assets;

    [Header("布局")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.8f, 0f);
    [SerializeField] private float   panelWidth  = 190f;
    [SerializeField] private float   rowHeight   = 24f;
    [SerializeField] private int     maxRows     = 8;

    private bool           isOpen;
    private EnemyInventory lootSource;
    private Inventory      playerInventory;
    private Camera         mainCam;

    public bool IsOpen => isOpen;

    private static Texture2D _white;
    private static Texture2D White
    {
        get
        {
            if (_white != null) return _white;
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            return _white;
        }
    }

    private void Awake() => mainCam = Camera.main;

    public void Open(EnemyInventory source, Inventory playerInv)
    {
        lootSource      = source;
        playerInventory = playerInv;
        isOpen          = true;
    }

    public void Close()
    {
        isOpen          = false;
        lootSource      = null;
        playerInventory = null;
    }

    private void Update()
    {
        if (!isOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (lootSource == null || !lootSource.IsSearchable) Close();
    }

    private void OnGUI()
    {
        if (!isOpen || lootSource == null || mainCam == null) return;

        var slots = GetValidSlots();

        // ── 世界坐标 → 屏幕坐标 ──────────────────────────────────────
        Vector3 world  = lootSource.transform.position + worldOffset;
        Vector3 screen = mainCam.WorldToScreenPoint(world);
        if (screen.z < 0) return;
        screen.y = Screen.height - screen.y;   // GUI Y 轴翻转

        int   visRows  = Mathf.Min(slots.Count, maxRows);
        float panelH   = 28f + visRows * rowHeight + (slots.Count > 0 ? 28f : 0f) + 4f;
        float px       = Mathf.Clamp(screen.x + 14f, 4f, Screen.width  - panelWidth - 4f);
        float py       = Mathf.Clamp(screen.y - panelH * 0.5f, 4f, Screen.height - panelH - 4f);

        Rect panel = new Rect(px, py, panelWidth, panelH);

        // ── 背景 ──────────────────────────────────────────────────────
        DrawTex(panel, assets?.panelBackground, new Color(0.08f, 0.08f, 0.08f, 0.92f));
        GUI.color = new Color(0.55f, 0.45f, 0.25f, 1f);
        GUI.DrawTexture(new Rect(px, py, panelWidth, 2f), White);
        GUI.DrawTexture(new Rect(px, py + panelH - 2f, panelWidth, 2f), White);
        GUI.color = Color.white;

        // ── 标题 ──────────────────────────────────────────────────────
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };
        titleStyle.normal.textColor = new Color(0.9f, 0.78f, 0.4f);
        GUI.Label(new Rect(px + 8f, py + 2f, panelWidth - 16f, 24f),
                  lootSource.gameObject.name, titleStyle);

        if (slots.Count == 0)
        {
            var emptyStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            emptyStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(px, py + 28f, panelWidth, rowHeight), "（空）", emptyStyle);
            return;
        }

        // ── 物品行 ────────────────────────────────────────────────────
        var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        nameStyle.normal.textColor = Color.white;

        var qtyStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
        qtyStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);

        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 10 };

        float btnW = 36f;
        float qtyW = 28f;

        for (int i = 0; i < visRows; i++)
        {
            var slot = slots[i];
            float ry = py + 28f + i * rowHeight;

            // 偶数行底纹
            if (i % 2 == 1)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.04f);
                GUI.DrawTexture(new Rect(px + 2f, ry, panelWidth - 4f, rowHeight), White);
                GUI.color = Color.white;
            }

            float nameW = panelWidth - qtyW - btnW - 20f;
            GUI.Label(new Rect(px + 6f, ry, nameW, rowHeight), slot.itemData.Name, nameStyle);
            GUI.Label(new Rect(px + 6f + nameW, ry, qtyW, rowHeight), $"×{slot.quantity}", qtyStyle);

            if (GUI.Button(new Rect(px + panelWidth - btnW - 4f, ry + 3f, btnW, rowHeight - 6f), "取", btnStyle))
                TakeItem(slot.itemData, slot.quantity);
        }

        if (slots.Count > maxRows)
        {
            var moreStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            moreStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(px, py + 28f + visRows * rowHeight, panelWidth, rowHeight),
                      $"… 还有 {slots.Count - maxRows} 件", moreStyle);
        }

        // ── 全部拿走 ──────────────────────────────────────────────────
        float footY = py + panelH - 28f;
        DrawTex(new Rect(px, footY, panelWidth, 28f), assets?.footerBackground, new Color(0.22f, 0.18f, 0.1f, 1f));

        if (GUI.Button(new Rect(px + 6f, footY + 4f, panelWidth - 12f, 20f), "全部拿走", btnStyle))
            TakeAll();
    }

    // ── 操作 ─────────────────────────────────────────────────────────────

    private void TakeItem(ItemData item, int qty)
    {
        if (playerInventory.AddItem(item, qty))
            lootSource.TakeItem(item, qty);
        else
            Debug.Log($"[LootUI] 背包已满，无法拾取 {item.Name}");
    }

    private void TakeAll()
    {
        foreach (var slot in GetValidSlots())
            TakeItem(slot.itemData, slot.quantity);
        Close();
    }

    private List<InventorySlot> GetValidSlots()
    {
        var result = new List<InventorySlot>();
        if (lootSource == null) return result;
        foreach (var s in lootSource.GetAllItems())
            if (s.itemData != null && s.quantity > 0) result.Add(s);
        return result;
    }

    // 有美术贴图用贴图，否则用纯色占位
    private static void DrawTex(Rect r, Texture2D tex, Color fallback)
    {
        if (tex != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex);
        }
        else
        {
            GUI.color = fallback;
            GUI.DrawTexture(r, White);
            GUI.color = Color.white;
        }
    }
}
