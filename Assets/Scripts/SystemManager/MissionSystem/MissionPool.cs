using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个任务入口（MissionPool 中的一条记录）
/// 包含任务引用、前置条件、优先级
/// </summary>
[System.Serializable]
public class MissionEntry
{
    [Tooltip("任务数据资产")]
    public MissionData missionData;

    [Tooltip("优先级，数值越高越优先激活（同优先级任务可并行）")]
    public int priority = 0;

    [Tooltip("前置条件（AND 逻辑）：全部满足后该任务才可被激活\n留空表示无前置条件，直接可用")]
    public List<MissionCondition> preconditions;

    [Tooltip("是否允许与其他任务并行激活\n\n" +
             "true  （默认/并行）：前置条件满足就立即激活，不受其他任务影响\n" +
             "false （串行）    ：仅当本任务的所有参与 Enemy 都没有更高优先级的任务在执行时才激活\n" +
             "                   保证 Enemy 按优先级顺序完成任务，不会跳过上一个任务提前执行\n\n" +
             "⚠ 示例：E01 正在执行 P5 任务 → P3 任务（false）等待 E01 完成 P5 后才激活\n" +
             "        但 E03 独立执行自己的 P3 任务，不受 E01 的 P5 任务影响\n\n" +
             "合作任务注意事项（多个 Enemy 共同参与）：\n" +
             "  · 使用 false 确保所有参与 Enemy 同步开始\n" +
             "  · 必须配置 failConditions（AssignedEnemyDefeated）防止成员阵亡导致任务卡死")]
    public bool allowParallel = true;
}

/// <summary>
/// MissionPool — 某个 GamePhase 下可用的任务集合
/// ScriptableObject，在 Project 窗口右键 Create > Mission > Mission Pool 创建
/// 每个 GamePhase 对应一个 MissionPool
///
/// 激活规则（MissionManager 执行）：
/// 1. 从上到下按 priority 排序
/// 2. 检查每个任务的 preconditions，全部满足则立即激活（不受其他任务优先级阻塞）
/// 3. 激活后推送 MissionContext 给参与 Enemy，Enemy 按自身队列的 priority 顺序执行
/// 4. allowParallel = false（独占）：推送给 Enemy 时清除该 Enemy 队列里低优先级任务
/// </summary>
[CreateAssetMenu(fileName = "MissionPool", menuName = "Mission/Mission Pool")]
public class MissionPool : ScriptableObject
{
    [Header("任务列表（从上到下为优先级顺序）")]
    [Tooltip("该阶段所有可用任务。MissionManager 从上到下检查前置条件，满足则激活")]
    public List<MissionEntry> missions;

    [Header("调试")]
    [Tooltip("是否在 Inspector 显示任务状态预览")]
    public bool showDebugInfo = true;

    // ══════════════════════════════════════════════════════
    // 便捷查询方法（供 MissionManager 调用）
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 获取所有无前置条件的任务（开局直接可用）
    /// </summary>
    public List<MissionEntry> GetImmediatelyAvailable()
    {
        var result = new List<MissionEntry>();
        if (missions == null) return result;

        foreach (var entry in missions)
        {
            if (entry.missionData == null) continue;
            if (entry.preconditions == null || entry.preconditions.Count == 0)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>
    /// 按优先级从高到低排序返回所有任务
    /// </summary>
    public List<MissionEntry> GetSortedByPriority()
    {
        if (missions == null) return new List<MissionEntry>();

        var sorted = new List<MissionEntry>(missions);
        sorted.Sort((a, b) => b.priority.CompareTo(a.priority));
        return sorted;
    }

    /// <summary>
    /// 通过 missionId 查找任务入口
    /// </summary>
    public MissionEntry FindById(string missionId)
    {
        if (missions == null) return null;
        foreach (var entry in missions)
        {
            if (entry.missionData != null && entry.missionData.missionId == missionId)
                return entry;
        }
        return null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (missions == null) return;

        // 检查重复 missionId
        var seen = new HashSet<string>();
        foreach (var entry in missions)
        {
            if (entry.missionData == null) continue;
            string id = entry.missionData.missionId;
            if (!string.IsNullOrEmpty(id))
            {
                if (seen.Contains(id))
                    Debug.LogWarning($"[MissionPool] {name}：发现重复的 missionId [{id}]，请检查");
                else
                    seen.Add(id);
            }
        }
    }
#endif
}
