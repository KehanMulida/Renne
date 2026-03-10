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
    private const int MAX_SEARCH_CONFIDENCE = 3;

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

        // 记录初始位置
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

            // 上一回合的访问格子 = 本回合的否定格子
            lastTurnVisitedCells = new HashSet<Vector2Int>(currentTurnVisitedCells);
            currentTurnVisitedCells.Clear();

            // 记录回合开始时的位置
            currentTurnVisitedCells.Add(unitMovement.CurrentGridPosition);

            Invoke(nameof(ExecuteSingleAction), 0.5f);
        }
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
                blackboard["inCombatRange"] = dist <= config.attackRange;
            }

            searchConfidence = MAX_SEARCH_CONFIDENCE;
            bool inRange = blackboard.ContainsKey("inCombatRange") && (bool)blackboard["inCombatRange"];
            return inRange ? "Combat" : "Chase";
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
                SetPatrolTarget();
                return "Patrol";
            }

            return "Chase(searching)";
        }

        if (blackboard.ContainsKey("lastHeardPosition"))
        {
            blackboard.Remove("lastHeardPosition");
            blackboard.Remove("lastHeardFloor");
        }

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
        Debug.Log($"[AI:{gameObject.name}] {state} | grid:{unitMovement.CurrentGridPosition} | AP:{turnBasedUnit.RemainingActionPoints}");

        behaviorTree.Tick();
        StartCoroutine(WaitForMovementComplete());
    }

    private IEnumerator WaitForMovementComplete()
    {
        yield return null;
        yield return new WaitUntil(() => !unitMovement.IsMoving);

        // 记录移动后的位置到本回合访问格子
        currentTurnVisitedCells.Add(unitMovement.CurrentGridPosition);

        // 保持朝向
        if (unitMovement.CurrentGridPosition != GridManager.Instance.WorldToGrid(
            transform.position - transform.forward))
        {
            // UnitMovement 已经处理了朝向，不需要额外处理
        }

        if (turnBasedUnit.CanAct)
            Invoke(nameof(ExecuteSingleAction), 0.3f);
        else
            EndTurn();
    }

    private void EndTurn()
    {
       // Debug.Log($"[AI:{gameObject.name}] EndTurn | conf:{searchConfidence}");

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
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector2Int offset = new Vector2Int(
                Random.Range(-6, 6),
                Random.Range(-6, 6)
            );

            // 至少走 3 格
            if (Mathf.Abs(offset.x) + Mathf.Abs(offset.y) < 3) continue;

            Vector2Int targetGrid = currentGrid + offset;

            if (!GridManager.Instance.IsValid(targetGrid)) continue;
            if (!GridManager.Instance.IsWalkable(targetGrid, currentFloor)) continue;

            // 否定格子：目标本身不能是上回合走过的格子
            if (lastTurnVisitedCells.Contains(targetGrid)) continue;

            var path = PathfindingService.FindPath(currentGrid, targetGrid, currentFloor);
            if (path == null || path.Count < 3) continue;

            // 路径的第一步也尽量不走上回合走过的格子
            bool firstStepVisited = path.Count > 0 && lastTurnVisitedCells.Contains(path[0]);
            if (firstStepVisited && attempt < 15) continue; // 前15次尝试避开，后5次放宽

            blackboard["patrolTarget"] = FloorManager.Instance != null
                ? FloorManager.Instance.GridToWorld(targetGrid, currentFloor)
                : GridManager.Instance.GridToWorld(targetGrid);

           // Debug.Log($"[AI:{gameObject.name}] Patrol target: {targetGrid} | avoiding {lastTurnVisitedCells.Count} cells");
            return;
        }

        // 实在找不到，放弃否定格子限制随便选一个
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2Int offset = new Vector2Int(Random.Range(-6, 6), Random.Range(-6, 6));
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

        // 最后兜底：原地不动
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