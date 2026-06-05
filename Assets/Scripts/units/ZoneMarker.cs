using System.Collections.Generic;
using UnityEngine;

// ══════════════════════════════════════════════════════════════════════════════
// ZoneMarker.cs
// 功能区标记组件，挂载在店铺/走廊/停车场等场景模块的空物体上
//
// 使用方式：
// 1. 在店铺 Prefab 根物体下新建空物体，命名如 "Zone_ConvenienceStore"
// 2. 挂载此组件，填写 zoneId 和 zoneType
// 3. 调整空物体的 Position（中心点）和 Scale（区域范围）
// 4. 运行时自动注册到 ZoneManager，楼层由 FloorManager 自动推断
//
// 地图种子友好设计：
// - Zone 数据跟随 Prefab，Prefab 放在哪里 Zone 就在哪里
// - 不需要手动维护任何配置表
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 功能区类型
/// 用于 Condition 查询和 AI 导航决策
/// </summary>
public enum ZoneType
{
    // ── 商业区 ──────────────────
    Shop,           // 普通商店
    Convenience,    // 便利店
    Restaurant,     // 餐厅/美食广场
    Supermarket,    // 超市
    Electronics,    // 电器/数码店
    Clothing,       // 服装店
    Pharmacy,       // 药店

    // ── 公共区域 ─────────────────
    Corridor,       // 走廊/通道
    Lobby,          // 大厅/中庭
    Escalator,      // 扶梯区
    Elevator,       // 电梯厅
    Stairwell,      // 楼梯间
    Restroom,       // 卫生间

    // ── 后勤区 ──────────────────
    Parking,        // 停车场
    LoadingDock,    // 装卸区
    Storage,        // 仓库/储藏室
    SecurityRoom,   // 保安室/监控室
    StaffOnly,      // 员工专区

    // ── 出入口 ──────────────────
    Entrance,       // 入口
    EmergencyExit,  // 紧急出口（逃脱结局触发点）

    // ── 扩展 ────────────────────
    Custom,         // 自定义（填写 customTypeName）
}

