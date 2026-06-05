using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景物体状态注册表（静态单例，无需挂载 GameObject）
///
/// 解决的问题：
///   WorldItem 被拾取后执行 Destroy，GameObject 消失。
///   ConditionEvaluator.FindWorldItemById 之后返回 null，
///   导致 Interrupted / PickedUpByPlayer 等条件无法正常求值。
///
/// 工作方式：
///   WorldItem.SetState() 在修改自身状态时，同步写入此注册表。
///   ConditionEvaluator.EvaluateWorldObject 找不到场景内物体时，
///   回退查询此注册表获取最后已知状态。
///
/// 生命周期：
///   场景加载/关卡重置时由 GameManager（或 SceneLoader）调用 Clear()。
/// </summary>
public static class WorldItemRegistry
{
    /// <summary>物体最后已知状态记录</summary>
    public struct ItemRecord
    {
        /// <summary>最后已知的 WorldItemState</summary>
        public WorldItemState state;

        /// <summary>
        /// 与物体交互（拾取/关闭/破坏）的角色所属阵营
        /// "Player" / "Enemy" / "" (未知)
        /// </summary>
        public string interactedByFaction;
    }

    // objectId → ItemRecord
    private static readonly Dictionary<string, ItemRecord> _cache
        = new Dictionary<string, ItemRecord>();

    // ── 写入 ──────────────────────────────────────────────

    /// <summary>
    /// 写入或更新记录
    /// 由 WorldItem.SetState() 自动调用，通常无需手动调用
    /// </summary>
    public static void Record(string objectId, WorldItemState state, string interactedByFaction = "")
    {
        if (string.IsNullOrEmpty(objectId)) return;
        _cache[objectId] = new ItemRecord
        {
            state               = state,
            interactedByFaction = interactedByFaction ?? "",
        };
        Debug.Log($"[WorldItemRegistry] {objectId} → {state}"
                + (string.IsNullOrEmpty(interactedByFaction) ? "" : $" (by:{interactedByFaction})"));
    }

    // ── 读取 ──────────────────────────────────────────────

    /// <summary>是否有此物体的记录（场景内存在的物体可能未注册）</summary>
    public static bool HasRecord(string objectId)
        => !string.IsNullOrEmpty(objectId) && _cache.ContainsKey(objectId);

    /// <summary>获取最后已知状态（无记录时返回 Active）</summary>
    public static WorldItemState GetState(string objectId)
        => _cache.TryGetValue(objectId, out var r) ? r.state : WorldItemState.Active;

    /// <summary>获取交互方阵营字符串（无记录或未知时返回 ""）</summary>
    public static string GetInteractedByFaction(string objectId)
        => _cache.TryGetValue(objectId, out var r) ? r.interactedByFaction : "";

    /// <summary>尝试获取完整记录，返回是否成功</summary>
    public static bool TryGetRecord(string objectId, out ItemRecord record)
        => _cache.TryGetValue(objectId, out record);

    // ── 清理 ──────────────────────────────────────────────

    /// <summary>
    /// 清空全部记录
    /// 关卡重置 / 新场景加载时调用，防止上一局数据污染
    /// </summary>
    public static void Clear()
    {
        _cache.Clear();
        Debug.Log("[WorldItemRegistry] Cleared");
    }
}
