using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 单位移动控制器
/// 职责：
/// 1. 管理单位在网格上的移动
/// 2. 提供移动相关的查询接口
/// 3. 发送移动事件通知
/// 修改点：
/// - 修复 InitializePosition 中占据标记顺序错误的 bug
/// - MoveToGrid 增加终点占据检查，防止移动到其他单位所在格
/// </summary>
public class UnitMovement : MonoBehaviour
{
    // ============ 配置参数 ============

    [Header("移动属性")]
    [SerializeField] private int moveRange = 5;
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float verticalMoveSpeed = 3f;

    [Header("移动手感")]
    [Tooltip("路径预判距离比例（0~0.9）\n越大转弯越圆滑，越小越贴格子边缘\n推荐 0.3~0.5")]
    [SerializeField][Range(0f, 0.9f)] private float lookAheadRatio = 0.4f;

    [Tooltip("转向速度（越大转向越快）\n推荐 8~15")]
    [SerializeField][Range(1f, 30f)] private float rotationSpeed = 10f;

    [Tooltip("起步速度倍率（0~1）\n0=从静止加速，1=直接全速\n推荐 0.4~0.6")]
    [SerializeField][Range(0f, 1f)] private float startSpeedRatio = 0.5f;

    [Tooltip("结尾速度倍率（0~1）\n0=接近终点完全停止，1=全速到底\n推荐 0.6~0.8")]
    [SerializeField][Range(0f, 1f)] private float endSpeedRatio = 0.7f;

    [Header("动画（可选）")]
    [SerializeField] private Animator animator;
    [SerializeField] private string moveAnimationParam = "IsMoving";

    // ============ 状态数据 ============

    private Vector2Int currentGridPosition;
    private int currentFloor = 0;
    private bool isMoving = false;
    private SoundEmitter soundEmitter;

    // ============ 公개属性 ============

    public Vector2Int CurrentGridPosition => currentGridPosition;
    public int CurrentFloor => currentFloor;
    public bool IsMoving => isMoving;
    public int MoveRange => moveRange;

    /// <summary>当前单位的移动速度（AI 和其他系统可读取）</summary>
    public float MoveSpeed => moveSpeed;

    /// <summary>起步速度倍率</summary>
    public float StartSpeedRatio => startSpeedRatio;

    /// <summary>结尾速度倍率</summary>
    public float EndSpeedRatio => endSpeedRatio;

    /// <summary>
    /// 根据路径进度（0~1）计算当前帧的速度倍率
    /// 供外部系统查询，也在内部移动协程里使用
    /// 曲线形状：起步从 startSpeedRatio 加速到 1.0，结尾从 1.0 减速到 endSpeedRatio
    /// 中段保持全速（倍率 = 1.0）
    /// </summary>
    public float EvaluateSpeedRatio(float progress)
    {
        // 前 30% 路程：从 startSpeedRatio 线性加速到 1.0
        if (progress < 0.3f)
            return Mathf.Lerp(startSpeedRatio, 1f, progress / 0.3f);

        // 后 30% 路程：从 1.0 线性减速到 endSpeedRatio
        if (progress > 0.7f)
            return Mathf.Lerp(1f, endSpeedRatio, (progress - 0.7f) / 0.3f);

        // 中间 40%：全速
        return 1f;
    }

    // ============ 配置接口 ============

    public void SetMoveRange(int range)
    {
        moveRange = Mathf.Max(1, range);
        Debug.Log($"[{gameObject.name}] MoveRange set to {moveRange}");
    }

    public void SetMoveSpeed(float speed)
    {
        moveSpeed = Mathf.Max(0.1f, speed);
        Debug.Log($"[{gameObject.name}] MoveSpeed set to {moveSpeed}");
    }

    public void SetVerticalMoveSpeed(float speed)
    {
        verticalMoveSpeed = Mathf.Max(0.1f, speed);
    }

    // ============ 事件系统 ============

    public event System.Action OnMoveComplete;
    public event System.Action<Vector2Int> OnPositionChanged;
    public event System.Action<int> OnFloorChanged;

