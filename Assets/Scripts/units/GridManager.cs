using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 网格单元格数据类
/// 职责：纯数据结构，存储单个格子的信息
/// 特点：无任何逻辑，只包含数据字段
/// </summary>
[System.Serializable]
public class GridCell
{
    public Vector2Int gridPosition;  // 格子的网格坐标（二维）
    public Vector3 worldPosition;    // 格子的世界坐标（三维）
    public int floor;                // 所属楼层
    public bool isWalkable = true;   // 是否可行走
    public int moveCost = 1;         // 移动消耗（用于不同地形）
    public int height = 0;           // 高度层级，用于2.5D地形

    public GridCell(Vector2Int gridPos, Vector3 worldPos, int floorNum)
    {
        gridPosition = gridPos;
        worldPosition = worldPos;
        floor = floorNum;
    }
}

/// <summary>
/// 网格管理器 - 核心数据层
/// 职责：
/// 1. 管理整个游戏场景的网格数据
/// 2. 提供坐标转换功能（世界坐标 <-> 网格坐标）
/// 3. 提供网格查询接口
/// 特点：
/// - 单例模式，全局唯一
/// - 只负责数据管理，不包含任何游戏逻辑
/// - 其他系统通过查询接口获取网格信息
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    // ============ 配置参数 ============
    
    [Header("网格配置")]
    [SerializeField] private int gridWidth = 20;      // 网格宽度（X轴格子数量）
    [SerializeField] private int gridHeight = 20;     // 网格高度（Z轴格子数量）
    [SerializeField] private float cellSize = 1f;     // 单个格子的尺寸
    [SerializeField] private Vector3 gridOrigin = Vector3.zero;  // 网格原点位置

    [Header("多楼层支持")]
    [SerializeField] private bool useFloorSystem = true;  // 是否启用楼层系统

    [Header("障碍物检测")]
    [SerializeField] private LayerMask obstacleLayer;           // 障碍物所在Layer
    [SerializeField] private float obstacleCheckRadius = 0.4f;  // 障碍物检测半径

    // 网格数据存储：使用3D坐标(x, z, floor)作为key
    private Dictionary<Vector3Int, GridCell> gridCells;  // 改为3D坐标索引
    private Dictionary<Vector2Int, GridCell> gridCells2D; // 兼容旧版（无楼层系统时使用）

    // 公开属性：供外部只读访问
    public int Width => gridWidth;
    public int Height => gridHeight;
    public float CellSize => cellSize;
    public Vector3 Origin => gridOrigin;

    void Awake()
    {
        // 单例模式：确保场景中只有一个GridManager
        if (Instance == null)
        {
            Instance = this;
            InitializeGrid();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 初始化网格数据
    /// 遍历所有格子位置，创建GridCell并检测障碍物
    /// 支持多楼层：为每个楼层创建独立的网格
    /// </summary>
    private void InitializeGrid()
    {
        if (useFloorSystem && FloorManager.Instance != null)
        {
            InitializeMultiFloorGrid();
        }
        else
        {
            InitializeSingleFloorGrid();
        }
    }

    /// <summary>
    /// 初始化多楼层网格
    /// </summary>
    private void InitializeMultiFloorGrid()
    {
        gridCells = new Dictionary<Vector3Int, GridCell>();
        int numberOfFloors = FloorManager.Instance.NumberOfFloors;

        for (int floor = 0; floor < numberOfFloors; floor++)
        {
            float floorY = FloorManager.Instance.GetFloorWorldY(floor);

            for (int x = 0; x < gridWidth; x++)
            {
                for (int z = 0; z < gridHeight; z++)
                {
                    Vector2Int gridPos = new Vector2Int(x, z);
                    Vector3 worldPos = gridOrigin + new Vector3(x * cellSize, floorY, z * cellSize);

                    GridCell cell = new GridCell(gridPos, worldPos, floor);

                    // 障碍物检测：在特定楼层高度检测
                    Vector3 checkPos = worldPos;
                    checkPos.y += 0.5f; // 稍微抬高检测点，避免检测到地板

                    if (Physics.CheckSphere(checkPos, obstacleCheckRadius, obstacleLayer))
                    {
                        cell.isWalkable = false;
                    }

                    // 使用3D坐标作为key
                    Vector3Int key = new Vector3Int(x, z, floor);
                    gridCells[key] = cell;
                }
            }

            Debug.Log($"[GridManager] Floor {floor} grid initialized at Y={floorY}");
        }

        Debug.Log($"[GridManager] Multi-floor grid initialized: {gridWidth}x{gridHeight} x {numberOfFloors} floors");
    }

    /// <summary>
    /// 初始化单楼层网格（兼容模式）
    /// </summary>
    private void InitializeSingleFloorGrid()
    {
        gridCells2D = new Dictionary<Vector2Int, GridCell>();

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2Int gridPos = new Vector2Int(x, y);
                Vector3 worldPos = gridOrigin + new Vector3(x * cellSize, 0, y * cellSize);

                GridCell cell = new GridCell(gridPos, worldPos, 0);

                if (Physics.CheckSphere(worldPos, obstacleCheckRadius, obstacleLayer))
                {
                    cell.isWalkable = false;
                }

                gridCells2D[gridPos] = cell;
            }
        }

        Debug.Log($"[GridManager] Single-floor grid initialized: {gridWidth}x{gridHeight} cells");
    }

    // ============ 坐标转换接口 ============
    
    /// <summary>
    /// 网格坐标转世界坐标
    /// 用途：根据格子坐标计算实际的3D世界位置
    /// </summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return gridOrigin + new Vector3(gridPos.x * cellSize, 0, gridPos.y * cellSize);
    }

    /// <summary>
    /// 世界坐标转网格坐标
    /// 用途：根据3D世界位置计算对应的格子坐标
    /// 注意：使用Round进行四舍五入，确保精确对齐到格子
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 offset = worldPos - gridOrigin;
        int x = Mathf.RoundToInt(offset.x / cellSize);
        int y = Mathf.RoundToInt(offset.z / cellSize);
        return new Vector2Int(x, y);
    }

    // ============ 数据查询接口 ============
    
    /// <summary>
    /// 获取指定位置的格子数据（支持楼层）
    /// 返回：格子对象，如果坐标无效则返回null
    /// </summary>
    public GridCell GetCell(Vector2Int gridPos, int floor = 0)
    {
        if (useFloorSystem && gridCells != null)
        {
            Vector3Int key = new Vector3Int(gridPos.x, gridPos.y, floor);
            return gridCells.TryGetValue(key, out GridCell cell) ? cell : null;
        }
        else if (gridCells2D != null)
        {
            return gridCells2D.TryGetValue(gridPos, out GridCell cell) ? cell : null;
        }
        return null;
    }

    /// <summary>
    /// 检查格子坐标是否在网格范围内
    /// </summary>
    public bool IsValid(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < gridWidth && 
               gridPos.y >= 0 && gridPos.y < gridHeight;
    }

    /// <summary>
    /// 检查格子是否可行走（支持楼层）
    /// 返回：true=可行走，false=有障碍物或坐标无效
    /// </summary>
    public bool IsWalkable(Vector2Int gridPos, int floor = 0)
    {
        GridCell cell = GetCell(gridPos, floor);
        return cell != null && cell.isWalkable;
    }

    /// <summary>
    /// 获取指定格子的相邻格子（四方向，支持楼层）
    /// 用途：寻路算法需要知道当前格子可以走到哪些相邻格子
    /// 返回：可行走的相邻格子列表
    /// </summary>
    public List<Vector2Int> GetNeighbors(Vector2Int gridPos, int floor = 0)
    {
        List<Vector2Int> neighbors = new List<Vector2Int>();
        
        // 四个方向：上下左右
        Vector2Int[] directions = {
            Vector2Int.up,    // 北 (0, 1)
            Vector2Int.down,  // 南 (0, -1)
            Vector2Int.left,  // 西 (-1, 0)
            Vector2Int.right  // 东 (1, 0)
        };

        foreach (var dir in directions)
        {
            Vector2Int neighborPos = gridPos + dir;
            // 只返回有效且可行走的相邻格子（同楼层）
            if (IsValid(neighborPos) && IsWalkable(neighborPos, floor))
            {
                neighbors.Add(neighborPos);
            }
        }

        return neighbors;
    }

    // ============ 数据修改接口 ============
    
    /// <summary>
    /// 手动设置格子的可行走状态（支持楼层）
    /// 用途：运行时动态改变地形（如放置/移除障碍物）
    /// </summary>
    public void SetWalkable(Vector2Int gridPos, bool walkable, int floor = 0)
    {
        GridCell cell = GetCell(gridPos, floor);
        if (cell != null)
        {
            cell.isWalkable = walkable;
        }
    }

    // ============ 调试可视化 ============
    
    /// <summary>
    /// Gizmos绘制：在Scene视图中显示网格
    /// 绿色=可行走，红色=障碍物
    /// 支持多楼层显示
    /// </summary>
    void OnDrawGizmos()
    {
        if (useFloorSystem && gridCells != null)
        {
            // 多楼层模式：显示所有楼层的网格
            foreach (var cell in gridCells.Values)
            {
                Gizmos.color = cell.isWalkable ? new Color(0, 1, 0, 0.1f) : new Color(1, 0, 0, 0.3f);
                Gizmos.DrawWireCube(cell.worldPosition, Vector3.one * cellSize * 0.9f);
            }
        }
        else if (gridCells2D != null)
        {
            // 单楼层模式
            foreach (var cell in gridCells2D.Values)
            {
                Gizmos.color = cell.isWalkable ? new Color(0, 1, 0, 0.1f) : new Color(1, 0, 0, 0.3f);
                Gizmos.DrawWireCube(cell.worldPosition, Vector3.one * cellSize * 0.9f);
            }
        }
    }

    /// <summary>
    /// 绘制指定楼层的网格（用于调试）
    /// </summary>
    public void DrawFloorGrid(int floor)
    {
        if (!useFloorSystem || gridCells == null) return;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3Int key = new Vector3Int(x, z, floor);
                if (gridCells.TryGetValue(key, out GridCell cell))
                {
                    Gizmos.color = cell.isWalkable ? new Color(0, 1, 0, 0.3f) : new Color(1, 0, 0, 0.5f);
                    Gizmos.DrawCube(cell.worldPosition, Vector3.one * cellSize * 0.9f);
                }
            }
        }
    }
}