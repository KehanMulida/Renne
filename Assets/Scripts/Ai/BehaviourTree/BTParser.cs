using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public static class BTParser
{
    public static BTNode Parse(string json)
    {
        var data = JsonUtility.FromJson<NodeData>(json);
        return BuildNode(data);
    }

    private static BTNode BuildNode(NodeData data)
    {
        if (data == null)
        {
            Debug.LogError("[BTParser] NodeData is null");
            return null;
        }

        switch (data.type)
        {
            case "Selector":
                var selector = new BTSelector();
                if (data.children != null)
                {
                    foreach (var child in data.children)
                    {
                        var childNode = BuildNode(child);
                        if (childNode != null)
                            selector.AddChild(childNode);
                    }
                }
                return selector;

            case "Sequence":
                var sequence = new BTSequence();
                if (data.children != null)
                {
                    foreach (var child in data.children)
                    {
                        var childNode = BuildNode(child);
                        if (childNode != null)
                            sequence.AddChild(childNode);
                    }
                }
                return sequence;

            case "Condition":
                var condition = new BTCondition();
                condition.key = data.key;
                condition.checkExists = data.exists;
                
                if (data.value != null)
                {
                    string valueStr = data.value.ToString();
                    if (valueStr == "True" || valueStr == "true")
                        condition.expectedValue = true;
                    else if (valueStr == "False" || valueStr == "false")
                        condition.expectedValue = false;
                    else
                        condition.expectedValue = data.value;
                }
                
                // Debug.Log($"[BTParser] Created Condition: key={condition.key}, exists={condition.checkExists}, value={condition.expectedValue}");
                return condition;

            case "Action":
                var action = new BTAction();
                action.executorName = data.executor;
                action.blackboardKeys = data.keys;

                // Debug.Log($"[BTParser] Created Action: executor={action.executorName}");
                return action;

            case "WeightCondition":
                var wc = new BTWeightCondition();
                wc.key = data.key;
                wc.threshold = data.threshold;
                return wc;

            case "TacticCondition":
                var tc = new BTTacticCondition();
                tc.expectedTactic = data.value?.ToString();
                return tc;

            default:
                Debug.LogError($"[BTParser] Unknown node type: {data.type}");
                return null;
        }
    }
}

[System.Serializable]
class NodeData
{
    public string type;
    public string name;
    public string key;
    public string value;  // 改为 string，JsonUtility 不支持 object
    public bool exists;
    public string executor;
    public string[] keys;
    public float threshold;
    public NodeData[] children;
}

// ══════════════════════════════════════════════════════════════════════════════
// 新增节点类型（原在 BTParser_Addition.cs，已合并）
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// WeightCondition 节点
/// 读取 Blackboard 里的权重值，大于 threshold 才返回 Success
/// JSON 写法：{ "type": "WeightCondition", "key": "weight_patrol", "threshold": 0.5 }
/// </summary>
public class BTWeightCondition : BTNode
{
    public string key;
    public float  threshold = 0.5f;

    public override NodeState Evaluate(Dictionary<string, object> blackboard,
                                       Dictionary<string, IActionExecutor> executors)
    {
        if (!blackboard.TryGetValue(key, out var val)) return NodeState.Failure;

        float weight = 0f;
        if      (val is float f)  weight = f;
        else if (val is double d) weight = (float)d;
        else if (val is int i)    weight = i;
        else if (!float.TryParse(val.ToString(), out weight))
            return NodeState.Failure;

        return weight >= threshold ? NodeState.Success : NodeState.Failure;
    }
}

/// <summary>
/// TacticCondition 节点
/// 检查 Blackboard 里的 combat_tactic 字符串是否等于期望值
/// JSON 写法：{ "type": "TacticCondition", "value": "Aggressive" }
/// </summary>
public class BTTacticCondition : BTNode
{
    public string expectedTactic;

    public override NodeState Evaluate(Dictionary<string, object> blackboard,
                                       Dictionary<string, IActionExecutor> executors)
    {
        if (!blackboard.TryGetValue("combat_tactic", out var val)) return NodeState.Failure;
        return val.ToString() == expectedTactic ? NodeState.Success : NodeState.Failure;
    }
}