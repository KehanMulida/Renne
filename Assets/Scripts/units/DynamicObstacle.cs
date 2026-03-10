using UnityEngine;

/// <summary>
/// 动态障碍物组件
/// 职责：
/// 1. 将物体注册为网格障碍物
/// 2. 支持运行时开启/关闭阻挡（如门、箱子）
/// 3. 自动在 Start 时更新 GridManager 的格子状态
/// 使用方法：
/// - 挂载到门、箱子、可移动的障碍物上
/// - 调用 SetBlocking(false) 开门，SetBlocking(true) 关门
/// - 如果物体本身会移动，调用 UpdatePosition() 更新注册位置
/// </summary>
public class DynamicObstacle : MonoBehaviour
{
    [Header("障碍物配置")]
    [SerializeField] private bool blockOnStart = true;          // 游戏开始时是否阻挡
    [SerializeField] private bool autoDetectFloor = true;       // 自动根据Y坐标检测楼层
    [SerializeField] private int manualFloor = 0;               // 手动指定楼层（autoDetectFloor=false时使用）

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;
    [SerializeField] private bool showGizmos = true;

    // 运行时数据
    private Vector2Int gridPos;
    private int floor;
    private bool isBlocking = false;
    private bool isRegistered = false;

    // ============ 公开属性 ============

    public bool IsBlocking => isBlocking;
    public Vector2Int GridPos => gridPos;
    public int Floor => floor;

    // ============ 事件 ============

    /// <summary>阻挡状态改变事件</summary>
    public event System.Action<bool> OnBlockingChanged;

    // ============ 初始化 ============

    void Start()
    {
        RegisterToGrid();
    }

    void OnDestroy()
    {
        // 销毁时恢复格子为可行走
        if (isRegistered && GridManager.Instance != null)
        {
            GridManager.Instance.SetWalkable(gridPos, true, floor);
            DebugLog($"Unregistered from grid {gridPos} floor {floor}");
        }
    }

    /// <summary>
    /// 注册到 GridManager
    /// </summary>
    private void RegisterToGrid()
    {
        if (GridManager.Instance == null)
        {
            Debug.LogWarning($"[DynamicObstacle:{gameObject.name}] GridManager not found!");
            Invoke(nameof(RegisterToGrid), 0.1f);
            return;
        }

        // 计算网格位置
        gridPos = GridManager.Instance.WorldToGrid(transform.position);

        // 确定楼层
        if (autoDetectFloor && FloorManager.Instance != null)
            floor = FloorManager.Instance.GetFloorFromWorldY(transform.position.y);
        else
            floor = manualFloor;

        isRegistered = true;

        // 设置初始阻挡状态
        SetBlocking(blockOnStart);

        DebugLog($"Registered at grid {gridPos}, floor {floor}, blocking: {isBlocking}");
    }

    // ============ 公开接口 ============

    /// <summary>
    /// 设置是否阻挡
    /// 用法：
    ///   door.SetBlocking(false)  // 开门
    ///   door.SetBlocking(true)   // 关门
    /// </summary>
    public void SetBlocking(bool blocking)
    {
        if (!isRegistered)
        {
            // 还没注册完成，先记录状态等注册后处理
            blockOnStart = blocking;
            return;
        }

        if (isBlocking == blocking) return;

        isBlocking = blocking;
        GridManager.Instance.SetWalkable(gridPos, !blocking, floor);

        DebugLog($"Blocking changed to {blocking} at grid {gridPos} floor {floor}");
        OnBlockingChanged?.Invoke(isBlocking);
    }

    /// <summary>
    /// 切换阻挡状态
    /// </summary>
    public void ToggleBlocking()
    {
        SetBlocking(!isBlocking);
    }

    /// <summary>
    /// 当物体本身移动后，更新在 GridManager 中的注册位置
    /// 用法：物体移动后调用此方法
    /// </summary>
    public void UpdatePosition()
    {
        if (!isRegistered || GridManager.Instance == null) return;

        // 清除旧位置
        GridManager.Instance.SetWalkable(gridPos, true, floor);

        // 计算新位置
        gridPos = GridManager.Instance.WorldToGrid(transform.position);

        if (autoDetectFloor && FloorManager.Instance != null)
            floor = FloorManager.Instance.GetFloorFromWorldY(transform.position.y);

        // 设置新位置
        if (isBlocking)
            GridManager.Instance.SetWalkable(gridPos, false, floor);

        DebugLog($"Position updated to grid {gridPos} floor {floor}");
    }

    // ============ 工具方法 ============

    private void DebugLog(string message)
    {
        if (enableDebugLog)
            Debug.Log($"[DynamicObstacle:{gameObject.name}] {message}");
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (!showGizmos || GridManager.Instance == null) return;

        Vector2Int pos = GridManager.Instance.WorldToGrid(transform.position);
        Vector3 worldPos;

        if (FloorManager.Instance != null)
        {
            int f = autoDetectFloor
                ? FloorManager.Instance.GetFloorFromWorldY(transform.position.y)
                : manualFloor;
            worldPos = FloorManager.Instance.GridToWorld(pos, f);
        }
        else
        {
            worldPos = GridManager.Instance.GridToWorld(pos);
            worldPos.y = transform.position.y;
        }

        // 阻挡中 = 红色，开放 = 绿色
        Gizmos.color = isBlocking
            ? new Color(1, 0, 0, 0.5f)
            : new Color(0, 1, 0, 0.3f);

        Gizmos.DrawCube(worldPos + Vector3.up * 0.5f, Vector3.one * GridManager.Instance.CellSize * 0.8f);
    }

    void OnDrawGizmosSelected()
    {
        if (GridManager.Instance == null) return;

#if UNITY_EDITOR
        Vector2Int pos = GridManager.Instance.WorldToGrid(transform.position);
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 1.5f,
            $"Grid: {pos}\nFloor: {(autoDetectFloor && FloorManager.Instance != null ? FloorManager.Instance.GetFloorFromWorldY(transform.position.y) : manualFloor)}\nBlocking: {(Application.isPlaying ? isBlocking.ToString() : blockOnStart.ToString())}"
        );
#endif
    }
}
