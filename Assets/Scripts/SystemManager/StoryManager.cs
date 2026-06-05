using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StoryManager — 全局故事标记管理器
/// 维护一个 Dictionary<string, bool> 记录所有已发生的剧情事件
/// 被 ConditionEvaluator、MissionManager、EndingResolver 共同查询
/// </summary>
public class StoryManager : MonoBehaviour
{
    public static StoryManager Instance { get; private set; }

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    private Dictionary<string, bool> flags = new();

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    // ── 核心接口 ─────────────────────────────────────────

    /// <summary>设置一个 Flag 为 true</summary>
    public void SetFlag(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        flags[key] = true;
        DebugLog($"Flag 设置：{key} = true");
    }

    /// <summary>获取一个 Flag 的值（不存在则返回 false）</summary>
    public bool GetFlag(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return flags.TryGetValue(key, out bool v) && v;
    }

    /// <summary>清除一个 Flag</summary>
    public void ClearFlag(string key)
    {
        if (flags.Remove(key))
            DebugLog($"Flag 清除：{key}");
    }

    /// <summary>获取所有 Flag（供调试控制台显示）</summary>
    public IReadOnlyDictionary<string, bool> GetAllFlags() => flags;

    private void DebugLog(string msg)
    {
        if (enableDebugLog) Debug.Log($"[StoryManager] {msg}");
    }
}
