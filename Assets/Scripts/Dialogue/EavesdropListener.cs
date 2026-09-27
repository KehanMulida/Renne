using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EavesdropListener — 挂在玩家身上的偷听结算器。**只判档，不拼任何显示文字**。
///
/// 订阅玩家自己的 SoundPerception，收到带 tag 的 Voice/Alert 事件时：
///   1. 用 tag 取回“刚说出口的那一版”台词（名字占位符已按演员表替换好）
///   2. 按距离判档：听懂（clearRadius）/ 抓关键词（keywordRadius）/ 察觉（audibleRadius）
///   3. 中间每隔一堵墙降一档——贴着隔间外只能抓关键词，得绕进去才听得清
///   4. 模糊档及以上捕到关键词：静默落旗标、浮出内心独白；关键词若是人名，就记住那人叫什么
///
/// 两道独立的闸，别混淆：
///   · SoundPerception.hearingRange —— 决定“这一声能不能到达我”
///   · DialogueData 的三个半径      —— 决定“到达后听得多清”
/// 所以把听力调大只会更早察觉“那边有人在说话”，不会让你听懂更多。
///
/// 玩家看得到的固定文字（方位词、"有人在低声交谈"、名字前缀的「？」）全在 DialogueUI 的 Inspector 里。
/// </summary>
[DisallowMultipleComponent]
public class EavesdropListener : MonoBehaviour
{
    [Header("墙体衰减")]
    [Tooltip("哪些层算“墙”。留空则自动取 Obstacle 层")]
    [SerializeField] private LayerMask wallMask;

    [Tooltip("每隔一堵墙降几档（清晰→模糊→极模糊→听不到）")]
    [Min(0)] [SerializeField] private int tiersLostPerWall = 1;

    [Tooltip("耳朵离脚底的高度。距离和墙体检测都从这里量到说话者的嘴。\n" +
             "用固定高度而不跟随姿态——否则蹲在矮墙后反而会被矮墙挡住，手感反直觉")]
    [SerializeField] private float earHeight = 1.5f;

    [Header("姿态加成")]
    [Tooltip("下蹲/匍匐时按多近来算——0.85 = 当作近了 15%（屏息贴墙偷听）。1 = 不启用")]
    [Range(0.5f, 1f)] [SerializeField] private float crouchDistanceFactor = 0.85f;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    /// <summary>听到一句话。UI 用它显示飘字——每次听到都会触发，飘字就是实时字幕。</summary>
    public event Action<HeardLine> OnLineHeard;

    /// <summary>捕到关键词时角色浮出的内心独白（同一句只触发一次）。</summary>
    public event Action<string> OnInnerThought;

    /// <summary>玩家得知某人的名字（speakerId, 名字）。名字可以被后来听到的覆盖——人会有外号、会改口。</summary>
    public event Action<string, string> OnNameLearned;

    public Vector3 EarPosition => transform.position + Vector3.up * earHeight;

    private SoundPerception _ears;
    private PlayerController _player;
    private bool _subscribed;

    // 已经触发过旗标/独白的句子——同一句不重复打扰玩家
    private readonly HashSet<string> _keywordCaught = new();

    // 玩家知道的名字：speakerId → 名字。没有记录的人，飘字前缀显示「？」
    private readonly Dictionary<string, string> _knownNames = new();

    private const int MaxWallProbe = 4;        // 最多数几堵墙（再多也是听不到）
    private const float WallStepPast = 0.05f;  // 数完一面墙后向前越过的距离

    private void Awake()
    {
        _ears   = GetComponent<SoundPerception>();
        _player = GetComponent<PlayerController>();

        if (wallMask == 0) wallMask = LayerMask.GetMask("Obstacle");

        if (_ears == null)
            Debug.LogWarning($"[偷听:{name}] 同物体上没有 SoundPerception，偷听不会生效。" +
                             $"请给玩家挂一个——它正是这套机制的听觉入口。", this);
    }

