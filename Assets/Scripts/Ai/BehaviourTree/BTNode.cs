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