    void Awake()
    {
        soundEmitter = GetComponent<SoundEmitter>();
    }

    void Start()
    {
        // GridManager.Start 和 UnitMovement.Start 执行顺序不确定
        // 用一帧延迟确保 GridManager 已经完成 InitializeGrid
        StartCoroutine(InitializePositionDelayed());
    }

    private IEnumerator InitializePositionDelayed()
    {
        // 等一帧，让所有 Start() 都跑完
        yield return null;
        InitializePosition();
    }

    /// <summary>
    /// 初始化单位位置
    /// 修复：先计算正确的 gridPosition 和 floor，再调用 SetOccupied
    /// 原来的代码在 currentGridPosition 还是 (0,0) 的时候就调用了 SetOccupied，导致错误位置被标记
    /// </summary>
    private void InitializePosition()
    {
        if (GridManager.Instance == null)
        {
            Debug.LogWarning($"[{gameObject.name}] GridManager not ready, delaying initialization");
            Invoke(nameof(InitializePosition), 0.1f);
            return;
        }

        float originalY = transform.position.y;

        // Step 1：先计算正确的网格坐标
        currentGridPosition = GridManager.Instance.WorldToGrid(transform.position);

        // Step 2：再确定楼层
        if (FloorManager.Instance != null)
        {
            currentFloor = FloorManager.Instance.GetFloorFromWorldY(originalY);
            Vector3 alignedPos = FloorManager.Instance.GridToWorld(currentGridPosition, currentFloor);
            transform.position = alignedPos;
        }
        else
        {
            currentFloor = 0;
            Vector3 alignedPos = GridManager.Instance.GridToWorld(currentGridPosition);
            alignedPos.y = originalY;
            transform.position = alignedPos;
        }

        // Step 3：位置确定后再标记占据（修复原来的顺序 bug）
        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);

