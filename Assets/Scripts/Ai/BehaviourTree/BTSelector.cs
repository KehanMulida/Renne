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

    // 三态：Idle = 未开始，Running = 执行中，Done = 已完成
    private enum ActionState { Idle, Running, Done }
    private ActionState state = ActionState.Idle;

    /// <summary>供 BTRunner.IsActionRunning 查询</summary>
    public bool IsExecuting => state == ActionState.Running;

    public override NodeState Evaluate(Dictionary<string, object> blackboard,
                                       Dictionary<string, IActionExecutor> executors)
    {
        if (!executors.ContainsKey(executorName))
        {
            Debug.LogError($"[BTAction] Executor not found: {executorName}");
            return NodeState.Failure;
        }

        var executor = executors[executorName];

        // 已完成：返回 Success，等待外部 Reset
        if (state == ActionState.Done)
            return NodeState.Success;

        // 未开始：启动协程
        if (state == ActionState.Idle)
        {
            if (!executor.CanExecute())
                return NodeState.Failure;

            if (coroutineRunner == null)
            {
                Debug.LogError("[BTAction] coroutineRunner not set.");
                return NodeState.Failure;
            }

            var context = BuildContext(blackboard);
            currentCoroutine = coroutineRunner.StartCoroutine(ExecuteAction(executor, context));
            state = ActionState.Running;
        }

        // 执行中：返回 Running
        return NodeState.Running;
    }

    /// <summary>
    /// 每回合 tick 前调用，重置状态让 Action 可以重新执行
    /// 由 BTRunner.ResetAll 在每次 Tick 前统一调用
    /// </summary>
    public void Reset()
    {
        if (state == ActionState.Running && currentCoroutine != null)
            coroutineRunner?.StopCoroutine(currentCoroutine);

        state = ActionState.Idle;
        currentCoroutine = null;
    }

    public void SetCoroutineRunner(MonoBehaviour runner)
    {
        coroutineRunner = runner;
    }

    private IEnumerator ExecuteAction(IActionExecutor executor, ActionContext context)
    {
        yield return executor.Execute(context);
        state = ActionState.Done;
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