using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DialogueManager — 对话调度中枢。
///
/// 职责：
///   1. 从 Resources/Dialogues/&lt;key&gt;.json 加载并缓存对话数据
///   2. 判定触发条件（复用任务系统的 ConditionEvaluator）、冷却、参与者占用
///   3. 按 mode 推进：turn = 每个指定阵营回合说一句；realtime = 按秒说一句
///   4. 每说一句，通过 SoundEmitter.Emit 发出一条带 tag 的声音
///
/// 关键设计：**本类不判断玩家能不能听见**。
/// 它只负责“把话说出去”，可听性完全交给现成的声音/听觉链路
/// （SoundManager 广播 → 玩家身上的 SoundPerception 过滤 → EavesdropListener 结算）。
/// 这样距离/楼层规则只有一份实现，不会在对话系统里重写一遍。
/// </summary>
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("资源")]
    [Tooltip("对话 JSON 所在的 Resources 子目录。往这个目录里放 .json 就自动生效，不用拖进来")]
    [SerializeField] private string resourceFolder = "Dialogues";
    [Tooltip("最远可察觉距离倍率，默认 1。只扩展远处模糊声音，不扩大清晰台词范围。")]
    [Min(1f)] [SerializeField] private float audibleRangeMultiplier = 1f;

    [Tooltip("【只读预览】上面目录里扫到的全部对话文件，编辑器里自动同步，双击可以打开。\n" +
             "不用手动往这里拖——运行时始终以目录为准，这里只是让你看得到")]
    [SerializeField] private List<TextAsset> dialogueFiles = new();

    [Header("回合推进")]
    [Tooltip("turn 模式下，在哪个阵营的回合开始时推进一句。\n" +
             "默认敌人回合——敌人在自己回合聊天，给玩家一个可预期的偷听窗口")]
    [SerializeField] private TurnFaction advanceOnFaction = TurnFaction.Enemy;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    /// <summary>每说出一句时触发（说话者、数据、行号、声音标签）。UI / 日志用。</summary>
    public event Action<DialogueSpeaker, DialogueData, int, string> OnLineSpoken;

    /// <summary>一段对话播完时触发。</summary>
    public event Action<DialogueData> OnDialogueFinished;

    // ── 运行时状态 ──────────────────────────────────────────────
    // ⚠ 键必须带语言：同一个 key 在中英下是两份不同的数据。
    //   只按 key 缓存的话，中文模式加载过的对话在切到英文后仍会命中旧缓存，
    //   表现为"切了语言但部分台词还是中文"，且只影响切换前播过的那几段，
    //   看起来就像随机时中时英。
    private readonly Dictionary<(string key, GameLanguage lang), DialogueData> _cache = new();
    private readonly Dictionary<string, int> _lastPlayedTurn = new();
    private readonly List<Session> _active = new();

    // 排队中的对话：现在播不了（人被占着），等空出来再播。
    // 按优先级降序，每回合开始推一次
    private readonly List<PendingDialogue> _pending = new();
    private readonly List<DialogueData> _all = new();
    private bool _allLoaded;

    // 每个声音标签最近一次“按演员表解析好”的台词（谁说的、{B} 换成了谁的名字）。
    // 发声是同步广播的，偷听方听到的那一刻，这里记的必然就是刚说出口的那一版。
    private readonly Dictionary<string, ResolvedLine> _resolvedByTag = new();

    // {X} 占位符：X 是角色位（A/B）或单位 id（E02）
    private static readonly System.Text.RegularExpressions.Regex Placeholder =
        new System.Text.RegularExpressions.Regex(@"\{([^{}\s]+)\}");

    // 已经提醒过“没填 displayName”的人，免得每句都刷一遍
    private readonly HashSet<DialogueSpeaker> _warnedNoName = new();
    private bool _subscribed;

    private class Session
    {
        public DialogueData data;
        public int nextLine;
        public float nextLineAt;                  // 下一句最早什么时候说
        public int burstRemaining;                // turn 模式：本回合还能说几句（0 = 等下个回合）
        public List<DialogueSpeaker> participants = new();

        // 角色位 → 实际说话者。
        // 任务演出里 lines[].speaker 直接写 E01（角色位名 == 单位 id）；
        // 闲聊是随机配对，写的是 A / B 这样的角色位，由导演在开播时绑定。
        public Dictionary<string, DialogueSpeaker> cast = new();
    }

    // ── 生命周期 ────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Start()
    {
        // 兜底：Start 时 TurnSystem.Awake 必定跑完，防初始化顺序漏订阅（与 SoundPerception 同样的处理）
        Subscribe();
        AllDialogues();     // 开场就加载，JSON 写坏了立刻报出来，而不是等到第一个敌人回合
        SelfCheck();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 编辑器里同步预览列表。延迟一帧：OnValidate 期间不宜加载资源
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            var found = new List<TextAsset>(Resources.LoadAll<TextAsset>(resourceFolder));
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (SameAssets(found, dialogueFiles)) return;
            dialogueFiles = found;
            UnityEditor.EditorUtility.SetDirty(this);
        };
    }

    private static bool SameAssets(List<TextAsset> a, List<TextAsset> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }
