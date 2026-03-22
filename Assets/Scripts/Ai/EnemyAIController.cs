using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyAIController : MonoBehaviour, IDamageable
{
    public EnemyConfig config;

    private PerceptionAggregator perception;
    private Dictionary<string, IActionExecutor> executors;
    private BTRunner behaviorTree;
    private Dictionary<string, object> blackboard;
    private TurnBasedUnit turnBasedUnit;
    private UnitMovement unitMovement;
    private bool isExecuting = false;

    private int searchConfidence = 0;

    // 运行时 delay 覆盖（由 CombatModeManager 修改，不直接改 ScriptableObject）
    private float runtimeTurnDelay = -1f;
    private float runtimeActionInterval = -1f;

    private float TurnDelay => runtimeTurnDelay >= 0 ? runtimeTurnDelay : config.turnStartDelay;
    private float ActionInterval => runtimeActionInterval >= 0 ? runtimeActionInterval : config.actionInterval;

    /// <summary>由 CombatModeManager 调用，覆盖运行时 delay</summary>
    public void SetRuntimeDelays(float turnDelay, float actionInterval)
    {
        runtimeTurnDelay = turnDelay;
        runtimeActionInterval = actionInterval;
    }

    /// <summary>由 CombatModeManager 调用，恢复默认 delay</summary>
    public void ResetRuntimeDelays()
    {
        runtimeTurnDelay = -1f;
        runtimeActionInterval = -1f;
    }

    // 缓存组件引用，避免每次 EvaluateState 都调用 GetComponent
    private EnemyEquipment enemyEquipment;
    // MAX_SEARCH_CONFIDENCE 移到 config.searchConfidenceMax，不再硬编码

    private int currentHp;
    private int currentSanity;

    // 上一回合走过的格子，Patrol 选目标时优先避开
    private HashSet<Vector2Int> lastTurnVisitedCells = new HashSet<Vector2Int>();
    // 本回合走过的格子，回合结束时转移到 lastTurnVisitedCells
    private HashSet<Vector2Int> currentTurnVisitedCells = new HashSet<Vector2Int>();

    public int MaxHp => config.maxHp;
    public int CurrentHp => currentHp;
    public bool IsAlive => currentHp > 0;

    public void TakeDamage(int damage)
    {
        currentHp -= damage;
        Debug.Log($"[Enemy:{gameObject.name}] TakeDamage: -{damage} | HP: {currentHp}/{MaxHp}");
        if (currentHp <= 0) Die();
    }

    private void Die() => Destroy(gameObject);

    // ============ 初始化 ============

    private void Awake()
    {
        blackboard    = new Dictionary<string, object>();
        turnBasedUnit = GetComponent<TurnBasedUnit>();
        unitMovement  = GetComponent<UnitMovement>();
        enemyEquipment = GetComponent<EnemyEquipment>(); // 可为 null（无武器敌人）
        currentHp     = config.maxHp;
        currentSanity = config.maxSanity;

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

        // 向 CombatModeManager 注册（只注册敌人，TurnBasedUnit 自动注册单位）
        if (CombatModeManager.Instance != null)
            CombatModeManager.Instance.RegisterEnemy(this);

        currentTurnVisitedCells.Add(unitMovement.CurrentGridPosition);
        SetPatrolTarget();
    }

    private void OnDestroy()
    {
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart -= OnMyTurnStart;
            turnBasedUnit.OnMyTurnEnd   -= OnMyTurnEnd;
        }

        // 死亡时注销
        if (CombatModeManager.Instance != null)
            CombatModeManager.Instance.UnregisterEnemy(this);
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
            blackboard["hasAttackedThisTurn"] = false;

            StartCoroutine(StartTurnDelayed());
        }
    }

    private IEnumerator StartTurnDelayed()
    {
        if (TurnDelay > 0.05f)
            yield return new WaitForSeconds(TurnDelay);
        else
            yield return null;

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
            if (blackboard["lastSeenTarget"] is Transform t)
            {
                float dist = Vector3.Distance(transform.position, t.position);

                float range = enemyEquipment != null
                    ? enemyEquipment.GetAttackRange(config)
                    : 1;

                bool inRange = dist <= range;
                bool canShoot = enemyEquipment == null || !enemyEquipment.HasWeapon || enemyEquipment.HasAmmo;
                blackboard["inCombatRange"] = inRange && canShoot;
            }

            searchConfidence = config.searchConfidenceMax;
            bool combatReady = blackboard.ContainsKey("inCombatRange") && (bool)blackboard["inCombatRange"];
            return combatReady ? "Combat" : "Chase";
        }

        if (blackboard.ContainsKey("lastSeenPosition"))
        {
            searchConfidence--;

            if (searchConfidence <= 0)
            {
                blackboard.Remove("lastSeenPosition");
                blackboard.Remove("lastSeenTarget");
                blackboard.Remove("lastSeenFloor");
                blackboard.Remove("inCombatRange");
                blackboard.Remove("hasVisualContact");
                searchConfidence = 0;

                // 通知 CombatModeManager 退出战斗
                if (CombatModeManager.Instance != null)
                    CombatModeManager.Instance.NotifyEnemyExitCombat(this);

                SetPatrolTarget();
                return "Patrol";
            }

            return "Chase(searching)";
        }

        // 无视野无追击目标，回到 Patrol
        if (blackboard.ContainsKey("lastHeardPosition"))
        {
            blackboard.Remove("lastHeardPosition");
            blackboard.Remove("lastHeardFloor");
        }

        // 通知退出战斗
        if (CombatModeManager.Instance != null)
            CombatModeManager.Instance.NotifyEnemyExitCombat(this);

        SetPatrolTarget();
        return "Patrol";
    }

    // ============ 执行 ============

    private void ExecuteSingleAction()
    {
        if (!turnBasedUnit.CanAct)
        {
            EndTurn();
            return;
        }

        string state = EvaluateState();
        Debug.Log($"[AI:{gameObject.name}] {state} | grid:{unitMovement.CurrentGridPosition} | AP:{turnBasedUnit.RemainingActionPoints} | inCombatRange:{blackboard.ContainsKey("inCombatRange") && (bool)blackboard["inCombatRange"]}");

        behaviorTree.Tick();
        StartCoroutine(WaitForMovementComplete());
    }

    private IEnumerator WaitForMovementComplete()
    {
        yield return null;
        yield return new WaitUntil(() => !unitMovement.IsMoving);

        currentTurnVisitedCells.Add(unitMovement.CurrentGridPosition);

        if (turnBasedUnit.CanAct)
        {
            bool attacked = blackboard.ContainsKey("hasAttackedThisTurn") &&
                            (bool)blackboard["hasAttackedThisTurn"];
            if (attacked)
            {
                Debug.Log($"[AI:{gameObject.name}] Attacked this turn, ending turn");
                EndTurn();
                yield break;
            }

            // ActionInterval 很小时直接用协程等待，避免 Invoke 竞争导致卡死
            if (ActionInterval > 0.05f)
                yield return new WaitForSeconds(ActionInterval);

            // 确认回合还在进行中才继续
            if (isExecuting && turnBasedUnit.CanAct)
                ExecuteSingleAction();
        }
        else
            EndTurn();
    }

    private void EndTurn()
    {
        Debug.Log($"[AI:{gameObject.name}] EndTurn | conf:{searchConfidence}");

        if (TurnSystem.Instance != null && turnBasedUnit.IsMyTurn)
            TurnSystem.Instance.EndCurrentTurn();
        isExecuting = false;
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

            Debug.Log($"[AI:{gameObject.name}] Patrol target: {targetGrid} | avoiding {lastTurnVisitedCells.Count} cells");
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

    // ============ 感知 ============

    private void HandlePerception(PerceptionEvent evt)
    {
        switch (evt.Type)
        {
            case PerceptionType.VisualContact:
                blackboard["lastSeenPosition"] = evt.Position;
                blackboard["lastSeenTarget"]   = evt.Target;
                blackboard["hasVisualContact"]  = true;
                blackboard["lastSeenFloor"]     = evt.Floor;

                // 通知 CombatModeManager 进入战斗模式
                if (CombatModeManager.Instance != null)
                    CombatModeManager.Instance.NotifyEnemyEnterCombat(this);
                break;

            case PerceptionType.VisualLost:
                blackboard["hasVisualContact"] = false;
                break;

            case PerceptionType.SoundHeard:
                blackboard["lastHeardPosition"] = evt.Position;
                blackboard["lastHeardFloor"]    = evt.Floor;
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
            ["move"]        = new MovementExecutor(),
            ["combat"]      = new CombatExecutor(),
            ["investigate"] = new InvestigationExecutor()
        };

        foreach (var executor in executors.Values)
            executor.Initialize(transform, config);
    }

    private void InitializeBehaviorTree()
    {
        behaviorTree = new BTRunner(config.behaviorTreeAsset);
        behaviorTree.Initialize(this, executors, blackboard);
    }
}