using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DialogueUI — 偷听飘字与内心独白（UGUI）。
///
/// **玩家看得到的所有固定文字都在本组件的 Inspector 里**：名字前缀的「？」、方位词、
/// “有人在低声交谈”、自动遮蔽的「……」、独白的括号，以及三档颜色和字体。
/// 台词内容本身在 Resources/Dialogues/*.json 里。
///
/// 两条**互不混用**的呈现通道：
///   · 台词飘字 —— 跟着说话者走，上浮淡出，按清晰度档位变样式。只显示玩家真的听到的内容。
///   · 内心独白 —— 屏幕下缘，无说话人名、括号包裹、颜色区分。
///     它是角色自己的念头，不是系统播报，所以绝不能长得像台词或任务提示。
///
/// 画布和字体都可以留空：会自动建一个 Overlay 画布、自动找系统里的中文字体。
/// 用 UGUI Text 而不用 TMP：项目里的 TMP 只带拉丁字形的 LiberationSans，中文会显示成方块。
/// 以后放进中文字体、做了 TMP 字体资源，再换 TMP 不迟。
///
/// 执行顺序排在最后：飘字位置要在相机移动之后算，否则会跟着相机抖一帧。
/// </summary>
[DefaultExecutionOrder(1000)]
public class DialogueUI : MonoBehaviour
{
    [Header("来源（留空则自动找玩家身上的）")]
    [SerializeField] private EavesdropListener listener;

    [Header("画布")]
    [Tooltip("留空则自动创建一个 Screen Space - Overlay 画布")]
    [SerializeField] private Canvas canvas;
    [Tooltip("自动创建画布时的层级。背包画布是 8，默认压在它下面——打开背包时飘字不会挡住背包")]
    [SerializeField] private int sortingOrder = 5;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

    [Header("字体")]
    [Tooltip("直接指定字体，优先级最高。\n" +
             "留空则走 Resources/DialogueFontSet 按当前语言查；再没有就用内置的系统字体回退")]
    [SerializeField] private Font font;

    [Tooltip("按语言配字体的资产（Resources/DialogueFontSet）。\n" +
             "留空会自动加载同名资产；资产也没有时退回内置默认，开箱仍能显示中文")]
    [SerializeField] private DialogueFontSet fontSet;

    [Tooltip("跟随 Localization.Current 自动换字体。\n" +
             "关掉则只在启动时取一次，之后切语言不影响已经显示的文字")]
    [SerializeField] private bool followLanguageChanges = true;

    // ══════════════════════════════════════════════════════════════════
    // 固定文字
    //
    // 这些曾经是 Inspector 里的字符串字段，现在搬进了本地化字符串表
    // （Resources/Localization/<lang>.json），因为它们是玩家可见文本、必须跟着语言走。
    // 改措辞去改 json，不用进 Unity；key 一律在下面这几个常量里，别散写字面量。
    // ══════════════════════════════════════════════════════════════════

    private const string KeyUnknownSpeaker = "dialogue.unknownSpeaker";  // 不知道名字时的「？」
    private const string KeyClearFormat    = "dialogue.clearFormat";     // {0}=名字 {1}=台词
    private const string KeyAutoBlur       = "dialogue.autoBlurFormat";  // {0}=关键词
    private const string KeyBlurNoKeyword  = "dialogue.blurNoKeyword";
    private const string KeyFaintFormat    = "dialogue.faintFormat";     // {0}=方位词
    private const string KeyThoughtFormat  = "dialogue.thoughtFormat";   // {0}=hint

    /// <summary>相对玩家朝向的 8 个方位，从正前方开始顺时针。索引对应 DirectionIndex 的返回值</summary>
    private static readonly string[] DirectionKeys =
    {
        "dir.front", "dir.frontRight", "dir.right", "dir.backRight",
        "dir.back",  "dir.backLeft",   "dir.left",  "dir.frontLeft",
    };
    private const string KeyNearWord = "dir.near";   // 声源几乎就在脚下

    [Header("档位颜色")]
    [SerializeField] private Color clearColor   = Color.white;
    [SerializeField] private Color blurredColor = new Color(0.78f, 0.78f, 0.72f);
    [SerializeField] private Color faintColor   = new Color(0.55f, 0.55f, 0.55f);
    [SerializeField] private Color floaterBackground = new Color(0f, 0f, 0f, 0.5f);

