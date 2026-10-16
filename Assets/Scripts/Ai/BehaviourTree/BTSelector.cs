using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Selector节点
public class BTSelector : BTNode
{
    public override NodeState Evaluate(Dictionary<string, object> blackboard, 
                                       Dictionary<string, IActionExecutor> executors)
    {
        foreach (var child in children)
        {
            if (child == null)  // 添加空检查
            {
                Debug.LogError("[BTSelector] Child node is null!");
                continue;
            }
            
            var state = child.Evaluate(blackboard, executors);
            if (state != NodeState.Failure)
                return state;
        }
        return NodeState.Failure;
    }
}

public class BTCondition : BTNode
{
    public string key;
    public object expectedValue;
    public bool checkExists;

    public override NodeState Evaluate(Dictionary<string, object> blackboard, 
                                       Dictionary<string, IActionExecutor> executors)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("[BTCondition] Key is null or empty");
            return NodeState.Failure;
        }

        if (checkExists)
        {
            return blackboard.ContainsKey(key) ? NodeState.Success : NodeState.Failure;
        }

        if (blackboard.TryGetValue(key, out var value))
        {
            // 如果 value 是 null
            if (value == null)
            {
                return expectedValue == null ? NodeState.Success : NodeState.Failure;
            }
            
            // 如果 expectedValue 是 null
            if (expectedValue == null)
            {
                return NodeState.Failure;
            }
            
            // bool 类型比较
            if (expectedValue is bool expectedBool && value is bool actualBool)
            {
                return expectedBool == actualBool ? NodeState.Success : NodeState.Failure;
            }
            
            // 修复：防止 value.Equals 抛异常
            try
            {
                return value.Equals(expectedValue) ? NodeState.Success : NodeState.Failure;
            }
            catch
            {
                Debug.LogError($"[BTCondition] Equals failed: key={key}, value={value}, expected={expectedValue}");
                return NodeState.Failure;
            }
        }
        
        return NodeState.Failure;
    }
}

public class BTSequence : BTNode
{
    public override NodeState Evaluate(Dictionary<string, object> blackboard,
                                       Dictionary<string, IActionExecutor> executors)
    {
        foreach (var child in children)
        {
            if (child == null)  // 添加空检查
            {
                Debug.LogError("[BTSequence] Child node is null!");
                return NodeState.Failure;
            }
            
            var state = child.Evaluate(blackboard, executors);
            if (state != NodeState.Success)
                return state;
        }
        return NodeState.Success;
    }
}

public class BTAction : BTNode
{
    public string executorName;
    public string[] blackboardKeys;
    private Coroutine currentCoroutine;
    private MonoBehaviour coroutineRunner;
    private bool isExecuting;

    public bool IsExecuting => isExecuting;

    public void SetCoroutineRunner(MonoBehaviour runner)
    {
        coroutineRunner = runner;
    }

    public void Reset()
    {
        if (!isExecuting)
            currentCoroutine = null;
    }

    public override NodeState Evaluate(Dictionary<string, object> blackboard,
                                       Dictionary<string, IActionExecutor> executors)
    {
        if (!executors.ContainsKey(executorName))
        {
            Debug.LogError($"[BTAction] Executor not found: {executorName}");
            return NodeState.Failure;
        }

        var executor = executors[executorName];
        
        if (!executor.CanExecute())
        {
            return NodeState.Failure;
        }

        // 如果还没开始执行，启动协程
        if (!isExecuting)
        {
            var context = BuildContext(blackboard);

            // 从 blackboard 里读取正确的 coroutineRunner，避免多个 Enemy 共用同一个
            if (coroutineRunner == null)
            {
                if (blackboard.TryGetValue("__owner", out var ownerObj) &&
                    ownerObj is MonoBehaviour mb)
                    coroutineRunner = mb;
                else
                    coroutineRunner = GameObject.FindObjectOfType<EnemyAIController>();
            }

            // Debug.Log($"[BTAction] Starting coroutine for {executorName} on {coroutineRunner?.gameObject.name}");
            currentCoroutine = coroutineRunner.StartCoroutine(ExecuteAction(executor, context));
            isExecuting = true;
        }

        return NodeState.Running;
    }

    private IEnumerator ExecuteAction(IActionExecutor executor, ActionContext context)
    {
        yield return executor.Execute(context);
        isExecuting = false;
        // Debug.Log($"[BTAction] Executor {executorName} completed");
        
        // ⚠ 这里原本会在每次 move 结束后，用一个未经校验的 Random.Range(-5,5) 偏移
        //   直接覆盖 patrolTarget。两个问题：
        //     ① 不校验可走/有效，目标经常落在墙里或地图外 → 下回合 FindPath 失败、原地不动
        //     ② 把 EnemyAIController.SetPatrolTarget() 那套（避开上回合走过的格子、
        //        校验可走、保证最小距离）整个盖掉，回合之间方向毫无连续性，看起来乱转
        //   现在交还给 SetPatrolTarget：它在 EvaluateState 里按需重选
        //   （只有"没有目标"或"已走到目标"时才换），方向能保持连续。
        //
        // 走到目标后把 patrolTarget 清掉，下次 EvaluateState 自然会重选一个合法的
        if (executorName == "move" && context.Blackboard.ContainsKey("patrolTarget"))
        {
            var controller = coroutineRunner as EnemyAIController;
            var unitMovement = controller != null ? controller.GetComponent<UnitMovement>() : null;

            if (unitMovement != null && GridManager.Instance != null)
            {
                Vector2Int targetGrid = GridManager.Instance.WorldToGrid(
                    (Vector3)context.Blackboard["patrolTarget"]);

                if (targetGrid == unitMovement.CurrentGridPosition)
                    context.Blackboard.Remove("patrolTarget");
            }
        }
    }
    private ActionContext BuildContext(Dictionary<string, object> blackboard)
    {
        var context = new ActionContext();
        context.Blackboard = blackboard;
        context.TargetFloor = -1;
        
        if (blackboardKeys == null) return context;
        
        foreach (var key in blackboardKeys)
        {
            if (!blackboard.TryGetValue(key, out var value) || value == null) continue;
            
            if (value is Vector3 vec3)
                context.TargetPosition = vec3;
            else if (value is Transform trans)
                context.TargetObject = trans;
            else if (value is int floor)
                context.TargetFloor = floor;
        }
        
        return context;
    }
}