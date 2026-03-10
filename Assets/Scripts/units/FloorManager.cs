using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 楼层连接类型
/// 定义楼层之间的连接方式
/// </summary>
public enum FloorConnectionType
{
    Stairs,         // 楼梯
    Escalator,      // 滚梯（扶梯）
    Elevator,       // 电梯
    Ladder,         // 梯子
    None            // 无连接
}

/// <summary>
/// 楼层连接点数据
/// 存储楼层之间的连接信息
/// </summary>
[System.Serializable]
public class FloorConnection
{
    public Vector2Int gridPosition;              // 连接点的网格坐标
    public int fromFloor;                        // 起始楼层
    public int toFloor;                          // 目标楼层
    public FloorConnectionType connectionType;   // 连接类型
    public bool isBidirectional = true;          // 是否双向（楼梯通常是双向的）
    public int moveCost = 1;                     // 使用此连接的移动消耗

    public FloorConnection(Vector2Int pos, int from, int to, FloorConnectionType type)
    {
        gridPosition = pos;
        fromFloor = from;
        toFloor = to;
        connectionType = type;
    }
}

/// <summary>
/// 楼层数据类
/// 存储单个楼层的信息
/// </summary>
public class FloorData
{
    public int floorNumber;                      // 楼层编号（0=地面层，1=二楼...）
    public float floorHeight;                    // 楼层的世界Y坐标高度
    public string floorName;                     // 楼层名称
    public List<FloorConnection> connections;    // 该楼层的所有连接点

    public FloorData(int number, float height, string name = "")
    {
        floorNumber = number;
        floorHeight = height;
        floorName = string.IsNullOrEmpty(name) ? $"Floor {number}" : name;
        connections = new List<FloorConnection>();
    }
}

/// <summary>
/// 楼层管理器
/// 职责：
/// 1. 管理多楼层网格数据
/// 2. 管理楼层之间的连接点（楼梯、电梯等）
/// 3. 提供跨楼层的坐标转换
/// 4. 计算声音在楼层间的衰减
/// 特点：
/// - 扩展GridManager，添加垂直维度
/// - 低耦合：不依赖具体的移动逻辑
/// - 灵活配置：支持多种连接类型
/// </summary>
// 执行顺序最高，确保在 GridManager(-100) 之前完成 Awake
[DefaultExecutionOrder(-200)]
public class FloorManager : MonoBehaviour
{
    public static FloorManager Instance { get; private set; }

    [Header("楼层配置")]
    [SerializeField] private int numberOfFloors = 3;            // 总楼层数
    [SerializeField] private float floorHeight = 3f;            // 每层高度（米）
    [SerializeField] private float groundFloorY = 0f;           // 地面层的Y坐标

    [Header("声音衰减")]
    [SerializeField] private float verticalSoundAttenuation = 0.5f;  // 楼层间声音衰减（0-1）
    [SerializeField] private int maxVerticalSoundRange = 2;          // 声音最多传播几层

    [Header("可视化")]
    [SerializeField] private bool showFloorConnections = true;
    [SerializeField] private Color connectionColor = Color.cyan;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    // 运行时数据
    private Dictionary<int, FloorData> floors;                  // 楼层数据字典
    private Dictionary<Vector3Int, FloorConnection> connectionMap;  // 连接点映射（3D坐标）

    // ============ 公开属性 ============

    public int NumberOfFloors => numberOfFloors;
    public float FloorHeight => floorHeight;

    // ============ 初始化 ============

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            InitializeFloors();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 初始化所有楼层
    /// </summary>
    private void InitializeFloors()
    {
        floors = new Dictionary<int, FloorData>();
        connectionMap = new Dictionary<Vector3Int, FloorConnection>();

        for (int i = 0; i < numberOfFloors; i++)
        {
            float height = groundFloorY + (i * floorHeight);
            FloorData floor = new FloorData(i, height, $"{i}层");
            floors[i] = floor;
        }

        Debug.Log($"[FloorManager] Initialized {numberOfFloors} floors");
    }

    // ============ 楼层坐标转换 ============

    /// <summary>
    /// 3D网格坐标转世界坐标
    /// 参数：gridPos - XZ网格坐标，floor - 楼层
    /// </summary>
    public Vector3 GridToWorld(Vector2Int gridPos, int floor)
    {
        if (!IsValidFloor(floor))
        {
            Debug.LogWarning($"Invalid floor: {floor}");
            floor = 0;
        }

        Vector3 worldPos = GridManager.Instance.GridToWorld(gridPos);
        worldPos.y = floors[floor].floorHeight;
        return worldPos;
    }

    /// <summary>
    /// 世界坐标转3D网格坐标
    /// 返回：(gridX, gridY, floor)
    /// </summary>
    public Vector3Int WorldToGrid3D(Vector3 worldPos)
    {
        Vector2Int gridPos = GridManager.Instance.WorldToGrid(worldPos);
        int floor = GetFloorFromWorldY(worldPos.y);
        return new Vector3Int(gridPos.x, gridPos.y, floor);
    }

