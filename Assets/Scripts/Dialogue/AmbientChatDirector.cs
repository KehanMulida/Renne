using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AmbientChatDirector — 让敌人自己开口。**不需要任何按键或外部调用。**
///
/// 每个敌人回合开始时，扫一遍所有 trigger = ambient 的对话，找出“现在就能聊起来”的：
///   · 角色位是真实单位 id（"speakers": ["E01","E02"]）——只能由这几个人聊，
///     他们得凑在一起、都不警觉。情报对话就这么写：E01 和 E02 碰头时才会说起换班的事
///   · 角色位是 A / B 这种抽象位——从附近空闲的人里随便凑一桌。闲聊就这么写
///   · 也可以混着写（["E02","A"]）：E02 和他身边随便哪个人聊
/// 然后按 weight 抽一段开播。
///
/// ⚠ **说话不占用回合内的行动。**
/// 本类只负责“让他们开口”，从不碰 AP、不写黑板、不干预行为树——
/// 敌人该巡逻还是巡逻，嘴上聊着不影响手上做什么。
///
/// 有人进入警觉（看到玩家 / 正在查动静）时，他参与的闲聊立刻中止——警觉的人不会站着扯淡。
/// </summary>
public class AmbientChatDirector : MonoBehaviour
{
    [Header("评估时机")]
    [Tooltip("在哪个阵营的回合开始时找人开聊。默认敌人回合")]
    [SerializeField] private TurnFaction evaluateOnFaction = TurnFaction.Enemy;

    [Header("频率")]
    [Tooltip("每次评估时开聊的概率。1 = 只要有人凑得齐就一定开聊")]
    [Range(0f, 1f)] [SerializeField] private float chancePerTurn = 1f;

    [Tooltip("两次开聊之间至少间隔几个回合（全局）。0 = 每回合都可以开新的")]
    [Min(0)] [SerializeField] private int globalCooldownTurns = 0;

    [Tooltip("同时最多几段对话在进行（不同的几拨人各聊各的、各自嘀咕）。\n" +
             "设成敌人数量，就是每个人每回合都可能开口")]
    [Min(1)] [SerializeField] private int maxConcurrent = 4;

    [Header("配对约束")]
    [Tooltip("要求参与者在同一楼层")]
    [SerializeField] private bool requireSameFloor = true;

    [Header("中止")]
    [Tooltip("每隔几秒检查一次参与者是否进入警觉。\n" +
             "对话是在回合内按秒连说的，只在回合开始时查的话，被发现后还会接着聊完")]
    [Min(0.05f)] [SerializeField] private float alertCheckInterval = 0.25f;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    private int   _lastStartTurn = int.MinValue;
    private bool  _subscribed;
    private float _nextAlertCheck;

    // 本导演开起来的对话，用于“有人警觉就掐掉”
    private class Running
    {
        public string id;
        public ChatState state;
        public List<DialogueSpeaker> cast;
    }
    private readonly List<Running> _running = new();

    // 一个“现在就能开播”的候选：哪段对话 + 由谁来演
    private struct Candidate
    {
        public DialogueData data;
        public List<DialogueSpeaker> cast;
    }

    // 复用的临时容器，避免每回合分配
    private readonly List<DialogueSpeaker> _available = new();   // 活着、不忙、不在战斗
    private readonly List<DialogueSpeaker> _idle = new();        // 其中空闲的
    private readonly List<DialogueSpeaker> _alerted = new();     // 其中正在查动静的
    private readonly List<string> _inCombat = new();             // 调试用
    private readonly List<Candidate>       _candidates = new();
    private readonly List<DialogueSpeaker> _near = new();

    // ── 生命周期 ────────────────────────────────────────────────

    private void OnEnable() => Subscribe();
    private void Start()    => Subscribe();      // 兜底，防初始化顺序漏订阅
    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        if (TurnSystem.Instance == null || DialogueManager.Instance == null) return;

