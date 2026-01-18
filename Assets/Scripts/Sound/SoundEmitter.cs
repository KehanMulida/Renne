using UnityEngine;

/// <summary>
/// 声音数据类
/// 存储声音的所有信息
/// </summary>
public class SoundEvent
{
    public Vector3 position;        // 声音产生的位置
    public float radius;            // 声音传播半径
    public GameObject source;       // 声音来源（谁发出的）
    public SoundType soundType;     // 声音类型
    public float intensity;         // 声音强度（0-1）
    public float timestamp;         // 产生时间

    public SoundEvent(Vector3 pos, float rad, GameObject src, SoundType type, float intense = 1f)
    {
        position = pos;
        radius = rad;
        source = src;
        soundType = type;
        intensity = intense;
        timestamp = Time.time;
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
    private const float minSoundInterval = 0.1f; // 最小声音间隔，避免频繁发声

    // ============ 配置接口 ============

    /// <summary>
    /// 设置移动声音半径
    /// </summary>
    public void SetMovementSoundRadius(float radius)
    {
        movementSoundRadius = Mathf.Max(0f, radius);
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

        SoundEvent soundEvent = new SoundEvent(
            transform.position,
            radius,
            gameObject,
            soundType,
            intensity
        );

        // 通过SoundManager广播声音
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.BroadcastSound(soundEvent);
        }

        lastSoundTime = Time.time;

        if (enableDebugLog)
        {
            Debug.Log($"[SoundEmitter:{gameObject.name}] Emitted {soundType} sound, radius: {radius}");
        }
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