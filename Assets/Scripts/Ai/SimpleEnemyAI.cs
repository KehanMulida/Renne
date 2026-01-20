using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 敌人AI状态枚举
/// </summary>
public enum EnemyAIState
{
    Idle,           // 待机
    Patrol,         // 巡逻
    Investigating,  // 调查（听到声音）
    Chasing,        // 追击（看到玩家）
    Searching       // 搜索（失去视线后）
}

/// <summary>
/// 敌人AI（状态机版本）
/// 整合视觉和听觉系统
/// </summary>
[RequireComponent(typeof(UnitMovement))]
[RequireComponent(typeof(TurnBasedUnit))]
public class SimpleEnemyAI : MonoBehaviour
{
    [Header("AI设置")]
    [SerializeField] private float thinkingTime = 1f;
    [SerializeField] private float moveDelay = 0.5f;
    [SerializeField] private bool enableDebugLog = true;

    [Header("初始状态")]
    [SerializeField] private EnemyAIState initialState = EnemyAIState.Patrol;

    [Header("感知系统")]
    [SerializeField] private bool useVision = true;
    [SerializeField] private bool useSound = true;

    [Header("追击设置")]
    [SerializeField] private int searchMovesAfterLostSight = 3;  // 失去视线后搜索移动次数
    [SerializeField] private float investigationRadius = 3f;      // 调查半径

    // 组件引用
    private UnitMovement unitMovement;
    private TurnBasedUnit turnBasedUnit;
    private VisionSensor visionSensor;
    private SoundListener soundListener;

    // 状态数据
    private EnemyAIState currentState;
    private bool isExecuting = false;

    // 追踪数据
    private Vector3 lastKnownPlayerPosition;
    private int movesSinceLastSight = 0;
    private Vector3? investigationTarget;
    private Vector3 lastHeardSoundPosition;
    private bool hasHeardSound = false;
    private int lastKnownPlayerFloor = 0;
    private int targetFloor = -1;

    // ============ 初始化 ============

    void Awake()
    {
        unitMovement = GetComponent<UnitMovement>();
        turnBasedUnit = GetComponent<TurnBasedUnit>();
        visionSensor = GetComponent<VisionSensor>();
        soundListener = GetComponent<SoundListener>();

        currentState = initialState;
    }

    void Start()
    {
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart += OnMyTurnStart;
            turnBasedUnit.OnMyTurnEnd += OnMyTurnEnd;
        }

        if (unitMovement != null)
        {
            unitMovement.OnMoveComplete += OnMoveComplete;
        }

        // 订阅声音事件
        if (useSound && soundListener != null)
        {
            soundListener.OnSoundHeard += OnSoundHeard;
        }

