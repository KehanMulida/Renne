using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DialogueFontSet —— 按语言配置对话 UI 的字体。
///
/// 右键 Create &gt; Dialogue &gt; Font Set 创建，**必须放在 Resources/ 下、命名为
/// DialogueFontSet**，DialogueUI 会按这个名字加载。没有这个资产也能跑：
/// 自动回退到系统字体（和以前一样），只是出包会依赖玩家机器装了对应字体。
///
/// 为什么要按语言分：中日韩和拉丁字形的字面大小差很多，同一个字号
/// 中文看着比英文大一圈；而且一个字体文件通常只覆盖一两种文字，
/// 塞一个全语言字体会让包体暴涨。所以每种语言各配各的，字号再单独微调。
/// </summary>
[CreateAssetMenu(fileName = "DialogueFontSet", menuName = "Dialogue/Font Set")]
public class DialogueFontSet : ScriptableObject
{
    /// <summary>DialogueUI 按这个名字从 Resources 加载</summary>
    public const string ResourceName = "DialogueFontSet";

    [Serializable]
    public class Entry
    {
        [Tooltip("这条配置对应哪种语言")]
        public GameLanguage language = GameLanguage.ChineseSimplified;

        [Tooltip("字体文件。拖项目里的 .ttf / .otf 进来。\n" +
                 "留空则按下面的系统字体名去找——能跑但出包会依赖玩家机器装了该字体")]
        public Font font;

        [Tooltip("font 留空时，按顺序找系统里第一个装了的。\n" +
                 "⚠ 要写字体的英文名：Windows 上「微软雅黑」这类本地化名实测不在已安装列表里")]
        public string[] osFontFallbacks;

        [Tooltip("字号缩放。不同文字的字面大小差异很大——\n" +
                 "同样 24 号，中文看着比英文大一圈，用这个把各语言拉到观感一致")]
        [Range(0.5f, 2f)]
        public float sizeScale = 1f;
    }

    [Header("各语言的字体")]
    [Tooltip("按语言查找。找不到当前语言时用下面的兜底")]
    public List<Entry> entries = new List<Entry>();

    [Header("兜底（没有匹配语言时用）")]
    public Entry fallback = new Entry
    {
        language        = GameLanguage.English,
        osFontFallbacks = new[] { "Segoe UI", "Arial", "Helvetica" },
        sizeScale       = 1f,
    };

    /// <summary>取这个语言该用哪条配置。没配就返回 fallback。</summary>
    public Entry Resolve(GameLanguage language)
    {
        if (entries != null)
            foreach (var e in entries)
                if (e != null && e.language == language)
                    return e;
        return fallback;
    }

    /// <summary>
    /// 从 Resources 加载。没有资产返回 null —— 调用方要能接受这种情况，
    /// 退回纯系统字体，别因为少一个配置文件就不显示文字。
    /// </summary>
    public static DialogueFontSet LoadOrNull()
        => Resources.Load<DialogueFontSet>(ResourceName);

    /// <summary>没有配置资产时用的内置默认，保证开箱能显示中文。</summary>
    public static Entry BuiltinDefault(GameLanguage language)
    {
        // ⚠ 这里写的都是**英文字体名**：Windows 上「微软雅黑」「黑体」这类
        //   本地化名实测不在 Font.GetOSInstalledFontNames() 返回的列表里，填了也不命中
        string[] os;
        switch (language)
        {
            case GameLanguage.English:
                os = new[] { "Segoe UI", "Arial", "Helvetica" };
                break;
            default:    // ChineseSimplified
                os = new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Noto Sans SC", "PingFang SC" };
                break;
        }
        return new Entry { language = language, font = null, osFontFallbacks = os, sizeScale = 1f };
    }
}
