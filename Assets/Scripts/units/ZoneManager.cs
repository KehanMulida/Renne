using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// ZoneManager — 全局功能区管理器
/// 职责：
/// 1. 接收 ZoneMarker 的注册/注销
/// 2. 提供按 ID、楼层、类型查询区域的接口
/// 3. 提供单位位置 → 所在区域的反查接口
/// 4. 供 Condition 求值器调用，判断单位是否在特定区域
///
/// 多 Box 共享同一 zoneId 设计：
/// - zoneDict 值改为 List<ZoneMarker>，同一 ID 的多个 Box 组成一个逻辑区域
/// - 注册时 Append（不覆盖），注销时只移除对应 marker 实例
/// - IsUnitInZone / TryGetRandomCellInZone 遍历所有同 ID 的 marker
/// </summary>
public class ZoneManager : MonoBehaviour
{
    public static ZoneManager Instance { get; private set; }

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;
    [SerializeField] private bool showDebugGUI = true;

    // ══════════════════════════════════════════════════════
    // 内部数据
    // ══════════════════════════════════════════════════════

    // 主字典：zoneId → List<ZoneMarker>（支持多 Box 共享同一 ID）
    private Dictionary<string, List<ZoneMarker>> zoneDict = new();

    // 按楼层分组：floor → List<ZoneMarker>（方便按楼层查询）
    private Dictionary<int, List<ZoneMarker>> zonesByFloor = new();

    // 按类型分组：ZoneType → List<ZoneMarker>（方便按类型查询）
    private Dictionary<ZoneType, List<ZoneMarker>> zonesByType = new();

    // ══════════════════════════════════════════════════════
    // 公开属性
    // ══════════════════════════════════════════════════════

    /// <summary>当前已注册的唯一 Zone ID 总数</summary>
    public int ZoneCount => zoneDict.Count;

    /// <summary>当前已注册的 ZoneMarker（Box）总数</summary>
    public int TotalMarkerCount => zoneDict.Values.Sum(list => list.Count);

    // ══════════════════════════════════════════════════════
    // 初始化
    // ══════════════════════════════════════════════════════

