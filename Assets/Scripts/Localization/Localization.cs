using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏支持的语言。
///
/// 刻意不用 Unity 的 SystemLanguage —— 那个枚举有几十个用不到的值，
/// 而且 Chinese / ChineseSimplified / ChineseTraditional 三个并存，
/// 配置时极易选错，运行时又对不上。只支持中英就用两个值说清楚。
///
/// ⚠ 数值会被存进 PlayerPrefs，**不要改已有项的数值**，新语言往后加。
/// </summary>
public enum GameLanguage
{
    ChineseSimplified = 0,
    English           = 1,
}

/// <summary>
/// Localization —— 全局文本本地化。
///
/// 两套数据，因为它们的形状不一样：
///   · UI 文本    → Resources/Localization/&lt;lang&gt;.json 的字符串表，按 key 取
///   · 对话台词   → Resources/Dialogues/&lt;lang&gt;/*.json，整份文件按语言放
///                 （台词的 text/blurred/keyword/hint 四件套必须一起翻才自洽，
///                   拆成孤立字符串会失去上下文、也防不住 flag 被改坏）
/// 两者共用这里的 Current。
///
/// 语言在主菜单的设置里改，**游戏内不切换**——所以读表只在加载时发生一次。
/// </summary>
public static class Localization
{
    public const string PrefsKey = "settings.language";

    /// <summary>字符串表的 Resources 路径前缀</summary>
    private const string TableFolder = "Localization";

    /// <summary>缺翻译时回退到这个语言。中文是基准语言（原文就是中文写的）</summary>
    public const GameLanguage BaseLanguage = GameLanguage.ChineseSimplified;

    private static bool _initialized;
    private static GameLanguage _current;
    private static Dictionary<string, string> _table;     // 当前语言
    private static Dictionary<string, string> _baseTable; // 基准语言，用于回退

    /// <summary>语言变了。主菜单里切语言时，界面上的文字要当场跟着变。</summary>
    public static event Action<GameLanguage> OnChanged;

    // ══════════════════════════════════════════════════════════════════
    // 当前语言
    // ══════════════════════════════════════════════════════════════════

    public static GameLanguage Current
    {
        get
        {
            if (!_initialized) Initialize();
            return _current;
        }
    }

    private static void Initialize()
    {
        _initialized = true;

        if (PlayerPrefs.HasKey(PrefsKey))
        {
            _current = (GameLanguage)PlayerPrefs.GetInt(PrefsKey, (int)BaseLanguage);
        }
        else
        {
            // 首次启动按系统语言猜一个：中文环境给中文，其余一律英文
            var sys = Application.systemLanguage;
            _current = (sys == SystemLanguage.Chinese ||
                        sys == SystemLanguage.ChineseSimplified ||
                        sys == SystemLanguage.ChineseTraditional)
                     ? GameLanguage.ChineseSimplified
                     : GameLanguage.English;
        }

        LoadTables();
    }

    /// <summary>
    /// 切换语言并存盘。设置界面调用。
    /// 游戏内不切语言，所以这个方法正常只会在主菜单里被调到。
    /// </summary>
    public static void Set(GameLanguage language)
    {
        if (!_initialized) Initialize();
        if (_current == language) return;

        _current = language;
        PlayerPrefs.SetInt(PrefsKey, (int)language);
        PlayerPrefs.Save();

        LoadTables();
        Debug.Log($"[本地化] 语言切换为 {language}");
        OnChanged?.Invoke(language);
    }

    // ══════════════════════════════════════════════════════════════════
    // 取文本
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 按 key 取本地化文本。
    ///
    /// 回退顺序：当前语言 → 基准语言（中文）→ **key 本身**。
    /// 最后一级刻意返回 key 而不是空串：界面上直接看到 "menu.start" 这种字样，
    /// 一眼就知道漏翻了；返回空串只会得到一片空白，很难发现。
    /// </summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (!_initialized) Initialize();

        if (_table != null && _table.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            return v;

        if (_baseTable != null && _baseTable.TryGetValue(key, out var b) && !string.IsNullOrEmpty(b))
            return b;

        return key;
    }

    /// <summary>取文本并做 string.Format 替换。</summary>
    public static string Format(string key, params object[] args)
    {
        var raw = Get(key);
        if (args == null || args.Length == 0) return raw;
        try   { return string.Format(raw, args); }
        catch { return raw; }   // 占位符数量对不上时不要炸，显示原文就行
    }

    /// <summary>这个 key 在当前语言里有没有真正的翻译（不含回退）。编辑器检查用。</summary>
    public static bool HasTranslation(string key)
    {
        if (!_initialized) Initialize();
        return _table != null && _table.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);
    }

    // ══════════════════════════════════════════════════════════════════
    // 字符串表
    // ══════════════════════════════════════════════════════════════════

    /// <summary>语言 → Resources 子路径 / 对话子目录名。</summary>
    public static string CodeOf(GameLanguage language)
    {
        switch (language)
        {
            case GameLanguage.English: return "en";
            default:                   return "zh";
        }
    }

    /// <summary>
    /// 对话文件的子目录。基准语言返回空串 —— 中文台词直接放在
    /// Resources/Dialogues/ 下（现有 46 个文件一个都不用动），
    /// 其他语言放 Resources/Dialogues/&lt;code&gt;/。
    /// </summary>
    public static string DialogueSubfolder(GameLanguage language)
        => language == BaseLanguage ? "" : CodeOf(language);

    private static void LoadTables()
    {
        _table = LoadTable(CodeOf(_current));
        _baseTable = _current == BaseLanguage ? _table : LoadTable(CodeOf(BaseLanguage));
    }

    private static Dictionary<string, string> LoadTable(string code)
    {
        var dict = new Dictionary<string, string>();
        var path = $"{TableFolder}/{code}";
        var asset = Resources.Load<TextAsset>(path);

        if (asset == null)
        {
            Debug.LogWarning($"[本地化] 找不到字符串表 Resources/{path}.json —— " +
                             $"该语言的 UI 文本会回退或显示成 key 本身");
            return dict;
        }

        try
        {
            var table = JsonUtility.FromJson<StringTable>(asset.text);
            if (table?.entries != null)
                foreach (var e in table.entries)
                    if (e != null && !string.IsNullOrEmpty(e.key))
                        dict[e.key] = e.value;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[本地化] 解析 {path}.json 失败：{ex.Message}" +
                           $"（注意 JsonUtility 不支持 // 注释）");
        }

        return dict;
    }

    [Serializable] private class Entry       { public string key; public string value; }
    [Serializable] private class StringTable { public List<Entry> entries; }
}
