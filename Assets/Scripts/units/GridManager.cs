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

    /// <summary>
    /// 被薄障碍挡住的「格边」位掩码：bit0=上(0,1) bit1=右(1,0) bit2=下(0,-1) bit3=左(-1,0)。
    /// 薄墙（如厕所隔板）立在两格之间、不占任何一格——格子级 isWalkable 表达不了这种阻挡：
    /// 检测不到就会被穿过去，检测到又会把两侧格子一起标成不可走（连单间都站不进）。
    /// 所以单独用「边」来表达：两侧格子都能站，但不能互相穿过。
    /// </summary>
    public byte blockedEdges = 0;

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

    [Tooltip("溢出阈值（0~0.5）\n障碍物与格子重叠面积占格子面积的比例超过此值才标记为不可走\n" +
             "0   = 只要碰到就占用\n" +
             "0.3 = 超出30%才占用（推荐）\n" +
             "与 DynamicObstacle.overflowThreshold 保持一致")]
    [Range(0f, 0.5f)]
    [SerializeField] private float overflowThreshold = 0.3f;

    [Tooltip("薄障碍「格边」检测盒：垂直范围（相对该层地面）与厚度。\n" +
             "厕所隔板这类薄墙不占格子、重叠面积达不到 overflowThreshold，格子级判定会整个忽略它，\n" +
             "人就穿过去了。做法：贴着两格之间那条边放一个薄盒子做 OverlapBox。\n" +
             "比「格心→格心射线」更稳——不受射线起点落在 collider 内、墙体略偏离格边等影响。\n" +
             "当前策略：只要存在薄障碍就挡移动，不分高矮。")]
    [SerializeField] private float edgeProbeMinHeight = 0.1f;
    [SerializeField] private float edgeProbeMaxHeight = 2.0f;
    [SerializeField] private float edgeProbeThickness = 0.08f;

    [Header("调试")]
    [SerializeField] private bool showOccupiedCells = true;   // Gizmos 显示被占据的格子
    [Tooltip("Gizmos 把被薄障碍隔断的『格边』画成红线——排查穿墙时用")]
    [SerializeField] private bool showBlockedEdges = true;

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
        if (useFloorSystem && FloorManager.Instance != null)
        {
            Debug.Log($"[GridManager] FloorManager found with {FloorManager.Instance.NumberOfFloors} floors, using multi-floor mode");
            InitializeMultiFloorGrid();
        }
        else
        {
            if (useFloorSystem)
                Debug.LogWarning("[GridManager] useFloorSystem=true but FloorManager.Instance is null! Falling back to single floor.");
            InitializeSingleFloorGrid();
        }

        // 烘焙完「格子可走性」后，再扫一遍「格边」：
        // 薄墙（厕所隔板等）不占格子、重叠面积达不到 overflowThreshold，格子级判定会整个忽略它，
        // 人就穿过去了。只能靠格心→邻格心的射线检出。
        int floorCount = (useFloorSystem && FloorManager.Instance != null)
            ? FloorManager.Instance.NumberOfFloors : 1;
        for (int f = 0; f < floorCount; f++) ScanFloorEdges(f);
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

                        // 溢出阈值判断：计算障碍物和格子的 XZ 平面重叠比例
                        // 只有重叠面积占格子面积的比例 >= (1 - overflowThreshold) 才标记不可走
                        Vector3 cellCenter = gridOrigin + new Vector3(x * cellSize, 0, z * cellSize);
                        float cellMinX = cellCenter.x - cellSize * 0.5f;
                        float cellMaxX = cellCenter.x + cellSize * 0.5f;
                        float cellMinZ = cellCenter.z - cellSize * 0.5f;
                        float cellMaxZ = cellCenter.z + cellSize * 0.5f;

                        float overlapX = Mathf.Min(bounds.max.x, cellMaxX) - Mathf.Max(bounds.min.x, cellMinX);
                        float overlapZ = Mathf.Min(bounds.max.z, cellMaxZ) - Mathf.Max(bounds.min.z, cellMinZ);

                        if (overlapX <= 0 || overlapZ <= 0) continue;

                        // 分轴判断：X 和 Z 各自的比例都必须超过阈值才占用
                        // 避免面积比例在瘦长物体上误判
                        float ratioX = overlapX / cellSize;
                        float ratioZ = overlapZ / cellSize;
                        if (ratioX < (1f - overflowThreshold) || ratioZ < (1f - overflowThreshold)) continue;

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

        // 先创建所有格子
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2Int gridPos = new Vector2Int(x, y);
                Vector3 worldPos = gridOrigin + new Vector3(x * cellSize, 0, y * cellSize);
                gridCells2D[gridPos] = new GridCell(gridPos, worldPos, 0);
            }
        }

        // 一次性获取所有障碍物，应用溢出阈值
        Vector3 center = gridOrigin + new Vector3(gridWidth * cellSize * 0.5f, 0.5f, gridHeight * cellSize * 0.5f);
        Vector3 half   = new Vector3(gridWidth * cellSize * 0.5f, 1f, gridHeight * cellSize * 0.5f);
        Collider[] allObstacles = Physics.OverlapBox(center, half, Quaternion.identity, obstacleLayer);

        int obstacleCount = 0;

        foreach (Collider col in allObstacles)
        {
            Bounds bounds = col.bounds;
            Vector2Int minGrid = WorldToGrid(new Vector3(bounds.min.x, 0, bounds.min.z));
            Vector2Int maxGrid = WorldToGrid(new Vector3(bounds.max.x, 0, bounds.max.z));

            for (int x = minGrid.x; x <= maxGrid.x; x++)
            {
                for (int z = minGrid.y; z <= maxGrid.y; z++)
                {
                    Vector2Int pos = new Vector2Int(x, z);
                    if (!IsValid(pos)) continue;

                    Vector3 cellCenter = gridOrigin + new Vector3(x * cellSize, 0, z * cellSize);
                    float overlapX = Mathf.Min(bounds.max.x, cellCenter.x + cellSize * 0.5f)
                                   - Mathf.Max(bounds.min.x, cellCenter.x - cellSize * 0.5f);
                    float overlapZ = Mathf.Min(bounds.max.z, cellCenter.z + cellSize * 0.5f)
                                   - Mathf.Max(bounds.min.z, cellCenter.z - cellSize * 0.5f);

                    if (overlapX <= 0 || overlapZ <= 0) continue;

                    // 分轴判断：X 和 Z 各自的比例都必须超过阈值才占用
                    float ratioX = overlapX / cellSize;
                    float ratioZ = overlapZ / cellSize;
                    if (ratioX < (1f - overflowThreshold) || ratioZ < (1f - overflowThreshold)) continue;

                    if (gridCells2D.TryGetValue(pos, out GridCell cell))
                    {
                        cell.isWalkable = false;
                        obstacleCount++;
                    }
                }
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
            if (IsValid(neighborPos) && IsWalkable(neighborPos, floor, ignoreOccupied: false)
                && CanCross(gridPos, neighborPos, floor))          // 薄墙：两格都能站但不能互穿
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
            if (IsValid(neighborPos) && IsWalkable(neighborPos, floor, ignoreOccupied: true)
                && CanCross(gridPos, neighborPos, floor))          // 薄墙：两格都能站但不能互穿
            {
                neighbors.Add(neighborPos);
            }
        }

        return neighbors;
    }

    // ============ 薄障碍：格边阻挡 ============

    /// <summary>格边方向表，顺序必须与 GridCell.blockedEdges 的 bit 一一对应</summary>
    private static readonly Vector2Int[] EdgeDirs =
    {
        new Vector2Int(0, 1),   // bit0 上
        new Vector2Int(1, 0),   // bit1 右
        new Vector2Int(0, -1),  // bit2 下
        new Vector2Int(-1, 0)   // bit3 左
    };

    private static int EdgeBitOf(Vector2Int dir)
    {
        for (int i = 0; i < 4; i++) if (EdgeDirs[i] == dir) return i;
        return -1;
    }

    /// <summary>
    /// 相邻两格之间能否通过（薄墙判定）。非相邻格返回 true——跨格移动请逐步校验。
    /// </summary>
    public bool CanCross(Vector2Int from, Vector2Int to, int floor = 0)
    {
        int bit = EdgeBitOf(to - from);
        if (bit < 0) return true;                     // 不相邻：不由本函数负责

        var a = GetCell(from, floor);
        if (a != null && (a.blockedEdges & (1 << bit)) != 0) return false;

        // 同一条边在对侧也记了一份（建表时对称写入），双向确认更稳
        var b = GetCell(to, floor);
        if (b != null && (b.blockedEdges & (1 << ((bit + 2) % 4))) != 0) return false;

        return true;
    }

    /// <summary>
    /// 扫描某格的四条边：格心→邻格心 射线打到 Obstacle 即视为被薄墙隔断。
    /// 对称写入两侧，保证 CanCross 双向一致。
    /// </summary>
    public void ScanCellEdges(Vector2Int gridPos, int floor = 0)
    {
        for (int i = 0; i < 4; i++) ScanOneEdge(gridPos, i, floor);
    }

    private static readonly Collider[] EdgeProbeBuf = new Collider[1];

    /// <summary>
    /// 扫描某格第 i 条边：贴着这条边放一个薄盒子，碰到 Obstacle 即视为隔断。
    /// 结果对称写入两侧，保证 CanCross 双向一致。
    /// </summary>
    private void ScanOneEdge(Vector2Int gridPos, int i, int floor)
    {
        var cell = GetCell(gridPos, floor);
        if (cell == null) return;
        var nCell = GetCell(gridPos + EdgeDirs[i], floor);
        if (nCell == null) return;

        Vector2Int d      = EdgeDirs[i];
        float      floorY = cell.worldPosition.y;

        // 盒子中心 = 两格中点（即那条边），高度取该层的探测区间
        Vector3 mid = (cell.worldPosition + nCell.worldPosition) * 0.5f;
        mid.y = floorY + (edgeProbeMinHeight + edgeProbeMaxHeight) * 0.5f;

        // 沿跨越方向很薄、沿边方向铺满一格
        Vector3 half = new Vector3(
            d.x != 0 ? edgeProbeThickness : cellSize * 0.45f,
            Mathf.Max(0.05f, (edgeProbeMaxHeight - edgeProbeMinHeight) * 0.5f),
            d.y != 0 ? edgeProbeThickness : cellSize * 0.45f);

        bool blocked = Physics.OverlapBoxNonAlloc(
            mid, half, EdgeProbeBuf, Quaternion.identity, obstacleLayer) > 0;

        // ★ 关键：OverlapBox 检测不到「非凸 MeshCollider」（Unity 已知限制），而射线可以。
        //   美术模型自带的 Mesh Collider 正属此类，所以必须再补射线兜底。
        //   高低各打一条，兼顾不同高度的薄墙。
        if (!blocked) blocked = LinecastEdge(cell.worldPosition, nCell.worldPosition, floorY);

        SetEdge(cell,  i,           blocked);
        SetEdge(nCell, (i + 2) % 4, blocked);   // 对侧同一条边
    }

    /// <summary>格心→邻格心 打两条不同高度的射线（能命中非凸 MeshCollider）</summary>
    private bool LinecastEdge(Vector3 a, Vector3 b, float floorY)
    {
        float hLow  = floorY + edgeProbeMinHeight + 0.3f;
        float hHigh = floorY + Mathf.Max(edgeProbeMinHeight + 0.4f, edgeProbeMaxHeight * 0.6f);

        return Physics.Linecast(new Vector3(a.x, hLow,  a.z), new Vector3(b.x, hLow,  b.z), obstacleLayer)
            || Physics.Linecast(new Vector3(a.x, hHigh, a.z), new Vector3(b.x, hHigh, b.z), obstacleLayer);
    }

    private static readonly Collider[] EdgeDiagBuf = new Collider[8];

    /// <summary>
    /// 诊断：列出某条格边上的**所有**碰撞体（不限 Layer），用来排查"看着有墙却没挡住"。
    /// 返回 null = 这条边上确实什么都没有。
    /// </summary>
    public string DescribeEdge(Vector2Int from, Vector2Int to, int floor = 0)
    {
        int i = EdgeBitOf(to - from);
        if (i < 0) return null;

        var cell  = GetCell(from, floor);
        var nCell = GetCell(to, floor);
        if (cell == null || nCell == null) return null;

        Vector2Int d = EdgeDirs[i];
        Vector3 mid = (cell.worldPosition + nCell.worldPosition) * 0.5f;
        mid.y = cell.worldPosition.y + (edgeProbeMinHeight + edgeProbeMaxHeight) * 0.5f;
        Vector3 half = new Vector3(
            d.x != 0 ? edgeProbeThickness : cellSize * 0.45f,
            Mathf.Max(0.05f, (edgeProbeMaxHeight - edgeProbeMinHeight) * 0.5f),
            d.y != 0 ? edgeProbeThickness : cellSize * 0.45f);

        var sb = new System.Text.StringBuilder();

        int n = Physics.OverlapBoxNonAlloc(mid, half, EdgeDiagBuf, Quaternion.identity, ~0);
        for (int k = 0; k < n; k++)
        {
            var c = EdgeDiagBuf[k];
            if (c == null) continue;
            sb.Append($"[Box:{c.name} layer={LayerMask.LayerToName(c.gameObject.layer)}({c.gameObject.layer})" +
                      $"{(c.isTrigger ? " Trigger" : "")}] ");
        }

        // 射线能命中非凸 MeshCollider（OverlapBox 不行），单独再报一次
        float floorY = cell.worldPosition.y;
        Vector3 a = cell.worldPosition, b = nCell.worldPosition;
        float hLow  = floorY + edgeProbeMinHeight + 0.3f;
        float hHigh = floorY + Mathf.Max(edgeProbeMinHeight + 0.4f, edgeProbeMaxHeight * 0.6f);
        foreach (float h in new[] { hLow, hHigh })
        {
            if (Physics.Linecast(new Vector3(a.x, h, a.z), new Vector3(b.x, h, b.z), out RaycastHit hit, ~0))
                sb.Append($"[Ray@{h - floorY:F1}:{hit.collider.name} " +
                          $"layer={LayerMask.LayerToName(hit.collider.gameObject.layer)}({hit.collider.gameObject.layer})] ");
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static void SetEdge(GridCell cell, int bit, bool blocked)
    {
        if (blocked) cell.blockedEdges |= (byte)(1 << bit);
        else         cell.blockedEdges &= unchecked((byte)~(1 << bit));
    }

    /// <summary>重扫整层所有格边（网格烘焙后、或地形大变动后调用）</summary>
    public void ScanFloorEdges(int floor = 0)
    {
        // 每条内部边只扫一次：只扫「上」「右」，对侧由 ScanOneEdge 对称写入（开销减半）
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                ScanOneEdge(new Vector2Int(x, y), 0, floor);   // 上
                ScanOneEdge(new Vector2Int(x, y), 1, floor);   // 右
            }

        // 诊断：统计被隔断的边。每条边在两侧各记一次，所以 /2。
        int bits = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                var c = GetCell(new Vector2Int(x, y), floor);
                if (c == null) continue;
                for (int i = 0; i < 4; i++)
                    if ((c.blockedEdges & (1 << i)) != 0) bits++;
            }

        Debug.Log($"[GridManager] 楼层 {floor} 格边扫描：{bits / 2} 条边被阻挡" +
                  (bits == 0 ? "  ← 0 条！薄墙多半不在 obstacleLayer(Obstacle) 上，检查它的 Layer" : ""));
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

        // 同步重扫这一格的四条边：门开关 / 物体被推走都可能改变薄障碍
        ScanCellEdges(gridPos, floor);

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

        ScanFloorEdges(floor);   // 格子重扫完，格边也要跟着重扫

        Debug.Log($"[GridManager] Rescanned floor {floor}: {markedCount} grid cells marked as obstacles");
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        // 薄障碍「格边」可视化：被隔断的边画成红色线段。
        // 排查穿墙：如果隔板处**没有红线**，说明边扫描没检出它（多半是 Layer 不对）。
        if (showBlockedEdges && gridCells != null)
        {
            Gizmos.color = new Color(1f, 0.25f, 0f, 0.95f);
            foreach (var cell in gridCells.Values)
            {
                if (cell.blockedEdges == 0) continue;
                for (int i = 0; i < 4; i++)
                {
                    if ((cell.blockedEdges & (1 << i)) == 0) continue;
                    Vector2Int d = EdgeDirs[i];
                    Vector3 dir  = new Vector3(d.x, 0, d.y);
                    Vector3 perp = new Vector3(-d.y, 0, d.x);
                    Vector3 mid  = cell.worldPosition + dir * (cellSize * 0.5f) + Vector3.up * 0.05f;
                    Gizmos.DrawLine(mid - perp * (cellSize * 0.5f), mid + perp * (cellSize * 0.5f));
                }
            }
        }

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