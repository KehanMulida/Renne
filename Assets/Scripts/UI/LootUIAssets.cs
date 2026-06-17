using UnityEngine;

/// <summary>
/// LootUI 美术资源包（ScriptableObject）
/// 在 Project 窗口右键 → Create → SRPG/UI/LootUI Assets 创建。
/// 创建后在 LootUIAssetsWindow（菜单 SRPG → LootUI 美术配置）里上传贴图。
/// 将 asset 拖入 LootUI 组件的 Assets 字段后生效。
/// </summary>
[CreateAssetMenu(fileName = "LootUIAssets", menuName = "SRPG/UI/LootUI Assets", order = 20)]
public class LootUIAssets : ScriptableObject
{
    [Header("面板背景")]
    [Tooltip("主面板背景贴图（留空使用纯色）")]
    public Texture2D panelBackground;

    [Tooltip("顶部标题栏贴图")]
    public Texture2D headerBackground;

    [Tooltip("底部按钮栏贴图")]
    public Texture2D footerBackground;

    [Header("物品行")]
    [Tooltip("奇数行背景贴图（留空不绘制）")]
    public Texture2D rowOdd;

    [Tooltip("偶数行背景贴图（留空使用淡色蒙版）")]
    public Texture2D rowEven;

    [Tooltip("鼠标悬停行背景贴图")]
    public Texture2D rowHover;

    [Header("按钮")]
    [Tooltip("「取」按钮 Normal 状态贴图")]
    public Texture2D btnNormal;

    [Tooltip("「取」按钮 Hover 状态贴图")]
    public Texture2D btnHover;

    [Tooltip("「全部拿走」按钮贴图")]
    public Texture2D btnTakeAll;

    [Header("装饰")]
    [Tooltip("标题图标（显示在 enemy 名称左侧，可留空）")]
    public Sprite titleIcon;

    [Tooltip("物品默认图标（无 icon 时的占位符）")]
    public Sprite defaultItemIcon;
}
