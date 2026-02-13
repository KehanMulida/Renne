// EnemyAIController.cs - 完整版本

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyAIController : MonoBehaviour
{
    [SerializeField] private EnemyConfig config;
    
    private PerceptionAggregator perception;
    private Dictionary<string, IActionExecutor> executors;
    private BTRunner behaviorTree;
    private Dictionary<string, object> blackboard;
    private TurnBasedUnit turnBasedUnit;
    private UnitMovement unitMovement;
    private bool isExecuting = false;
    private int searchConfidence = 0;  // 搜索信心值
    private const int MAX_SEARCH_MOVES = 3;  // 最多搜索3个回合 

    private void Awake()
    {
        blackboard = new Dictionary<string, object>();
        turnBasedUnit = GetComponent<TurnBasedUnit>();
        unitMovement = GetComponent<UnitMovement>();
        
        InitializePerception();
        InitializeExecutors();
        InitializeBehaviorTree();
    }

    private void Update(){
        perception.UpdateAll();
    }

    private void Start()
    {
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart += OnMyTurnStart;
            turnBasedUnit.OnMyTurnEnd += OnMyTurnEnd;
        }
        
        // 删除这两行
        // if (unitMovement != null)
        // {
        //     unitMovement.OnMoveComplete += OnMoveComplete;
        // }
        
        SetRandomPatrolTarget();
    }

    private void OnDestroy()
    {
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart -= OnMyTurnStart;
            turnBasedUnit.OnMyTurnEnd -= OnMyTurnEnd;
        }
        
        // 删除这四行
        // if (unitMovement != null)
        // {
        //     unitMovement.OnMoveComplete -= OnMoveComplete;
        // }
    }

    private void OnMyTurnStart()
    {
        Debug.Log($"[AI] My turn started");
        if (!isExecuting)
        {
            isExecuting = true;
            Invoke(nameof(ExecuteNextMove), 0.5f);
        }
    }

    private void OnMyTurnEnd()
    {
        Debug.Log($"[AI] My turn ended");
        isExecuting = false;
    }

    private void ExecuteNextMove()
    {
        if (!turnBasedUnit.CanAct)
        {
            Debug.Log("[AI] No AP, ending turn");
            EndTurn();
            return;
        }

      //  perception.UpdateAll();  /// 这里不需要更新感知，因为感知是全局更新的
        StartCoroutine(ExecuteAndWait());
    }

    private IEnumerator ExecuteAndWait()
    {
        behaviorTree.Tick();
        
        // 等待移动完成
        yield return new WaitUntil(() => !unitMovement.IsMoving);
        
        Debug.Log($"[AI] Move complete, checking perception...");
        
        // 移动后立即检查感知
        perception.UpdateAll();
        
        // 如果在搜索状态且没找到玩家，降低信心值
        if (!blackboard.ContainsKey("hasVisualContact") || 
            !(bool)blackboard["hasVisualContact"])
        {
            if (searchConfidence > 0)
            {
                searchConfidence--;
                Debug.Log($"[AI] No target found, search confidence decreased to {searchConfidence}");
                
                // 信心耗尽，放弃搜索
                if (searchConfidence <= 0)
                {
                    blackboard.Remove("lastSeenPosition");
                    blackboard.Remove("lastHeardPosition");
                    Debug.Log("[AI] Search confidence exhausted, returning to patrol");
                }
            }
        }
        
        Debug.Log($"[AI] AP remaining: {turnBasedUnit.RemainingActionPoints}");
        
        if (turnBasedUnit.CanAct)
        {
            Invoke(nameof(ExecuteNextMove), 0.5f);
        }
        else
        {
            EndTurn();
        }
    }

    // 删除整个 OnMoveComplete 方法
    // private void OnMoveComplete() { ... }

    private void EndTurn()
    {
        if (TurnSystem.Instance != null && turnBasedUnit.IsMyTurn)
        {
            TurnSystem.Instance.EndCurrentTurn();
        }
        isExecuting = false;
    }

    private void SetRandomPatrolTarget()
    {
        Vector2Int randomOffset = new Vector2Int(Random.Range(-5, 5), Random.Range(-5, 5));
        Vector2Int targetGrid = unitMovement.CurrentGridPosition + randomOffset;
        blackboard["patrolTarget"] = FloorManager.Instance.GridToWorld(targetGrid, unitMovement.CurrentFloor);
    }

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
            ["move"] = new MovementExecutor(),
            ["combat"] = new CombatExecutor(),
            ["investigate"] = new InvestigationExecutor()
        };

        foreach (var executor in executors.Values)
        {
            executor.Initialize(transform, config);
        }
    }

    private void InitializeBehaviorTree()
    {
        behaviorTree = new BTRunner(config.behaviorTreeAsset);
        behaviorTree.Initialize(this, executors, blackboard);
    }

    private void HandlePerception(PerceptionEvent evt)
    {
         switch (evt.Type)
        {
            case PerceptionType.VisualContact:
                blackboard["lastSeenPosition"] = evt.Position;
                blackboard["lastSeenTarget"] = evt.Target;
                blackboard["hasVisualContact"] = true;
                
                // 计算距离，判断是否在战斗范围内
                float distance = Vector3.Distance(transform.position, evt.Position);
                blackboard["inCombatRange"] = distance <= config.attackRange;
                
                // 重置搜索信心
                searchConfidence = MAX_SEARCH_MOVES;
                
                Debug.Log($"[AI] Visual contact! Distance: {distance:F1}, InCombatRange: {blackboard["inCombatRange"]}");
                break;
                
            case PerceptionType.VisualLost:
                blackboard["hasVisualContact"] = false;
                blackboard["inCombatRange"] = false;
                // 保留 lastSeenPosition 用于搜索
                Debug.Log($"[AI] Lost visual, will search with confidence: {searchConfidence}");
                break;
                
            case PerceptionType.SoundHeard:
                blackboard["lastHeardPosition"] = evt.Position;
                blackboard["lastHeardFloor"] = evt.Floor;
                searchConfidence = 2;  // 声音搜索信心较低
                Debug.Log($"[AI] Sound heard at {evt.Position}, confidence: {searchConfidence}");
                break;
        }
    }
}