using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IActionExecutor
{
    void Initialize(Transform owner, EnemyConfig config);
    bool CanExecute();
    IEnumerator Execute(ActionContext context);
}

public class ActionContext
{
    public Vector3 TargetPosition;
    public Transform TargetObject;
    public int TargetFloor;
    public Dictionary<string, object> Blackboard;
}