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

    [Tooltip("转身速度（度/秒）。说话转向同伴、聊完转回原朝向都用它。0 = 瞬间转\n" +
             "移动中不转身——那时朝向归 UnitMovement 管，两边都写会打架抖动")]
    [Min(0f)] [SerializeField] private float turnSpeed = 360f;

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

    // 开聊前的朝向。对话期间会转向同伴，结束时要转回去
    private Quaternion _facingBeforeTalk;
    private bool       _facingSaved;
    private UnitMovement _movement;
    private bool         _movementResolved;

    // 想转到哪个朝向。有值 = Update 正在往那儿转；转到位或开始移动就清空
    private Quaternion _desiredFacing;
    private bool       _hasDesiredFacing;

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
        // 组件被禁用时丢掉暂存的朝向：重新启用后那个值早就过期了，
        // 留着会在下一段对话结束时把人转到一个莫名其妙的方向
        _facingSaved      = false;
        _hasDesiredFacing = false;
    }

    /// <summary>说这一句时的表现：转向对方。台词与发声由 DialogueManager 负责。</summary>
    public void PlayLineVisual(DialogueSpeaker listener)
    {
        if (!turnToListener || listener == null || listener == this) return;

        Vector3 dir = listener.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        // 第一次开口前记下原朝向，供 RestoreFacing() 还原。
        // 一段对话里会说好几句、每句都转一次，所以只在首句保存
        if (!_facingSaved)
        {
            _facingBeforeTalk = transform.rotation;
            _facingSaved      = true;
        }

        SetDesiredFacing(Quaternion.LookRotation(dir.normalized, Vector3.up));
    }

    /// <summary>
    /// 把朝向转回开聊之前。由 DialogueManager.FinishSession 调用（正常结束和中止都会走到）。
    ///
    /// 为什么必须还原：视觉感知是基于 transform.forward 的锥形
    /// （VisionPerception 用 Vector3.Angle(owner.forward, 目标方向) 判断是否在视野内）。
    /// 对话期间互相对视会把视野锥指向同伴 —— 那是有意的设计，对话制造盲区。
    /// 但聊完不还原的话，原地不动的敌人（守点任务、AP 耗尽、无事可做）
    /// 会永久盯着同伴站过的方向，盲区固定在那儿不走，等于白送一条潜行路线。
    ///
    /// 正在移动的敌人不处理：UnitMovement 每帧会把朝向 Slerp 到移动方向，
    /// 这里再写一次只会和它打架，交给它自己纠正就行。
    /// </summary>
    public void RestoreFacing()
    {
        if (!_facingSaved) return;
        _facingSaved = false;

        if (IsMoving) { _hasDesiredFacing = false; return; }
        SetDesiredFacing(_facingBeforeTalk);
    }

    private bool IsMoving
    {
        get
        {
            if (!_movementResolved)
            {
                _movement         = GetComponent<UnitMovement>();
                _movementResolved = true;
            }
            return _movement != null && _movement.IsMoving;
        }
    }

    private void SetDesiredFacing(Quaternion target)
    {
        // 移动中一律不转身：UnitMovement 每帧把朝向 Slerp 到移动方向，
        // 这里再写一次就是两头抢 transform.rotation —— 说话那帧 snap 向同伴、
        // 下一帧又被拉回去，表现为边走边说时朝向疯狂抖动
        if (IsMoving) { _hasDesiredFacing = false; return; }

        if (turnSpeed <= 0f)
        {
            transform.rotation = target;
            _hasDesiredFacing  = false;
            return;
        }

        _desiredFacing    = target;
        _hasDesiredFacing = true;
    }

    private void Update()
    {
        if (!_hasDesiredFacing) return;

        // 聊到一半开始走了 → 交还给 UnitMovement，别抢
        if (IsMoving) { _hasDesiredFacing = false; return; }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, _desiredFacing, turnSpeed * Time.deltaTime);

        if (Quaternion.Angle(transform.rotation, _desiredFacing) < 0.5f)
        {
            transform.rotation = _desiredFacing;
            _hasDesiredFacing  = false;
        }
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
