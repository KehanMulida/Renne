using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class EnemyAIController : MonoBehaviour, IDamageable
{
    public EnemyConfig config;

    [Header("身份")]
    [Tooltip("Enemy 编号，对应 MissionData.assignedEnemies，如 E01")]
    public string enemyId;

    // 任务优先级队列：index 0 = 当前执行的最高优先级任务
    private List<MissionContext> _missionQueue = new List<MissionContext>();
    public MissionContext CurrentMission => _missionQueue.Count > 0 ? _missionQueue[0] : null;
    // 向后兼容的属性名
    public MissionContext MissionContext => CurrentMission;

    private PerceptionAggregator perception;
    private Dictionary<string, IActionExecutor> executors;
    private BTRunner behaviorTree;
    private Dictionary<string, object> blackboard;
    private TurnBasedUnit turnBasedUnit;
    private UnitMovement unitMovement;
    private bool isExecuting = false;
    public bool IsExecuting => isExecuting;

    private int  searchConfidence = 0;
    // searchConfidence / soundInvestigateCountdown 每回合只应递减一次
    private bool _confidenceDecrementedThisTurn = false;
    // 本次 action 开始时的格子位置（判断是否真的移动了，决定是否等待 ActionInterval）
    private Vector2Int _lastActionStartGrid;

    // 第一次收到 MissionContext 时缓存 UnitMovement 的原始速度
    // 之后所有速度倍率都基于这个基准值计算，避免倍率叠乘
    private float _baseMoveSpeed = -1f;

    private float TurnDelay => config.turnStartDelay;
    private float ActionInterval => config.actionInterval;

    // 缓存组件引用，避免每次 EvaluateState 都调用 GetComponent
    private EnemyEquipment enemyEquipment;
    private EnemyInventory enemyInventory;
    // MAX_SEARCH_CONFIDENCE 移到 config.searchConfidenceMax，不再硬编码

    private int currentHp;

    // 上一回合走过的格子，Patrol 选目标时优先避开
    private HashSet<Vector2Int> lastTurnVisitedCells = new HashSet<Vector2Int>();
    // 本回合走过的格子，回合结束时转移到 lastTurnVisitedCells
    private HashSet<Vector2Int> currentTurnVisitedCells = new HashSet<Vector2Int>();

    public int MaxHp => config.maxHp;
    public int CurrentHp => currentHp;
    public bool IsAlive => currentHp > 0;
    public string EnemyId => enemyId;

    public void TakeDamage(int damage, GameObject attacker = null)
    {
        if (!IsAlive) return;

        currentHp -= damage;
        Debug.Log($"[Enemy:{gameObject.name}] TakeDamage: -{damage} | HP: {currentHp}/{MaxHp}");

        // 被攻击时进入战斗模式
        if (attacker != null)
        {
            // 记录攻击者位置，让敌人往那个方向追
            blackboard["lastSeenPosition"] = attacker.transform.position;
            blackboard["lastSeenFloor"]    = unitMovement.CurrentFloor;
            blackboard["hasVisualContact"] = false; // 不一定能看到，但知道来源方向
            searchConfidence               = config.searchConfidenceMax;

            Debug.Log($"[Enemy:{gameObject.name}] Attacked by {attacker.name}, heading to {attacker.transform.position}");

            // 通知进入战斗模式
            if (CombatModeManager.Instance != null)
                CombatModeManager.Instance.NotifyEnemyEnterCombat(this);
        }

        if (currentHp <= 0)
            SetDeadState();
    }

    /// <summary>
    /// 统一数值事件入口。扩展新 stat 只需在此加一个 case。
    /// </summary>
    private void HandleStatEffect(EnemyStatEffect effect)
    {
        switch (effect.statKey)
        {
            case EnemyStatEffect.HP:
                Heal(effect.value);
                break;
            case EnemyStatEffect.Stamina:
                // stamina 系统暂未实现，占位
                Debug.Log($"[{gameObject.name}] Stamina +{effect.value}（系统待实现）");
                break;
            case EnemyStatEffect.Sanity:
                // sanity 系统暂未实现，占位
                Debug.Log($"[{gameObject.name}] Sanity +{effect.value}（系统待实现）");
                break;
            default:
                Debug.LogWarning($"[{gameObject.name}] 未处理的 stat key: {effect.statKey}");
                break;
        }
    }

    public void Heal(int amount)
    {
        if (!IsAlive) return;
        currentHp = Mathf.Min(currentHp + amount, config.maxHp);
        Debug.Log($"[Enemy:{gameObject.name}] Heal: +{amount} | HP: {currentHp}/{config.maxHp}");
    }

    private void SetDeadState()
    {
        currentHp = 0;
        Debug.Log($"[Enemy:{gameObject.name}] Dead");

        // 停止感知更新
        if (perception != null)
            perception.OnAnyPerception -= HandlePerception;

        // 通知 CombatModeManager
        if (CombatModeManager.Instance != null)
            CombatModeManager.Instance.UnregisterEnemy(this);

        // 释放占据的格子，让玩家可以走过尸体位置（可选）
        // 如果想让尸体阻挡，注释掉下面这行
        if (GridManager.Instance != null)
            GridManager.Instance.SetOccupied(
                unitMovement.CurrentGridPosition, unitMovement.CurrentFloor, false);

        // 死亡动画接入点
        // animator?.SetTrigger("Die");

        // 禁用 AI 组件但保留 GameObject
        this.enabled = false;
    }

    // ============ 初始化 ============

    private void Awake()
    {
        blackboard     = new Dictionary<string, object>();
        turnBasedUnit  = GetComponent<TurnBasedUnit>();
        unitMovement   = GetComponent<UnitMovement>();
        enemyEquipment = GetComponent<EnemyEquipment>(); // 可为 null（无武器敌人）
        enemyInventory = GetComponent<EnemyInventory>(); // 可为 null（无物品栏敌人）
        if (enemyInventory != null)
            enemyInventory.OnStatEffectRequested += HandleStatEffect;
        currentHp      = config.maxHp;

        var floorMarker = GetComponent<FloorObjectMarker>();
        if (floorMarker != null)
            floorMarker.Initialize(true);

        InitializePerception();
        InitializeExecutors();
        InitializeBehaviorTree();
    }

    private void Start()
    {
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart += OnMyTurnStart;
            turnBasedUnit.OnMyTurnEnd   += OnMyTurnEnd;
        }

        // 监听玩家上楼事件
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            UnitMovement playerMovement = player.GetComponent<UnitMovement>();
            if (playerMovement != null)
                playerMovement.OnFloorChanged += OnPlayerFloorChanged;
        }

        if (CombatModeManager.Instance != null)
            CombatModeManager.Instance.RegisterEnemy(this);

        currentTurnVisitedCells.Add(unitMovement.CurrentGridPosition);
        SetPatrolTarget();

        // 订阅自身楼层变化事件，同步更新 FloorVisibility 注册
        if (unitMovement != null)
            unitMovement.OnFloorChanged += OnEnemyFloorChanged;
    }

    private void OnDestroy()
    {
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart -= OnMyTurnStart;
            turnBasedUnit.OnMyTurnEnd   -= OnMyTurnEnd;
        }

        if (enemyInventory != null)
            enemyInventory.OnStatEffectRequested -= HandleStatEffect;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            UnitMovement playerMovement = player.GetComponent<UnitMovement>();
            if (playerMovement != null)
                playerMovement.OnFloorChanged -= OnPlayerFloorChanged;
        }

        if (CombatModeManager.Instance != null)
            CombatModeManager.Instance.UnregisterEnemy(this);

        if (unitMovement != null)
            unitMovement.OnFloorChanged -= OnEnemyFloorChanged;
    }

    /// <summary>
    /// 玩家上楼事件回调
    /// 不立即执行，记录目标楼层，等敌人自己的回合再追
    /// 避免在玩家回合中行动导致回合系统冲突
    /// </summary>
    private void OnPlayerFloorChanged(int newFloor)
    {
        if (unitMovement.CurrentFloor == newFloor) return;

        bool knowsPlayer = (blackboard.ContainsKey("hasVisualContact") &&
                            (bool)blackboard["hasVisualContact"])
                        || blackboard.ContainsKey("lastSeenPosition");

        if (!knowsPlayer) return;

        Debug.Log($"[AI:{gameObject.name}] Player went to floor {newFloor}, will follow on my turn");
        blackboard["pendingFollowFloor"] = newFloor;
    }
    private void OnEnemyFloorChanged(int newFloor)
    {
        // 更新 FloorObjectMarker 的楼层注册，确保楼层可见性正确
        var marker = GetComponent<FloorObjectMarker>();
        if (marker != null)
            marker.ChangeFloor(newFloor);
    }

    // ============ Update ============

    private void Update()
    {
        perception.UpdateAll();
    }

    // ============ 回合 ============

    private void OnMyTurnStart()
    {
        if (!isExecuting)
        {
            isExecuting = true;

            lastTurnVisitedCells = new HashSet<Vector2Int>(currentTurnVisitedCells);
            currentTurnVisitedCells.Clear();
            currentTurnVisitedCells.Add(unitMovement.CurrentGridPosition);
            blackboard["hasAttackedThisTurn"]   = false;
            _confidenceDecrementedThisTurn      = false; // 每回合开始重置，允许本回合减一次

            // ── 战况评估：每回合开始时写入 Blackboard ──
            // HP 百分比
            float hpRatio = config.maxHp > 0 ? (float)currentHp / config.maxHp : 0f;
            blackboard["combat_hpRatio"] = hpRatio;

            // 周围友军数量（SoundPerception 范围内的存活 Enemy）
            int nearbyAllies = 0;
            foreach (var other in FindObjectsOfType<EnemyAIController>())
            {
                if (other == this || !other.IsAlive) continue;
                float dist = Vector3.Distance(transform.position, other.transform.position);
                if (dist <= config.hearingRange)
                    nearbyAllies++;
            }
            blackboard["combat_nearbyAllies"] = nearbyAllies;

            // 战术选择写入（供 BTCondition 读取）
            string combatTactic;
            if (hpRatio > 0.6f && nearbyAllies > 0)      combatTactic = "Aggressive";
            else if (hpRatio > 0.6f)                      combatTactic = "Engage";
            else if (hpRatio > 0.3f)                      combatTactic = "Defensive";
            else if (nearbyAllies > 0)                    combatTactic = "CallSupport";
            else                                           combatTactic = "Retreat";
            blackboard["combat_tactic"] = combatTactic;

            // ── 物品栏决策（如有）──
            enemyInventory?.EvaluateAndWriteBlackboard(blackboard, hpRatio);

            // ── 非战斗拾取评估（冷却 tick + 概率检测）──
            // 只在非战斗状态（无视野接触）时写入 item_can_pickup
            bool inCombat = blackboard.TryGetValue("hasVisualContact", out var v) && v is bool b && b;
            if (!inCombat)
            {
                enemyInventory?.TickPickupCooldown();
                enemyInventory?.EvaluatePickup(blackboard);
            }
            else
            {
                // 战斗中清除拾取标记，避免残留
                blackboard.Remove("item_can_pickup");
                blackboard.Remove("item_pickup_target");
            }

            StartCoroutine(StartTurnDelayed());
        }
    }

    private IEnumerator StartTurnDelayed()
    {
        if (TurnDelay > 0.05f)
            yield return new WaitForSeconds(TurnDelay);
        else
            yield return null;

        if (!isExecuting) yield break;

        if (isExecuting)
            ExecuteSingleAction();
    }

    private void OnMyTurnEnd()
    {
        isExecuting = false;
    }

    // ============ 状态判断 ============

    private string EvaluateState()
    {
        bool hasVisual = blackboard.ContainsKey("hasVisualContact") &&
                         (bool)blackboard["hasVisualContact"];

        if (hasVisual)
        {
            if (blackboard.TryGetValue("lastSeenTarget", out var tObj) && tObj is Transform t)
            {
                // XZ 平面距离（与 MovementExecutor 保持一致，避免楼层 Y 差干扰射程判断）
                float dist = new Vector2(
                    transform.position.x - t.position.x,
                    transform.position.z - t.position.z).magnitude;

                // 射程转换为世界单位（格子数 × CellSize），与 CombatExecutor 保持一致
                float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
                float range = enemyEquipment != null
                    ? enemyEquipment.GetAttackRange(config) * cellSize
                    : cellSize;

                bool inRange  = dist <= range;
                bool canShoot = enemyEquipment == null || !enemyEquipment.HasWeapon || enemyEquipment.HasAmmo;
                blackboard["inCombatRange"] = inRange && canShoot;
            }

            searchConfidence = config.searchConfidenceMax;
            bool combatReady = blackboard.ContainsKey("inCombatRange") && (bool)blackboard["inCombatRange"];

            // Combat 优先级最高，即使在上楼中也打断
            if (combatReady) return "Combat";
        }

        // FloorChase：在回合内追玩家到新楼层
        if (blackboard.ContainsKey("pendingFollowFloor"))
        {
            int pendingFloor = (int)blackboard["pendingFollowFloor"];
            if (unitMovement.CurrentFloor != pendingFloor)
                return "FloorChase";
            else
                blackboard.Remove("pendingFollowFloor");
        }

        if (hasVisual)
            return "Chase";

        if (blackboard.ContainsKey("lastSeenPosition"))
        {
            // ── 到达搜索位置但玩家不在：停止追踪（修复来回折返 bug）──────────
            // 当敌人站在 lastSeenPosition 格子或相邻格时，说明已经搜索过该位置，
            // 玩家不在此处，应清除记忆而不是原地震荡
            if (GridManager.Instance != null)
            {
                Vector3    searchPos  = (Vector3)blackboard["lastSeenPosition"];
                Vector2Int searchGrid = GridManager.Instance.WorldToGrid(searchPos);
                int manhattanDist = Mathf.Abs(searchGrid.x - unitMovement.CurrentGridPosition.x)
                                  + Mathf.Abs(searchGrid.y - unitMovement.CurrentGridPosition.y);
                if (manhattanDist <= 1)
                {
                    // 已到达/已相邻，强制归零让下面的清除逻辑触发
                    searchConfidence = 0;
                }
            }

            // 每回合只递减一次（EvaluateState 每次 action 都会调用，标志位防止一回合内减多次）
            if (!_confidenceDecrementedThisTurn)
            {
                searchConfidence--;
                _confidenceDecrementedThisTurn = true;
            }

            if (searchConfidence <= 0)
            {
                blackboard.Remove("lastSeenPosition");
                blackboard.Remove("lastSeenTarget");
                blackboard.Remove("lastSeenFloor");
                blackboard.Remove("inCombatRange");
                blackboard.Remove("hasVisualContact");
                searchConfidence = 0;

                if (CombatModeManager.Instance != null)
                    CombatModeManager.Instance.NotifyEnemyExitCombat(this);

                SetPatrolTarget();
                return "Patrol";
            }

            return "Chase(searching)";
        }

        // ── 声音调查 ──────────────────────────────────────────────────
        // lastHeardPosition 由感知事件写入，不在这里立即清除。
        // 使用倒计时控制调查持续回合数，让 BT 的 Investigate_Sound 节点有机会触发。
        if (blackboard.ContainsKey("lastHeardPosition"))
        {
            if (blackboard.TryGetValue("soundInvestigateCountdown", out var cdObj) && cdObj is int cd)
            {
                // ── 先判断是否还有调查回合，再递减 ─────────────────────────────────
                // 关键：cd 必须先检查 > 0 再决定调查，递减后再检查会让调查少一回合
                // 例：soundInvestigateRounds=2
                //   旧逻辑：cd=2→1 返回Investigate；cd=1→0 清除（只调查1回合）
                //   新逻辑：cd=2 返回Investigate，递减到1；cd=1 返回Investigate，递减到0；cd=0 清除（正确2回合）
                if (cd <= 0)
                {
                    // 倒计时已耗尽，清除声音线索
                    blackboard.Remove("lastHeardPosition");
                    blackboard.Remove("lastHeardFloor");
                    blackboard.Remove("soundInvestigateCountdown");
                }
                else
                {
                    // 本回合还有调查机会，先返回 Investigate，再递减倒计时（每回合只减一次）
                    if (!_confidenceDecrementedThisTurn)
                    {
                        cd--;
                        _confidenceDecrementedThisTurn = true;
                        blackboard["soundInvestigateCountdown"] = cd;
                    }
                    return "Investigate(sound)";
                }
            }
            else
            {
                // 没有倒计时（兜底），立即清除
                blackboard.Remove("lastHeardPosition");
                blackboard.Remove("lastHeardFloor");
            }
        }

        // 通知退出战斗
        if (CombatModeManager.Instance != null)
            CombatModeManager.Instance.NotifyEnemyExitCombat(this);

        // 只在没有有效巡逻目标时才重新选（EvaluateState 每次 action 都调用，
        // 若每次都 SetPatrolTarget 会导致每步换方向、看起来乱转）
        if (!blackboard.ContainsKey("patrolTarget") ||
            GridManager.Instance != null &&
            GridManager.Instance.WorldToGrid((Vector3)blackboard["patrolTarget"])
                == unitMovement.CurrentGridPosition)
        {
            SetPatrolTarget();
        }

        return "Patrol";
    }

    // ============ 执行 ============

    private void ExecuteSingleAction()
    {
        // 临时诊断：打印任务相关 Blackboard 值
        var bbKeys = string.Join(", ", blackboard.Keys);
        Debug.Log($"[AI:{enemyId}] ExecuteSingleAction | Blackboard keys: {bbKeys}");
        if (blackboard.TryGetValue("targetObjectId", out var dbgObj))
            Debug.Log($"[AI:{enemyId}] targetObjectId={dbgObj}  weight_interact={( blackboard.TryGetValue("weight_interact", out var wi) ? wi : "missing")}  objectiveInteractMode={( blackboard.TryGetValue("objectiveInteractMode", out var om) ? om : "missing")}");
        //
        if (!turnBasedUnit.CanAct)
        {
            EndTurn();
            return;
        }

        string state = EvaluateState();

        // 每回合状态日志（调试用，正常运行时可注释）
        // Debug.Log($"[AI:{gameObject.name}] " +
        //           $"State:{state} | " +
        //           $"Floor:{unitMovement.CurrentFloor} | " +
        //           $"Grid:{unitMovement.CurrentGridPosition} | " +
        //           $"AP:{turnBasedUnit.RemainingActionPoints} | " +
        //           $"HP:{currentHp}/{MaxHp} | " +
        //           $"Conf:{searchConfidence}");

        _lastActionStartGrid = unitMovement.CurrentGridPosition; // 记录 action 开始前的格子
        behaviorTree.Tick();
        StartCoroutine(WaitForMovementComplete());
    }

    private IEnumerator WaitForMovementComplete()
    {
        yield return null;

        // ── 防死锁：执行器 yield break 后 IsMoving 不会变 true ──────────────
        // 若本帧结束后既没有移动启动、也没有攻击发生，说明执行器什么都没做。
        // 但要注意：部分执行器（如 FollowLeaderExecutor）内部循环消耗所有 AP，
        // 在两次移动之间 IsMoving 短暂为 false 但执行器协程仍在运行（IsActionRunning=true）。
        // 只有当执行器也已经完全退出时，才视为真正的"什么都没做"。
        bool didAttack = blackboard.ContainsKey("hasAttackedThisTurn") &&
                         (bool)blackboard["hasAttackedThisTurn"];

        if (!unitMovement.IsMoving && !didAttack && !behaviorTree.IsActionRunning)
        {
            Debug.Log($"[AI:{gameObject.name}] 执行器无动作（路径受阻或无有效目标），结束回合");
            EndTurn();
            yield break;
        }

        // 攻击后不需要等移动，直接结束回合
        if (didAttack)
        {
            Debug.Log($"[AI:{gameObject.name}] Attacked this turn, ending turn");
            EndTurn();
            yield break;
        }

        // 等待移动完成 AND 执行器协程完全退出。
        // 对内部循环执行器（FollowLeaderExecutor）：两次移动之间 IsMoving 短暂为 false，
        // 但 IsActionRunning 仍为 true，WaitUntil 不会提前唤醒，避免重入 ExecuteSingleAction。
        // 超时保护：若执行器 15 秒内未完成（如跨楼层协程挂死），强制结束回合，防止游戏冻结。
        float waitDeadline = Time.time + 15f;
        yield return new WaitUntil(() =>
            (!unitMovement.IsMoving && !behaviorTree.IsActionRunning) ||
            Time.time > waitDeadline);

        if (Time.time > waitDeadline)
        {
            Debug.LogWarning($"[AI:{gameObject.name}] WaitForMovementComplete 超时（15s），强制结束回合");
            EndTurn();
            yield break;
        }

        currentTurnVisitedCells.Add(unitMovement.CurrentGridPosition);

        if (turnBasedUnit.CanAct)
        {
            // 下次 action 之前的间隔
            // 只有本次有实际移动时才等待（静止等待会让玩家感觉 AI 卡住）
            bool didMove = unitMovement.CurrentGridPosition != _lastActionStartGrid;
            if (didMove && ActionInterval > 0.05f)
                yield return new WaitForSeconds(ActionInterval);

            // 确认回合还在进行中才继续
            if (isExecuting && turnBasedUnit.CanAct)
                ExecuteSingleAction();
            else if (isExecuting)
                EndTurn(); // AP 在等待期间耗尽（如移动），需要显式结束回合
        }
        else
            EndTurn();
    }

    private void EndTurn()
    {
        Debug.Log($"[AI:{gameObject.name}] EndTurn | conf:{searchConfidence}");
        isExecuting = false;

        if (TurnSystem.Instance == null || !turnBasedUnit.IsMyTurn) return;

        var allEnemies = FindObjectsOfType<EnemyAIController>();
        foreach (var enemy in allEnemies)
        {
            if (enemy == null) continue;
            if (enemy.IsExecuting) return;
        }

        Debug.Log($"[AI:{gameObject.name}] All enemies done, ending faction turn");
        TurnSystem.Instance.EndCurrentTurn();
    }

    // ============ 巡逻目标（否定格子逻辑在这里）============

    private void SetPatrolTarget()
    {
        Vector2Int currentGrid = unitMovement.CurrentGridPosition;
        int currentFloor = unitMovement.CurrentFloor;

        // 先尝试找一个不在上回合访问格子里的目标
        for (int attempt = 0; attempt < config.patrolMaxAttempts; attempt++)
        {
            Vector2Int offset = new Vector2Int(
                Random.Range(-config.patrolMaxDistance, config.patrolMaxDistance),
                Random.Range(-config.patrolMaxDistance, config.patrolMaxDistance)
            );

            if (Mathf.Abs(offset.x) + Mathf.Abs(offset.y) < config.patrolMinDistance) continue;

            Vector2Int targetGrid = currentGrid + offset;

            if (!GridManager.Instance.IsValid(targetGrid)) continue;
            if (!GridManager.Instance.IsWalkable(targetGrid, currentFloor)) continue;

            if (lastTurnVisitedCells.Contains(targetGrid)) continue;

            var path = PathfindingService.FindPath(currentGrid, targetGrid, currentFloor);
            if (path == null || path.Count < config.patrolMinDistance) continue;

            bool firstStepVisited = path.Count > 0 && lastTurnVisitedCells.Contains(path[0]);
            if (firstStepVisited && attempt < config.patrolMaxAttempts - 5) continue;

            blackboard["patrolTarget"] = FloorManager.Instance != null
                ? FloorManager.Instance.GridToWorld(targetGrid, currentFloor)
                : GridManager.Instance.GridToWorld(targetGrid);

            // Debug.Log($"[AI:{gameObject.name}] Patrol target: {targetGrid} | avoiding {lastTurnVisitedCells.Count} cells");
            return;
        }

        // 放弃否定格子限制随便选一个
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2Int offset = new Vector2Int(
                Random.Range(-config.patrolMaxDistance, config.patrolMaxDistance),
                Random.Range(-config.patrolMaxDistance, config.patrolMaxDistance)
            );
            if (Mathf.Abs(offset.x) + Mathf.Abs(offset.y) < 2) continue;

            Vector2Int targetGrid = currentGrid + offset;
            if (!GridManager.Instance.IsValid(targetGrid)) continue;
            if (!GridManager.Instance.IsWalkable(targetGrid, currentFloor)) continue;

            var path = PathfindingService.FindPath(currentGrid, targetGrid, currentFloor);
            if (path == null || path.Count == 0) continue;

            blackboard["patrolTarget"] = FloorManager.Instance != null
                ? FloorManager.Instance.GridToWorld(targetGrid, currentFloor)
                : GridManager.Instance.GridToWorld(targetGrid);
            return;
        }

        // 兜底：原地不动
        blackboard["patrolTarget"] = FloorManager.Instance != null
            ? FloorManager.Instance.GridToWorld(currentGrid, currentFloor)
            : GridManager.Instance.GridToWorld(currentGrid);
    }

    private void HandlePerception(PerceptionEvent evt)
    {
        switch (evt.Type)
        {
            case PerceptionType.VisualContact:
                blackboard["lastSeenPosition"] = evt.Position;
                blackboard["lastSeenTarget"]   = evt.Target;
                blackboard["hasVisualContact"]  = true;
                blackboard["lastSeenFloor"]     = evt.Floor;

                if (CombatModeManager.Instance != null)
                    CombatModeManager.Instance.NotifyEnemyEnterCombat(this);
                break;

            case PerceptionType.VisualLost:
                blackboard["hasVisualContact"] = false;
                break;

            case PerceptionType.SoundHeard:
                blackboard["lastHeardPosition"]        = evt.Position;
                blackboard["lastHeardFloor"]           = evt.Floor;
                // 重置调查倒计时（每次新声音都刷新）
                blackboard["soundInvestigateCountdown"] = config.soundInvestigateRounds;
                break;
        }
    }

    // ============ 初始化方法 ============

    private void InitializePerception()
    {
        perception = new PerceptionAggregator();

        var vision = new VisionPerception();
        vision.Initialize(transform, config);
        perception.RegisterModule(vision);

        var sound = new SoundPerception();
        sound.Initialize(transform, config);
        perception.RegisterModule(sound);

        perception.OnAnyPerception += HandlePerception;
    }

    private void InitializeExecutors()
    {
        executors = new Dictionary<string, IActionExecutor>
        {
            ["move"]              = new MovementExecutor(),
            ["combat"]            = new CombatExecutor(),
            ["investigate"]       = new InvestigationExecutor(),
            ["floorChase"]        = new FloorChaseExecutor(),
            ["moveToZone"]        = new MoveToZoneExecutor(),
            ["holdZone"]          = new HoldZoneExecutor(),
            ["patrolInZone"]      = new PatrolInZoneExecutor(),
            ["followLeader"]      = new FollowLeaderExecutor(),
            ["objectiveInteract"] = new ObjectiveInteractExecutor(),
            ["moveToPosition"]    = new MoveToPositionExecutor(),
            ["useItem"]           = new UseItemExecutor(),
            ["throwItem"]         = new ThrowItemExecutor(),
            ["pickupItem"]        = new PickupItemExecutor(),
        };

        foreach (var executor in executors.Values)
            executor.Initialize(transform, config);
    }

    // ── Mission 接口（由 MissionManager 调用）──────────────

    /// <summary>
    /// 将任务压入优先级队列。
    /// 已存在相同 missionId 则原地更新，否则插入后按 priority 降序重排。
    /// 结束后自动应用当前最高优先级任务。
    ///
    /// 串行控制说明：
    ///   任务的先后顺序由 MissionManager.ActivateEligibleMissions 在激活阶段控制
    ///   （allowParallel=false 的任务等参与 Enemy 完成高优先级任务后才激活）。
    ///   激活后推送到这里时，Enemy 的队列天然按 priority 排序执行，无需在此再次干预。
    ///   ⚠ 不要在此清除低优先级任务：合作任务的 Enemy 可能同时持有多个不同优先级的任务；
    ///     强制清队列会让 MissionManager 误以为任务在执行，但 Enemy 实际已放弃，导致状态错乱。
    /// </summary>
    public void PushMissionContext(MissionContext context, bool exclusive = false)
    {
        int idx = _missionQueue.FindIndex(m => m.missionId == context.missionId);
        if (idx >= 0)
            _missionQueue[idx] = context;          // 更新已有任务
        else
            _missionQueue.Add(context);            // 新任务入队

        // 稳定排序：priority 相同时保持原先入队顺序
        _missionQueue = _missionQueue
            .Select((m, i) => (m, i))
            .OrderByDescending(x => x.m.priority)
            .ThenBy(x => x.i)
            .Select(x => x.m)
            .ToList();

        ApplyTopContext();
    }

    /// <summary>
    /// 从队列移除指定任务，自动应用下一个任务的 Context。
    /// 先重置倍率到默认值再应用新顶部，避免倍率残留。
    /// </summary>
    public void PopMissionContext(string missionId)
    {
        _missionQueue.RemoveAll(m => m.missionId == missionId);
        ApplyTopContext();
    }

    /// <summary>
    /// 清空整个任务队列（Phase 切换时由 MissionManager 调用非持久任务 Pop 后可能为空）
    /// </summary>
    public void ClearMissionQueue()
    {
        _missionQueue.Clear();
        ApplyTopContext();
    }

    /// <summary>
    /// 应用队列顶部任务：
    /// 1. 先将倍率归为 default（1f）
    /// 2. 清除 Blackboard 中上一任务写入的 key
    /// 3. 再应用新顶部任务的 Blackboard + 倍率
    /// </summary>
    private void ApplyTopContext()
    {
        // ── 1. 倍率回到默认 ──────────────────────────────────
        config.SetMissionAPMultiplier(1f);
        if (unitMovement != null && _baseMoveSpeed >= 0f)
            unitMovement.SetMoveSpeed(_baseMoveSpeed);

        // ── 2. 清除旧 Blackboard key ─────────────────────────
        // 先清除所有 weight_* 权重 key，防止旧任务的权重残留干扰新任务的分支选择
        // 例：Mission A 写了 weight_patrol=0.8，切换到 Mission B（weight_defend=0.9）时
        // 若不清除，BT 会先命中 weight_patrol 分支而非正确的 weight_defend 分支
        var staleWeightKeys = new List<string>();
        foreach (var key in blackboard.Keys)
            if (key.StartsWith("weight_")) staleWeightKeys.Add(key);
        foreach (var key in staleWeightKeys)
            blackboard.Remove(key);

        // 清除所有任务相关 key，防止旧任务值残留影响新任务
        MissionContext.ClearMissionBlackboard(blackboard);

        var top = CurrentMission;
        if (top == null)
        {
            Debug.Log($"[Enemy:{enemyId}] 任务队列为空，倍率已重置");
            return;
        }

        // ── 3. 应用新顶部 ────────────────────────────────────
        top.ApplyToBlackboard(blackboard);

        config.SetMissionAPMultiplier(top.apMultiplier);

        if (unitMovement != null)
        {
            if (_baseMoveSpeed < 0f) _baseMoveSpeed = unitMovement.MoveSpeed;
            unitMovement.SetMoveSpeed(_baseMoveSpeed * top.speedMultiplier);
        }

        if (turnBasedUnit != null && turnBasedUnit.IsMyTurn)
            turnBasedUnit.SetAP(config.GetCurrentAP());

        Debug.Log($"[Enemy:{enemyId}] ApplyTopContext → [{top.missionId}] P{top.priority} " +
                  $"AP×{top.apMultiplier} Speed×{top.speedMultiplier} " +
                  $"Leader:{(string.IsNullOrEmpty(top.leaderId) ? "none" : top.leaderId)}");
    }

    private void InitializeBehaviorTree()
    {
        behaviorTree = new BTRunner(config.behaviorTreeAsset);
        blackboard["__owner"] = this;
        behaviorTree.Initialize(this, executors, blackboard);
    }
}