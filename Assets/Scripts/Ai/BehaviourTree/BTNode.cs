using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// BTNode.cs
public abstract class BTNode
{
    protected List<BTNode> children = new List<BTNode>();

    public void AddChild(BTNode child)
    {
        children.Add(child);
    }

    /// <summary>返回子节点列表，供 BTRunner 递归注入 coroutineRunner</summary>
    public IReadOnlyList<BTNode> GetChildren() => children;

    public abstract NodeState Evaluate(Dictionary<string, object> blackboard,
                                       Dictionary<string, IActionExecutor> executors);
}

// NodeState.cs
public enum NodeState
{
    Success,
    Failure,
    Running
}