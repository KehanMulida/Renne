using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 单位移动控制器
/// 职责：
/// 1. 管理单位在网格上的移动
/// 2. 提供移动相关的查询接口
/// 3. 发送移动事件通知
/// 特点：
/// - 只负责移动逻辑，不处理输入
/// - 通过事件与其他系统解耦
/// - 可以被AI或玩家输入系统调用
/// 设计模式：
/// - 观察者模式（事件系统）
/// - 命令模式（MoveToGrid作为移动命令）
/// </summary>
public class UnitMovement : MonoBehaviour
{
    // ============ 配置参数 ============
    
    [Header("移动属性")]
    [SerializeField] private int moveRange = 5;       // 每回合可移动的格子数
    [SerializeField] private float moveSpeed = 5f;    // 移动速度（格子/秒）
    [SerializeField] private float verticalMoveSpeed = 3f;  // 垂直移动速度（楼层切换时）

    [Header("动画（可选）")]
    [SerializeField] private Animator animator;                      // 动画控制器
    [SerializeField] private string moveAnimationParam = "IsMoving"; // 移动动画的Bool参数名

    // ============ 状态数据 ============
    
    private Vector2Int currentGridPosition;  // 当前所在的网格坐标（XZ）
    private int currentFloor = 0;            // 当前所在楼层
    private bool isMoving = false;           // 是否正在移动中
    private SoundEmitter soundEmitter;       // 声音发射器（可选）

    // ============ 公开属性 ============
    
    /// <summary>当前网格位置（只读）</summary>
    public Vector2Int CurrentGridPosition => currentGridPosition;
    
    /// <summary>当前楼层（只读）</summary>
    public int CurrentFloor => currentFloor;
    
    /// <summary>是否正在移动中（只读）</summary>
    public bool IsMoving => isMoving;
    
    /// <summary>移动范围（只读）</summary>
    public int MoveRange => moveRange;

    // ============ 配置接口 ============
    
    /// <summary>
    /// 设置移动范围
    /// </summary>
    public void SetMoveRange(int range)
    {
        moveRange = Mathf.Max(1, range);
        Debug.Log($"[{gameObject.name}] MoveRange set to {moveRange}");
    }

    /// <summary>
    /// 设置移动速度
    /// </summary>
    public void SetMoveSpeed(float speed)
    {
        moveSpeed = Mathf.Max(0.1f, speed);
        Debug.Log($"[{gameObject.name}] MoveSpeed set to {moveSpeed}");
    }
    /// <summary>
    /// 设置垂直移动速度
    /// </summary>
    public void SetVerticalMoveSpeed(float speed)
    {
        verticalMoveSpeed = Mathf.Max(0.1f, speed);
    }

    /// <summary>
    /// 事件系统
    /// </summary>
    
    /// <summary>移动完成事件：当单位完成移动时触发</summary>
    public event System.Action OnMoveComplete;
    
    /// <summary>位置改变事件：当单位到达新格子时触发（包括路径中的每一步）</summary>
    public event System.Action<Vector2Int> OnPositionChanged;
    
    /// <summary>楼层改变事件：当单位切换楼层时触发</summary>
    public event System.Action<int> OnFloorChanged;

    void Awake()
    {
        // 获取声音发射器（可选）
        soundEmitter = GetComponent<SoundEmitter>();
    }

    void Start()
    {
        // 在Start中初始化位置，确保FloorManager已经准备好
        InitializePosition();
    }

    /// <summary>
    /// 初始化单位位置
    /// 将单位的世界坐标对齐到最近的网格格子
    /// 注意：保持当前Y坐标，根据Y坐标识别楼层
    /// </summary>
    private void InitializePosition()
    {
        // 等待GridManager初始化
        if (GridManager.Instance == null)
        {
            Debug.LogWarning($"[{gameObject.name}] GridManager not ready, delaying initialization");
            Invoke(nameof(InitializePosition), 0.1f);
            return;
        }
        if (GridManager.Instance != null)
        {
            GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);
        }

        // 保存原始Y坐标
        float originalY = transform.position.y;

        // 根据当前世界坐标计算对应的网格坐标
        currentGridPosition = GridManager.Instance.WorldToGrid(transform.position);
        
