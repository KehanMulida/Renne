using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DialogueSpeaker — 挂在会说话的单位（敌人/NPC）身上，提供一个稳定的 speakerId。
///
/// 职责只有两件：登记自己、说话时朝向对话对象。
/// 发声由 DialogueManager 代劳——保证“发声”仍然只有 SoundEmitter.Emit 一条路径。
/// </summary>
[DisallowMultipleComponent]
public class DialogueSpeaker : MonoBehaviour
{
    [Header("身份")]
    [Tooltip("对话 JSON 里 lines[].speaker 引用的 id，如 E01。\n" +
             "留空则依次回退到 EnemyAIController.enemyId → 物体名。敌人一般留空即可")]
    [SerializeField] private string speakerId;

    [Tooltip("别人怎么称呼他（如 小刘、老王）。一般留空——名字统一写在 Resources/DialogueSpeakers.json 里，\n" +
             "这里填了会覆盖文件里的。\n" +
             "台词里写 {E02} 或 {B} 会替换成这个名字；玩家听到这个名字之前，他的飘字前缀显示「？」")]
    [SerializeField] private string displayName;

    [Header("表现")]
    [Tooltip("说话时转向对话的另一方")]
    [SerializeField] private bool turnToListener = true;

    [Tooltip("发声点相对本物体的偏移（大致嘴/头的高度）")]
    [SerializeField] private Vector3 mouthOffset = new Vector3(0f, 1.6f, 0f);

    /// <summary>
    /// 说话者 id。手填优先；留空时回退到 enemyId（与任务系统的单位 id 保持同一套口径，
    /// 省得每个敌人都要再填一遍），再不行才用物体名。
    /// </summary>
    public string SpeakerId
    {
        get
        {
            if (!string.IsNullOrEmpty(speakerId)) return speakerId;
            var ai = GetComponent<EnemyAIController>();
            if (ai != null && !string.IsNullOrEmpty(ai.EnemyId)) return ai.EnemyId;
            return gameObject.name;
        }
    }

    /// <summary>
    /// 别人口中他的名字：Inspector 填了用 Inspector 的，否则查名字表，都没有就退回 speakerId
    /// ——会直接出现在台词里，一眼就能发现漏配了。
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            string fromTable = SpeakerNames.Get(SpeakerId);
            return string.IsNullOrEmpty(fromTable) ? SpeakerId : fromTable;
        }
    }

    public bool HasDisplayName =>
        !string.IsNullOrEmpty(displayName) || !string.IsNullOrEmpty(SpeakerNames.Get(SpeakerId));

    public Vector3 MouthPosition => transform.position + mouthOffset;

    // 登记时用的 id。注销必须用同一个——enemyId 运行时若被改，重新算出来的就对不上了
    private string _registeredId;

    /// <summary>当前是否已被某段对话占用（防止同一个人同时参与两段对话）。</summary>
    public bool IsBusy { get; internal set; }

    // ── 全局登记表：DialogueManager 按 id 找人 ───────────────────────
    private static readonly Dictionary<string, DialogueSpeaker> _registry = new();

    // 关掉“进入 Play 时重载域”后静态字段会跨次 Play 残留，每次进 Play 前清空
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _registry.Clear();

    public static bool TryGet(string id, out DialogueSpeaker speaker)
        => _registry.TryGetValue(id, out speaker) && speaker != null;

    public static IEnumerable<DialogueSpeaker> All => _registry.Values;

    private void OnEnable()
    {
        string id = SpeakerId;
        if (_registry.TryGetValue(id, out var existing) && existing != null && existing != this)
        {
            Debug.LogWarning($"[对话] speakerId 重复：\"{id}\" 已被 {existing.name} 占用，" +
                             $"{name} 的登记会覆盖它。请给其中一个改名。", this);
        }
        _registry[id] = this;
        _registeredId = id;
    }

    private void OnDisable()
    {
        // 只有登记的确实是自己才移除，避免重名时把别人的登记删掉
        if (_registeredId != null && _registry.TryGetValue(_registeredId, out var cur) && cur == this)
            _registry.Remove(_registeredId);
        _registeredId = null;

        IsBusy = false;
    }

    /// <summary>说这一句时的表现：转向对方。台词与发声由 DialogueManager 负责。</summary>
    public void PlayLineVisual(DialogueSpeaker listener)
    {
        if (!turnToListener || listener == null || listener == this) return;

        Vector3 dir = listener.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}

/// <summary>
/// 名字表：Resources/DialogueSpeakers.json —— 谁叫什么，和台词一样在文件里改。
/// 格式：{ "speakers": [ { "id": "E02", "displayName": "小刘" }, ... ] }
/// 放在 Dialogues 目录外面，免得被当成一段对话去解析。
/// </summary>
public static class SpeakerNames
{
    [Serializable] private class Entry { public string id; public string displayName; }
    [Serializable] private class Table { public List<Entry> speakers; }

    private const string ResourcePath = "DialogueSpeakers";
    private static Dictionary<string, string> _table;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _table = null;     // 每次 Play 重新读，改了文件立刻生效

    /// <summary>这个 id 在名字表里叫什么；没有记录返回 null。</summary>
    public static string Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Load();
        return _table.TryGetValue(id, out var n) ? n : null;
    }

    private static void Load()
    {
        if (_table != null) return;
        _table = new Dictionary<string, string>();

        var asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null) return;

        try
        {
            var t = JsonUtility.FromJson<Table>(asset.text);
            if (t?.speakers == null) return;
            foreach (var e in t.speakers)
                if (e != null && !string.IsNullOrEmpty(e.id) && !string.IsNullOrEmpty(e.displayName))
                    _table[e.id] = e.displayName;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[对话] 解析 Resources/{ResourcePath}.json 失败：{ex.Message}（JsonUtility 不支持 // 注释）");
        }
    }
}
