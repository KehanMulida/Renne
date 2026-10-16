using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MainMenuUI —— 主菜单 + 设置。
///
/// 整个界面用代码搭，不依赖预制件和美术资源：项目现在还没有 UI 美术
/// （LootUI 也还是 OnGUI），等美术就绪再转成预制件不迟。
/// 做法和 DialogueUI 自建画布一致，保持一套风格。
///
/// 语言只能在这里改 —— 进游戏后不提供切换入口，所以玩法场景里不需要
/// 处理"切语言时正在播的对话怎么办"这类问题。
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("场景")]
    [Tooltip("点「开始游戏」加载哪个场景。必须加进 Build Settings")]
    [SerializeField] private string gameSceneName = "Test";

    [Header("外观")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private Color   background   = new Color(0.06f, 0.07f, 0.09f, 1f);
    [SerializeField] private Color   textColor    = new Color(0.92f, 0.92f, 0.90f);
    [SerializeField] private Color   accentColor  = new Color(0.45f, 0.72f, 0.85f);
    [SerializeField] private int     titleSize    = 72;
    [SerializeField] private int     buttonSize   = 32;

    private Font _font;
    private RectTransform _root;
    private GameObject _mainPanel;
    private GameObject _settingsPanel;
    private readonly List<Button> _langButtons = new();
    private readonly List<GameLanguage> _langOrder = new()
    {
        GameLanguage.ChineseSimplified,
        GameLanguage.English,
    };

    // ── 构建 ────────────────────────────────────────────────────

    private void Start()
    {
        _font = ResolveFont();
        BuildCanvas();
        BuildMainPanel();
        BuildSettingsPanel();
        ShowMain();

        // ⚠ 必须用具名方法订阅：lambda 每次求值都是新实例，
        //   用另一个 lambda 去 -= 根本退不掉，事件会一直挂着泄漏
        Localization.OnChanged += OnLanguageChanged;
    }

    private void OnDestroy() => Localization.OnChanged -= OnLanguageChanged;

    private void OnLanguageChanged(GameLanguage _) => RefreshLangButtons();

    private void BuildCanvas()
    {
        var go = new GameObject("MainMenuCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight  = 0.5f;

        _root = (RectTransform)go.transform;

        // 铺满的背景板
        var bg = NewUIObject("Background", _root);
        Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = background;

        // 点击要能被接收，必须有 EventSystem
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }
    }

    private void BuildMainPanel()
    {
        _mainPanel = NewUIObject("MainPanel", _root).gameObject;
        Stretch((RectTransform)_mainPanel.transform);

        MakeLabel(_mainPanel.transform, "menu.title", titleSize, new Vector2(0f, 240f), accentColor);

        MakeButton(_mainPanel.transform, "menu.start",    new Vector2(0f,  40f), StartGame);
        MakeButton(_mainPanel.transform, "menu.settings", new Vector2(0f, -40f), ShowSettings);
        MakeButton(_mainPanel.transform, "menu.quit",     new Vector2(0f, -120f), QuitGame);
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = NewUIObject("SettingsPanel", _root).gameObject;
        Stretch((RectTransform)_settingsPanel.transform);

        MakeLabel(_settingsPanel.transform, "settings.title", 56, new Vector2(0f, 240f), accentColor);
        MakeLabel(_settingsPanel.transform, "settings.language", 32, new Vector2(0f, 120f), textColor);

        // 语言按钮横排
        float spacing = 220f;
        float startX  = -(spacing * (_langOrder.Count - 1)) * 0.5f;
        for (int i = 0; i < _langOrder.Count; i++)
        {
            var lang = _langOrder[i];
            var btn = MakeButton(_settingsPanel.transform,
                                 lang == GameLanguage.English ? "lang.en" : "lang.zh",
                                 new Vector2(startX + spacing * i, 50f),
                                 () => SelectLanguage(lang),
                                 width: 200f);
            _langButtons.Add(btn);
        }

        MakeLabel(_settingsPanel.transform, "settings.language.hint", 22, new Vector2(0f, -30f),
                  new Color(textColor.r, textColor.g, textColor.b, 0.55f), width: 900f);

        MakeButton(_settingsPanel.transform, "menu.back", new Vector2(0f, -160f), ShowMain);

        RefreshLangButtons();
    }

    // ── 行为 ────────────────────────────────────────────────────

    private void ShowMain()
    {
        _mainPanel.SetActive(true);
        _settingsPanel.SetActive(false);
    }

    private void ShowSettings()
    {
        _mainPanel.SetActive(false);
        _settingsPanel.SetActive(true);
        RefreshLangButtons();
    }

    private void SelectLanguage(GameLanguage lang)
    {
        Localization.Set(lang);   // 内部会存 PlayerPrefs 并广播，LocalizedText 自己刷新
        RefreshLangButtons();
    }

    /// <summary>当前语言的按钮高亮，让玩家看得出选中了哪个。</summary>
    private void RefreshLangButtons()
    {
        for (int i = 0; i < _langButtons.Count && i < _langOrder.Count; i++)
        {
            if (_langButtons[i] == null) continue;
            bool selected = _langOrder[i] == Localization.Current;
            var img = _langButtons[i].GetComponent<Image>();
            if (img != null)
                img.color = selected ? accentColor : new Color(1f, 1f, 1f, 0.12f);

            var txt = _langButtons[i].GetComponentInChildren<Text>();
            if (txt != null)
                txt.color = selected ? new Color(0.06f, 0.07f, 0.09f) : textColor;
        }
    }

    private void StartGame()
    {
        if (Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        Debug.LogError($"[主菜单] 场景「{gameSceneName}」没有加进 Build Settings，无法加载。\n" +
                       "File > Build Settings 里把它加进 Scenes In Build。");
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── UI 构件 ─────────────────────────────────────────────────

    private static RectTransform NewUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private Text MakeLabel(Transform parent, string key, int size, Vector2 pos, Color color,
                           float width = 800f)
    {
        var rt = NewUIObject($"Label_{key}", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, size * 1.8f);

        var text = rt.gameObject.AddComponent<Text>();
        text.font      = _font;
        text.fontSize  = size;
        text.color     = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow   = VerticalWrapMode.Overflow;
        text.raycastTarget      = false;

        // LocalizedText 负责填内容并在切语言时刷新
        rt.gameObject.AddComponent<LocalizedText>().SetKey(key);
        return text;
    }

    private Button MakeButton(Transform parent, string key, Vector2 pos,
                              UnityEngine.Events.UnityAction onClick, float width = 320f)
    {
        var rt = NewUIObject($"Button_{key}", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, 64f);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.12f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var labelRT = NewUIObject("Text", rt);
        Stretch(labelRT);
        var text = labelRT.gameObject.AddComponent<Text>();
        text.font      = _font;
        text.fontSize  = buttonSize;
        text.color     = textColor;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        labelRT.gameObject.AddComponent<LocalizedText>().SetKey(key);

        return btn;
    }

    /// <summary>
    /// 复用对话系统那套字体解析：优先 DialogueFontSet 里按语言配的字体文件，
    /// 没有就退回系统字体。这样主菜单和游戏内字体一致，不会一边中文一边方块。
    /// </summary>
    private Font ResolveFont()
    {
        var set   = DialogueFontSet.LoadOrNull();
        var entry = set != null ? set.Resolve(Localization.Current)
                                : DialogueFontSet.BuiltinDefault(Localization.Current);

        if (entry?.font != null) return entry.font;

        if (entry?.osFontFallbacks != null)
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var n in entry.osFontFallbacks)
                if (!string.IsNullOrEmpty(n) && installed.Contains(n))
                {
                    var f = Font.CreateDynamicFontFromOSFont(n, 32);
                    if (f != null) return f;
                }
        }

        Debug.LogWarning("[主菜单] 没找到可用的中文字体，退回 Unity 内置字体（中文会显示成方块）");
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