    void Awake()
    {
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

    // ══════════════════════════════════════════════════════
    // 注册 / 注销（由 ZoneMarker 调用）
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 注册一个区域 Box（由 ZoneMarker.Start() 调用）
    /// 同一 zoneId 可注册多个 Box，所有 Box 共同构成该逻辑区域
    /// </summary>
    public void RegisterZone(ZoneMarker marker)
    {
        if (marker == null) return;

        string id = marker.zoneId;

        // 主字典：追加到同 ID 的列表（而非覆盖）
        if (!zoneDict.TryGetValue(id, out var idList))
        {
            idList = new List<ZoneMarker>();
            zoneDict[id] = idList;
        }
        if (!idList.Contains(marker))
            idList.Add(marker);

        // 按楼层分组
        int floor = marker.Floor;
        if (!zonesByFloor.ContainsKey(floor))
            zonesByFloor[floor] = new List<ZoneMarker>();
        if (!zonesByFloor[floor].Contains(marker))
            zonesByFloor[floor].Add(marker);

        // 按类型分组
        ZoneType type = marker.zoneType;
        if (!zonesByType.ContainsKey(type))
            zonesByType[type] = new List<ZoneMarker>();
        if (!zonesByType[type].Contains(marker))
            zonesByType[type].Add(marker);

        DebugLog($"注册 Zone [{id}] | 类型: {type} | 楼层: {floor} " +
                 $"| 该 ID 共 {idList.Count} 个 Box | 全局共 {zoneDict.Count} 个 ID");
    }

    /// <summary>
    /// 注销指定 ZoneMarker 实例（由 ZoneMarker.OnDestroy() 调用）
    /// 只移除该实例；同 ID 其余 Box 继续有效
    /// </summary>
    public void UnregisterZone(ZoneMarker marker)
    {
        if (marker == null) return;

        string id = marker.zoneId;

        // 从主字典移除
        if (zoneDict.TryGetValue(id, out var idList))
        {
            idList.Remove(marker);
            if (idList.Count == 0)
                zoneDict.Remove(id);
        }

        // 从楼层分组移除
        int floor = marker.Floor;
        if (zonesByFloor.ContainsKey(floor))
            zonesByFloor[floor].Remove(marker);

        // 从类型分组移除
        ZoneType type = marker.zoneType;
        if (zonesByType.ContainsKey(type))
            zonesByType[type].Remove(marker);

        int remaining = zoneDict.TryGetValue(id, out var rem) ? rem.Count : 0;
        DebugLog($"注销 Zone [{id}] 一个 Box | 该 ID 剩余 {remaining} 个 Box");
    }

    /// <summary>
    /// 注销指定 ID 的全部 ZoneMarker（程序化批量清除用）
    /// </summary>
    public void UnregisterZone(string zoneId)
    {
        if (!zoneDict.TryGetValue(zoneId, out var list)) return;

        int count = list.Count;

        // 从辅助索引中逐一移除
        foreach (var marker in list)
        {
            int floor = marker.Floor;
            if (zonesByFloor.ContainsKey(floor))
                zonesByFloor[floor].Remove(marker);

            ZoneType type = marker.zoneType;
            if (zonesByType.ContainsKey(type))
                zonesByType[type].Remove(marker);
        }

        zoneDict.Remove(zoneId);
        DebugLog($"注销 Zone [{zoneId}] 全部 {count} 个 Box | 剩余 {zoneDict.Count} 个 ID");
    }

    // ══════════════════════════════════════════════════════
    // 查询接口（供 Condition 求值器和 AI 系统调用）
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 通过 ID 获取第一个 ZoneMarker（向后兼容；多 Box 场景请用 GetZones）
    /// </summary>
    public ZoneMarker GetZone(string zoneId)
    {
        if (zoneDict.TryGetValue(zoneId, out var list) && list.Count > 0)
            return list[0];
        return null;
    }

    /// <summary>
    /// 通过 ID 获取该逻辑区域下的所有 ZoneMarker（多 Box 支持）
    /// </summary>
    public List<ZoneMarker> GetZones(string zoneId)
    {
        return zoneDict.TryGetValue(zoneId, out var list)
            ? new List<ZoneMarker>(list)
            : new List<ZoneMarker>();
    }

    /// <summary>
    /// 获取指定楼层的所有 Zone
    /// </summary>
    public List<ZoneMarker> GetZonesOnFloor(int floor)
    {
        return zonesByFloor.TryGetValue(floor, out var list)
            ? new List<ZoneMarker>(list)
            : new List<ZoneMarker>();
    }

    /// <summary>
    /// 获取指定类型的所有 Zone
    /// </summary>
    public List<ZoneMarker> GetZonesByType(ZoneType type)
    {
        return zonesByType.TryGetValue(type, out var list)
            ? new List<ZoneMarker>(list)
            : new List<ZoneMarker>();
    }

    /// <summary>
    /// 获取指定类型且在指定楼层的所有 Zone
    /// </summary>
    public List<ZoneMarker> GetZonesByTypeAndFloor(ZoneType type, int floor)
    {
        var byType = GetZonesByType(type);
        return byType.Where(z => z.Floor == floor).ToList();
    }

    // ══════════════════════════════════════════════════════
    // 核心判断接口（Condition 求值器直接调用这里）
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 检查单位是否在指定 Zone 内（遍历该 ID 的所有 Box，任一包含即返回 true）
    /// </summary>
    /// <param name="zoneId">目标区域 ID（可对应多个 Box）</param>
    /// <param name="unitGridPos">单位当前网格坐标</param>
    /// <param name="unitFloor">单位当前楼层</param>
    public bool IsUnitInZone(string zoneId, Vector2Int unitGridPos, int unitFloor)
    {
        if (!zoneDict.TryGetValue(zoneId, out var list))
        {
            Debug.LogWarning($"[ZoneManager] IsUnitInZone：找不到 zoneId [{zoneId}]，返回 false");
            return false;
        }

        foreach (var marker in list)
        {
            if (marker.ContainsUnit(unitGridPos, unitFloor))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查单位是否在指定类型的任意 Zone 内
    /// 用于"玩家进入任意便利店"这类条件
    /// </summary>
    public bool IsUnitInAnyZoneOfType(ZoneType type, Vector2Int unitGridPos, int unitFloor)
    {
        var zones = GetZonesByType(type);
        foreach (var zone in zones)
        {
            if (zone.ContainsUnit(unitGridPos, unitFloor))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 获取单位当前所在的所有 Zone（一个单位可能同时在走廊和门口区域）
    /// </summary>
    public List<ZoneMarker> GetZonesContainingUnit(Vector2Int unitGridPos, int unitFloor)
    {
        var result = new List<ZoneMarker>();

        // 只在同楼层的 Zone 里查找，提升性能
        if (!zonesByFloor.TryGetValue(unitFloor, out var floorZones)) return result;

        foreach (var zone in floorZones)
        {
            if (zone.ContainsUnit(unitGridPos, unitFloor))
                result.Add(zone);
        }
        return result;
    }

    /// <summary>
    /// 获取指定 Zone 内的随机可行走格子（供 AI 导航使用）
    /// 多 Box 支持：从该 ID 所有 Box 的格子中随机选取
    /// </summary>
    public bool TryGetRandomCellInZone(string zoneId, out Vector2Int cell, out int floor)
    {
        cell  = Vector2Int.zero;
        floor = 0;

        if (!zoneDict.TryGetValue(zoneId, out var list) || list.Count == 0)
            return false;

        // 收集所有 Box 的可行走格子
        var walkable = new List<(Vector2Int cell, int floor)>();
        foreach (var marker in list)
        {
            if (marker.Cells == null) continue;
            foreach (var c in marker.Cells)
            {
                if (GridManager.Instance != null &&
                    GridManager.Instance.IsWalkable(c, marker.Floor))
                    walkable.Add((c, marker.Floor));
            }
        }

        if (walkable.Count == 0) return false;

        var picked = walkable[Random.Range(0, walkable.Count)];
        cell  = picked.cell;
        floor = picked.floor;
        return true;
    }

    // ══════════════════════════════════════════════════════
    // 调试
    // ══════════════════════════════════════════════════════

    private void DebugLog(string message)
    {
        if (enableDebugLog)
            Debug.Log($"[ZoneManager] {message}");
    }

    void OnGUI()
    {
        if (!showDebugGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.white;

        // 右上角，GameManager 调试框下方
        float boxWidth = 280;
        float startY   = 200;

        int totalMarkers = TotalMarkerCount;
        string info = $"[ZoneManager]\n已注册：{ZoneCount} 个 ID / {totalMarkers} 个 Box\n";

        // 按楼层显示（相同 ID 的多 Box 会合并为"ID (×N)"形式）
        foreach (var kvp in zonesByFloor.OrderBy(k => k.Key))
        {
            info += $"楼层 {kvp.Key}：{kvp.Value.Count} 个 Box\n";

            // 按 ID 分组显示，避免重复
            var grouped = kvp.Value.GroupBy(z => z.zoneId);
            foreach (var g in grouped)
            {
                string suffix = g.Count() > 1 ? $" (×{g.Count()})" : "";
                info += $"  [{g.Key}]{suffix} {g.First().zoneType}\n";
            }
        }

        float boxHeight = Mathf.Max(80, 50 + totalMarkers * 18);
        GUI.Box(new Rect(Screen.width - boxWidth - 10, startY, boxWidth, boxHeight), info, style);
    }
}