/// <summary>
/// 功能区标记组件
/// 挂在店铺模块 Prefab 下的空物体上，定义该模块的区域范围和属性
/// 运行时自动向 ZoneManager 注册
/// </summary>
public class ZoneMarker : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    // Inspector 配置
    // ══════════════════════════════════════════════════════

    [Header("区域标识")]

    [Tooltip("区域 ID（不含空格，如 patrol_zone_b1）\n" +
             "同一 ID 的多个 Box 共同构成一个逻辑区域（ZoneManager 会合并它们的格子）\n" +
             "需要分隔开的独立区域请使用不同 ID")]
    public string zoneId;

    [Tooltip("策划用可读名称，显示在调试 GUI 上")]
    public string displayName;

    [Tooltip("功能区类型，用于 Condition 查询和 AI 导航")]
    public ZoneType zoneType = ZoneType.Corridor;

    [Tooltip("自定义类型名称（zoneType = Custom 时填写）")]
    public string customTypeName;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = false;
    [SerializeField] private bool showGizmos = true;

    // ══════════════════════════════════════════════════════
    // 运行时数据（由系统自动计算，不需要手动填写）
    // ══════════════════════════════════════════════════════

    private int floor = -1;                     // 所属楼层（由 FloorManager 自动推断）
    private List<Vector2Int> cachedCells;       // 区域内的格子列表（运行时缓存）
    private Bounds worldBounds;                 // 世界空间包围盒（由 Transform 自动计算）
    private bool isRegistered = false;

    // ══════════════════════════════════════════════════════
    // 公开属性
    // ══════════════════════════════════════════════════════

    public int Floor => floor;
    public List<Vector2Int> Cells => cachedCells;
    public Bounds WorldBounds => worldBounds;
    public bool IsRegistered => isRegistered;

    // ══════════════════════════════════════════════════════
    // 初始化
    // ══════════════════════════════════════════════════════

    void Start()
    {
        if (string.IsNullOrEmpty(zoneId))
        {
            Debug.LogError($"[ZoneMarker:{gameObject.name}] zoneId 未填写！Zone 将不会被注册");
            return;
        }

        RegisterToZoneManager();
    }

    void OnDestroy()
    {
        if (isRegistered && ZoneManager.Instance != null)
        {
            // 传入 this 而非 zoneId，确保只移除本 Box，不影响同 ID 的其他 Box
            ZoneManager.Instance.UnregisterZone(this);
            DebugLog($"Zone [{zoneId}] 已注销");
        }
    }

    // ══════════════════════════════════════════════════════
    // 注册逻辑
    // ══════════════════════════════════════════════════════

    private void RegisterToZoneManager()
    {
        if (ZoneManager.Instance == null)
        {
            Debug.LogWarning($"[ZoneMarker:{gameObject.name}] ZoneManager 未找到，0.1s 后重试");
            Invoke(nameof(RegisterToZoneManager), 0.1f);
            return;
        }

        if (GridManager.Instance == null || FloorManager.Instance == null)
        {
            Debug.LogWarning($"[ZoneMarker:{gameObject.name}] GridManager 或 FloorManager 未就绪，0.1s 后重试");
            Invoke(nameof(RegisterToZoneManager), 0.1f);
            return;
        }

        // 1. 用 FloorManager 自动推断楼层（和 DynamicObstacle 完全一致）
        floor = FloorManager.Instance.GetFloorFromWorldY(transform.position.y);

        // 2. 计算世界空间包围盒（用 Transform.lossyScale 作为区域尺寸）
        worldBounds = new Bounds(transform.position, transform.lossyScale);

        // 3. 把包围盒内的所有格子转换为网格坐标并缓存
        cachedCells = CalculateCellsInBounds();

        // 4. 注册到 ZoneManager
        ZoneManager.Instance.RegisterZone(this);
        isRegistered = true;

        DebugLog($"Zone [{zoneId}] 注册完成 | 楼层: {floor} | 格子数: {cachedCells.Count} | 类型: {zoneType}");
    }

    /// <summary>
    /// 将包围盒内的所有格子坐标收集成列表
    /// 遍历包围盒的 X-Z 范围，逐格检查是否在范围内
    /// </summary>
    private List<Vector2Int> CalculateCellsInBounds()
    {
        var cells = new List<Vector2Int>();
        float cellSize = GridManager.Instance.CellSize;

        // 包围盒的世界坐标范围
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;

        // 从最小格到最大格遍历
        for (float x = min.x; x <= max.x; x += cellSize)
        {
            for (float z = min.z; z <= max.z; z += cellSize)
            {
                Vector2Int cell = GridManager.Instance.WorldToGrid(new Vector3(x, transform.position.y, z));
                if (!cells.Contains(cell) && GridManager.Instance.IsValid(cell))
                {
                    cells.Add(cell);
                }
            }
        }

        return cells;
    }

    // ══════════════════════════════════════════════════════
    // 公开查询接口
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 检查指定单位是否在此区域内
    /// </summary>
    public bool ContainsUnit(Vector2Int unitGridPos, int unitFloor)
    {
        if (unitFloor != floor) return false;
        return cachedCells != null && cachedCells.Contains(unitGridPos);
    }

    /// <summary>
    /// 检查世界坐标点是否在此区域内
    /// </summary>
    public bool ContainsWorldPoint(Vector3 worldPos)
    {
        return worldBounds.Contains(worldPos);
    }

    /// <summary>
    /// 获取区域内的随机一个格子（供 AI 导航使用）
    /// </summary>
    public Vector2Int GetRandomCell()
    {
        if (cachedCells == null || cachedCells.Count == 0)
            return Vector2Int.zero;
        return cachedCells[Random.Range(0, cachedCells.Count)];
    }

    /// <summary>
    /// 获取区域中心格子（近似）
    /// </summary>
    public Vector2Int GetCenterCell()
    {
        return GridManager.Instance.WorldToGrid(transform.position);
    }

    // ══════════════════════════════════════════════════════
    // 调试
    // ══════════════════════════════════════════════════════

    private void DebugLog(string message)
    {
        if (enableDebugLog)
            Debug.Log($"[ZoneMarker] {message}");
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;

        // 用颜色区分不同功能区类型
        Gizmos.color = GetZoneColor(zoneType);
        Gizmos.DrawWireCube(transform.position, transform.lossyScale);

        // 半透明填充
        Color fill = GetZoneColor(zoneType);
        fill.a = 0.15f;
        Gizmos.color = fill;
        Gizmos.DrawCube(transform.position, transform.lossyScale);
    }

    void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        // 选中时显示详细信息
        string label = $"Zone: {(string.IsNullOrEmpty(zoneId) ? "（未填ID）" : zoneId)}\n" +
                       $"类型: {zoneType}\n" +
                       $"楼层: {(Application.isPlaying ? floor.ToString() : "运行时推断")}\n" +
                       $"格子数: {(Application.isPlaying && cachedCells != null ? cachedCells.Count.ToString() : "运行时计算")}";

        UnityEditor.Handles.Label(transform.position + Vector3.up * (transform.lossyScale.y * 0.5f + 0.3f), label);
#endif
    }

    /// <summary>
    /// 不同功能区用不同颜色显示，方便编辑器识别
    /// </summary>
    private Color GetZoneColor(ZoneType type)
    {
        return type switch
        {
            ZoneType.Shop or ZoneType.Convenience or ZoneType.Restaurant
                or ZoneType.Supermarket or ZoneType.Electronics
                or ZoneType.Clothing or ZoneType.Pharmacy   => new Color(0.2f, 0.6f, 1f),   // 蓝色：商业区
            ZoneType.Corridor or ZoneType.Lobby             => new Color(0.8f, 0.8f, 0.8f), // 灰色：公共通道
            ZoneType.Escalator or ZoneType.Elevator
                or ZoneType.Stairwell                       => new Color(1f, 0.8f, 0.2f),   // 黄色：垂直交通
            ZoneType.Parking or ZoneType.LoadingDock        => new Color(0.5f, 0.5f, 0.5f), // 深灰：后勤
            ZoneType.Storage or ZoneType.SecurityRoom
                or ZoneType.StaffOnly                       => new Color(1f, 0.4f, 0.4f),   // 红色：限制区
            ZoneType.Entrance                               => new Color(0.2f, 1f, 0.4f),   // 绿色：入口
            ZoneType.EmergencyExit                          => new Color(1f, 0.2f, 0.2f),   // 亮红：紧急出口
            _                                               => new Color(0.8f, 0.6f, 1f),   // 紫色：自定义
        };
    }
}