        DebugLog($"AI initialized - Initial State: {currentState}");
    }
    /// <summary>
    /// 销毁时取消订阅事件
    /// </summary>
    void OnDestroy()
    {
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart -= OnMyTurnStart;
            turnBasedUnit.OnMyTurnEnd -= OnMyTurnEnd;
        }

        if (unitMovement != null)
        {
            unitMovement.OnMoveComplete -= OnMoveComplete;
        }

        if (soundListener != null)
        {
            soundListener.OnSoundHeard -= OnSoundHeard;
        }
    }

    // ============ 回合事件 ============

    private void OnMyTurnStart()
    {
        DebugLog($"=== Turn started - State: {currentState} ===");
        
        if (isExecuting) return;

        // 直接调用 ExecuteNextMove，不再用 ExecuteAI
        Invoke(nameof(ExecuteNextMove), thinkingTime);
    }

    private void OnMyTurnEnd()
    {
        isExecuting = false;
    }

    // ============ 感知事件 ============

    private void OnSoundHeard(SoundEvent soundEvent, float intensity)
    {
        // 检查是否是玩家的声音
          // *** 最开头加这行，确认方法是否被调用 ***
        Debug.LogWarning($"[AI:{gameObject.name}] OnSoundHeard CALLED! Source: {soundEvent.source?.name}, Intensity: {intensity}");

        // 检查是否是玩家的声音
        if (soundEvent.source == null) 
        {
            DebugLog("Sound source is null, ignoring");
            return;
        }

        TurnBasedUnit sourceUnit = soundEvent.source.GetComponent<TurnBasedUnit>();
        if (sourceUnit == null) 
        {
         DebugLog("Sound source has no TurnBasedUnit, ignoring");
            return;
        }
        
        if (sourceUnit.Faction != TurnFaction.Player) 
        {
            DebugLog($"Sound source faction is {sourceUnit.Faction}, not Player, ignoring");
            return;
        }
        // *** 移除回合限制，任何时候都记录声音位置 ***
        
        // 记录声音位置
        lastHeardSoundPosition = soundEvent.position;
        hasHeardSound = true;
        
        DebugLog($"Heard player sound at {soundEvent.position}, intensity: {intensity}");

        // 只有在非追击状态才切换到调查状态
        if (currentState != EnemyAIState.Chasing && currentState != EnemyAIState.Searching)
        {
            investigationTarget = soundEvent.position;
            ChangeState(EnemyAIState.Investigating);
        }
        
        // *** 添加：转向声音方向 ***
        LookTowardsPosition(soundEvent.position);
    }
    /// <summary>
    /// 获取最接近目标楼层的连接点
    /// </summary>
    private FloorConnection GetNearestFloorConnection(int toFloor)
    {
        if(FloorManager.Instance == null) return null;

        int currentFloor =  unitMovement.CurrentFloor;
        Vector2Int currentPos =unitMovement.CurrentGridPosition;

        FloorData floorData = FloorManager.Instance. GetFloor(currentFloor);
        if(floorData == null || floorData.connections == null) return null;

        FloorConnection nearestConnection = null;
        float nearestDistance = float.MaxValue; // 最近距离

        // 遍历所有连接，找到最接近目标楼层的连接
        foreach (var connection in floorData.connections)
        {
            // 检查这个连接是否通向目标楼层
            if (connection.toFloor == toFloor)
            {
                float distance = Vector2Int.Distance(currentPos, connection.gridPosition);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestConnection = connection;
                }
            }
        }
          if (nearestConnection != null)
        {
            DebugLog($"[Floor] Nearest connection to floor {toFloor}: {nearestConnection.gridPosition} ({nearestConnection.connectionType}), distance: {nearestDistance:F1}");
        }
        else
        {
            DebugLog($"[Floor] No connection found to floor {toFloor}");
        }

        return nearestConnection;
    }
    /// <summary>
    /// 获取所有可用的楼层连接点
    /// </summary>
    private List<FloorConnection> GetAllFloorConnections()
    {
        if (FloorManager.Instance == null) return new List<FloorConnection>();

        int currentFloor = unitMovement.CurrentFloor;
        FloorData floorData = FloorManager.Instance.GetFloor(currentFloor);
        
        if (floorData == null) return new List<FloorConnection>();
        
        return floorData.connections;
    }
    

    // ============ 状态机 ============

    /// <summary>
    /// 更新视觉感知
    /// </summary>
    private void UpdateVision()
    {
        if (!useVision || visionSensor == null) return;

        // 强制立即执行一次视觉检测
        visionSensor.PerformDetection();

        bool wasChasing = (currentState == EnemyAIState.Chasing);

        if (visionSensor.PlayerVisible)
        {
            // 看到玩家！
            lastKnownPlayerPosition = visionSensor.LastSeenPosition;
            movesSinceLastSight = 0;

            // *** 添加：记录玩家楼层 ***
            if (FloorManager.Instance != null)
            {
                lastKnownPlayerFloor = FloorManager.Instance.GetFloorFromWorldY(lastKnownPlayerPosition.y);
            }

            if (currentState != EnemyAIState.Chasing)
            {
                ChangeState(EnemyAIState.Chasing);
                Debug.LogWarning($"[AI:{gameObject.name}] !!! SPOTTED PLAYER !!! Position: {lastKnownPlayerPosition}, Floor: {lastKnownPlayerFloor}");
            }
        }
        else if (currentState == EnemyAIState.Chasing)
        {
            // 正在追击但失去视线
            ChangeState(EnemyAIState.Searching);
            Debug.LogWarning($"[AI:{gameObject.name}] Lost sight of player, starting search");
        }
    }
    // ============ 状态执行 ============

    private Vector2Int? ExecuteIdle()
    {
        return null;  // 不移动
    }

    private Vector2Int? ExecutePatrol()
    {
        return SelectRandomMove();  // 随机巡逻
    }
    /// <summary>
    /// 执行调查
    /// </summary>

   private Vector2Int? ExecuteInvestigate()
    {
        if (!investigationTarget.HasValue)
        {
            hasHeardSound = false;
            ChangeState(EnemyAIState.Patrol);
            return SelectRandomMove();
        }

        int myFloor = unitMovement.CurrentFloor;
        Vector2Int currentPos = unitMovement.CurrentGridPosition;
        
        // 获取声音来源的楼层
        int soundFloor = FloorManager.Instance != null 
            ? FloorManager.Instance.GetFloorFromWorldY(investigationTarget.Value.y) 
            : 0;

        // 检查是否需要跨楼层
        if (myFloor != soundFloor)
        {
            DebugLog($"[Investigate] Sound on different floor! My floor: {myFloor}, Sound floor: {soundFloor}");

            FloorConnection connection = GetNearestFloorConnection(soundFloor);

            if (connection != null)
            {
                if (currentPos == connection.gridPosition)
                {
                    DebugLog($"[Investigate] At connection point, moving to floor {soundFloor}");
                    unitMovement.MoveToGrid(connection.gridPosition, connection.toFloor);
                    return null;
                }
                else
                {
                    DebugLog($"[Investigate] Moving to connection point at {connection.gridPosition}");
                    return SelectMoveTowardsPosition(connection.gridPosition);
                }
            }
        }

        // 同楼层调查
        Vector2Int targetGrid = GridManager.Instance.WorldToGrid(investigationTarget.Value);

        // 到达调查点
        if (Vector2Int.Distance(currentPos, targetGrid) < investigationRadius)
        {
            investigationTarget = null;
            hasHeardSound = false;
            ChangeState(EnemyAIState.Patrol);
            DebugLog("Investigation complete, returning to patrol");
            return SelectRandomMove();
        }

        DebugLog($"[Investigate] Moving towards sound at grid {targetGrid}");
        return SelectMoveTowardsPosition(targetGrid);
    }

    private Vector2Int? ExecuteChase()
    {
        int myFloor = unitMovement.CurrentFloor;
        Vector2Int currentPos = unitMovement.CurrentGridPosition;

        // 检查是否需要跨楼层
        if (myFloor != lastKnownPlayerFloor)
        {
            DebugLog($"[Chase] Player on different floor! My floor: {myFloor}, Player floor: {lastKnownPlayerFloor}");

            // 找到最近的楼层连接点
            FloorConnection connection = GetNearestFloorConnection(lastKnownPlayerFloor);

            if (connection != null)
            {
                // 检查是否已经在连接点上
                if (currentPos == connection.gridPosition)
                {
                    // 在连接点上，执行跨楼层移动
                    DebugLog($"[Chase] At connection point, moving to floor {lastKnownPlayerFloor}");
                    unitMovement.MoveToGrid(connection.gridPosition, connection.toFloor);
                    return null; // 返回null因为已经调用了MoveToGrid
                }
                else
                {
                    // 移动到连接点
                    DebugLog($"[Chase] Moving to connection point at {connection.gridPosition}");
                    return SelectMoveTowardsPosition(connection.gridPosition);
                }
            }
            else
            {
                DebugLog($"[Chase] No connection to floor {lastKnownPlayerFloor}, searching on current floor");
            }
        }

        // 同楼层追击
        Vector2Int targetGrid = GridManager.Instance.WorldToGrid(lastKnownPlayerPosition);
        DebugLog($"[Chase] Same floor, targeting grid {targetGrid}");
        return SelectMoveTowardsPosition(targetGrid);
    }

    private Vector2Int? ExecuteSearch()
    {
        movesSinceLastSight++;

        // 搜索次数用完，回到巡逻
        if (movesSinceLastSight >= searchMovesAfterLostSight)
        {
            ChangeState(EnemyAIState.Patrol);
            DebugLog($"Search attempts exhausted ({searchMovesAfterLostSight}), returning to patrol");
            return SelectRandomMove();
        }

        int myFloor = unitMovement.CurrentFloor;
        Vector2Int currentPos = unitMovement.CurrentGridPosition;

        // 检查是否需要跨楼层搜索
        if (myFloor != lastKnownPlayerFloor)
        {
            DebugLog($"[Search] Player was on floor {lastKnownPlayerFloor}, I'm on floor {myFloor}");

            FloorConnection connection = GetNearestFloorConnection(lastKnownPlayerFloor);

            if (connection != null)
            {
                if (currentPos == connection.gridPosition)
                {
                    // 在连接点上，执行跨楼层移动
                    DebugLog($"[Search] At connection point, moving to floor {lastKnownPlayerFloor}");
                    unitMovement.MoveToGrid(connection.gridPosition, connection.toFloor);
                    return null;
                }
                else
                {
                    // 移动到连接点
                    DebugLog($"[Search] Moving to connection point at {connection.gridPosition}");
                    return SelectMoveTowardsPosition(connection.gridPosition);
                }
            }
        }

        // 同楼层搜索
        Vector2Int targetGrid = GridManager.Instance.WorldToGrid(lastKnownPlayerPosition);
        DebugLog($"Searching... ({movesSinceLastSight}/{searchMovesAfterLostSight})");
        return SelectMoveTowardsPosition(targetGrid);
    }

    // ============ 移动选择 ============

    private Vector2Int? SelectRandomMove()
    {
        HashSet<Vector2Int> range = unitMovement.GetMovementRange();  // 获取移动范围
        if (range == null || range.Count == 0) return null;

        range.Remove(unitMovement.CurrentGridPosition);  // 排除当前位置
        if (range.Count == 0) return null;

        List<Vector2Int> positions = range.ToList();
        return positions[Random.Range(0, positions.Count)];
    }

    private Vector2Int? SelectMoveTowardsPosition(Vector2Int targetPos)
    {
        HashSet<Vector2Int> range = unitMovement.GetMovementRange();
        if (range == null || range.Count == 0) return null;

        Vector2Int currentPos = unitMovement.CurrentGridPosition;

        // 如果目标在范围内，直接移动
        if (range.Contains(targetPos))
            return targetPos;

        // 选择最接近目标的位置
        Vector2Int? bestPos = null;
        float bestDistance = float.MaxValue;

        foreach (Vector2Int pos in range)
        {
            if (pos == currentPos) continue;

            float distance = Vector2Int.Distance(pos, targetPos);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPos = pos;
            }
        }

        return bestPos;
    }

    private Vector2Int? SelectPatrolMove()
    {
        return SelectRandomMove();
    }

    // ============ 状态切换 ============

    private void ChangeState(EnemyAIState newState)
    {
        if (currentState == newState) return;

        EnemyAIState oldState = currentState;
        currentState = newState;

        DebugLog($"State changed: {oldState} → {newState}");

        // 状态切换时的清理
        switch (newState)
        {
            case EnemyAIState.Patrol:
                movesSinceLastSight = 0;
                investigationTarget = null;
                hasHeardSound = false;  // *** 添加 ***
                break;

            case EnemyAIState.Searching:
                movesSinceLastSight = 0;
                break;
                
            case EnemyAIState.Chasing:
                hasHeardSound = false;  // *** 添加：追击时清除声音标记 ***
                break;
        }
    }
    /// <summary>
    /// 转向指定位置
    /// </summary>
    private void LookTowardsPosition(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0; // 保持水平
        
        if (direction.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
            DebugLog($"Turned towards sound at {targetPosition}");
        }
    }

    /// <summary>
    /// 执行下一次移动（每消耗1AP执行一次）
    /// </summary>
    private void ExecuteNextMove()
    {
          if (!turnBasedUnit.CanAct)
        {
            DebugLog("No action points, ending turn");
            EndTurn();
            return;
        }

        isExecuting = true;

        // 记录检测前的状态
        EnemyAIState stateBeforeVision = currentState;

        // 移动前先检测
        UpdateVision();
        
        // *** 添加：如果状态发生变化，输出日志确认 ***
        if (currentState != stateBeforeVision)
        {
            DebugLog($"State changed during vision check: {stateBeforeVision} → {currentState}");
        }

        DebugLog($"[Before Move] Current state: {currentState}, AP: {turnBasedUnit.RemainingActionPoints}");

        // 根据当前状态选择目标（状态已更新，会用新状态的逻辑）
        Vector2Int? targetPos = null;

        switch (currentState)
        {
            case EnemyAIState.Idle:
                targetPos = ExecuteIdle();
                break;

            case EnemyAIState.Patrol:
                targetPos = ExecutePatrol();
                break;

            case EnemyAIState.Investigating:
                targetPos = ExecuteInvestigate();
                break;

            case EnemyAIState.Chasing:
                targetPos = ExecuteChase();
                break;

            case EnemyAIState.Searching:
                targetPos = ExecuteSearch();
                break;
        }

        // 执行移动
        if (targetPos.HasValue)
        {
            DebugLog($"Moving to {targetPos.Value}");
            unitMovement.MoveToGrid(targetPos.Value);
        }
        else
        {
             // 没有有效目标，跳过这次行动但继续检查是否还有AP
            DebugLog("No valid target, skipping action");
            turnBasedUnit.SkipAction();
            
            if (turnBasedUnit.CanAct)
            {
                Invoke(nameof(ExecuteNextMove), moveDelay);
            }
            else
            {
                EndTurn();
            }
        }
    }

    // ============ 移动完成 ============

    private void OnMoveComplete()
    {
        if (!isExecuting) return;

        DebugLog($"[After Move] Position: {unitMovement.CurrentGridPosition}, Floor: {unitMovement.CurrentFloor}, AP: {turnBasedUnit.RemainingActionPoints}");

        // 移动后立即再检测一次
        UpdateVision();
        
        DebugLog($"[After Vision Check] State: {currentState}");

        // 如果还有AP，继续执行下一次移动
        if (turnBasedUnit.CanAct)
        {
            DebugLog($"Continuing with {turnBasedUnit.RemainingActionPoints} AP remaining");
            Invoke(nameof(ExecuteNextMove), moveDelay);
        }
        else
        {
            DebugLog("AP exhausted, ending turn");
            Invoke(nameof(EndTurn), moveDelay);
        }
    }

    private void EndTurn()
    {
        if (TurnSystem.Instance != null && turnBasedUnit.IsMyTurn)
        {
            TurnSystem.Instance.EndCurrentTurn();
        }
        isExecuting = false;
    }

    private void DebugLog(string message)
    {
        if (enableDebugLog)
        {
            // 根据状态使用不同颜色的日志
            string coloredState = currentState switch
            {
                EnemyAIState.Chasing => "<color=red>CHASING</color>",
                EnemyAIState.Searching => "<color=orange>SEARCHING</color>",
                EnemyAIState.Investigating => "<color=yellow>INVESTIGATING</color>",
                _ => currentState.ToString()
            };

            Debug.Log($"[AI:{gameObject.name}:{coloredState}] {message}");
        }
    }

    // ============ 可视化 ============

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // 根据状态显示不同颜色和大小
        Color stateColor;
        float sphereSize;

        switch (currentState)
        {
            case EnemyAIState.Patrol:
                stateColor = Color.green;
                sphereSize = 0.4f;
                break;
            case EnemyAIState.Investigating:
                stateColor = Color.yellow;
                sphereSize = 0.5f;
                break;
            case EnemyAIState.Chasing:
                stateColor = Color.red;
                sphereSize = 0.7f;  // 追击时更大更明显
                break;
            case EnemyAIState.Searching:
                stateColor = new Color(1f, 0.5f, 0f);  // 橙色
                sphereSize = 0.6f;
                break;
            default:
                stateColor = Color.white;
                sphereSize = 0.4f;
                break;
        }

        // 头顶状态球（更明显）
        Gizmos.color = stateColor;
        Vector3 indicatorPos = transform.position + Vector3.up * 3f;
        Gizmos.DrawSphere(indicatorPos, sphereSize);

        // 绘制状态文字
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(indicatorPos + Vector3.up * 0.5f, 
            $"<color=#{ColorUtility.ToHtmlStringRGB(stateColor)}>{currentState}</color>");
        #endif

        // 显示最后已知位置
        if (currentState == EnemyAIState.Chasing || currentState == EnemyAIState.Searching)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up, lastKnownPlayerPosition + Vector3.up);
            Gizmos.DrawWireSphere(lastKnownPlayerPosition + Vector3.up * 0.5f, 0.5f);
            
            // 绘制搜索次数
            if (currentState == EnemyAIState.Searching)
            {
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(lastKnownPlayerPosition + Vector3.up, 
                    $"Search: {movesSinceLastSight}/{searchMovesAfterLostSight}");
                #endif
            }
        }

        // 显示调查目标
        if (currentState == EnemyAIState.Investigating && investigationTarget.HasValue)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position + Vector3.up, investigationTarget.Value + Vector3.up);
            Gizmos.DrawWireSphere(investigationTarget.Value + Vector3.up * 0.5f, 0.5f);
        }
    }

    void OnGUI()
    {
        if (!enableDebugLog || !Application.isPlaying) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3.5f);
        if (screenPos.z > 0)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;

            // 根据状态设置颜色
            Color textColor = currentState switch
            {
                EnemyAIState.Chasing => Color.red,
                EnemyAIState.Searching => new Color(1f, 0.5f, 0f),
                EnemyAIState.Investigating => Color.yellow,
                _ => Color.green
            };

            style.normal.textColor = textColor;

            string stateText = currentState.ToString().ToUpper();
            if (currentState == EnemyAIState.Searching)
            {
                stateText += $"\n({movesSinceLastSight}/{searchMovesAfterLostSight})";
            }

            GUI.Label(new Rect(screenPos.x - 50, Screen.height - screenPos.y - 40, 100, 40), 
                     stateText, style);
        }
    }
}