    private void OnEnable() => Subscribe();
    private void Start()    => Subscribe();      // 兜底，防初始化顺序漏订阅
    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed || _ears == null) return;
        _ears.OnPerceptionEvent += HandlePerception;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (_ears != null) _ears.OnPerceptionEvent -= HandlePerception;
        _subscribed = false;
    }

    // ── 名字 ────────────────────────────────────────────────────

    /// <summary>玩家知道的这个人的名字；还不知道时返回 null（UI 显示「？」）。</summary>
    public string GetKnownName(string speakerId)
        => !string.IsNullOrEmpty(speakerId) && _knownNames.TryGetValue(speakerId, out var n) ? n : null;

    private void LearnName(string speakerId, string name)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(name)) return;
        if (_knownNames.TryGetValue(speakerId, out var old) && old == name) return;

        _knownNames[speakerId] = name;
        if (enableDebugLog)
            Debug.Log(old == null
                ? $"[偷听] 得知 {speakerId} 叫「{name}」"
                : $"[偷听] {speakerId} 的名字从「{old}」变成「{name}」");
        OnNameLearned?.Invoke(speakerId, name);
    }

    // ── 结算 ────────────────────────────────────────────────────

    private void HandlePerception(PerceptionEvent evt)
    {
        if (evt == null || evt.Type != PerceptionType.SoundHeard) return;
        if (string.IsNullOrEmpty(evt.Tag)) return;                  // 不是对话声
        if (DialogueManager.Instance == null) return;

        if (!DialogueManager.Instance.TryGetResolved(evt.Tag, out var r)) return;

        // ① 距离判档（下蹲时按更近来算）
        // 从耳朵量到嘴，而不是从脚底量：脚底到 1.6m 高的嘴有一截垂直差，
        // 会让水平贴近 2m 时实测 2.56m、悄悄超出 2.5m 的清晰半径
        float distance  = Vector3.Distance(EarPosition, evt.Position);
        float effective = distance * StanceDistanceFactor();
        ClarityTier byDistance = r.data.TierAtDistance(effective);
        if (byDistance == ClarityTier.None) return;

        // ② 墙体降档：evt.Position 就是说话者嘴的位置（DialogueManager 从 MouthPosition 发声）
        int walls = CountWalls(EarPosition, evt.Position);
        int tierValue = (int)byDistance - walls * tiersLostPerWall;
        if (tierValue <= (int)ClarityTier.None)
        {
            if (enableDebugLog)
                Debug.Log($"[偷听] 被 {walls} 堵墙挡住，什么也听不到（距离 {distance:F1}）");
            return;
        }
        var tier = (ClarityTier)tierValue;

        var entry = new HeardLine
        {
            turn      = TurnSystem.Instance != null ? TurnSystem.Instance.GlobalTurnNumber : 0,
            tag       = evt.Tag,
            speakerId = r.speaker != null ? r.speaker.SpeakerId : r.line.speaker,
            speaker   = r.speaker != null ? r.speaker.transform : null,
            position  = evt.Position,
            tier      = tier,
            text      = r.text,
            blurred   = r.blurred,
            keyword   = r.keyword,
            direction = tier == ClarityTier.Faint ? DirectionIndex(evt.Position) : -1
        };

        if (enableDebugLog)
            Debug.Log($"[偷听] {tier}（距离 {distance:F1}，按 {effective:F1} 算，隔 {walls} 堵墙）：{r.text}");

        // 先结算关键词（会填上 entry.thought、可能学到名字），再抛事件，
        // 这样 UI 拿到这句时，名字前缀已经能从「？」变过来了
        if (tier >= ClarityTier.Blurred) CatchKeyword(r, entry);

        OnLineHeard?.Invoke(entry);
    }

    private void CatchKeyword(ResolvedLine r, HeardLine entry)
    {
        if (string.IsNullOrEmpty(r.keyword)) return;

        // 名字每次都学：闲聊是随机配对的，同一行 "{B}，借个火" 这次 B 是老王、下次是老李，
        // 不能因为这行听过就不再学。LearnName 自己会忽略重复
        if (!string.IsNullOrEmpty(r.nameOfSpeakerId)) LearnName(r.nameOfSpeakerId, r.keyword);

        if (!_keywordCaught.Add(r.data.MakeTag(r.index))) return;   // 旗标/独白每句只触发一次

        // 旗标是静默的：只悄悄打开后续可能性，绝不向玩家播报“你获得了情报”
        if (!string.IsNullOrEmpty(r.flag) && StoryManager.Instance != null)
            StoryManager.Instance.SetFlag(r.flag);

        // 提示不是系统播报，是角色被这个词勾起了一个念头
        if (!string.IsNullOrEmpty(r.hint))
        {
            entry.thought = r.hint;
            OnInnerThought?.Invoke(r.hint);
        }
    }

    // ── 几何 ────────────────────────────────────────────────────

    /// <summary>
    /// 数耳朵到嘴之间隔了几堵墙。
    ///
    /// 不用 RaycastAll：它每个碰撞体只报一次，一整块建筑外壳做成一个 MeshCollider 时
    /// 穿过三面墙也只算一堵。改为逐面前进——打到一面就越过它继续打：
    ///   · MeshCollider 只命中正面，出墙那面是背面、不会重复计数
    ///   · Box 等凸体从内部发射检测不到自己，越过入射面后也不会重复计数
    /// 必须忽略 trigger：本项目开着 queriesHitTriggers，区域触发器会被当成墙。
    /// </summary>
    private int CountWalls(Vector3 from, Vector3 to)
    {
        if (wallMask == 0 || tiersLostPerWall <= 0) return 0;

        Vector3 dir = to - from;
        float remaining = dir.magnitude;
        if (remaining < 0.01f) return 0;
        dir /= remaining;

        int walls = 0;
        Vector3 origin = from;
        for (int i = 0; i < MaxWallProbe; i++)
        {
            if (!Physics.Raycast(origin, dir, out var hit, remaining, wallMask, QueryTriggerInteraction.Ignore))
                break;

            walls++;
            float step = hit.distance + WallStepPast;
            origin    += dir * step;
            remaining -= step;
            if (remaining <= 0f) break;
        }
        return walls;
    }

    /// <summary>下蹲/匍匐时的距离折算系数。没有 PlayerController 或站立时为 1。</summary>
    private float StanceDistanceFactor()
    {
        if (_player == null || _player.CurrentStance == Stance.Stand) return 1f;
        return Mathf.Clamp(crouchDistanceFactor, 0.5f, 1f);
    }

    /// <summary>
    /// 相对玩家朝向的 8 方位编号：0=正前方，顺时针每 45° 加 1；-1 = 近处。
    /// 用朝向而非世界方位——等距视角下“北”对玩家没有意义。方位词本身在 DialogueUI 里配。
    /// </summary>
    private int DirectionIndex(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return -1;

        float angle = Vector3.SignedAngle(transform.forward, to.normalized, Vector3.up);
        float a = Mathf.Repeat(angle + 22.5f, 360f);    // 让“正前方”这一格以 0° 为中心
        return Mathf.Clamp(Mathf.FloorToInt(a / 45f), 0, 7);
    }
}
