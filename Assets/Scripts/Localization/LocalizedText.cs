using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 挂在 UI.Text 上，按 key 自动填本地化文本，语言变了当场刷新。
///
/// 游戏内不切语言，所以这个组件实际只在主菜单起作用——
/// 但设置界面自己就得跟着变（在设置里把语言改成英文，界面还是中文会很怪），
/// 所以订阅语言事件是必要的，不是多余设计。
/// </summary>
[RequireComponent(typeof(Text))]
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("字符串表里的 key，对应 Resources/Localization/<lang>.json\n" +
             "查不到会原样显示 key —— 这是故意的，一眼就能看出漏翻")]
    [SerializeField] private string key;

    private Text _text;

    /// <summary>运行时换 key（比如同一个标签要显示不同内容）。</summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        Refresh();
    }

    private void Awake() => _text = GetComponent<Text>();

    private void OnEnable()
    {
        Localization.OnChanged += OnLanguageChanged;
        Refresh();
    }

    private void OnDisable() => Localization.OnChanged -= OnLanguageChanged;

    private void OnLanguageChanged(GameLanguage _) => Refresh();

    private void Refresh()
    {
        if (_text == null) _text = GetComponent<Text>();
        if (_text != null && !string.IsNullOrEmpty(key))
            _text.text = Localization.Get(key);
    }
}
