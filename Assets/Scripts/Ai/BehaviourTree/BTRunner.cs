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
        this.executors  = executors;
        this.blackboard = blackboard;

        InjectCoroutineRunner(root, controller);
    }

    /// <summary>
    /// 当前是否有 BTAction 正在执行中
    /// WaitForActionComplete 等这个变为 false 才继续
    /// </summary>
    public bool IsActionRunning => CheckAnyActionRunning(root);

    private bool CheckAnyActionRunning(BTNode node)
    {
        if (node == null) return false;
        if (node is BTAction action && action.IsExecuting) return true;
        foreach (var child in node.GetChildren())
            if (CheckAnyActionRunning(child)) return true;
        return false;
    }

    private void InjectCoroutineRunner(BTNode node, MonoBehaviour runner)
    {
        if (node == null) return;

        if (node is BTAction action)
            action.SetCoroutineRunner(runner);

        // 递归注入所有子节点
        foreach (var child in node.GetChildren())
            InjectCoroutineRunner(child, runner);
    }

    public void Tick()
    {
        if (root == null) { Debug.LogError("[BTRunner] Root is null"); return; }
        if (blackboard == null) { Debug.LogError("[BTRunner] Blackboard is null"); return; }
        if (executors == null) { Debug.LogError("[BTRunner] Executors is null"); return; }

        // 每次 Tick 前重置所有 BTAction 状态
        // 确保每回合都是全新的决策，不会受上一回合残留状态影响
        ResetAllActions(root);

        root.Evaluate(blackboard, executors);
    }

    private void ResetAllActions(BTNode node)
    {
        if (node == null) return;
        if (node is BTAction action) action.Reset();
        foreach (var child in node.GetChildren())
            ResetAllActions(child);
    }
}