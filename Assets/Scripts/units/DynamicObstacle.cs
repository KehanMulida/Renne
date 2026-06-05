using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 动态障碍物组件
/// 职责：
/// 1. 将物体注册为网格障碍物
/// 2. 支持运行时开启/关闭阻挡（如门、箱子）
/// 3. 支持溢出阈值：物体边缘超出格子边界超过阈值才占用该格子
/// </summary>
public class DynamicObstacle : MonoBehaviour
{
    [Header("障碍物配置")]
    [SerializeField] private bool blockOnStart = true;
    [SerializeField] private bool autoDetectFloor = true;
    [SerializeField] private int manualFloor = 0;

    [Header("溢出配置")]
    [Tooltip("使用碰撞体 Bounds 检测占用格子（比单点更准确）\n关闭则只用中心点")]
    [SerializeField] private bool useBoundsDetection = true;

    [Tooltip("溢出阈值（0~0.5）\n物体边缘超出格子边界超过此比例才占用该格子\n" +
             "0   = 只要碰到就占用\n" +
             "0.3 = 超出格子30%才占用（推荐）\n" +
             "0.5 = 超出一半才占用\n\n" +
             "⚠️ 需要配合 GridManager.obstacleCheckRadius 使用\n" +
             "obstacleCheckRadius = cellSize × (0.5 - overflowThreshold)\n" +
             "例：cellSize=1, threshold=0.3 → radius应设为0.2")]
    [Range(0f, 0.5f)]
    [SerializeField] private float overflowThreshold = 0.3f;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;
    [SerializeField] private bool showGizmos = true;

    // 运行时数据
    private List<Vector2Int> occupiedGrids = new List<Vector2Int>(); // 支持多格占用
    private int floor;
    private bool isBlocking = false;
    private bool isRegistered = false;

    public bool IsBlocking => isBlocking;
    public int Floor => floor;

    // 兼容旧接口（只取第一个格子）
    public Vector2Int GridPos => occupiedGrids.Count > 0 ? occupiedGrids[0] : Vector2Int.zero;

    public event System.Action<bool> OnBlockingChanged;

    // ============ 初始化 ============

    void Start()
    {
        RegisterToGrid();
    }

    void OnDestroy()
    {
        if (isRegistered && GridManager.Instance != null)
        {
            foreach (var pos in occupiedGrids)
                GridManager.Instance.SetWalkable(pos, true, floor);
        }
    }

    private void RegisterToGrid()
    {
        if (GridManager.Instance == null)
        {
            Invoke(nameof(RegisterToGrid), 0.1f);
            return;
        }

        floor = autoDetectFloor && FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorFromWorldY(transform.position.y)
            : manualFloor;

        occupiedGrids = CalculateOccupiedGrids();
        isRegistered = true;

        SetBlocking(blockOnStart);

        DebugLog($"Registered at {string.Join(", ", occupiedGrids)} floor {floor} | blocking:{isBlocking}");
    }

    /// <summary>
    /// 计算当前物体实际占用的格子列表
    /// 基于 Bounds + 溢出阈值，避免边缘微小溢出多占格子
    /// </summary>
    private List<Vector2Int> CalculateOccupiedGrids()
    {
        var result = new List<Vector2Int>();
        float cellSize = GridManager.Instance.CellSize;

        if (!useBoundsDetection)
        {
            // 只用中心点
            result.Add(GridManager.Instance.WorldToGrid(transform.position));
            return result;
        }

        // 获取碰撞体 Bounds
        Bounds bounds = GetCombinedBounds();

        // 计算 Bounds 覆盖的格子范围
        Vector2Int minGrid = GridManager.Instance.WorldToGrid(
            new Vector3(bounds.min.x, transform.position.y, bounds.min.z));
        Vector2Int maxGrid = GridManager.Instance.WorldToGrid(
            new Vector3(bounds.max.x, transform.position.y, bounds.max.z));

        for (int x = minGrid.x; x <= maxGrid.x; x++)
        {
            for (int y = minGrid.y; y <= maxGrid.y; y++)
            {
                Vector2Int gridPos = new Vector2Int(x, y);
                if (!GridManager.Instance.IsValid(gridPos)) continue;

                // 计算格子的世界空间范围
                Vector3 cellCenter = GridManager.Instance.GridToWorld(gridPos);
                float cellMinX = cellCenter.x - cellSize * 0.5f;
                float cellMaxX = cellCenter.x + cellSize * 0.5f;
                float cellMinZ = cellCenter.z - cellSize * 0.5f;
                float cellMaxZ = cellCenter.z + cellSize * 0.5f;

                // 计算 Bounds 和格子的重叠量
                float overlapX = Mathf.Min(bounds.max.x, cellMaxX) - Mathf.Max(bounds.min.x, cellMinX);
                float overlapZ = Mathf.Min(bounds.max.z, cellMaxZ) - Mathf.Max(bounds.min.z, cellMinZ);

                if (overlapX <= 0 || overlapZ <= 0) continue;

                // 分轴判断：X 和 Z 各自的比例都必须超过阈值才占用
                float ratioX = overlapX / cellSize;
                float ratioZ = overlapZ / cellSize;
                if (ratioX < (1f - overflowThreshold) || ratioZ < (1f - overflowThreshold)) continue;

                result.Add(gridPos);
            }
        }

        // 至少占用中心格子
        if (result.Count == 0)
            result.Add(GridManager.Instance.WorldToGrid(transform.position));

        return result;
    }

