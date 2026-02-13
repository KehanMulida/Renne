using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BTRunner
{
    private BTNode root;
    private EnemyAIController controller;
    private Dictionary<string, IActionExecutor> executors;
    private Dictionary<string, object> blackboard;

    public BTRunner(TextAsset treeAsset)
    {
        root = BTParser.Parse(treeAsset.text);
    }

    public void Initialize(EnemyAIController controller, 
                          Dictionary<string, IActionExecutor> executors,
                          Dictionary<string, object> blackboard)
    {
        this.controller = controller;
        this.executors = executors;
        this.blackboard = blackboard;
    }

    public void Tick()
    {
         if (root == null)
    {
        Debug.LogError("[BTRunner] Root node is null!");
        return;
    }
    
    if (blackboard == null)
    {
        Debug.LogError("[BTRunner] Blackboard is null!");
        return;
    }
    
    if (executors == null)
    {
        Debug.LogError("[BTRunner] Executors is null!");
        return;
    }
        root.Evaluate(blackboard, executors);
    }
}