#endif

    /// <summary>
    /// 开场自检：一次性列出这套系统缺了什么。没显示文字时先看这一段日志。
    /// </summary>
    private void SelfCheck()
    {
        var sb = new System.Text.StringBuilder("[对话自检]\n");
        bool ok = true;

        void Line(bool good, string msg)
        {
            sb.Append(good ? "  ✔ " : "  ✘ ").Append(msg).Append('\n');
            if (!good) ok = false;
        }

        Line(_all.Count > 0, _all.Count > 0
            ? $"对话 {_all.Count} 段：{string.Join("、", _all.ConvertAll(d => $"{d.id}({(d.TriggerKind == DialogueTrigger.Ambient ? "自动" : "手动")})"))}"
            : $"Resources/{resourceFolder}/ 下没有能用的对话 JSON");

        var speakers = new List<string>();
        foreach (var sp in DialogueSpeaker.All)
            if (sp != null) speakers.Add(sp.HasDisplayName ? $"{sp.SpeakerId}「{sp.DisplayName}」" : sp.SpeakerId);
        Line(speakers.Count >= 2, speakers.Count >= 2
            ? $"说话者 {speakers.Count} 人：{string.Join("、", speakers)}"
            : $"说话者只有 {speakers.Count} 人——每个敌人都要挂 DialogueSpeaker，至少两人才聊得起来");

        Line(FindObjectOfType<AmbientChatDirector>() != null,
             "AmbientChatDirector" + (FindObjectOfType<AmbientChatDirector>() != null ? "" : " 缺失——没有它，对话不会自动开"));

        var ear = FindObjectOfType<EavesdropListener>();
        Line(ear != null, ear != null
            ? $"EavesdropListener 在「{ear.name}」上"
            : "EavesdropListener 缺失——要挂在玩家身上，否则玩家听不到");
        if (ear != null)
            Line(ear.GetComponent<SoundPerception>() != null,
                 ear.GetComponent<SoundPerception>() != null
                     ? "玩家的 SoundPerception 在"
                     : $"「{ear.name}」上没有 SoundPerception——偷听的听觉入口缺失");

        Line(FindObjectOfType<DialogueUI>() != null,
             "DialogueUI" + (FindObjectOfType<DialogueUI>() != null ? "" : " 缺失——没有它就不会显示任何文字"));

        Line(StoryManager.Instance != null,
             "StoryManager" + (StoryManager.Instance != null ? "" : " 缺失——旗标落不下去，带条件的对话永远不会播"));

        Line(TurnSystem.Instance != null,
             "TurnSystem" + (TurnSystem.Instance != null ? "" : " 缺失——没有敌人回合，对话永远不会开口"));

        if (ok) sb.Append("  全部就绪。对话由敌人自己发起：同层、离得够近（默认 5 米）的空闲敌人会在敌人回合开聊");
        if (ok) Debug.Log(sb.ToString(), this);
        else    Debug.LogWarning(sb.ToString(), this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Subscribe()
    {
        if (_subscribed || TurnSystem.Instance == null) return;
        TurnSystem.Instance.OnTurnStart += HandleTurnStart;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (TurnSystem.Instance != null) TurnSystem.Instance.OnTurnStart -= HandleTurnStart;
        _subscribed = false;
    }

    private void Update()
    {
        // 两种模式都在这里按秒出句：
        //   realtime —— 一路说到完
        //   turn     —— 只在本回合的额度内说（额度由 HandleTurnStart 发放），用完就停到下个回合
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var s = _active[i];
            if (Time.time < s.nextLineAt) continue;

            if (!s.data.IsRealtime)
            {
                if (s.burstRemaining <= 0) continue;
                s.burstRemaining--;
            }

            // 说完最后一句就立刻收场，别让参与者白白占着“忙”到下个回合
            if (!SpeakNext(s) || s.nextLine >= s.data.lines.Count)
            {
                FinishSession(i);
                continue;
            }
            s.nextLineAt = Time.time + Mathf.Max(0.1f, s.data.secondsPerLine);
        }
    }

    private void HandleTurnStart(TurnData turn)
    {
        if (turn == null || turn.currentFaction != advanceOnFaction) return;

        // 先推队列再发额度：排队中的高优先级对话可能抢占掉正在进行的闲聊，
        // 顺序反过来的话会先给即将被掐掉的会话白发一轮额度
        PumpPending();

        foreach (var s in _active)
            if (!s.data.IsRealtime) StartBurst(s);
    }

    /// <summary>发放本回合的发言额度。上一回合的连说若还没说完，沿用原节奏，不立刻插话。</summary>
    private static void StartBurst(Session s)
    {
        if (s.burstRemaining <= 0) s.nextLineAt = Time.time;
        s.burstRemaining = s.data.linesPerTurn <= 0 ? int.MaxValue : s.data.linesPerTurn;
    }

    // ── 公开接口 ────────────────────────────────────────────────

    /// <summary>
    /// 播放一段对话。填的是 GameManager / MissionData 里预留的那个“对话键值”。
    /// </summary>
    /// <returns>是否真的开播（条件不满足/冷却中/参与者忙 都会返回 false）</returns>
    public bool Play(string key) => PlayInternal(key, null);

    /// <summary>
    /// 指定演员表播放。台词里的 speaker 当作**角色位**（如 A/B），按首次出现顺序
    /// 绑定到 cast 里的人。闲聊导演用它把随机凑到一起的两个敌人塞进同一段台词。
    /// </summary>
    public bool PlayWithCast(string key, IList<DialogueSpeaker> cast) => PlayInternal(key, cast);

    private bool PlayInternal(string key, IList<DialogueSpeaker> cast)
    {
        var data = Load(key);
        if (data == null) return false;
        if (!CheckPlayable(data, log: true)) return false;

        int turn = CurrentTurn;
        var session = new Session { data = data, nextLine = 0 };

        // ── 抢占 ────────────────────────────────────────────────────
        // 场上就几个敌人，闲聊占用率实测 72.5%，任务/剧情对话十有八九
        // 撞上有人在聊。不抢占的话 ResolveCast 直接判定不开播，演出静默消失。
        // 所以：本段优先级更高时，把挡路的低优先级对话掐掉再试一次。
        if (!ResolveCast(data, session, cast))
        {
            if (!PreemptBlockers(data, cast))
                return false;

            session = new Session { data = data, nextLine = 0 };
            if (!ResolveCast(data, session, cast))
                return false;
        }

        foreach (var p in session.participants) p.IsBusy = true;

        _active.Add(session);
        _lastPlayedTurn[data.id] = turn;
        DebugLog($"开始播放「{data.id}」（{data.mode}，{data.lines.Count} 句）");

        // 什么时候开口：
        //   realtime —— 立刻（下一帧由 Update 说第一句）
        //   turn     —— 如果现在正是说话阵营的回合（闲聊导演就是在敌人回合开始时开聊的），立刻开说；
        //              否则等到他们的回合。玩家回合里手动触发时，这给了玩家先走近的机会
        if (data.IsRealtime)
        {
            session.nextLineAt = Time.time;
        }
        else if (TurnSystem.Instance != null && TurnSystem.Instance.CurrentFaction == advanceOnFaction)
        {
            StartBurst(session);
        }

        return true;
    }

    /// <summary>
    /// 取这段对话最后开口的人。
    /// 走散中止时用它决定谁说那句「人呢？」——刚说完话却没人应的那个，由他发问最自然。
    ///
    /// ⚠ 必须在 Stop() 之前调用：Stop 会把会话移出 _active，之后就查不到了。
    /// </summary>
    public bool TryGetLastSpeaker(string dialogueId, out DialogueSpeaker speaker)
    {
        speaker = null;
        for (int i = 0; i < _active.Count; i++)
        {
            var s = _active[i];
            if (s.data == null || s.data.id != dialogueId) continue;

            // nextLine 指向"下一句要说的"，所以刚说完的是它前面那句
            int idx = s.nextLine - 1;
            if (s.data.lines == null || idx < 0 || idx >= s.data.lines.Count) return false;

            string role = s.data.lines[idx].speaker;
            if (string.IsNullOrEmpty(role)) return false;
            return s.cast.TryGetValue(role, out speaker) && speaker != null;
        }
        return false;
    }

    // ══════════════════════════════════════════════════════════════════
    // 抢占 / 排队 / 打断
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 把挡住这段对话的、优先级更低的会话掐掉。
    /// 只要有一个挡路的优先级 ≥ 本段，就整体放弃——不能把半段高优先级演出打散。
    /// </summary>
    /// <returns>是否真的腾出了人</returns>
    private bool PreemptBlockers(DialogueData data, IList<DialogueSpeaker> cast)
    {
        // 先确定这段需要哪些人
        var needed = new List<DialogueSpeaker>();
        if (cast != null)
        {
            foreach (var sp in cast) if (sp != null) needed.Add(sp);
        }
        else
        {
            foreach (var role in GetRoles(data))
                if (DialogueSpeaker.TryGet(role, out var sp) && sp != null)
                    needed.Add(sp);
        }
        if (needed.Count == 0) return false;

        // 找出占着这些人的会话
        var blockers = new List<Session>();
        foreach (var s in _active)
            foreach (var p in s.participants)
                if (needed.Contains(p) && !blockers.Contains(s))
                    blockers.Add(s);

        if (blockers.Count == 0) return false;

        foreach (var b in blockers)
        {
            if (b.data.priority >= data.priority)
            {
                DebugLog($"「{data.id}」(P{data.priority}) 想抢占，但「{b.data.id}」" +
                         $"(P{b.data.priority}) 优先级不低于它，放弃");
                return false;
            }
        }

        foreach (var b in blockers)
        {
            DebugLog($"「{data.id}」(P{data.priority}) 抢占「{b.data.id}」(P{b.data.priority})");
            int idx = _active.IndexOf(b);
            if (idx >= 0) FinishSession(idx);
        }
        return true;
    }

    /// <summary>
    /// 排队：现在播不了就挂起，等人空出来再播。
    ///
    /// 和 Play 的区别是「播不了」的处理：Play 直接返回 false 丢弃，
    /// 这个会留在队列里重试，直到成功或超时。任务/剧情演出应该用这个。
    /// </summary>
    /// <param name="expireTurns">最多等几个回合。过时的剧情对话突然冒出来比不播更糟</param>
    public bool Enqueue(string key, int expireTurns = 3, string reason = "")
    {
        if (string.IsNullOrEmpty(key)) return false;

        var data = Load(key);
        if (data == null) return false;

        // 能立刻播就别排队
        if (PlayInternal(key, null)) return true;

        if (_pending.Exists(p => p.key == key))
        {
            DebugLog($"「{key}」已在队列里，不重复入队");
            return false;
        }

        _pending.Add(new PendingDialogue
        {
            key          = key,
            priority     = data.priority,
            cast         = null,
            enqueuedTurn = CurrentTurn,
            expireTurns  = Mathf.Max(0, expireTurns),
            reason       = reason,
        });
        // 高优先级排前面
        _pending.Sort((a, b) => b.priority.CompareTo(a.priority));

        DebugLog($"「{key}」(P{data.priority}) 现在播不了，已入队" +
                 $"（最多等 {expireTurns} 回合）{(string.IsNullOrEmpty(reason) ? "" : $"：{reason}")}");
        return false;
    }

    /// <summary>
    /// 打断所有优先级低于 threshold 的对话。
    /// 遭遇袭击、进入战斗这类事件调用它，先清场再插自己的台词。
    /// </summary>
    /// <returns>掐掉了几段</returns>
    public int InterruptBelow(int threshold, string reason)
    {
        int n = 0;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].data.priority >= threshold) continue;
            DebugLog($"「{_active[i].data.id}」被打断：{reason}");
            FinishSession(i);
            n++;
        }

        // 队列里低于阈值的也一并清掉——事件已经改变了情境，
        // 那些排着队的闲聊再播出来就不合时宜了
        int dropped = _pending.RemoveAll(p => p.priority < threshold);
        if (dropped > 0) DebugLog($"队列里清掉 {dropped} 段低优先级对话：{reason}");

        return n;
    }

    /// <summary>
    /// 队列里是否有优先级 ≥ threshold 的在等。
    /// 闲聊导演用它避让：有剧情等着开口时，别再开新的闲聊去占人。
    /// </summary>
    public bool HasPendingAtLeast(int threshold)
    {
        foreach (var p in _pending)
            if (p.priority >= threshold) return true;
        return false;
    }

    /// <summary>每回合推一次队列：能播的播掉，过期的丢掉。</summary>
    private void PumpPending()
    {
        if (_pending.Count == 0) return;

        int turn = CurrentTurn;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];

            if (p.IsExpired(turn))
            {
                _pending.RemoveAt(i);
                Debug.LogWarning($"[对话] 「{p.key}」(P{p.priority}) 等了 {p.expireTurns} 回合仍无法播放，放弃。" +
                                 $"{(string.IsNullOrEmpty(p.reason) ? "" : $"原因：{p.reason}。")}" +
                                 "多半是指定的说话人一直忙或不在场。");
                continue;
            }

            if (PlayInternal(p.key, p.cast))
            {
                DebugLog($"「{p.key}」从队列里出队并开播");
                _pending.RemoveAt(i);
            }
        }
    }

    /// <summary>这段对话当前是否在播。</summary>
    public bool IsPlaying(string dialogueId)
    {
        for (int i = 0; i < _active.Count; i++)
            if (_active[i].data.id == dialogueId) return true;
        return false;
    }

    /// <summary>中止一段正在播的对话。</summary>
    public void Stop(string dialogueId)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
            if (_active[i].data.id == dialogueId) FinishSession(i);
    }

    /// <summary>
    /// 按声音标签取回台词。玩家侧 EavesdropListener 用它把“听到的一声”还原成具体哪一句。
    /// </summary>
    public bool TryGetLine(string tag, out DialogueData data, out DialogueLine line, out int lineIndex)
    {
        data = null; line = null; lineIndex = -1;

        if (!DialogueData.ParseTag(tag, out string id, out lineIndex)) return false;
        data = Load(id);
        if (data == null || data.lines == null) return false;
        if (lineIndex < 0 || lineIndex >= data.lines.Count) return false;

        line = data.lines[lineIndex];
        return line != null;
    }

    /// <summary>
    /// 加载目录下的**全部**对话（只扫一次，之后走缓存）。
    /// 闲聊导演用它抽签——往 Resources/Dialogues/ 丢一个新 JSON 就自动进池子，不用改代码。
    /// </summary>
    public IReadOnlyList<DialogueData> AllDialogues()
    {
        if (_allLoaded) return _all;

        _allLoaded = true;
        var assets = Resources.LoadAll<TextAsset>(resourceFolder);
        foreach (var a in assets)
        {
            if (a == null) continue;
            var d = Load(a.name);
            if (d != null) _all.Add(d);
        }

        dialogueFiles = new List<TextAsset>(assets);     // 运行时也同步一下预览
        return _all;
    }

    /// <summary>
    /// 取回刚说出口的那一版台词：已按演员表替换好名字占位符，并标明实际说话者、
    /// 关键词是谁的名字。偷听方用它结算。
    /// </summary>
    public bool TryGetResolved(string tag, out ResolvedLine resolved)
        => _resolvedByTag.TryGetValue(tag ?? "", out resolved) && resolved != null;

    /// <summary>加载并缓存一段对话（找不到返回 null）。</summary>
    /// <summary>拼 Resources 路径：&lt;folder&gt;/&lt;lang&gt;/&lt;key&gt;，空的段自动略过。</summary>
    private static string BuildPath(string folder, string subfolder, string key)
    {
        if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(subfolder))
            return $"{folder}/{subfolder}/{key}";
        if (!string.IsNullOrEmpty(folder))    return $"{folder}/{key}";
        if (!string.IsNullOrEmpty(subfolder)) return $"{subfolder}/{key}";
        return key;
    }

    public DialogueData Load(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var cacheKey = (key, Localization.Current);
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        // ── 按语言查找，缺翻译时回退到基准语言 ──────────────────────────
        // 中文（基准语言）直接放 Resources/Dialogues/ 下，其他语言放子目录，
        // 例如 Resources/Dialogues/en/guard_shift_talk.json。
        // 回退是刻意的：翻译可以增量做，没翻的那几段照常用中文播，
        // 而不是整段消失——对话断掉比语言不一致严重得多。
        string baseFolder = resourceFolder ?? "";
        string subfolder  = Localization.DialogueSubfolder(Localization.Current);

        string path  = BuildPath(baseFolder, subfolder, key);
        var    asset = Resources.Load<TextAsset>(path);

        if (asset == null && !string.IsNullOrEmpty(subfolder))
        {
            string fallbackPath = BuildPath(baseFolder, "", key);
            asset = Resources.Load<TextAsset>(fallbackPath);
            if (asset != null)
                DebugLog($"「{key}」没有 {subfolder} 翻译，回退到基准语言");
            path = fallbackPath;
        }

        if (asset == null)
        {
            Debug.LogWarning($"[对话] 找不到资源 Resources/{path}.json");
            _cache[cacheKey] = null;      // 记下缺失，避免每次触发都走一遍 Resources.Load
            return null;
        }

        DialogueData data;
        try
        {
            data = JsonUtility.FromJson<DialogueData>(asset.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"[对话] 解析 {path}.json 失败：{e.Message}（注意 JsonUtility 不支持 // 注释）");
            _cache[cacheKey] = null;
            return null;
        }

        if (data == null || data.lines == null || data.lines.Count == 0)
        {
            Debug.LogWarning($"[对话] {path}.json 解析结果为空或没有 lines");
            _cache[cacheKey] = null;
            return null;
        }

        if (string.IsNullOrEmpty(data.id)) data.id = key;
        if (data.id != key)
        {
            // 用 key 作为索引口径，否则 tag 反查会对不上
            Debug.LogWarning($"[对话] {path}.json 里的 id（{data.id}）与文件名（{key}）不一致，以文件名为准");
            data.id = key;
        }

        data.audibleRadius = Mathf.Max(data.keywordRadius, data.audibleRadius * Mathf.Max(1f,audibleRangeMultiplier));
        _cache[cacheKey] = data;
        return data;
    }

    // ── 内部 ────────────────────────────────────────────────────

    private static int CurrentTurn => TurnSystem.Instance != null ? TurnSystem.Instance.GlobalTurnNumber : 0;

    /// <summary>
    /// 静默检查：这段对话现在能不能播（在播中 / 冷却 / 不可重复 / 条件）。不播、不打日志。
    /// 闲聊导演每回合用它筛候选——否则“条件不满足”会每回合刷一遍 Console。
    /// </summary>
    public bool CanPlay(DialogueData data) => data != null && CheckPlayable(data, log: false);

    private bool CheckPlayable(DialogueData data, bool log)
    {
        if (IsPlaying(data.id))
        {
            if (log) DebugLog($"「{data.id}」已在播放中，忽略本次触发");
            return false;
        }

        int turn = CurrentTurn;
        if (_lastPlayedTurn.TryGetValue(data.id, out int last))
        {
            if (!data.repeatable)
            {
                if (log) DebugLog($"「{data.id}」不可重复，已播过");
                return false;
            }
            if (turn - last < data.cooldownTurns)
            {
                if (log) DebugLog($"「{data.id}」冷却中（还差 {data.cooldownTurns - (turn - last)} 回合）");
                return false;
            }
        }

        if (!ConditionsMet(data))
        {
            if (log) DebugLog($"「{data.id}」触发条件不满足");
            return false;
        }

        return true;
    }

    // 条件只转换一次：写错的枚举名只告警一次，别每回合刷
    private readonly Dictionary<string, List<MissionCondition>> _conditionCache = new();

    private bool ConditionsMet(DialogueData data)
    {
        if (data.conditions == null || data.conditions.Count == 0) return true;

        if (!_conditionCache.TryGetValue(data.id, out var converted))
        {
            converted = new List<MissionCondition>(data.conditions.Count);
            foreach (var dc in data.conditions)
            {
                if (dc == null) continue;
                var mc = dc.ToMissionCondition(data.id);
                if (mc == null) { converted = null; break; }   // 枚举名写错 → 永远视为不满足，已告警
                converted.Add(mc);
            }
            _conditionCache[data.id] = converted;
        }

        if (converted == null) return false;
        return converted.Count == 0 || ConditionEvaluator.EvaluateAll(converted);
    }

    /// <summary>台词里出现的角色位，按首次出现顺序。speakers 填了就以它为准。</summary>
    public static List<string> GetRoles(DialogueData data)
    {
        var roles = new List<string>();
        if (data.speakers != null && data.speakers.Count > 0)
        {
            foreach (var s in data.speakers)
                if (!string.IsNullOrEmpty(s) && !roles.Contains(s)) roles.Add(s);
            return roles;
        }

        foreach (var l in data.lines)
            if (l != null && !string.IsNullOrEmpty(l.speaker) && !roles.Contains(l.speaker))
                roles.Add(l.speaker);
        return roles;
    }

    /// <summary>
    /// 绑定角色位 → 实际说话者。
    /// cast 为 null 时按“角色位名就是单位 id”去场景登记表里找（任务演出的用法）；
    /// 给了 cast 就按顺序绑（闲聊的用法）。有人不在场或正忙都不开播。
    /// </summary>
    private bool ResolveCast(DialogueData data, Session session, IList<DialogueSpeaker> cast)
    {
        var roles = GetRoles(data);
        if (roles.Count == 0) return false;

        if (cast != null && cast.Count < roles.Count)
        {
            DebugLog($"「{data.id}」需要 {roles.Count} 个说话者，只给了 {cast.Count} 个，不开播");
            return false;
        }

        for (int i = 0; i < roles.Count; i++)
        {
            DialogueSpeaker sp;
            if (cast != null)
            {
                sp = cast[i];
                if (sp == null)
                {
                    DebugLog($"「{data.id}」演员表第 {i} 位是空的，不开播");
                    return false;
                }
            }
            else if (!DialogueSpeaker.TryGet(roles[i], out sp))
            {
                DebugLog($"「{data.id}」缺少说话者 {roles[i]}（场景里没有对应的 DialogueSpeaker），不开播");
                return false;
            }

            if (sp.IsBusy)
            {
                DebugLog($"「{data.id}」的 {sp.SpeakerId} 正在参与别的对话，不开播");
                return false;
            }
            if (session.participants.Contains(sp))
            {
                DebugLog($"「{data.id}」同一个人被派了两个角色位，不开播");
                return false;
            }

            session.cast[roles[i]] = sp;
            session.participants.Add(sp);
        }

        return true;
    }

    /// <summary>说下一句。返回 false 表示这段对话已说完。</summary>
    private bool SpeakNext(Session s)
    {
        var data = s.data;
        if (s.nextLine >= data.lines.Count) return false;

        var line = data.lines[s.nextLine];
        int index = s.nextLine;
        s.nextLine++;

        if (line == null || string.IsNullOrEmpty(line.text)) return s.nextLine < data.lines.Count;

        // 角色位 → 实际说话者。闲聊里 line.speaker 是 A/B，任务演出里就是 E01
        if (!s.cast.TryGetValue(line.speaker, out var speaker) || speaker == null)
        {
            Debug.LogWarning($"[对话:{data.id}] 第 {index} 句的角色位 {line.speaker} 没有绑定到人，跳过这句");
            return true;
        }

        // 表现：转向对话的另一方
        speaker.PlayLineVisual(FindOther(s, speaker));

        // 发声：走统一入口，带上 tag 让偷听方能认出是哪一句。
        // alertsOthers 的对话用 Alert 发（会惊动同伴），普通闲聊用 Voice（敌人之间互不干扰）
        string tag = data.MakeTag(index);
        _resolvedByTag[tag] = Resolve(s, line, index, speaker);   // 必须在 Emit 之前：偷听方在广播中同步反查
        SoundEmitter.Emit(
            speaker.MouthPosition,
            data.alertsOthers ? SoundType.Alert : SoundType.Voice,
            data.audibleRadius,
            speaker.gameObject,
            1f,
            tag);

        DebugLog($"「{data.id}」#{index} {speaker.SpeakerId}（{line.speaker}）：{_resolvedByTag[tag].text}" +
                 $"　｜ {DescribeForPlayer(speaker.MouthPosition, data)}");
        OnLineSpoken?.Invoke(speaker, data, index, tag);

        return true;
    }

    private EavesdropListener _playerEar;

    /// <summary>调试用：这句话玩家大概听不听得到、为什么。没看到飘字时对照这一段。</summary>
    private string DescribeForPlayer(Vector3 mouth, DialogueData data)
    {
        if (_playerEar == null) _playerEar = FindObjectOfType<EavesdropListener>();
        if (_playerEar == null) return "场景里没有 EavesdropListener，玩家听不到";

        var fm = FloorManager.Instance;
        if (fm != null && fm.GetFloorFromWorldY(_playerEar.transform.position.y) != fm.GetFloorFromWorldY(mouth.y))
            return "玩家在别的楼层，听不到";

        float d = Vector3.Distance(_playerEar.EarPosition, mouth);
        var ears = _playerEar.GetComponent<SoundPerception>();
        if (ears != null && d > ears.HearingRange)
            return $"玩家在 {d:F1} 米外，超出玩家听力 {ears.HearingRange:F0} 米";

        return data.TierAtDistance(d) switch
        {
            ClarityTier.Clear   => $"玩家 {d:F1} 米，能听清（不计墙）",
            ClarityTier.Blurred => $"玩家 {d:F1} 米，能抓到关键词（不计墙）",
            ClarityTier.Faint   => $"玩家 {d:F1} 米，只知道有人在说话（不计墙）",
            _                   => $"玩家 {d:F1} 米，超出这段对话的 {data.audibleRadius:F0} 米，听不到"
        };
    }

    // ── 名字占位符 ──────────────────────────────────────────────

    /// <summary>按本次演员表解析一句台词：替换 {X} 名字占位符，并判定关键词是不是谁的名字。</summary>
    private ResolvedLine Resolve(Session s, DialogueLine line, int index, DialogueSpeaker speaker)
    {
        var r = new ResolvedLine
        {
            data    = s.data,
            line    = line,
            index   = index,
            speaker = speaker,
            text    = Expand(s, line.text),
            blurred = Expand(s, line.blurred),
            keyword = Expand(s, line.keyword),
            hint    = Expand(s, line.hint),
            flag    = line.flag
        };

        // 关键词是谁的名字：显式 nameOf 优先（用于名字“变了”的情况）；
        // 否则 keyword 整个就是一个 {X} 时，自然就是 X 的名字
        string who = line.nameOf;
        if (string.IsNullOrEmpty(who) && !string.IsNullOrEmpty(line.keyword))
        {
            string kw = line.keyword.Trim();
            var m = Placeholder.Match(kw);
            if (m.Success && m.Length == kw.Length) who = m.Groups[1].Value;
        }

        if (!string.IsNullOrEmpty(who) && !string.IsNullOrEmpty(r.keyword))
        {
            var target = ResolveWho(s, who);
            if (target != null) r.nameOfSpeakerId = target.SpeakerId;
            else Debug.LogWarning($"[对话:{s.data.id}] 第 {index} 句 nameOf=\"{who}\" 找不到对应的人（不是本段的角色位，场景里也没有这个 id）");
        }

        return r;
    }

    /// <summary>角色位（A/B）先查本段演员表，查不到再当单位 id（E02）去场景登记表里找。</summary>
    private static DialogueSpeaker ResolveWho(Session s, string who)
    {
        if (s.cast.TryGetValue(who, out var sp) && sp != null) return sp;
        return DialogueSpeaker.TryGet(who, out sp) ? sp : null;
    }

    private string Expand(Session s, string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('{') < 0) return raw;

        return Placeholder.Replace(raw, m =>
        {
            var sp = ResolveWho(s, m.Groups[1].Value);
            if (sp == null) return m.Value;          // 认不出的占位符原样留着，写错了一眼能看到

            if (!sp.HasDisplayName && _warnedNoName.Add(sp))
                Debug.LogWarning($"[对话] {sp.name} 的 DialogueSpeaker 没填 displayName，" +
                                 $"台词里的 {m.Value} 暂时显示成 {sp.SpeakerId}", sp);
            return sp.DisplayName;
        });
    }

    private DialogueSpeaker FindOther(Session s, DialogueSpeaker self)
    {
        foreach (var p in s.participants)
            if (p != null && p != self) return p;
        return null;
    }

    private void FinishSession(int index)
    {
        if (index < 0 || index >= _active.Count) return;

        var s = _active[index];
        foreach (var p in s.participants)
        {
            if (p == null) continue;
            p.IsBusy = false;
            // 对话期间转向了同伴，这里转回去——不还原的话原地不动的敌人
            // 会永久盯着同伴站过的方向，视野盲区固定下来（详见 RestoreFacing 注释）
            p.RestoreFacing();
        }

        _active.RemoveAt(index);
        DebugLog($"「{s.data.id}」播放结束");
        OnDialogueFinished?.Invoke(s.data);
    }

    private void DebugLog(string msg)
    {
        if (enableDebugLog) Debug.Log($"[对话] {msg}");
    }
}