    /// <summary>获取所有碰撞体的合并 Bounds</summary>
    private Bounds GetCombinedBounds()
    {
        var colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            // 没有碰撞体，用 Renderer Bounds
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                return b;
            }
            return new Bounds(transform.position, Vector3.one * 0.5f);
        }

        Bounds bounds = colliders[0].bounds;
        foreach (var col in colliders)
            bounds.Encapsulate(col.bounds);
        return bounds;
    }

    // ============ 公开接口 ============

    public void SetBlocking(bool blocking)
    {
        if (!isRegistered) { blockOnStart = blocking; return; }
        if (isBlocking == blocking) return;

        isBlocking = blocking;
        foreach (var pos in occupiedGrids)
            GridManager.Instance.SetWalkable(pos, !blocking, floor);

        DebugLog($"Blocking:{blocking} | grids:{string.Join(", ", occupiedGrids)}");
        OnBlockingChanged?.Invoke(isBlocking);
    }

    public void ToggleBlocking() => SetBlocking(!isBlocking);

    public void UpdatePosition()
    {
        if (!isRegistered || GridManager.Instance == null) return;

        // 清除旧格子
        foreach (var pos in occupiedGrids)
            GridManager.Instance.SetWalkable(pos, true, floor);

        // 重新计算
        if (autoDetectFloor && FloorManager.Instance != null)
            floor = FloorManager.Instance.GetFloorFromWorldY(transform.position.y);

        occupiedGrids = CalculateOccupiedGrids();

        if (isBlocking)
            foreach (var pos in occupiedGrids)
                GridManager.Instance.SetWalkable(pos, false, floor);

        DebugLog($"Position updated | grids:{string.Join(", ", occupiedGrids)}");
    }

    // ============ 调试 ============

    private void DebugLog(string message)
    {
        if (enableDebugLog)
            Debug.Log($"[DynamicObstacle:{gameObject.name}] {message}");
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || GridManager.Instance == null) return;

        int f = autoDetectFloor && FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorFromWorldY(transform.position.y)
            : manualFloor;

        if (Application.isPlaying)
        {
            foreach (var pos in occupiedGrids)
            {
                Vector3 worldPos = FloorManager.Instance != null
                    ? FloorManager.Instance.GridToWorld(pos, f)
                    : GridManager.Instance.GridToWorld(pos);

                Gizmos.color = isBlocking
                    ? new Color(1, 0, 0, 0.5f)
                    : new Color(0, 1, 0, 0.3f);
                Gizmos.DrawCube(worldPos + Vector3.up * 0.5f,
                    Vector3.one * GridManager.Instance.CellSize * 0.8f);
            }
        }
        else
        {
            // Editor 中预览
            var previewGrids = CalculateOccupiedGridsEditor(f);
            foreach (var pos in previewGrids)
            {
                Vector3 worldPos = FloorManager.Instance != null
                    ? FloorManager.Instance.GridToWorld(pos, f)
                    : GridManager.Instance.GridToWorld(pos);

                Gizmos.color = blockOnStart
                    ? new Color(1, 0, 0, 0.4f)
                    : new Color(0, 1, 0, 0.2f);
                Gizmos.DrawCube(worldPos + Vector3.up * 0.5f,
                    Vector3.one * GridManager.Instance.CellSize * 0.8f);
            }
        }
    }

    private List<Vector2Int> CalculateOccupiedGridsEditor(int f)
    {
        if (GridManager.Instance == null) return new List<Vector2Int>();
        return CalculateOccupiedGrids();
    }

    void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (GridManager.Instance == null) return;
        int f = autoDetectFloor && FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorFromWorldY(transform.position.y)
            : manualFloor;

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 1.5f,
            $"Floor:{f} | Blocking:{(Application.isPlaying ? isBlocking.ToString() : blockOnStart.ToString())}\n" +
            $"Threshold:{overflowThreshold:P0} | Grids:{(Application.isPlaying ? string.Join(",", occupiedGrids) : "runtime only")}"
        );
#endif
    }
}