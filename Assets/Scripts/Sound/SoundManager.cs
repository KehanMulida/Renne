using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 声音管理器 - 中央管理系统
/// 职责：
/// 1. 接收所有发出的声音
/// 2. 将声音广播给所有监听器
/// 3. 提供声音事件查询接口
/// 特点：
/// - 单例模式，全局唯一
/// - 解耦发声者和监听者
/// - 自动管理监听器注册
/// - 可选的声音历史记录
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("系统配置")]
    [SerializeField] private bool enableSystem = true;          // 是否启用声音系统
    [SerializeField] private bool recordHistory = true;         // 是否记录历史
    [SerializeField] private int maxHistorySize = 50;           // 最大历史记录数

    [Header("可视化")]
    [SerializeField] private bool visualizeSounds = true;       // 是否可视化声音
    [SerializeField] private float visualDuration = 1f;         // 可视化持续时间

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = false;

    // 运行时数据
    private List<SoundListener> registeredListeners = new List<SoundListener>();
    private List<SoundEvent> soundHistory = new List<SoundEvent>();
    private List<VisualizedSound> visualizedSounds = new List<VisualizedSound>();

    // 用于可视化的声音数据
    private class VisualizedSound
    {
        public SoundEvent soundEvent;
        public float startTime;

        public VisualizedSound(SoundEvent evt)
        {
            soundEvent = evt;
            startTime = Time.time;
        }
    }

    // ============ 事件系统 ============

    /// <summary>
    /// 声音广播事件：每次有新声音时触发
    /// </summary>
    public event System.Action<SoundEvent> OnSoundBroadcast;

    // ============ 初始化 ============

    void Awake()
    {
        // 单例模式
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        // 清理过期的可视化
        if (visualizeSounds)
        {
            visualizedSounds.RemoveAll(v => Time.time - v.startTime > visualDuration);
        }
    }

    // ============ 公开接口 - 监听器管理 ============

    /// <summary>
    /// 注册监听器
    /// </summary>
    public void RegisterListener(SoundListener listener)
    {
        if (!registeredListeners.Contains(listener))
        {
            registeredListeners.Add(listener);
            
            if (enableDebugLog)
            {
                Debug.Log($"[SoundManager] Registered listener: {listener.gameObject.name}, " +
                         $"Total listeners: {registeredListeners.Count}");
            }
        }
    }

    /// <summary>
    /// 取消注册监听器
    /// </summary>
    public void UnregisterListener(SoundListener listener)
    {
        registeredListeners.Remove(listener);
        
        if (enableDebugLog)
        {
            Debug.Log($"[SoundManager] Unregistered listener: {listener.gameObject.name}, " +
                     $"Remaining listeners: {registeredListeners.Count}");
        }
    }

    // ============ 公开接口 - 声音广播 ============

    /// <summary>
    /// 广播声音事件
    /// 由SoundEmitter调用
    /// </summary>
    public void BroadcastSound(SoundEvent soundEvent)
    {
        if (!enableSystem) return;

        // 记录历史
        if (recordHistory)
        {
            soundHistory.Add(soundEvent);
            if (soundHistory.Count > maxHistorySize)
            {
                soundHistory.RemoveAt(0);
            }
        }

        // 添加到可视化列表
        if (visualizeSounds)
        {
            visualizedSounds.Add(new VisualizedSound(soundEvent));
        }

        // 触发广播事件
        OnSoundBroadcast?.Invoke(soundEvent);

        // 通知所有监听器
        foreach (var listener in registeredListeners)
        {
            if (listener != null)
            {
                listener.ReceiveSound(soundEvent);
            }
        }

        if (enableDebugLog)
        {
            Debug.Log($"[SoundManager] Broadcast {soundEvent.soundType} from {soundEvent.source.name}, " +
                     $"radius: {soundEvent.radius}m, listeners notified: {registeredListeners.Count}");
        }
    }

    // ============ 公开接口 - 查询 ============

    /// <summary>
    /// 获取声音历史
    /// </summary>
    public List<SoundEvent> GetSoundHistory()
    {
        return new List<SoundEvent>(soundHistory);
    }

    /// <summary>
    /// 获取指定时间范围内的声音
    /// </summary>
    public List<SoundEvent> GetSoundsInTimeRange(float startTime, float endTime)
    {
        return soundHistory.FindAll(s => s.timestamp >= startTime && s.timestamp <= endTime);
    }

    /// <summary>
    /// 获取指定区域内的声音
    /// </summary>
    public List<SoundEvent> GetSoundsInArea(Vector3 center, float radius)
    {
        return soundHistory.FindAll(s => Vector3.Distance(s.position, center) <= radius);
    }

    /// <summary>
    /// 清除历史记录
    /// </summary>
    public void ClearHistory()
    {
        soundHistory.Clear();
        visualizedSounds.Clear();
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (!visualizeSounds || !Application.isPlaying) return;

        // 绘制当前活跃的声音
        foreach (var visual in visualizedSounds)
        {
            float age = Time.time - visual.startTime;
            float alpha = 1f - (age / visualDuration);

            // 根据声音类型选择颜色
            Color color = GetSoundColor(visual.soundEvent.soundType);
            color.a = alpha * 0.5f;

            Gizmos.color = color;
            Gizmos.DrawWireSphere(visual.soundEvent.position, visual.soundEvent.radius * alpha);

            // 绘制声音源标记
            Gizmos.DrawSphere(visual.soundEvent.position, 0.3f);
        }
    }

    private Color GetSoundColor(SoundType type)
    {
        switch (type)
        {
            case SoundType.Movement: return Color.yellow;
            case SoundType.Attack: return Color.red;
            case SoundType.Skill: return Color.magenta;
            case SoundType.Environmental: return Color.green;
            case SoundType.Alert: return Color.cyan;
            default: return Color.white;
        }
    }

    void OnGUI()
    {
        if (!enableDebugLog) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperLeft;

        string info = $"[Sound System]\n" +
                     $"Listeners: {registeredListeners.Count}\n" +
                     $"History: {soundHistory.Count}\n" +
                     $"Active Sounds: {visualizedSounds.Count}";

        GUI.Box(new Rect(Screen.width - 160, 180, 150, 90), info, style);
    }
}