        TurnSystem.Instance.OnTurnStart             += HandleTurnStart;
        DialogueManager.Instance.OnDialogueFinished += HandleFinished;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (TurnSystem.Instance != null) TurnSystem.Instance.OnTurnStart -= HandleTurnStart;
        if (DialogueManager.Instance != null) DialogueManager.Instance.OnDialogueFinished -= HandleFinished;
        _subscribed = false;
    }

    private void Update()
    {
        if (_running.Count == 0 || Time.time < _nextAlertCheck) return;
        _nextAlertCheck = Time.time + alertCheckInterval;
        AbortIfAlerted();
    }

    private void HandleFinished(DialogueData data)
    {
        if (data == null) return;
        _running.RemoveAll(r => r.id == data.id);
    }

    private void HandleTurnStart(TurnData turn)
    {
        if (turn == null || turn.currentFaction != evaluateOnFaction) return;

        AbortIfAlerted();
        TryStartChats(turn.globalTurnNumber);
    }

    // ── 中止 ────────────────────────────────────────────────────

    /// <summary>
    /// 参与者状态变了就掐掉：有人阵亡或进入战斗 → 任何对话都停；
    /// idle 对话里有人开始查动静 → 停（警觉的人不会站着扯淡）。
    /// alerted 对话本来就是查动静时说的，不因为警觉而停。
    /// </summary>
    private void AbortIfAlerted()
    {
        for (int i = _running.Count - 1; i >= 0; i--)
        {
            var r = _running[i];
            string why = null;

            foreach (var sp in r.cast)
            {
                if (sp == null) { why = "参与者已不在场"; break; }
                var ai = sp.GetComponent<EnemyAIController>();
                if (ai == null) continue;
                if (!ai.IsAlive)   { why = $"{sp.SpeakerId} 阵亡"; break; }
                if (ai.IsInCombat) { why = $"{sp.SpeakerId} 进入战斗"; break; }
                if (r.state == ChatState.Idle && ai.IsAlerted) { why = $"{sp.SpeakerId} 听到动静"; break; }
            }

            if (why == null) continue;

            DebugLog($"「{r.id}」中止：{why}");
            // 先移出列表再停：Stop 会同步触发 OnDialogueFinished → HandleFinished 按 id 再删一遍，
            // 顺序反过来的话这里的 RemoveAt(i) 会删错一条，或在末尾越界
            _running.RemoveAt(i);
            DialogueManager.Instance?.Stop(r.id);
        }
    }

    // ── 开聊 ────────────────────────────────────────────────────

    private void TryStartChats(int turn)
    {
        var mgr = DialogueManager.Instance;
        if (mgr == null) return;

        // 可能一回合开好几拨（maxConcurrent > 1）：每开一段就重新收集空闲的人
        bool startedAny = false;
        string reason = null;
        while (_running.Count < maxConcurrent)
        {
            if (turn - _lastStartTurn < globalCooldownTurns) { reason ??= $"全局冷却中（globalCooldownTurns={globalCooldownTurns}）"; break; }
            if (Random.value > chancePerTurn) { reason ??= $"这回合没掷中（chancePerTurn={chancePerTurn:F2}）"; break; }
            if (!TryStartOne(mgr, turn, out reason)) break;
            startedAny = true;
        }

        // 一段都没开成时说明原因——“怎么没人说话”基本都能从这一行看出来
        if (!startedAny && _running.Count == 0 && reason != null)
            DebugLog($"本回合没人开聊：{reason}");
    }

    private bool TryStartOne(DialogueManager mgr, int turn, out string reason)
    {
        reason = null;
        CollectSpeakers();
        if (_available.Count < 1)
        {
            reason = "场上没有能开口的人（都在别的对话里，或都在战斗）" + (_inCombat.Count > 0 ? $"——战斗中：{string.Join("、", _inCombat)}" : "");
            return false;
        }

        // 候选 = 能播（静默检查冷却/条件）且凑得齐人的对话
        _candidates.Clear();
        int playable = 0;
        foreach (var d in mgr.AllDialogues())
        {
            if (d == null || d.TriggerKind != DialogueTrigger.Ambient || d.weight <= 0f) continue;
            if (!mgr.CanPlay(d)) continue;
            playable++;
            if (TryFindCast(d, out var cast)) _candidates.Add(new Candidate { data = d, cast = cast });
        }
        if (_candidates.Count == 0)
        {
            reason = playable == 0
                ? "没有能播的对话（trigger 不是 ambient，或都在冷却 / 条件不满足）"
                : $"有 {playable} 段对话能播，但凑不齐人——{DescribeCrowd()}";
            return false;
        }

        var pick = _candidates[PickWeighted(_candidates)];
        if (!mgr.PlayWithCast(pick.data.id, pick.cast)) return false;

        _lastStartTurn = turn;
        _running.Add(new Running { id = pick.data.id, state = pick.data.StateKind, cast = pick.cast });

        DebugLog($"开聊「{pick.data.id}」{(string.IsNullOrEmpty(pick.data.topic) ? "" : $"[{pick.data.topic}] ")}" +
                 $"参与者：{string.Join("、", pick.cast.ConvertAll(s => s.SpeakerId))}");
        return true;
    }

    /// <summary>收集能开口的人：活着、没在参与别的对话、不在战斗。再按空闲 / 查动静分开。</summary>
    private void CollectSpeakers()
    {
        _available.Clear();
        _idle.Clear();
        _alerted.Clear();
        _inCombat.Clear();

        foreach (var sp in DialogueSpeaker.All)
        {
            if (sp == null || !sp.isActiveAndEnabled || sp.IsBusy) continue;

            var ai = sp.GetComponent<EnemyAIController>();
            if (ai != null && !ai.IsAlive) continue;
            if (ai != null && ai.IsInCombat) { _inCombat.Add(sp.SpeakerId); continue; }   // 战斗中一律不聊

            _available.Add(sp);
            if (ai != null && ai.IsAlerted) _alerted.Add(sp);
            else                            _idle.Add(sp);
        }
    }

    private List<DialogueSpeaker> PoolFor(DialogueData data) => data.StateKind switch
    {
        ChatState.Alerted => _alerted,
        ChatState.Any     => _available,
        _                 => _idle
    };

    /// <summary>
    /// 给这段对话凑一桌人。返回的 cast 与 DialogueManager.GetRoles 的顺序一一对应。
    ///   · 角色位是场景里真实存在的单位 id → 必须就是那个人（固定角色）
    ///   · 其余角色位（A/B）→ 从固定角色身边、或随机发起者身边的空闲者里补
    /// </summary>
    private bool TryFindCast(DialogueData data, out List<DialogueSpeaker> cast)
    {
        cast = null;
        var roles = DialogueManager.GetRoles(data);
        if (roles.Count == 0) return false;

        var pool = PoolFor(data);
        if (pool.Count == 0) return false;
        float maxDist = Mathf.Max(0.1f, data.maxSpeakerDistance);

        // ① 固定角色：找得到这个 id 的人，就必须是他
        var slots = new DialogueSpeaker[roles.Count];
        DialogueSpeaker anchor = null;
        for (int i = 0; i < roles.Count; i++)
        {
            if (!DialogueSpeaker.TryGet(roles[i], out var sp)) continue;   // 抽象角色位，稍后补
            if (!pool.Contains(sp)) return false;                         // 这个人不在场/在忙/警觉
            if (anchor != null && !CloseEnough(anchor, sp, maxDist)) return false;
            slots[i] = sp;
            anchor ??= sp;
        }

        // ② 抽象角色：从发起者身边补人。没有固定角色时，随机换几个发起者试试
        int attempts = anchor != null ? 1 : Mathf.Min(pool.Count, 6);
        for (int a = 0; a < attempts; a++)
        {
            var center = anchor ?? pool[Random.Range(0, pool.Count)];

            _near.Clear();
            foreach (var other in pool)
            {
                if (other == null || other == center || System.Array.IndexOf(slots, other) >= 0) continue;
                if (CloseEnough(center, other, maxDist)) _near.Add(other);
            }

            var trial = (DialogueSpeaker[])slots.Clone();
            bool centerUsed = System.Array.IndexOf(trial, center) >= 0;
            int nearIdx = 0;
            bool ok = true;

            for (int i = 0; i < trial.Length && ok; i++)
            {
                if (trial[i] != null) continue;
                if (!centerUsed) { trial[i] = center; centerUsed = true; continue; }
                if (nearIdx < _near.Count) trial[i] = _near[nearIdx++];
                else ok = false;
            }

            if (!ok) continue;
            cast = new List<DialogueSpeaker>(trial);
            return true;
        }

        return false;
    }

    /// <summary>调试用：现在场上的人为什么凑不成一桌。</summary>
    private string DescribeCrowd()
    {
        var alerted = _alerted.ConvertAll(s => s.SpeakerId);
        string head = $"空闲 {_idle.Count} 人" +
                      (alerted.Count   > 0 ? $"，查动静中：{string.Join("、", alerted)}" : "") +
                      (_inCombat.Count > 0 ? $"，战斗中：{string.Join("、", _inCombat)}" : "");
        if (_idle.Count < 2) return head;

        // 最近的一对，以及是不是同层
        float best = float.MaxValue; DialogueSpeaker a = null, b = null;
        for (int i = 0; i < _idle.Count; i++)
            for (int j = i + 1; j < _idle.Count; j++)
            {
                float d = Vector3.Distance(_idle[i].transform.position, _idle[j].transform.position);
                if (d < best) { best = d; a = _idle[i]; b = _idle[j]; }
            }

        bool sameFloor = FloorOf(a) == FloorOf(b);
        return $"{head}；离得最近的是 {a.SpeakerId}—{b.SpeakerId}，相距 {best:F1} 米" +
               (sameFloor ? "" : "、不同楼层") + "（要求同层且在对话的 maxSpeakerDistance 以内）";
    }

    private bool CloseEnough(DialogueSpeaker a, DialogueSpeaker b, float maxDist)
    {
        if (Vector3.Distance(a.transform.position, b.transform.position) > maxDist) return false;
        return !requireSameFloor || FloorOf(a) == FloorOf(b);
    }

    private static int FloorOf(DialogueSpeaker sp)
        => FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorFromWorldY(sp.transform.position.y)
            : 0;

    private static int PickWeighted(List<Candidate> list)
    {
        float total = 0f;
        foreach (var c in list) total += Mathf.Max(0f, c.data.weight);
        if (total <= 0f) return 0;

        float roll = Random.value * total;
        for (int i = 0; i < list.Count; i++)
        {
            roll -= Mathf.Max(0f, list[i].data.weight);
            if (roll <= 0f) return i;
        }
        return list.Count - 1;
    }

    private void DebugLog(string msg)
    {
        if (enableDebugLog) Debug.Log($"[闲聊] {msg}");
    }
}
