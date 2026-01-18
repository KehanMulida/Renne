using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 声音监听器
/// 职责：
/// 1. 监听附近的声音事件
/// 2. 根据距离判断是否能听到
/// 3. 记录听到的声音信息
/// 特点：
/// - 低耦合：通过SoundManager接收声音，不直接依赖发声者
/// - 智能过滤：自动过滤自己发出的声音
/// - 距离衰减：根据距离计算实际听到的强度
/// </summary>
public class SoundListener : MonoBehaviour
{
    [Header("监听配置")]
    [SerializeField] private bool enableListening = true;       // 是否启用监听
    [SerializeField] private float hearingRange = 10f;          // 听觉范围
    [SerializeField] private float hearingThreshold = 0.1f;     // 听觉阈值（最小可听强度）

    [Header("声音记忆")]
    [SerializeField] private int maxSoundMemory = 10;           // 最多记住多少个声音
    [SerializeField] private float soundMemoryDuration = 5f;    // 声音记忆持续时间

    [Header("可视化")]
    [SerializeField] private bool showHearingRange = true;
    [SerializeField] private Color hearingColor = new Color(0f, 1f, 1f, 0.2f);

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = false;

    // 运行时数据
    public List<HeardSound> heardSounds = new List<HeardSound>();

    // 听到的声音信息
    public class HeardSound
    {
        public SoundEvent soundEvent;
        public float heardIntensity;  // 实际听到的强度
        public float heardTime;       // 听到的时间

        public HeardSound(SoundEvent evt, float intensity)
        {
            soundEvent = evt;
            heardIntensity = intensity;
            heardTime = Time.time;
        }
    }

    // ============ 事件系统 ============

    /// <summary>
    /// 听到声音事件：当听到新声音时触发
    /// </summary>
    public event System.Action<SoundEvent, float> OnSoundHeard; // 参数：声音事件，实际听到的强度

    // ============ 初始化 ============

    void Start()
    {
        // 注册到SoundManager
        if (enableListening && SoundManager.Instance != null)
        {
            SoundManager.Instance.RegisterListener(this);
        }
    }

    void OnDestroy()
    {
        // 取消注册
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.UnregisterListener(this);
        }
    }

    void Update()
    {
        // 清理过期的声音记忆
        CleanOldSounds();
    }

    // ============ 公开接口 ============
    /// <summary>
    /// 设置听觉范围
    /// </summary>
    public void SetHearingRange(float range)
    {
        hearingRange = Mathf.Max(0f, range);
    }
    
    /// <summary>
    /// 处理声音事件（由SoundManager调用）
    /// </summary>
    public void ReceiveSound(SoundEvent soundEvent)
    {
        if (!enableListening) return;

        // 过滤自己发出的声音
        if (soundEvent.source == gameObject)
            return;

        // 计算3D距离（考虑楼层）
        float distance = Vector3.Distance(transform.position, soundEvent.position);

        // 检查是否在听觉范围内
        if (distance > hearingRange)
            return;

        // 检查是否在声音半径内
        if (distance > soundEvent.radius)
            return;

        // 计算楼层差并应用垂直衰减
        float verticalAttenuation = 1f;
        if (FloorManager.Instance != null)
        {
            UnitMovement myUnit = GetComponent<UnitMovement>();
            UnitMovement sourceUnit = soundEvent.source.GetComponent<UnitMovement>();
            
            if (myUnit != null && sourceUnit != null)
            {
                int floorDiff = FloorManager.Instance.GetFloorDifference(myUnit.CurrentFloor, sourceUnit.CurrentFloor);
                verticalAttenuation = FloorManager.Instance.GetVerticalSoundAttenuation(floorDiff);
                
                // 如果楼层差太大，声音完全听不到
                if (verticalAttenuation <= 0f)
                    return;
            }
        }

        // 计算实际听到的强度（距离衰减 + 楼层衰减）
        float heardIntensity = CalculateHeardIntensity(soundEvent, distance) * verticalAttenuation;

        // 检查是否超过听觉阈值
        if (heardIntensity < hearingThreshold)
            return;

        // 记录听到的声音
        RecordSound(soundEvent, heardIntensity);

        // 触发事件
        OnSoundHeard?.Invoke(soundEvent, heardIntensity);

        if (enableDebugLog)
        {
            Debug.Log($"[SoundListener:{gameObject.name}] Heard {soundEvent.soundType} from {soundEvent.source.name}, " +
                     $"distance: {distance:F1}m, vertical atten: {verticalAttenuation:F2}, final intensity: {heardIntensity:F2}");
        }
    }

    /// <summary>
    /// 获取最近听到的声音
    /// </summary>
    public List<HeardSound> GetRecentSounds()
    {
        return new List<HeardSound>(heardSounds);
    }

    /// <summary>
    /// 获取指定类型的最近声音
    /// </summary>
    public HeardSound GetLatestSoundOfType(SoundType type)
    {
        for (int i = heardSounds.Count - 1; i >= 0; i--)
        {
            if (heardSounds[i].soundEvent.soundType == type)
            {
                return heardSounds[i];
            }
        }
        return null;
    }

    /// <summary>
    /// 清除所有声音记忆
    /// </summary>
    public void ClearSoundMemory()
    {
        heardSounds.Clear();
    }

    // ============ 私有方法 ============

    /// <summary>
    /// 计算实际听到的强度
    /// 考虑距离衰减
    /// </summary>
    private float CalculateHeardIntensity(SoundEvent soundEvent, float distance)
    {
        // 线性衰减：在声音半径内，距离越远强度越低
        float distanceRatio = 1f - (distance / soundEvent.radius);
        float heardIntensity = soundEvent.intensity * distanceRatio;
        
        return Mathf.Clamp01(heardIntensity);
    }

    /// <summary>
    /// 记录听到的声音
    /// </summary>
    private void RecordSound(SoundEvent soundEvent, float heardIntensity)
    {
        HeardSound heardSound = new HeardSound(soundEvent, heardIntensity);
        heardSounds.Add(heardSound);

        // 限制记忆数量
        if (heardSounds.Count > maxSoundMemory)
        {
            heardSounds.RemoveAt(0);
        }
    }

    /// <summary>
    /// 清理过期的声音记忆
    /// </summary>
    private void CleanOldSounds()
    {
        float currentTime = Time.time;
        heardSounds.RemoveAll(s => currentTime - s.heardTime > soundMemoryDuration);
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (!showHearingRange || !enableListening) return;

        // 绘制听觉范围
        Gizmos.color = hearingColor;
        Gizmos.DrawWireSphere(transform.position, hearingRange);
    }

    void OnDrawGizmosSelected()
    {
        if (!enableListening) return;

        // 选中时显示更清晰
        Gizmos.color = new Color(hearingColor.r, hearingColor.g, hearingColor.b, 0.4f);
        Gizmos.DrawSphere(transform.position, hearingRange);

        // 绘制最近听到的声音位置
        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            foreach (var heard in heardSounds)
            {
                Gizmos.DrawLine(transform.position, heard.soundEvent.position);
                Gizmos.DrawWireSphere(heard.soundEvent.position, 0.5f);
            }
        }
    }
}