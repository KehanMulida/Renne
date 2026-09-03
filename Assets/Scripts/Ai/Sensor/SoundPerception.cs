using System;
using UnityEngine;

/// <summary>
/// 声音感知（可编辑组件）。挂在需要“听”的单位上（敌人 / 玩家均可）。
/// 订阅 SoundManager 广播，按 hearingRange / 楼层等过滤后，通过 OnPerceptionEvent 上报：
///   · 敌人：EnemyAIController 的 PerceptionAggregator 会消费该事件（写 lastHeardPosition 去调查）。
///   · 玩家：可自行订阅本组件的 OnPerceptionEvent 做玩家听觉相关逻辑（当前无消费者时不产生副作用）。
/// 参数 Inspector 可调，也提供运行时属性 / Setter（SetHearingRange 等）随时修改。
///
/// 只负责“判断位置并上报”，不旋转 transform、不驱动移动——转身/移动由控制层决定。
/// </summary>
[DisallowMultipleComponent]
public class SoundPerception : MonoBehaviour, IPerceptionModule
{
    /// <summary>诊断开关：打印听觉链路日志（排查发声/收声）。需要时置 true。</summary>
    public static bool DebugHearing = false;

    [Header("听觉参数（Inspector 可调；运行时可用属性/Setter 修改）")]
    [Tooltip("能听到多远的声音（世界单位）")]
    [Min(0f)] [SerializeField] private float hearingRange = 10f;
    [Tooltip("勾选=能听到其它楼层的声音；不勾=只听同层")]
    [SerializeField] private bool canHearAcrossFloors = false;
    [Tooltip("忽略其它 Enemy 单位发出的移动声（敌人之间不被彼此脚步惊动）")]
    [SerializeField] private bool ignoreOtherEnemySound = true;

    /// <summary>听力范围（世界单位）。运行时可读写。</summary>
    public float HearingRange { get => hearingRange; set => hearingRange = Mathf.Max(0f, value); }
    /// <summary>是否能跨楼层听。运行时可读写。</summary>
    public bool CanHearAcrossFloors { get => canHearAcrossFloors; set => canHearAcrossFloors = value; }
    /// <summary>是否忽略其它敌人的移动声。运行时可读写。</summary>
    public bool IgnoreOtherEnemySound { get => ignoreOtherEnemySound; set => ignoreOtherEnemySound = value; }
    /// <summary>运行时设置听力范围（等价于 HearingRange 属性）。</summary>
    public void SetHearingRange(float range) => HearingRange = range;

    public event Action<PerceptionEvent> OnPerceptionEvent;

    private bool _subscribed;

    // IPerceptionModule：EnemyAIController 初始化感知时调用（owner 即本物体）。
    // 组件化后以组件字段为准，不用 config 覆盖，保证 Inspector / 运行时可编辑。
    public void Initialize(Transform owner, EnemyConfig config) => Subscribe();

    public void UpdatePerception() { }

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();
    // 兜底：Start 时所有 Awake 已跑完，SoundManager.Instance 必定就绪，防初始化顺序漏订阅。
    private void Start()     => Subscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.OnSoundBroadcast += HandleSound;
            _subscribed = true;
        }
        else if (DebugHearing)
        {
            Debug.LogWarning($"[听觉:{name}] SoundManager.Instance 为空，暂未订阅（将于 Start 重试）", this);
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (SoundManager.Instance != null)
            SoundManager.Instance.OnSoundBroadcast -= HandleSound;
        _subscribed = false;
    }

    private void HandleSound(SoundEvent soundEvent)
    {
        float distance = Vector3.Distance(transform.position, soundEvent.position);

        if (distance > hearingRange)
        {
            if (DebugHearing)
                Debug.Log($"[听觉:{name}] 忽略：太远 dist={distance:F1} > range={hearingRange}");
            return;
        }
        if (!canHearAcrossFloors && GetFloor(transform) != soundEvent.floor)
        {
            if (DebugHearing)
                Debug.Log($"[听觉:{name}] 忽略：楼层不符 我={GetFloor(transform)}(y={transform.position.y:F2}) 声音={soundEvent.floor}(y={soundEvent.position.y:F2})");
            return;
        }

        // 忽略自己发出的声音
        if (soundEvent.source != null && soundEvent.source == gameObject) return;

        // 忽略同阵营 Enemy 发出的声音（可关）
        if (ignoreOtherEnemySound && soundEvent.source != null &&
            soundEvent.source.GetComponent<EnemyAIController>() != null) return;

        if (DebugHearing)
            Debug.Log($"[听觉:{name}] 听到声音！pos={soundEvent.position} floor={soundEvent.floor} → 上报 lastHeardPosition", this);

        // 听觉只负责“判断位置”：上报供控制层在自己回合调查；不在此转身/瞄准。
        OnPerceptionEvent?.Invoke(new PerceptionEvent
        {
            Type       = PerceptionType.SoundHeard,
            Position   = soundEvent.position,
            Confidence = hearingRange > 0f ? 1f - (distance / hearingRange) : 1f,
            Floor      = soundEvent.floor
        });
    }

    private int GetFloor(Transform target)
    {
        // 与全局楼层系统一致（不能硬编码 y/4f），否则 lastHeardFloor 与真实楼层不符、调查失败。
        return FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorFromWorldY(target.position.y)
            : 0;
    }
}