        Debug.Log($"[{gameObject.name}] Initialized at grid: {currentGridPosition}, floor: {currentFloor}, world: {transform.position}");
    }

    // ============ 公开接口 ============

    /// <summary>
    /// 移动到目标网格（支持跨楼层）
    /// 新增：移动前检查终点是否被其他单位占据
    /// </summary>
    public void MoveToGrid(Vector2Int targetGridPos, int targetFloor = -1)
    {
        if (isMoving)
        {
            Debug.LogWarning($"[{gameObject.name}] Already moving!");
            return;
        }

        if (targetFloor < 0)
            targetFloor = currentFloor;

        if (targetGridPos == currentGridPosition && targetFloor == currentFloor)
        {
            Debug.Log($"[{gameObject.name}] Already at target position");
            return;
        }

        // 跨楼层移动
        if (targetFloor != currentFloor)
        {
            FloorConnection connection = null;
            if (FloorManager.Instance != null)
                connection = FloorManager.Instance.GetConnection(targetGridPos, currentFloor);

            if (connection != null && connection.toFloor == targetFloor)
            {
                StartCoroutine(MoveToFloorCoroutine(targetGridPos, targetFloor, connection));
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] No floor connection from floor {currentFloor} to {targetFloor} at {targetGridPos}");
            }
            return;
        }

        // 同楼层移动：检查终点是否被占据
        if (GridManager.Instance.IsOccupied(targetGridPos, currentFloor))
        {
            Debug.LogWarning($"[{gameObject.name}] Target {targetGridPos} is occupied by another unit!");
            return;
        }

        List<Vector2Int> path = PathfindingService.FindPath(currentGridPosition, targetGridPos, currentFloor);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[{gameObject.name}] No valid path to {targetGridPos}");
            return;
        }

        StartCoroutine(MoveAlongPathCoroutine(path));
    }

    /// <summary>
    /// 检查是否可以移动到目标位置
    /// 包含占据检查：不能移动到其他单位所在的格子
    /// </summary>
    public bool CanMoveTo(Vector2Int targetGridPos)
    {
        if (isMoving) return false;

        // IsWalkable 默认 ignoreOccupied=false，会同时检查障碍物和占据
        if (!GridManager.Instance.IsWalkable(targetGridPos, currentFloor)) return false;

        HashSet<Vector2Int> range = PathfindingService.CalculateMovementRange(
            currentGridPosition,
            moveRange,
            currentFloor
        );
        return range.Contains(targetGridPos);
    }

    /// <summary>
    /// 获取移动范围（用于可视化）
    /// 使用剩余 AP 或默认 moveRange
    /// </summary>
    public HashSet<Vector2Int> GetMovementRange()
    {
        TurnBasedUnit turnUnit = GetComponent<TurnBasedUnit>();
        int usablePoints = turnUnit != null ? turnUnit.RemainingActionPoints : moveRange;

        return PathfindingService.CalculateMovementRange(
            currentGridPosition,
            usablePoints,
            currentFloor
        );
    }

    /// <summary>
    /// 强制瞬移到指定格子（不检查占据，用于传送/复活等）
    /// </summary>
    public void SetGridPosition(Vector2Int gridPos)
    {
        if (!GridManager.Instance.IsValid(gridPos))
        {
            Debug.LogError($"[{gameObject.name}] Invalid grid position: {gridPos}");
            return;
        }

        // 清除旧占据
        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, false);

        currentGridPosition = gridPos;
        transform.position = GridManager.Instance.GridToWorld(gridPos);

        // 标记新占据
        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);

        OnPositionChanged?.Invoke(currentGridPosition);
    }

    /// <summary>
    /// 设置楼层（楼梯/电梯用）
    /// </summary>
    public void SetFloor(int floor)
    {
        if (FloorManager.Instance == null || !FloorManager.Instance.IsValidFloor(floor))
        {
            Debug.LogError($"[{gameObject.name}] Invalid floor: {floor}");
            return;
        }

        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, false);

        int oldFloor = currentFloor;
        currentFloor = floor;

        Vector3 newWorldPos = FloorManager.Instance.GridToWorld(currentGridPosition, currentFloor);
        transform.position = newWorldPos;

        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);

        OnFloorChanged?.Invoke(currentFloor);
        Debug.Log($"[{gameObject.name}] Floor changed: {oldFloor} -> {currentFloor}");
    }

    // ============ 私有方法 ============

    private IEnumerator MoveAlongPathCoroutine(List<Vector2Int> path)
    {
        isMoving = true;

        if (animator != null)
            animator.SetBool(moveAnimationParam, true);

        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, false);

        float cellSize = GridManager.Instance.CellSize;

        // 预判阈值：距格子中心小于此距离时提前切换到下一格目标
        float lookAheadDist = cellSize * lookAheadRatio;

        // 用于速度曲线：记录整条路径的总步数和当前步数
        int totalSteps = path.Count;
        int currentStep = 0;

        foreach (Vector2Int gridPos in path)
        {
            Vector3 targetWorldPos = FloorManager.Instance != null
                ? FloorManager.Instance.GridToWorld(gridPos, currentFloor)
                : GridManager.Instance.GridToWorld(gridPos);

            // 用于速度曲线的归一化进度（0=路径起点，1=路径终点）
            float stepProgress = (float)currentStep / Mathf.Max(totalSteps - 1, 1);

            while (true)
            {
                float dist = Vector3.Distance(transform.position, targetWorldPos);

                // ---- 1. 路径预判 ----
                // 距格子中心足够近时提前视为到达，进入下一格
                // 最后一格不做预判，必须精确到达
                bool isLastStep = (currentStep == totalSteps - 1);
                if (!isLastStep && dist < lookAheadDist)
                    break;

                // 最后一格精确到达
                if (isLastStep && dist < 0.001f)
                    break;

                // ---- 2. 速度曲线 ----
                // 用 EvaluateSpeedRatio 根据路径进度计算速度倍率
                // 起步加速、中段全速、结尾减速
                float curveMultiplier = EvaluateSpeedRatio(stepProgress);
                float frameSpeed = moveSpeed * Mathf.Clamp(curveMultiplier, 0.3f, 2f);

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    targetWorldPos,
                    frameSpeed * Time.deltaTime
                );

                // ---- 3. 平滑旋转（Slerp）----
                Vector3 dir = (targetWorldPos - transform.position);
                dir.y = 0; // 保持水平，不抬头低头
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRot,
                        rotationSpeed * Time.deltaTime
                    );
                }

                yield return null;
            }

            // 最后一格精确对齐，消除浮点误差
            if (currentStep == totalSteps - 1)
                transform.position = targetWorldPos;

            currentGridPosition = gridPos;
            currentStep++;

            if (soundEmitter != null)
                soundEmitter.EmitMovementSound();

            OnPositionChanged?.Invoke(currentGridPosition);
        }

        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);

        if (animator != null)
            animator.SetBool(moveAnimationParam, false);

        isMoving = false;

        TurnBasedUnit turnUnit = GetComponent<TurnBasedUnit>();
        if (turnUnit != null && turnUnit.IsMyTurn)
            turnUnit.ConsumeAP(path.Count);

        OnMoveComplete?.Invoke();

        Debug.Log($"[{gameObject.name}] Move complete → grid: {currentGridPosition}, floor: {currentFloor}");
    }

    private IEnumerator MoveToFloorCoroutine(Vector2Int targetGridPos, int targetFloor, FloorConnection connection)
    {
        isMoving = true;

        if (animator != null)
            animator.SetBool(moveAnimationParam, true);

        // 移动到楼层连接点
        if (targetGridPos != currentGridPosition)
        {
            GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, false);

            List<Vector2Int> pathToConnection = PathfindingService.FindPath(currentGridPosition, targetGridPos, currentFloor);
            if (pathToConnection != null)
            {
                foreach (Vector2Int gridPos in pathToConnection)
                {
                    Vector3 targetWorldPos = FloorManager.Instance.GridToWorld(gridPos, currentFloor);
                    yield return StartCoroutine(MoveToPositionCoroutine(targetWorldPos));
                    currentGridPosition = gridPos;

                    if (soundEmitter != null)
                        soundEmitter.EmitMovementSound();
                }
            }

            GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);
        }

        // 楼层切换（垂直移动）
        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, false);

        int oldFloor = currentFloor;
        currentFloor = targetFloor;

        Vector3 startFloorPos = FloorManager.Instance.GridToWorld(targetGridPos, oldFloor);
        Vector3 endFloorPos = FloorManager.Instance.GridToWorld(targetGridPos, targetFloor);

        float verticalDuration = Mathf.Abs(endFloorPos.y - startFloorPos.y) / verticalMoveSpeed;
        float elapsed = 0f;

        while (elapsed < verticalDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startFloorPos, endFloorPos, elapsed / verticalDuration);
            yield return null;
        }

        transform.position = endFloorPos;
        currentGridPosition = targetGridPos;

        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);

        OnFloorChanged?.Invoke(currentFloor);
        Debug.Log($"[{gameObject.name}] Floor changed: {oldFloor} -> {currentFloor} via {connection.connectionType}");

        if (animator != null)
            animator.SetBool(moveAnimationParam, false);

        isMoving = false;
        OnMoveComplete?.Invoke();
    }

    private IEnumerator MoveToPositionCoroutine(Vector3 targetPos)
    {
        Vector3 direction = (targetPos - transform.position).normalized;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction);

        while (Vector3.Distance(transform.position, targetPos) > 0.001f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                moveSpeed * Time.deltaTime
            );
            yield return null;
        }

        transform.position = targetPos;
    }

    // ============ 调试可视化 ============

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        // 移动范围（绿色，忽略占据）
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        HashSet<Vector2Int> range = GetMovementRange();

        foreach (Vector2Int pos in range)
        {
            Vector3 worldPos = FloorManager.Instance != null
                ? FloorManager.Instance.GridToWorld(pos, currentFloor)
                : GridManager.Instance.GridToWorld(pos);

            Gizmos.DrawCube(worldPos + Vector3.up * 0.1f, Vector3.one * GridManager.Instance.CellSize * 0.8f);
        }

        // 楼层标识
        if (FloorManager.Instance != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 3f, 0.5f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, $"Floor {currentFloor}");
#endif
        }
    }
}