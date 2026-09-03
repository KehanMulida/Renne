using UnityEngine;

/// <summary>
/// 声音数据类
/// 存储声音的所有信息
/// </summary>
public class SoundEvent
{
    public Vector3 position;
    public float radius;
    public GameObject source;
    public SoundType soundType;
    public float intensity;
    public float timestamp;
    public int floor; // 添加这一行

    public SoundEvent(Vector3 pos, float rad, GameObject src, SoundType type, float intense = 1f)
    {
        position = pos;
        radius = rad;
        source = src;
        soundType = type;
        intensity = intense;
        timestamp = Time.time;
        // 楼层必须用 FloorManager 的真实分层（floorHeight 可配置，默认 3m），
        // 不能硬编码 y/4f —— 否则声音楼层与敌人 CurrentFloor / 网格寻路的楼层不一致，
        // 导致 InvestigationExecutor 在错误楼层做 IsWalkable/FindPath 而放弃调查。
        floor = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorFromWorldY(pos.y)
            : 0;
    }
}

/// <summary>
/// 声音类型枚举
/// </summary>
public enum SoundType
{
    Movement,       // 移动声音
    Attack,         // 攻击声音
    Skill,          // 技能声音
    Environmental,  // 环境声音
    Alert           // 警报声音
}

/// <summary>
/// 声音发射器
/// 职责：
/// 1. 角色行动时发出声音
/// 2. 通过SoundManager广播声音事件
/// 特点：
/// - 低耦合：不直接通知其他角色，通过中央管理器
/// - 可配置：不同行动产生不同强度的声音
/// - 可视化：Scene视图显示声音范围
/// </summary>
public class SoundEmitter : MonoBehaviour
{
    [Header("声音配置")]
    [SerializeField] private bool enableSound = true;           // 是否启用声音系统
    [SerializeField] private float movementSoundRadius = 3f;    // 移动声音半径
    [SerializeField] private float movementSoundIntensity = 0.5f; // 移动声音强度
    
    [Header("可视化")]
    [SerializeField] private bool showSoundRadius = true;       // 是否显示声音范围
    [SerializeField] private Color soundColor = new Color(1f, 1f, 0f, 0.3f);

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = false;

    // 运行时数据
    private float lastSoundTime = 0f;
    private const float minSoundInterval = 0.1f;
    private float _baseMovementRadius;   // 记录 Inspector 设置的原始半径
    private PlayerController _player;

    void Awake()
    {
        _baseMovementRadius = movementSoundRadius;

        var movement = GetComponent<UnitMovement>();
        if (movement != null) movement.OnStep += EmitMovementSound;

        _player = GetComponent<PlayerController>();
        if (_player != null) _player.OnStanceChanged += OnStanceChanged;
    }

    void OnDestroy()
    {
        var movement = GetComponent<UnitMovement>();
        if (movement != null) movement.OnStep -= EmitMovementSound;

        if (_player != null) _player.OnStanceChanged -= OnStanceChanged;
    }

    // 姿态变化时按当前姿态的声音半径倍率缩放移动噪音：站立1.0 → 下蹲0.3 → 匍匐0.1（最静）
    private void OnStanceChanged(Stance stance)
    {
        float mult = _player != null ? _player.SoundRadiusMultiplier : 1f;
        movementSoundRadius = _baseMovementRadius * mult;
    }

    // ============ 配置接口 ============

    /// <summary>
    /// 设置移动声音半径
    /// </summary>
    public void SetMovementSoundRadius(float radius)
    {
        // 这是“基准”半径（配置层 BaseNoiceLevel 驱动），记录下来供姿态缩放使用，
        // 并按当前姿态倍率立即重算生效半径。修复：此前 _baseMovementRadius 只在 Awake
        // 抓 Inspector 值，被 ApplyConfigToComponents 覆盖后基准与生效值脱节。
        _baseMovementRadius = Mathf.Max(0f, radius);
        float mult = _player != null ? _player.SoundRadiusMultiplier : 1f;
        movementSoundRadius = _baseMovementRadius * mult;
    }

    /// <summary>
    /// 设置移动声音强度
    /// </summary>
    public void SetMovementSoundIntensity(float intensity)
    {
        movementSoundIntensity = Mathf.Clamp01(intensity);
    }

    /// <summary>
    /// 修改声音半径（相对修改）
    /// </summary>
    public void ModifySoundRadius(float modifier)
    {
        movementSoundRadius = Mathf.Max(0f, movementSoundRadius + modifier);
    }

    // ============ 公开接口 ============

    /// <summary>
    /// 发出移动声音
    /// 用途：角色移动时调用
    /// </summary>
    public void EmitMovementSound()
    {
        // Debug.LogWarning($"[SoundEmitter:{gameObject.name}] EmitMovementSound called! enableSound={enableSound}, radius={movementSoundRadius}");
        if (!enableSound) return;
        if (Time.time - lastSoundTime < minSoundInterval) return;

        EmitSound(SoundType.Movement, movementSoundRadius, movementSoundIntensity);
    }

    /// <summary>
    /// 发出自定义声音
    /// 用途：其他系统需要发出特定声音时调用
    /// </summary>
    public void EmitSound(SoundType soundType, float radius, float intensity = 1f)
    {
        if (!enableSound) return;

        // 走统一入口发声（构造 + 广播都在 Emit 里）
        Emit(transform.position, soundType, radius, gameObject, intensity);

        lastSoundTime = Time.time;

        if (enableDebugLog)
        {
            Debug.Log($"[SoundEmitter:{gameObject.name}] Emitted {soundType} sound, radius: {radius}");
        }
    }

    /// <summary>
    /// 统一发声入口（唯一）：构造 SoundEvent 并通过 SoundManager 广播。
    /// 所有声音——单位移动声（SoundEmitter 实例）和一次性道具/环境声（钻机/发电机/投掷物/场景物品）——
    /// 都经此发出，保证“发声”只有一条路径、SoundEvent 的构造只有一处。SoundManager 缺席时安全跳过。
    /// </summary>
    /// <param name="position">发声世界坐标</param>
    /// <param name="soundType">声音类型</param>
    /// <param name="radius">声音半径（米）</param>
    /// <param name="source">发声者 GameObject（用于忽略自身声、判定阵营等）</param>
    /// <param name="intensity">强度 0~1</param>
    public static void Emit(Vector3 position, SoundType soundType, float radius, GameObject source, float intensity = 1f)
    {
        if (SoundManager.Instance == null) return;
        SoundManager.Instance.BroadcastSound(
            new SoundEvent(position, radius, source, soundType, intensity));
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (!showSoundRadius || !enableSound) return;

        // 绘制声音半径
        Gizmos.color = soundColor;
        Gizmos.DrawWireSphere(transform.position, movementSoundRadius);
    }

    void OnDrawGizmosSelected()
    {
        if (!enableSound) return;

        // 选中时显示更清晰的声音范围
        Gizmos.color = new Color(soundColor.r, soundColor.g, soundColor.b, 0.5f);
        Gizmos.DrawSphere(transform.position, movementSoundRadius);
    }
}