    [Header("飘字")]
    [SerializeField] private float floaterDuration = 4f;
    [Tooltip("锚点相对说话者嘴部的偏移（米）")]
    [SerializeField] private Vector3 floaterWorldOffset = new Vector3(0f, 0.6f, 0f);
    [Tooltip("整个生命周期内上浮多少（画布单位，按参考分辨率）")]
    [SerializeField] private float floaterRise = 60f;
    [SerializeField] private float floaterMaxWidth = 420f;
    [SerializeField] private int floaterFontSize = 24;
    [SerializeField] private Vector2 floaterPadding = new Vector2(12f, 6f);
    [Tooltip("极模糊档的飘字不锚在说话者身上，而是锚在玩家身边、朝声源偏这么多米。\n" +
             "察觉半径很大，锚在声源会把迷雾里敌人的精确位置暴露出来——极模糊只该给方位")]
    [SerializeField] private float faintAnchorOffset = 1.5f;

    [Header("内心独白")]
    [SerializeField] private float thoughtDuration = 5f;
    [SerializeField] private int thoughtFontSize = 28;
    [SerializeField] private Color thoughtColor = new Color(0.72f, 0.88f, 0.95f);
    [Tooltip("离屏幕底部多远（画布单位）。底部有快捷栏的话调大些，别被挡住")]
    [SerializeField] private float thoughtBottomMargin = 180f;
    [SerializeField] private float thoughtMaxWidth = 1000f;
    [SerializeField] private float thoughtSpacing = 8f;
    [Tooltip("同时最多显示几条，多了挤掉最早的")]
    [Min(1)] [SerializeField] private int thoughtMaxShown = 3;

    // ── 运行时 ──────────────────────────────────────────────────

    private Camera _cam;
    private RectTransform _canvasRT;
    private Font _font;

    // 当前语言的字号缩放。不同文字字面大小差很多，统一在这里乘
    private float _fontSizeScale = 1f;

    // 所有建出来的 Text（含在池子里待复用的）。切语言时要把它们全部换一遍字体，
    // 只换新建的会导致屏幕上已有的飘字还是旧字体，看起来像没生效
    private readonly List<Text> _allTexts = new();

    private class FloaterView
    {
        public RectTransform root;
        public CanvasGroup   group;
        public Image         background;
        public RectTransform textRT;
        public Text          text;
    }

    private class Floater
    {
        public HeardLine   entry;
        public string      key;           // 同 key 的新飘字顶掉旧的（同一个人连说几句时不叠在一起）
        public Vector3     world;         // 固定锚点；跟随目标丢失（比如人死了）时也退回这里
        public Transform   follow;        // 跟着说话者走——说话不占用行动，人是边说边移动的
        public Vector3     followOffset;
        public float       born, until;
        public string      shownName;     // 上次排版用的名字。名字一变（「？」→ 小刘）就重排
        public FloaterView view;

        public float   Life01 => Mathf.InverseLerp(born, until, Time.time);
        public Vector3 Anchor => follow != null ? follow.position + followOffset : world;
    }

    private class ThoughtView
    {
        public RectTransform root;
        public CanvasGroup   group;
        public Text          text;
    }

    private class Thought
    {
        public float       born, until;
        public float       height;        // 创建时排一次版就记下，别每帧重排（会每帧重建文字网格）
        public ThoughtView view;
        public float Life01 => Mathf.InverseLerp(born, until, Time.time);
    }

    private const string FaintKey = "__faint__";   // 极模糊飘字只保留最新一条

    private readonly List<Floater> _floaters = new();
    private readonly List<Thought> _thoughts = new();
    private readonly Stack<FloaterView> _floaterPool = new();
    private readonly Stack<ThoughtView> _thoughtPool = new();

    // ── 生命周期 ────────────────────────────────────────────────

    private void Awake()
    {
        _cam = Camera.main;
        ApplyLanguageFont(Localization.Current);
        EnsureCanvas();
    }

    private bool _bound;

    private void OnEnable()
    {
        BindListener(warnIfMissing: false);
        if (followLanguageChanges) Localization.OnChanged += ApplyLanguageFont;
    }

