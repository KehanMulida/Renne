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
        
        // 如果是巡逻移动完成，设置新的随机巡逻点
        if (executorName == "move" && context.Blackboard.ContainsKey("patrolTarget"))
        {
            var controller = coroutineRunner as EnemyAIController;
            if (controller != null)
            {
                // 设置新的随机巡逻目标
                var unitMovement = controller.GetComponent<UnitMovement>();
                Vector2Int randomOffset = new Vector2Int(Random.Range(-5, 5), Random.Range(-5, 5));
                Vector2Int targetGrid = unitMovement.CurrentGridPosition + randomOffset;
                context.Blackboard["patrolTarget"] = FloorManager.Instance.GridToWorld(targetGrid, unitMovement.CurrentFloor);
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