        // 根据Y坐标确定楼层
        if (FloorManager.Instance != null)
        {
            currentFloor = FloorManager.Instance.GetFloorFromWorldY(originalY);
            
            // 对齐到网格中心，但使用楼层系统计算的精确Y坐标
            Vector3 alignedPos = FloorManager.Instance.GridToWorld(currentGridPosition, currentFloor);
            transform.position = alignedPos;
            
            Debug.Log($"[{gameObject.name}] Initialized at grid: {currentGridPosition}, floor: {currentFloor}, world: {transform.position}");
        }
        else
        {
            // 没有FloorManager，使用传统2D逻辑
            Vector3 alignedPos = GridManager.Instance.GridToWorld(currentGridPosition);
            alignedPos.y = originalY; // 保持原始Y坐标
            transform.position = alignedPos;
            
            currentFloor = 0;
            
            Debug.Log($"[{gameObject.name}] Initialized at grid: {currentGridPosition}, floor: 0 (no FloorManager), world: {transform.position}");
        }
    }

    // ============ 公开接口 ============
    
    /// <summary>
    /// 移动到目标网格（支持跨楼层）
    /// 用途：这是移动的主要接口，外部通过调用此方法来移动单位
    /// 参数：
    ///   targetGridPos - 目标网格坐标
    ///   targetFloor - 目标楼层（可选，默认-1表示同楼层）
    /// 流程：计算路径 -> 沿路径移动 -> 触发事件
    /// </summary>
    public void MoveToGrid(Vector2Int targetGridPos, int targetFloor = -1)
    {
        // 防止重复移动
        if (isMoving)
        {
            Debug.LogWarning("Unit is already moving!");
            return;
        }

        // 默认同楼层移动
        if (targetFloor < 0)
        {
            targetFloor = currentFloor;
        }

        // 已经在目标位置
        if (targetGridPos == currentGridPosition && targetFloor == currentFloor)
        {
            Debug.Log("Already at target position");
            return;
        }

        // 检查是否是跨楼层移动
        if (targetFloor != currentFloor)
        {
            // 跨楼层移动：检查是否有连接点
            FloorConnection connection = null;
            
            if (FloorManager.Instance != null)
            {
                connection = FloorManager.Instance.GetConnection(targetGridPos, currentFloor);
            }

            if (connection != null && connection.toFloor == targetFloor)
            {
                // 使用楼层连接移动
                StartCoroutine(MoveToFloorCoroutine(targetGridPos, targetFloor, connection));
            }
            else
            {
                Debug.LogWarning($"No floor connection from floor {currentFloor} to {targetFloor} at {targetGridPos}");
                return;
            }
        }
        // 同楼层移动：使用A*寻路
        else
        {
            List<Vector2Int> path = PathfindingService.FindPath(currentGridPosition, targetGridPos, currentFloor);

            if (path == null || path.Count == 0)
            {
                Debug.LogWarning("No valid path found!");
                return;
            }

            StartCoroutine(MoveAlongPathCoroutine(path));
        }
    }

    /// <summary>
    /// 检查是否可以移动到目标位置（支持楼层）
    /// 用途：在移动前进行检查，避免无效移动
    /// 检查项：
    /// 1. 单位是否正在移动
    /// 2. 目标格子是否可行走
    /// 3. 目标格子是否在移动范围内
    /// 注意：只检查同楼层移动
    /// </summary>
    public bool CanMoveTo(Vector2Int targetGridPos)
    {
        if (isMoving) return false;
        if (!GridManager.Instance.IsWalkable(targetGridPos, currentFloor)) return false;
        
        // 计算移动范围并检查目标是否在范围内（传入当前楼层）
        HashSet<Vector2Int> range = PathfindingService.CalculateMovementRange(
            currentGridPosition, 
            moveRange, 
            currentFloor
        );
        return range.Contains(targetGridPos);
    }

    /// <summary>
    /// 获取当前移动范围（支持楼层）
    /// 用途：供可视化系统或AI系统查询
    /// 返回：所有可到达的格子集合（当前楼层）
    /// </summary>
    public HashSet<Vector2Int> GetMovementRange()
    {
        TurnBasedUnit turnUnit = GetComponent<TurnBasedUnit>();

        // 如果有回合系统，用剩余 AP 作为移动范围；否则 fallback 用 moveRange
        int usablePoints = turnUnit != null ? turnUnit.RemainingActionPoints : moveRange;

        return PathfindingService.CalculateMovementRange(
            currentGridPosition,
            usablePoints,
            currentFloor
        );
    }

    /// <summary>
    /// 强制设置位置
    /// 用途：用于传送、复活等需要瞬移的场景
    /// 注意：不会播放移动动画，直接瞬移
    /// </summary>
    public void SetGridPosition(Vector2Int gridPos)
    {
        if (!GridManager.Instance.IsValid(gridPos))
        {
            Debug.LogError($"Invalid grid position: {gridPos}");
            return;
        }

        currentGridPosition = gridPos;
        transform.position = GridManager.Instance.GridToWorld(gridPos);
        OnPositionChanged?.Invoke(currentGridPosition);
    }
    /// <summary>
    /// 设置楼层（用于电梯/楼梯切换）
    /// 直接改变楼层，不播放移动动画
    /// </summary>
    public void SetFloor(int floor)
    {
        if (!FloorManager.Instance.IsValidFloor(floor))
        {
            Debug.LogError($"[UnitMovement] Invalid floor: {floor}");
            return;
        }

        int oldFloor = currentFloor;
        currentFloor = floor;

        // 更新世界坐标到新楼层
        Vector3 newWorldPos = FloorManager.Instance.GridToWorld(currentGridPosition, currentFloor);
        transform.position = newWorldPos;

        // 触发楼层改变事件
        OnFloorChanged?.Invoke(currentFloor);

        Debug.Log($"[{gameObject.name}] Floor changed: {oldFloor} -> {currentFloor}");
    }


    // ============ 私有方法 ============
    
    /// <summary>
    /// 沿路径移动的协程
    /// 核心移动逻辑：
    /// 1. 遍历路径上的每个格子
    /// 2. 计算朝向并旋转
    /// 3. 平滑移动到目标格子
    /// 4. 更新当前位置并触发事件
    /// </summary>
    private IEnumerator MoveAlongPathCoroutine(List<Vector2Int> path)
    {
        isMoving = true;
       

        if (animator != null)
        {
            animator.SetBool(moveAnimationParam, true);
        }

        // 移除旧位置占据标记
        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, false);


        // 播放移动动画
        if (animator != null)
        {
            animator.SetBool(moveAnimationParam, true);
        }

        // 遍历路径中的每个格子
        foreach (Vector2Int gridPos in path)
        {
            Vector3 targetWorldPos = FloorManager.Instance != null 
                ? FloorManager.Instance.GridToWorld(gridPos, currentFloor)
                : GridManager.Instance.GridToWorld(gridPos);
            
            // 计算朝向（2.5D俯视角：只旋转Y轴）
            Vector3 direction = (targetWorldPos - transform.position).normalized;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = targetRotation;
            }

            // 平滑移动到目标位置
            float distance = Vector3.Distance(transform.position, targetWorldPos);
            float duration = distance / moveSpeed;  // 根据速度计算移动时间
            float elapsed = 0f;

            Vector3 startPos = transform.position;

            // 使用Lerp进行平滑插值移动
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                transform.position = Vector3.Lerp(startPos, targetWorldPos, t);
                yield return null;
            }

            // 确保精确到达目标位置（避免浮点误差）
            transform.position = targetWorldPos;
            currentGridPosition = gridPos;
            
            // 发出移动声音
            if (soundEmitter != null)
            {
                soundEmitter.EmitMovementSound();
            }
        
            // 触发位置改变事件（每走一步都触发）
            OnPositionChanged?.Invoke(currentGridPosition);
        }
        GridManager.Instance.SetOccupied(currentGridPosition, currentFloor, true);  // 设置新位置占据标记
        // 停止移动动画
        if (animator != null)
        {
            animator.SetBool(moveAnimationParam, false);
        }

        isMoving = false;
        
        // 触发移动完成事件
        TurnBasedUnit turnUnit = GetComponent<TurnBasedUnit>();
        if (turnUnit != null && turnUnit.IsMyTurn)
        {
            turnUnit.ConsumeActionPoint(path.Count);
        }

        // 2.再触发移动完成
        OnMoveComplete?.Invoke();

        Debug.Log($"Move complete. Current position: {currentGridPosition}, Floor: {currentFloor}");
    }

    /// <summary>
    /// 跨楼层移动协程
    /// 用于楼梯、电梯等楼层切换
    /// </summary>
    private IEnumerator MoveToFloorCoroutine(Vector2Int targetGridPos, int targetFloor, FloorConnection connection)
    {
        isMoving = true;

        if (animator != null)
        {
            animator.SetBool(moveAnimationParam, true);
        }

        // 第一步：移动到连接点
        if (targetGridPos != currentGridPosition)
        {
            List<Vector2Int> pathToConnection = PathfindingService.FindPath(currentGridPosition, targetGridPos);
            
            if (pathToConnection != null)
            {
                foreach (Vector2Int gridPos in pathToConnection)
                {
                    Vector3 targetWorldPos = FloorManager.Instance.GridToWorld(gridPos, currentFloor);
                    yield return StartCoroutine(MoveToPositionCoroutine(targetWorldPos));
                    currentGridPosition = gridPos;
                    
                    if (soundEmitter != null)
                    {
                        soundEmitter.EmitMovementSound();
                    }
                }
            }
        }

        // 第二步：楼层切换（垂直移动）
        int oldFloor = currentFloor;
        currentFloor = targetFloor;

        Vector3 startFloorPos = FloorManager.Instance.GridToWorld(targetGridPos, oldFloor);
        Vector3 endFloorPos = FloorManager.Instance.GridToWorld(targetGridPos, targetFloor);

        // 根据连接类型调整移动方式
        float verticalDuration = Mathf.Abs(endFloorPos.y - startFloorPos.y) / verticalMoveSpeed;
        float elapsed = 0f;

        while (elapsed < verticalDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / verticalDuration;
            transform.position = Vector3.Lerp(startFloorPos, endFloorPos, t);
            yield return null;
        }

        transform.position = endFloorPos;

        // 触发楼层改变事件
        OnFloorChanged?.Invoke(currentFloor);
        
        Debug.Log($"[{gameObject.name}] Changed floor: {oldFloor} -> {currentFloor} via {connection.connectionType}");

        if (animator != null)
        {
            animator.SetBool(moveAnimationParam, false);
        }

        isMoving = false;
        OnMoveComplete?.Invoke();
    }

    /// <summary>
    /// 移动到指定世界坐标的协程
    /// 辅助方法，用于路径移动
    /// </summary>
    private IEnumerator MoveToPositionCoroutine(Vector3 targetPos)
    {
        Vector3 direction = (targetPos - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        float distance = Vector3.Distance(transform.position, targetPos);
        float duration = distance / moveSpeed;
        float elapsed = 0f;
        Vector3 startPos = transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, targetPos, elapsed / duration);
            yield return null;
        }

        transform.position = targetPos;
    }

    // ============ 调试可视化 ============
    
    /// <summary>
    /// Gizmos绘制：在Scene视图中显示移动范围
    /// 绿色半透明方块表示可移动的格子
    /// 支持多楼层：在正确的楼层高度显示
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = new Color(0, 1, 0, 0.3f);
        HashSet<Vector2Int> range = GetMovementRange();
        
        foreach (Vector2Int pos in range)
        {
            Vector3 worldPos;
            
            // 使用正确的楼层高度
            if (FloorManager.Instance != null)
            {
                worldPos = FloorManager.Instance.GridToWorld(pos, currentFloor);
            }
            else
            {
                worldPos = GridManager.Instance.GridToWorld(pos);
            }
            
            Gizmos.DrawCube(worldPos + Vector3.up * 0.1f, Vector3.one * GridManager.Instance.CellSize * 0.8f);
        }

        // 绘制当前楼层标识
        if (FloorManager.Instance != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 floorIndicator = transform.position + Vector3.up * 3f;
            Gizmos.DrawWireSphere(floorIndicator, 0.5f);
            
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(floorIndicator, $"Floor {currentFloor}");
            #endif
        }
    }
}