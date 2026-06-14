using UnityEngine;

/// <summary>
/// EnemyHPDisplay — 敌人血量调试显示
///
/// 挂到 Enemy GameObject 上，在 Game 视图中显示血量条和数值，
/// 用于测试道具使用效果，不依赖任何预制体。
///
/// Inspector 参数：
///   worldOffset  — 血量条相对于 Enemy 的世界偏移（默认头顶上方）
///   barSize      — 血量条像素尺寸
///   hpColor      — 满血时的颜色（低血时自动渐变为红色）
///   showName     — 是否在血量条上方显示 Enemy 名称
/// </summary>
public class EnemyHPDisplay : MonoBehaviour
{
    [Header("位置")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 2.2f, 0f);

    [Header("外观")]
    [SerializeField] private Vector2 barSize  = new Vector2(80f, 10f);
    [SerializeField] private Color   hpColor  = new Color(0.2f, 0.85f, 0.2f);
    [SerializeField] private Color   bgColor  = new Color(0.08f, 0.08f, 0.08f, 0.85f);
    [SerializeField] private bool    showName = true;

    private EnemyAIController enemyAI;
    private Camera            mainCam;

    // 懒加载纯白贴图，供 GUI.DrawTexture 使用
    private static Texture2D _whiteTex;
    private static Texture2D WhiteTex
    {
        get
        {
            if (_whiteTex != null) return _whiteTex;
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
            return _whiteTex;
        }
    }

    private void Awake()
    {
        enemyAI = GetComponent<EnemyAIController>();
        mainCam = Camera.main;
    }

    private void OnGUI()
    {
        if (enemyAI == null || mainCam == null) return;
        if (!enemyAI.IsAlive) return;

        // 世界坐标 → 屏幕坐标（GUI 的 Y 轴是反的）
        Vector3 screenPos = mainCam.WorldToScreenPoint(transform.position + worldOffset);
        if (screenPos.z < 0f) return;                    // 在摄像机背后
        screenPos.y = Screen.height - screenPos.y;       // 翻转 Y

        float ratio = enemyAI.MaxHp > 0
            ? Mathf.Clamp01((float)enemyAI.CurrentHp / enemyAI.MaxHp)
            : 0f;

        float x = screenPos.x - barSize.x * 0.5f;
        float y = screenPos.y - barSize.y * 0.5f;

        // ── 背景 ──
        GUI.color = bgColor;
        GUI.DrawTexture(new Rect(x - 1f, y - 1f, barSize.x + 2f, barSize.y + 2f), WhiteTex);

        // ── 血量填充（满血=hpColor，低血渐变为红色）──
        GUI.color = Color.Lerp(Color.red, hpColor, ratio);
        GUI.DrawTexture(new Rect(x, y, barSize.x * ratio, barSize.y), WhiteTex);

        // ── 文字标签 ──
        GUI.color = Color.white;
        string label = showName
            ? $"{gameObject.name}  {enemyAI.CurrentHp} / {enemyAI.MaxHp}"
            : $"{enemyAI.CurrentHp} / {enemyAI.MaxHp}";
        GUI.Label(new Rect(x, y - 18f, barSize.x + 80f, 18f), label);

        GUI.color = Color.white; // 重置，防止影响其他 GUI
    }
}