    // 兜底：自动装配时组件的添加顺序、或场景里的初始化顺序，都可能让 OnEnable 时玩家还没挂上偷听组件
    private void Start() => BindListener(warnIfMissing: true);

    private void OnDisable()
    {
        Localization.OnChanged -= ApplyLanguageFont;

        if (!_bound || listener == null) return;
        listener.OnLineHeard    -= HandleLineHeard;
        listener.OnInnerThought -= HandleInnerThought;
        _bound = false;
    }

    private void BindListener(bool warnIfMissing)
    {
        if (_bound) return;
        if (listener == null) listener = FindObjectOfType<EavesdropListener>();
        if (listener == null)
        {
            if (warnIfMissing)
                Debug.LogWarning("[对话UI] 场景里找不到 EavesdropListener，飘字不会显示。" +
                                 "玩家物体的 Tag 要是 Player，自动装配才能找到它", this);
            return;
        }

        listener.OnLineHeard    += HandleLineHeard;
        listener.OnInnerThought += HandleInnerThought;
        _bound = true;
    }

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        UpdateFloaters();
        UpdateThoughts();
    }

    /// <summary>
    /// 不依赖任何对话，直接在玩家头顶弹一条飘字、屏幕下方弹一条独白。
    /// 用来单独确认“UI 本身能不能显示”——能显示就说明问题出在对话没开或玩家没听到。
    /// 用法：Play 模式下，在本组件标题栏右键（或点右上角 ⋮）→「显示测试文字」。
    /// </summary>
    [ContextMenu("显示测试文字")]
    private void ShowTestText()
    {
        if (!Application.isPlaying || _canvasRT == null)
        {
            Debug.LogWarning("[对话UI] 请在 Play 模式下使用「显示测试文字」");
            return;
        }

        if (listener == null) listener = FindObjectOfType<EavesdropListener>();
        Transform anchor = listener != null ? listener.transform
                         : (_cam != null ? _cam.transform : transform);

        HandleLineHeard(new HeardLine
        {
            speakerId = "__test__",
            speaker   = anchor,
            position  = anchor.position + Vector3.up * 1.6f,
            tier      = ClarityTier.Clear,
            text      = "测试：飘字能正常显示"
        });
        HandleInnerThought("测试：内心独白能正常显示");

        Debug.Log($"[对话UI] 已弹出测试文字。字体：{(_font != null ? _font.name : "（无）")}，" +
                  $"画布：{canvas.name}（层级 {canvas.sortingOrder}）。" +
                  (listener == null ? "⚠ 场景里没有 EavesdropListener，真正的对话不会显示" : ""));
    }

    /// <summary>
    /// Play 模式下在组件标题栏右键用：轮换语言，当场看字体有没有换过去。
    /// 屏幕上已有的飘字也应该一起变——没变就说明 _allTexts 的登记漏了。
    /// </summary>
    [ContextMenu("切换语言（测试）")]
    private void CycleLanguageForTest()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[对话UI] 请在 Play 模式下使用「切换语言（测试）」");
            return;
        }

        Localization.Set(Localization.Current == GameLanguage.ChineseSimplified
            ? GameLanguage.English
            : GameLanguage.ChineseSimplified);

        Debug.Log($"[对话UI] 当前语言 {Localization.Current}，" +
                  $"字体 {(_font != null ? _font.name : "（无）")}，字号缩放 {_fontSizeScale:F2}");
    }

    // ── 事件 ────────────────────────────────────────────────────

    private void HandleLineHeard(HeardLine entry)
    {
        if (entry == null || _canvasRT == null) return;

        bool faint = entry.tier == ClarityTier.Faint;
        string key = faint ? FaintKey : (entry.speakerId ?? entry.tag);

        // 同一个人的新台词顶掉旧的：一回合内连说几句时，飘字不会叠成一团
        for (int i = _floaters.Count - 1; i >= 0; i--)
            if (_floaters[i].key == key) ReleaseFloater(i);

        var f = new Floater
        {
            entry = entry,
            key   = key,
            world = AnchorFor(entry),
            born  = Time.time,
            until = Time.time + floaterDuration,
            view  = RentFloaterView()
        };

        // 模糊及以上跟着说话者走；极模糊固定在玩家身边（只给方位，不能跟着暴露位置）
        if (!faint && entry.speaker != null)
        {
            f.follow       = entry.speaker;
            f.followOffset = entry.position - entry.speaker.position;
        }

        f.view.text.color         = TierColor(entry.tier);
        f.view.background.color   = floaterBackground;
        LayoutFloater(f);
        _floaters.Add(f);
    }

    private void HandleInnerThought(string hint)
    {
        if (string.IsNullOrEmpty(hint) || _canvasRT == null) return;

        while (_thoughts.Count >= thoughtMaxShown) ReleaseThought(0);   // 挤掉最早的

        var t = new Thought
        {
            born  = Time.time,
            until = Time.time + thoughtDuration,
            view  = RentThoughtView()
        };
        t.view.text.text  = Localization.Format(KeyThoughtFormat, hint);
        t.view.text.color = thoughtColor;
        t.height = LayoutText(t.view.text, t.view.root, thoughtMaxWidth).y;
        _thoughts.Add(t);
    }

    // ── 文字 ────────────────────────────────────────────────────

    /// <summary>按档位拼出这句要显示的文字。name 输出本次用的名字（只有清晰档才有）。</summary>
    private string Compose(HeardLine e, out string nameUsed)
    {
        nameUsed = null;
        switch (e.tier)
        {
            case ClarityTier.Clear:
            {
                nameUsed = listener != null ? listener.GetKnownName(e.speakerId) : null;
                string nm = string.IsNullOrEmpty(nameUsed) ? Localization.Get(KeyUnknownSpeaker) : nameUsed;
                if (string.IsNullOrEmpty(nm)) return e.text ?? "";
                return Localization.Format(KeyClearFormat, nm, e.text ?? "");
            }

            case ClarityTier.Blurred:
                if (!string.IsNullOrEmpty(e.blurred)) return e.blurred;
                if (!string.IsNullOrEmpty(e.keyword)) return Localization.Format(KeyAutoBlur, e.keyword);
                return Localization.Get(KeyBlurNoKeyword);

            default:
            {
                // 极模糊：只知道有人在低声交谈 + 方位。**不泄露任何内容**
                string dirKey = e.direction >= 0 && e.direction < DirectionKeys.Length
                    ? DirectionKeys[e.direction]
                    : KeyNearWord;
                return Localization.Format(KeyFaintFormat, Localization.Get(dirKey));
            }
        }
    }

    private Color TierColor(ClarityTier tier)
    {
        switch (tier)
        {
            case ClarityTier.Clear:   return clearColor;
            case ClarityTier.Blurred: return blurredColor;
            default:                  return faintColor;
        }
    }

    /// <summary>
    /// 飘字锚点。模糊及以上锚在声源（离得够近，知道是谁在说）；
    /// 极模糊锚在玩家身边、朝声源方向偏一点——只给方位，不暴露精确位置。
    /// </summary>
    private Vector3 AnchorFor(HeardLine entry)
    {
        bool concealed = entry.speaker != null && !PlayerSceneVisibility.CanReveal(entry.speaker);
        if ((!concealed && entry.tier != ClarityTier.Faint) || listener == null) return entry.position;

        Vector3 self = listener.transform.position;
        Vector3 to = entry.position - self;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return entry.position;

        float offset = Mathf.Min(faintAnchorOffset, to.magnitude);
        // 高度取玩家耳朵附近，别让它飘到声源那层楼的高度去
        return listener.EarPosition + to.normalized * offset;
    }

    // ── 每帧 ────────────────────────────────────────────────────

    private void UpdateFloaters()
    {
        float now = Time.time;
        for (int i = _floaters.Count - 1; i >= 0; i--)
            if (now > _floaters[i].until) ReleaseFloater(i);

        if (_cam == null || _canvasRT == null) return;
        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        foreach (var f in _floaters)
        {
            // 名字变了（刚听到有人叫他）就重排——「？」会当场变成名字
            if (f.entry.tier == ClarityTier.Clear && listener != null &&
                listener.GetKnownName(f.entry.speakerId) != f.shownName)
                LayoutFloater(f);

            bool concealed = f.entry.speaker != null && !PlayerSceneVisibility.CanReveal(f.entry.speaker);
            Vector3 sp = _cam.WorldToScreenPoint((concealed ? AnchorFor(f.entry) : f.Anchor) + floaterWorldOffset);
            if (sp.z < 0f) { f.view.group.alpha = 0f; continue; }   // 在摄像机背后

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, sp, uiCam, out Vector2 local);

            float t = f.Life01;
            local.y += floaterRise * t;                                // 上浮
            f.view.root.anchoredPosition = local - _canvasRT.rect.center;

            // 前 15% 淡入、后 35% 淡出，中间保持——避免刚出现就闪
            f.view.group.alpha = Mathf.Min(Mathf.InverseLerp(0f, 0.15f, t),
                                           Mathf.InverseLerp(1f, 0.65f, t));
        }
    }

    private void UpdateThoughts()
    {
        float now = Time.time;
        for (int i = _thoughts.Count - 1; i >= 0; i--)
            if (now > _thoughts[i].until) ReleaseThought(i);

        // 最新的在最下面，旧的往上排
        float y = thoughtBottomMargin;
        for (int i = _thoughts.Count - 1; i >= 0; i--)
        {
            var t = _thoughts[i];
            t.view.root.anchoredPosition = new Vector2(0f, y);
            y += t.height + thoughtSpacing;

            t.view.group.alpha = Mathf.Min(Mathf.InverseLerp(0f, 0.12f, t.Life01),
                                           Mathf.InverseLerp(1f, 0.7f, t.Life01));
        }
    }

    private void LayoutFloater(Floater f)
    {
        f.view.text.text = Compose(f.entry, out f.shownName);
        Vector2 size = LayoutText(f.view.text, f.view.textRT, floaterMaxWidth);
        f.view.root.sizeDelta = size + floaterPadding * 2f;
    }

    /// <summary>按内容定宽（不超过 maxWidth，超了就换行），把 rt 设成文字实际大小并返回。</summary>
    private static Vector2 LayoutText(Text text, RectTransform rt, float maxWidth)
    {
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        float w = Mathf.Min(text.preferredWidth, maxWidth);

        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        rt.sizeDelta = new Vector2(w, 0f);           // 先定宽，preferredHeight 按这个宽度换行来算
        var size = new Vector2(w, text.preferredHeight);
        rt.sizeDelta = size;
        return size;
    }

    // ── 视图创建与复用 ──────────────────────────────────────────

    private FloaterView RentFloaterView()
    {
        if (_floaterPool.Count > 0)
        {
            var v = _floaterPool.Pop();
            v.root.gameObject.SetActive(true);
            v.group.alpha = 0f;
            return v;
        }

        var go = new GameObject("DialogueFloater", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_canvasRT, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);                 // 底边中点对准锚点，文字长在头顶上方

        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var bg = go.GetComponent<Image>();
        bg.raycastTarget = false;                         // 绝不挡住点地移动

        var text = CreateText(rt, "Text", floaterFontSize, TextAnchor.MiddleCenter, FontStyle.Normal);
        var textRT = (RectTransform)text.transform;
        textRT.anchorMin = textRT.anchorMax = new Vector2(0.5f, 0.5f);
        textRT.pivot = new Vector2(0.5f, 0.5f);

        return new FloaterView { root = rt, group = group, background = bg, textRT = textRT, text = text };
    }

    private void ReleaseFloater(int index)
    {
        var f = _floaters[index];
        _floaters.RemoveAt(index);
        if (f.view == null) return;
        f.view.root.gameObject.SetActive(false);
        _floaterPool.Push(f.view);
    }

    private ThoughtView RentThoughtView()
    {
        if (_thoughtPool.Count > 0)
        {
            var v = _thoughtPool.Pop();
            v.root.gameObject.SetActive(true);
            v.group.alpha = 0f;
            return v;
        }

        var go = new GameObject("DialogueThought", typeof(RectTransform), typeof(CanvasGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_canvasRT, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);   // 屏幕底边中点
        rt.pivot = new Vector2(0.5f, 0f);

        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        // 独白直接用文字本身占位，不另套背景：它是念头，不该长得像对话框
        var text = go.AddComponent<Text>();
        SetupText(text, thoughtFontSize, TextAnchor.MiddleCenter, FontStyle.Italic);
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        return new ThoughtView { root = rt, group = group, text = text };
    }

    private void ReleaseThought(int index)
    {
        var t = _thoughts[index];
        _thoughts.RemoveAt(index);
        if (t.view == null) return;
        t.view.root.gameObject.SetActive(false);
        _thoughtPool.Push(t.view);
    }

    private Text CreateText(RectTransform parent, string name, int size, TextAnchor align, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        SetupText(text, size, align, style);
        return text;
    }

    private void SetupText(Text text, int size, TextAnchor align, FontStyle style)
    {
        // 登记进来，切语言时才能把已经建出来的文字一起换掉
        if (!_allTexts.Contains(text)) _allTexts.Add(text);

        text.font               = _font;
        text.fontSize           = ScaledSize(size);
        text.fontStyle          = style;
        text.alignment          = align;
        text.supportRichText    = false;               // 台词里的 < > 不该被当成标签
        text.raycastTarget      = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow   = VerticalWrapMode.Overflow;
        text.lineSpacing        = 1.1f;
    }

    // ── 画布与字体 ──────────────────────────────────────────────

    private void EnsureCanvas()
    {
        if (canvas == null)
        {
            // 不加 GraphicRaycaster：这张画布纯展示，永远不该吃掉鼠标点击
            var go = new GameObject("DialogueCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);

            canvas = go.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight  = 0.5f;
        }

        _canvasRT = (RectTransform)canvas.transform;
    }

    /// <summary>
    /// 按语言取字体并应用到所有已建出来的文字（含池子里待复用的）。
    /// 切语言时当场生效，不用等下一条飘字。
    /// </summary>
    private void ApplyLanguageFont(GameLanguage language)
    {
        var entry = ResolveEntry(language);
        _font          = ResolveFont(entry);
        _fontSizeScale = entry != null ? Mathf.Max(0.1f, entry.sizeScale) : 1f;

        // 已经在屏幕上的、以及池子里待复用的，全部换掉。
        // 字号要重算：各语言的 sizeScale 不同
        foreach (var t in _allTexts)
        {
            if (t == null) continue;
            t.font = _font;
            // 独白字号比飘字大，用父物体名区分（两类视图建出来时名字不同）
            bool isThought = t.gameObject.name == "DialogueThought";
            t.fontSize = ScaledSize(isThought ? thoughtFontSize : floaterFontSize);
        }

        // 换了字体字形宽高都变了，屏幕上现有的飘字要重排，否则背景框对不上文字
        foreach (var f in _floaters) LayoutFloater(f);
        foreach (var th in _thoughts)
            if (th.view != null)
                th.height = LayoutText(th.view.text, th.view.root, thoughtMaxWidth).y;
    }

    private int ScaledSize(int baseSize) => Mathf.Max(1, Mathf.RoundToInt(baseSize * _fontSizeScale));

    private DialogueFontSet.Entry ResolveEntry(GameLanguage language)
    {
        if (fontSet == null) fontSet = DialogueFontSet.LoadOrNull();
        return fontSet != null ? fontSet.Resolve(language)
                               : DialogueFontSet.BuiltinDefault(language);
    }

    /// <summary>
    /// 字体优先级：Inspector 直接指定 &gt; 语言配置里的字体文件 &gt; 该语言的系统字体 &gt; Unity 内置。
    /// 只有最后一级会显示成方块（内置字体没有中日韩字形），所以到那一步要明确报错。
    /// </summary>
    private Font ResolveFont(DialogueFontSet.Entry entry)
    {
        if (font != null) return font;                 // Inspector 指定的最优先
        if (entry != null && entry.font != null) return entry.font;

        var names = entry?.osFontFallbacks;
        if (names != null && names.Length > 0)
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n) && installed.Contains(n))
                {
                    var f = Font.CreateDynamicFontFromOSFont(n, 32);
                    if (f != null) return f;
                }

            Debug.LogWarning(
                $"[对话UI] 语言 {entry.language} 既没配字体文件，系统里也找不到 " +
                $"[{string.Join(", ", names)}] 中的任何一个。\n" +
                "退回 Unity 内置字体——它只有拉丁字形，中日韩会显示成方块。\n" +
                "修法：把字体文件放进项目，拖到 DialogueUI 的 font 栏，" +
                "或配进 Resources/DialogueFontSet 对应语言那一条。", this);
        }

        var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (builtin == null)
            Debug.LogError("[对话UI] 连 Unity 内置字体都没取到——文字会显示不出来。请在 DialogueUI 的 font 栏手动拖一个字体", this);
        return builtin;
    }
}
