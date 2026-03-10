using UnityEngine;
using System.Collections.Generic;

// 执行顺序：FloorManager(-200) → GridManager(-100) → 默认(0，包括UnitMovement、TurnBasedUnit等)
// 这样可以保证 GridManager.Awake 时 FloorManager.Instance 已经存在
// 在 Unity 里也可以通过 Edit > Project Settings > Script Execution Order 设置同样效果
[DefaultExecutionOrder(-100)]

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
    public bool isWalkable = true;   // 是否可行走（静态障碍物，如墙壁）
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
/// 4. 管理动态障碍物（门、箱子等可变物体）
/// 5. 管理单位占据状态（防止多个单位站同一格）
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
    [SerializeField] private int gridWidth = 20;
    [SerializeField] private int gridHeight = 20;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector3 gridOrigin = Vector3.zero;

    [Header("多楼层支持")]
    [SerializeField] private bool useFloorSystem = true;

    [Header("障碍物检测")]
    [SerializeField] private LayerMask obstacleLayer;
    // 检测半径自动根据 cellSize 缩放，也可在 Inspector 手动覆盖
    [SerializeField] private bool autoCalculateCheckRadius = true;
    [SerializeField] private float obstacleCheckRadius = 0.4f;

    [Header("调试")]
    [SerializeField] private bool showOccupiedCells = true;   // Gizmos 显示被占据的格子

    // ============ 内部数据 ============

    // 网格数据：3D坐标(x, z, floor)作为key
    private Dictionary<Vector3Int, GridCell> gridCells;
    private Dictionary<Vector2Int, GridCell> gridCells2D;

    // 单位占据状态（玩家、敌人站在哪个格子）
    private HashSet<Vector3Int> occupiedPositions = new HashSet<Vector3Int>();

    // ============ 公开属性 ============

    public int Width => gridWidth;
    public int Height => gridHeight;
    public float CellSize => cellSize;
    public Vector3 Origin => gridOrigin;

    // ============ 初始化 ============

    void Awake()
    {
        // 只在 Awake 里注册单例
        // 网格初始化放到 Start，确保 FloorManager.Awake 已经跑完，Instance 已经赋值
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        InitializeGrid();
    }

    private void InitializeGrid()
    {
        if (autoCalculateCheckRadius)
        {
            obstacleCheckRadius = cellSize * 0.4f;
        }

        if (useFloorSystem && FloorManager.Instance != null)
        {
            Debug.Log($"[GridManager] FloorManager found with {FloorManager.Instance.NumberOfFloors} floors, using multi-floor mode");
            InitializeMultiFloorGrid();
        }
        else
        {
            if (useFloorSystem)
                Debug.LogWarning("[GridManager] useFloorSystem=true but FloorManager.Instance is null! Falling back to single floor. Check that FloorManager exists in scene.");
            InitializeSingleFloorGrid();
        }
    }

    private void InitializeMultiFloorGrid()
    {
        gridCells = new Dictionary<Vector3Int, GridCell>();
        int numberOfFloors = FloorManager.Instance.NumberOfFloors;
        float floorHeight = FloorManager.Instance.FloorHeight;

        // 先创建所有楼层的空格子（全部可行走）
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
                    gridCells[new Vector3Int(x, z, floor)] = cell;
                }
            }
        }

        // 找场景里所有 obstacleLayer 上的碰撞体
        // 用整个场景范围的大 Box 一次性获取所有障碍物
        Vector3 sceneBoundsCenter = gridOrigin + new Vector3(
            gridWidth * cellSize * 0.5f,
            floorHeight * numberOfFloors * 0.5f,
            gridHeight * cellSize * 0.5f
        );
        Vector3 sceneBoundsHalf = new Vector3(
            gridWidth * cellSize * 0.5f,
            floorHeight * numberOfFloors * 0.5f,
            gridHeight * cellSize * 0.5f
        );

        Collider[] allObstacles = Physics.OverlapBox(
            sceneBoundsCenter, sceneBoundsHalf, Quaternion.identity, obstacleLayer
        );

        int markedCount = 0;

        foreach (Collider col in allObstacles)
        {
            // 用碰撞体的 Bounds 计算它在 XZ 平面上覆盖的所有格子
            // 同时用 Bounds 的 Y 范围判断它跨越了哪些楼层
            Bounds bounds = col.bounds;

            // 计算 XZ 覆盖的格子范围
            Vector2Int minGrid = WorldToGrid(new Vector3(bounds.min.x, 0, bounds.min.z));
            Vector2Int maxGrid = WorldToGrid(new Vector3(bounds.max.x, 0, bounds.max.z));

            // 计算 Y 方向覆盖的楼层范围
            int minFloor = FloorManager.Instance.GetFloorFromWorldY(bounds.min.y);
            int maxFloor = FloorManager.Instance.GetFloorFromWorldY(bounds.max.y);

            // 遍历所有被覆盖的格子和楼层
            for (int floor = minFloor; floor <= maxFloor; floor++)
            {
                if (!FloorManager.Instance.IsValidFloor(floor)) continue;

                for (int x = minGrid.x; x <= maxGrid.x; x++)
                {
                    for (int z = minGrid.y; z <= maxGrid.y; z++)
                    {
                        if (!IsValid(new Vector2Int(x, z))) continue;

                        Vector3Int key = new Vector3Int(x, z, floor);
                        if (gridCells.TryGetValue(key, out GridCell cell))
                        {
                            cell.isWalkable = false;
                            markedCount++;
                        }
                    }
                }
            }

            Debug.Log($"[GridManager] Obstacle '{col.gameObject.name}' " +
                      $"covers grids X[{minGrid.x}~{maxGrid.x}] Z[{minGrid.y}~{maxGrid.y}] " +
                      $"floors[{minFloor}~{maxFloor}]");
        }

        Debug.Log($"[GridManager] Multi-floor grid ready: {gridWidth}x{gridHeight} x {numberOfFloors} floors, {markedCount} grid cells marked as obstacles");
    }

    private void InitializeSingleFloorGrid()
    {
        gridCells2D = new Dictionary<Vector2Int, GridCell>();
        int obstacleCount = 0;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2Int gridPos = new Vector2Int(x, y);
                Vector3 worldPos = gridOrigin + new Vector3(x * cellSize, 0, y * cellSize);

                GridCell cell = new GridCell(gridPos, worldPos, 0);

                Vector3 checkPos = new Vector3(worldPos.x, 0.5f, worldPos.z);
                if (Physics.CheckSphere(checkPos, obstacleCheckRadius, obstacleLayer))
                {
                    cell.isWalkable = false;
                    obstacleCount++;
                }

                gridCells2D[gridPos] = cell;
            }
        }

        Debug.Log($"[GridManager] Single-floor grid ready: {gridWidth}x{gridHeight}, obstacles: {obstacleCount}");
    }

    // ============ 坐标转换 ============

    /// <summary>网格坐标转世界坐标（XZ平面，Y=0）</summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return gridOrigin + new Vector3(gridPos.x * cellSize, 0, gridPos.y * cellSize);
    }

    /// <summary>
    /// 世界坐标转网格坐标
    /// 使用 FloorToInt + 0.5 偏移确保格子中心对齐
    /// 避免在格子边缘因浮点误差跳到相邻格子
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 offset = worldPos - gridOrigin;
        // 加 0.5*cellSize 偏移后 FloorToInt，等价于以格子中心为基准取整
        // 比 RoundToInt 更稳定，不受浮点精度影响
        int x = Mathf.FloorToInt((offset.x + cellSize * 0.5f) / cellSize);
        int y = Mathf.FloorToInt((offset.z + cellSize * 0.5f) / cellSize);
        return new Vector2Int(x, y);
    }

    /// <summary>
    /// 把世界坐标吸附到最近格子的中心点
    /// 在 PlayerInputController 里点击后调用，确保显示的目标位置精确居中
    /// </summary>
    public Vector3 SnapToGrid(Vector3 worldPos, int floor = 0)
    {
        Vector2Int gridPos = WorldToGrid(worldPos);

        if (FloorManager.Instance != null)
            return FloorManager.Instance.GridToWorld(gridPos, floor);

        Vector3 snapped = GridToWorld(gridPos);
        snapped.y = worldPos.y;
        return snapped;
    }

    // ============ 格子查询 ============

    /// <summary>获取格子数据（支持楼层）</summary>
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

    /// <summary>格子坐标是否在网格范围内</summary>
    public bool IsValid(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < gridWidth &&
               gridPos.y >= 0 && gridPos.y < gridHeight;
    }

    /// <summary>
    /// 检查格子是否可行走
    /// ignoreOccupied = true：只检查静态障碍物（用于计算移动范围可视化）
    /// ignoreOccupied = false（默认）：同时检查静态障碍物和单位占据（用于寻路和实际移动）
    /// </summary>
    public bool IsWalkable(Vector2Int gridPos, int floor = 0, bool ignoreOccupied = false)
    {
        GridCell cell = GetCell(gridPos, floor);
        if (cell == null || !cell.isWalkable) return false;

        // 检查单位占据
        if (!ignoreOccupied && IsOccupied(gridPos, floor)) return false;

        return true;
    }

    // ============ 单位占据管理 ============

    /// <summary>
    /// 标记/取消格子被单位占据
    /// 在 UnitMovement 的移动开始/结束时调用
    /// </summary>
    public void SetOccupied(Vector2Int gridPos, int floor, bool occupied)
    {
        Vector3Int key = new Vector3Int(gridPos.x, gridPos.y, floor);
        if (occupied)
            occupiedPositions.Add(key);
        else
            occupiedPositions.Remove(key);
    }

    /// <summary>检查格子是否被单位占据</summary>
    public bool IsOccupied(Vector2Int gridPos, int floor = 0)
    {
        Vector3Int key = new Vector3Int(gridPos.x, gridPos.y, floor);
        return occupiedPositions.Contains(key);
    }

    /// <summary>
    /// 获取相邻可行走格子（用于寻路）
    /// 注意：寻路时需要考虑占据状态，所以 ignoreOccupied = false
    /// </summary>
    public List<Vector2Int> GetNeighbors(Vector2Int gridPos, int floor = 0)
    {
        List<Vector2Int> neighbors = new List<Vector2Int>();

        Vector2Int[] directions = {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (var dir in directions)
        {
            Vector2Int neighborPos = gridPos + dir;
            if (IsValid(neighborPos) && IsWalkable(neighborPos, floor, ignoreOccupied: false))
            {
                neighbors.Add(neighborPos);
            }
        }

        return neighbors;
    }

    /// <summary>
    /// 获取相邻可行走格子（忽略占据，用于移动范围可视化）
    /// </summary>
    public List<Vector2Int> GetNeighborsIgnoreOccupied(Vector2Int gridPos, int floor = 0)
    {
        List<Vector2Int> neighbors = new List<Vector2Int>();

        Vector2Int[] directions = {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (var dir in directions)
        {
            Vector2Int neighborPos = gridPos + dir;
            if (IsValid(neighborPos) && IsWalkable(neighborPos, floor, ignoreOccupied: true))
            {
                neighbors.Add(neighborPos);
            }
        }

        return neighbors;
    }

    // ============ 动态障碍物管理 ============

    /// <summary>
    /// 手动设置格子的可行走状态
    /// 用途：
    /// - DynamicObstacle 组件调用（门开/关、箱子放置/移除）
    /// - 运行时动态改变地形
    /// </summary>
    public void SetWalkable(Vector2Int gridPos, bool walkable, int floor = 0)
    {
        GridCell cell = GetCell(gridPos, floor);
        if (cell != null)
        {
            cell.isWalkable = walkable;
            Debug.Log($"[GridManager] Grid {gridPos} floor {floor} walkable set to {walkable}");
        }
        else
        {
            Debug.LogWarning($"[GridManager] SetWalkable: cell not found at {gridPos} floor {floor}");
        }
    }

    /// <summary>
    /// 重新扫描指定格子的障碍物状态
    /// 用途：当场景中有物体移动后，重新检测该格子
    /// </summary>
    /// <summary>
    /// 重新扫描指定格子
    /// 检查是否有障碍物的 Bounds 覆盖了这个格子
    /// </summary>
    public void RescanCell(Vector2Int gridPos, int floor = 0)
    {
        GridCell cell = GetCell(gridPos, floor);
        if (cell == null) return;

        float floorY = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorWorldY(floor) : 0f;
        float floorHeight = FloorManager.Instance != null
            ? FloorManager.Instance.FloorHeight : 3f;

        // 在这个格子的世界位置做一个小范围检测
        Vector3 cellWorldPos = cell.worldPosition;
        Vector3 center = new Vector3(cellWorldPos.x, floorY + floorHeight * 0.5f, cellWorldPos.z);
        Vector3 half = new Vector3(cellSize * 0.5f, floorHeight * 0.5f, cellSize * 0.5f);

        Collider[] hits = Physics.OverlapBox(center, half, Quaternion.identity, obstacleLayer);

        bool hasObstacle = false;
        foreach (var hit in hits)
        {
            // 确认障碍物的 Bounds 确实覆盖了当前楼层高度
            int hitMinFloor = FloorManager.Instance != null
                ? FloorManager.Instance.GetFloorFromWorldY(hit.bounds.min.y) : 0;
            int hitMaxFloor = FloorManager.Instance != null
                ? FloorManager.Instance.GetFloorFromWorldY(hit.bounds.max.y) : 0;

            if (floor >= hitMinFloor && floor <= hitMaxFloor)
            {
                hasObstacle = true;
                break;
            }
        }

        cell.isWalkable = !hasObstacle;
        Debug.Log($"[GridManager] Rescanned {gridPos} floor {floor}: walkable={cell.isWalkable}");
    }

    /// <summary>
    /// 重新扫描整个楼层的障碍物状态
    /// 用途：当楼层有大量物体变化时调用（性能较重，谨慎使用）
    /// </summary>
    /// <summary>
    /// 重新扫描整个楼层
    /// 用碰撞体的 Bounds 重新计算该楼层所有障碍物覆盖的格子
    /// </summary>
    public void RescanFloor(int floor)
    {
        if (!useFloorSystem || gridCells == null) return;

        // 先把这层所有格子重置为可行走
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3Int key = new Vector3Int(x, z, floor);
                if (gridCells.TryGetValue(key, out GridCell cell))
                    cell.isWalkable = true;
            }
        }

        float floorY = FloorManager.Instance != null ? FloorManager.Instance.GetFloorWorldY(floor) : 0f;
        float floorHeight = FloorManager.Instance != null ? FloorManager.Instance.FloorHeight : 3f;

        // 只扫这一层高度范围内的障碍物
        Vector3 center = gridOrigin + new Vector3(
            gridWidth * cellSize * 0.5f,
            floorY + floorHeight * 0.5f,
            gridHeight * cellSize * 0.5f
        );
        Vector3 half = new Vector3(
            gridWidth * cellSize * 0.5f,
            floorHeight * 0.5f,
            gridHeight * cellSize * 0.5f
        );

        Collider[] obstacles = Physics.OverlapBox(center, half, Quaternion.identity, obstacleLayer);

        int markedCount = 0;
        foreach (Collider col in obstacles)
        {
            // 用 Bounds 计算覆盖的格子范围
            Bounds bounds = col.bounds;

            // 确认这个障碍物的 Y 范围确实属于当前楼层
            int colMinFloor = FloorManager.Instance != null
                ? FloorManager.Instance.GetFloorFromWorldY(bounds.min.y) : 0;
            int colMaxFloor = FloorManager.Instance != null
                ? FloorManager.Instance.GetFloorFromWorldY(bounds.max.y) : 0;

            if (floor < colMinFloor || floor > colMaxFloor) continue;

            Vector2Int minGrid = WorldToGrid(new Vector3(bounds.min.x, 0, bounds.min.z));
            Vector2Int maxGrid = WorldToGrid(new Vector3(bounds.max.x, 0, bounds.max.z));

            for (int x = minGrid.x; x <= maxGrid.x; x++)
            {
                for (int z = minGrid.y; z <= maxGrid.y; z++)
                {
                    if (!IsValid(new Vector2Int(x, z))) continue;

                    Vector3Int key = new Vector3Int(x, z, floor);
                    if (gridCells.TryGetValue(key, out GridCell cell))
                    {
                        cell.isWalkable = false;
                        markedCount++;
                    }
                }
            }
        }

        Debug.Log($"[GridManager] Rescanned floor {floor}: {markedCount} grid cells marked as obstacles");
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (useFloorSystem && gridCells != null)
        {
            foreach (var cell in gridCells.Values)
            {
                if (!cell.isWalkable)
                {
                    Gizmos.color = new Color(1, 0, 0, 0.3f);
                    Gizmos.DrawWireCube(cell.worldPosition, Vector3.one * cellSize * 0.9f);
                }
                else
                {
                    Gizmos.color = new Color(0, 1, 0, 0.05f);
                    Gizmos.DrawWireCube(cell.worldPosition, Vector3.one * cellSize * 0.9f);
                }
            }
        }
        else if (gridCells2D != null)
        {
            foreach (var cell in gridCells2D.Values)
            {
                Gizmos.color = cell.isWalkable ? new Color(0, 1, 0, 0.05f) : new Color(1, 0, 0, 0.3f);
                Gizmos.DrawWireCube(cell.worldPosition, Vector3.one * cellSize * 0.9f);
            }
        }

        // 显示被占据的格子（蓝色）
        if (showOccupiedCells && Application.isPlaying)
        {
            Gizmos.color = new Color(0, 0.5f, 1f, 0.5f);
            foreach (var key in occupiedPositions)
            {
                Vector2Int gridPos = new Vector2Int(key.x, key.y);
                int floor = key.z;

                Vector3 worldPos;
                if (FloorManager.Instance != null)
                    worldPos = FloorManager.Instance.GridToWorld(gridPos, floor);
                else
                    worldPos = GridToWorld(gridPos);

                Gizmos.DrawCube(worldPos + Vector3.up * 0.1f, Vector3.one * cellSize * 0.5f);
            }
        }
    }

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