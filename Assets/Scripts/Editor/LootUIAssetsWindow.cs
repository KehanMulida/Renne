#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// LootUI 美术资源上传窗口
/// 菜单：SRPG → LootUI 美术配置
/// 位置：Assets/Scripts/Editor/LootUIAssetsWindow.cs
///
/// 使用方式：
///   1. 菜单 SRPG → LootUI 美术配置 打开窗口
///   2. 点击「新建资源包」或拖入已有 LootUIAssets asset
///   3. 上传各部位贴图（支持拖拽 / 选择文件）
///   4. 点击「应用到场景」把资源包推送给场景中的 LootUI 组件
/// </summary>
public class LootUIAssetsWindow : EditorWindow
{
    private LootUIAssets target;
    private SerializedObject so;
    private Vector2 scroll;

    // 分组折叠状态
    private bool foldPanel  = true;
    private bool foldRows   = true;
    private bool foldBtn    = true;
    private bool foldDeco   = true;

    [MenuItem("SRPG/LootUI 美术配置")]
    public static void Open() => GetWindow<LootUIAssetsWindow>("LootUI 美术配置");

    private void OnGUI()
    {
        DrawToolbar();

        if (target == null)
        {
            DrawEmpty();
            return;
        }

        so ??= new SerializedObject(target);
        so.Update();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawSection("面板背景", ref foldPanel, new[]
        {
            "panelBackground", "headerBackground", "footerBackground"
        });
        DrawSection("物品行", ref foldRows, new[]
        {
            "rowOdd", "rowEven", "rowHover"
        });
        DrawSection("按钮", ref foldBtn, new[]
        {
            "btnNormal", "btnHover", "btnTakeAll"
        });
        DrawSection("装饰", ref foldDeco, new[]
        {
            "titleIcon", "defaultItemIcon"
        });
        EditorGUILayout.EndScrollView();

        so.ApplyModifiedProperties();

        EditorGUILayout.Space(6);
        DrawApplyButton();
    }

    // ── 工具栏 ─────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("资源包", EditorStyles.boldLabel, GUILayout.Width(60));

        var picked = (LootUIAssets)EditorGUILayout.ObjectField(
            target, typeof(LootUIAssets), false, GUILayout.ExpandWidth(true));

        if (picked != target)
        {
            target = picked;
            so     = null;
        }

        if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(44)))
            CreateNew();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);
    }

    // ── 空状态提示 ─────────────────────────────────────────────────────

    private void DrawEmpty()
    {
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginVertical();
        GUILayout.Label("选择或新建一个 LootUIAssets 资源包", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("新建资源包", GUILayout.Width(120), GUILayout.Height(28)))
            CreateNew();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
    }

    // ── 分组区块 ───────────────────────────────────────────────────────

    private void DrawSection(string title, ref bool foldout, string[] propNames)
    {
        foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, title);
        if (foldout)
        {
            EditorGUI.indentLevel++;
            foreach (var name in propNames)
            {
                var prop = so.FindProperty(name);
                if (prop != null)
                    DrawAssetField(prop);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ── 单个资源字段（带预览缩略图）──────────────────────────────────

    private void DrawAssetField(SerializedProperty prop)
    {
        EditorGUILayout.BeginHorizontal();

        // 左侧：属性字段（支持拖拽）
        EditorGUILayout.PropertyField(prop, GUILayout.ExpandWidth(true));

        // 右侧：缩略图预览（仅 Texture2D / Sprite）
        Object obj = prop.objectReferenceValue;
        Texture2D preview = null;

        if (obj is Texture2D tex)
            preview = tex;
        else if (obj is Sprite spr)
            preview = spr.texture;

        if (preview != null)
        {
            Rect r = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
            EditorGUI.DrawPreviewTexture(r, preview, null, ScaleMode.ScaleToFit);
        }
        else
        {
            GUILayout.Space(40);
        }

        // 清空按钮
        if (obj != null && GUILayout.Button("✕", GUILayout.Width(20), GUILayout.Height(20)))
            prop.objectReferenceValue = null;

        EditorGUILayout.EndHorizontal();
    }

    // ── 应用到场景 ─────────────────────────────────────────────────────

    private void DrawApplyButton()
    {
        bool hasScene = FindObjectOfType<LootUI>() != null;

        EditorGUI.BeginDisabledGroup(!hasScene);
        if (GUILayout.Button("应用到场景中的 LootUI", GUILayout.Height(30)))
            ApplyToScene();
        EditorGUI.EndDisabledGroup();

        if (!hasScene)
            EditorGUILayout.HelpBox("场景中未找到 LootUI 组件，请先运行游戏或手动挂载 LootUI。", MessageType.Info);
    }

    private void ApplyToScene()
    {
        var lootUI = FindObjectOfType<LootUI>();
        if (lootUI == null) return;

        var soLoot = new SerializedObject(lootUI);
        var assetsProp = soLoot.FindProperty("assets");
        if (assetsProp != null)
        {
            assetsProp.objectReferenceValue = target;
            soLoot.ApplyModifiedProperties();
            EditorUtility.SetDirty(lootUI);
            Debug.Log($"[LootUIAssetsWindow] 已将 {target.name} 应用到 {lootUI.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("[LootUIAssetsWindow] LootUI 组件没有 'assets' 字段，请确认 LootUI.cs 已添加该字段。");
        }
    }

    // ── 新建资源包 ─────────────────────────────────────────────────────

    private void CreateNew()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "新建 LootUIAssets", "LootUIAssets", "asset",
            "选择保存位置", "Assets/Data/UI");

        if (string.IsNullOrEmpty(path)) return;

        var asset = CreateInstance<LootUIAssets>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        target = asset;
        so     = null;
        EditorGUIUtility.PingObject(asset);
    }
}
#endif
