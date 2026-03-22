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
                
                if (!string.IsNullOrEmpty(data.value))
                {
                    if (data.value == "true" || data.value == "True")
                        condition.expectedValue = true;
                    else if (data.value == "false" || data.value == "False")
                        condition.expectedValue = false;
                    else
                        condition.expectedValue = data.value;
                }
                
                Debug.Log($"[BTParser] Condition: key={condition.key} exists={condition.checkExists} value={condition.expectedValue} ({condition.expectedValue?.GetType().Name})");
                return condition;

            case "Action":
                var action = new BTAction();
                action.executorName = data.executor;
                action.blackboardKeys = data.keys;
                
                Debug.Log($"[BTParser] Created Action: executor={action.executorName}");
                return action;

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
    public string value;   // JsonUtility 不支持 object，改为 string 后在 BTParser 里手动转型
    public bool exists;
    public string executor;
    public string[] keys;
    public NodeData[] children;
}