    /// <summary>
    /// 根据世界Y坐标获取楼层
    /// </summary>
    public int GetFloorFromWorldY(float worldY)
    {
        for (int i = numberOfFloors - 1; i >= 0; i--)
        {
            if (worldY >= floors[i].floorHeight - floorHeight * 0.3f)
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>
    /// 获取楼层的世界Y坐标
    /// </summary>
    public float GetFloorWorldY(int floor)
    {
        if (IsValidFloor(floor))
        {
            return floors[floor].floorHeight;
        }
        return groundFloorY;
    }

    // ============ 楼层连接管理 ============

    /// <summary>
    /// 添加楼层连接点
    /// 用途：在关卡编辑时设置楼梯、电梯位置
    /// </summary>
    public void AddConnection(Vector2Int gridPos, int fromFloor, int toFloor, FloorConnectionType type)
    {
        if (!IsValidFloor(fromFloor) || !IsValidFloor(toFloor))
        {
            Debug.LogError($"Invalid floor connection: {fromFloor} -> {toFloor}");
            return;
        }

        FloorConnection connection = new FloorConnection(gridPos, fromFloor, toFloor, type);
        
        // 添加到楼层数据
        floors[fromFloor].connections.Add(connection);

        // 如果是双向的，添加反向连接
        if (connection.isBidirectional)
        {
            FloorConnection reverseConnection = new FloorConnection(gridPos, toFloor, fromFloor, type);
            floors[toFloor].connections.Add(reverseConnection);
        }

        // 添加到快速查找字典
        Vector3Int key = new Vector3Int(gridPos.x, gridPos.y, fromFloor);
        connectionMap[key] = connection;

        if (enableDebugLog)
        {
            Debug.Log($"[FloorManager] Added {type} connection at {gridPos}: Floor {fromFloor} -> {toFloor}");
        }
    }

    /// <summary>
    /// 检查指定位置是否有楼层连接
    /// </summary>
    public FloorConnection GetConnection(Vector2Int gridPos, int floor)
    {
        Vector3Int key = new Vector3Int(gridPos.x, gridPos.y, floor);
        return connectionMap.TryGetValue(key, out FloorConnection conn) ? conn : null;
    }

    /// <summary>
    /// 获取指定位置的所有可用连接
    /// </summary>
    public List<FloorConnection> GetAvailableConnections(Vector2Int gridPos, int currentFloor)
    {
        List<FloorConnection> available = new List<FloorConnection>();

        if (!IsValidFloor(currentFloor))
            return available;

        foreach (var connection in floors[currentFloor].connections)
        {
            if (connection.gridPosition == gridPos)
            {
                available.Add(connection);
            }
        }

        return available;
    }

    // ============ 楼层查询 ============

    /// <summary>
    /// 检查楼层编号是否有效
    /// </summary>
    public bool IsValidFloor(int floor)
    {
        return floor >= 0 && floor < numberOfFloors;
    }

    /// <summary>
    /// 获取楼层数据
    /// </summary>
    public FloorData GetFloor(int floor)
    {
        return floors.TryGetValue(floor, out FloorData data) ? data : null;
    }

    // ============ 声音传播计算 ============

    /// <summary>
    /// 计算跨楼层的声音衰减
    /// 用途：声音系统计算不同楼层之间的声音强度
    /// 参数：floorDifference - 楼层差（绝对值）
    /// 返回：衰减系数（0-1）
    /// </summary>
    public float GetVerticalSoundAttenuation(int floorDifference)
    {
        if (floorDifference == 0)
            return 1f; // 同楼层，无衰减

        if (floorDifference > maxVerticalSoundRange)
            return 0f; // 超过范围，完全听不到

        // 每层衰减
        float attenuation = Mathf.Pow(verticalSoundAttenuation, floorDifference);
        return attenuation;
    }

    /// <summary>
    /// 计算3D距离（考虑楼层）
    /// </summary>
    public float Calculate3DDistance(Vector3 pos1, Vector3 pos2)
    {
        return Vector3.Distance(pos1, pos2);
    }

    /// <summary>
    /// 计算楼层差
    /// </summary>
    public int GetFloorDifference(int floor1, int floor2)
    {
        return Mathf.Abs(floor1 - floor2);
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (!showFloorConnections || floors == null) return;

        // 绘制所有楼层连接点
        foreach (var floorData in floors.Values)
        {
            foreach (var connection in floorData.connections)
            {
                Vector3 fromPos = GridToWorld(connection.gridPosition, connection.fromFloor);
                Vector3 toPos = GridToWorld(connection.gridPosition, connection.toFloor);

                // 根据连接类型选择颜色
                Gizmos.color = GetConnectionColor(connection.connectionType);

                // 绘制连接点标记
                Gizmos.DrawSphere(fromPos, 0.3f);
                Gizmos.DrawLine(fromPos, toPos);
                Gizmos.DrawWireSphere(toPos, 0.3f);

                // 绘制箭头指示方向
                DrawArrow(fromPos, toPos);
            }
        }
    }

    private Color GetConnectionColor(FloorConnectionType type)
    {
        switch (type)
        {
            case FloorConnectionType.Stairs: return Color.cyan;
            case FloorConnectionType.Escalator: return Color.magenta;
            case FloorConnectionType.Elevator: return Color.yellow;
            case FloorConnectionType.Ladder: return Color.green;
            default: return Color.white;
        }
    }

    private void DrawArrow(Vector3 from, Vector3 to)
    {
        Vector3 direction = (to - from).normalized;
        Vector3 right = Vector3.Cross(direction, Vector3.forward).normalized;
        Vector3 arrowHead1 = to - direction * 0.3f + right * 0.15f;
        Vector3 arrowHead2 = to - direction * 0.3f - right * 0.15f;

        Gizmos.DrawLine(to, arrowHead1);
        Gizmos.DrawLine(to, arrowHead2);
    }

    void OnGUI()
    {
        if (!enableDebugLog || floors == null) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperLeft;

        string info = $"[Floor System]\n" +
                     $"Floors: {numberOfFloors}\n" +
                     $"Floor Height: {floorHeight}m\n" +
                     $"Connections: {connectionMap.Count}";

        GUI.Box(new Rect(Screen.width - 160, 400, 150, 90), info, style);
    }
}