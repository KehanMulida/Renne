using System.Collections;
using UnityEngine;

public class InvestigationExecutor : IActionExecutor
{
    private Transform owner;
    private EnemyConfig config;
    private UnitMovement unitMovement;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner = owner;
        this.config = config;
        this.unitMovement = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        Vector2Int targetGrid = GridManager.Instance.WorldToGrid(context.TargetPosition);
        int targetFloor = context.TargetFloor != -1 ? context.TargetFloor : unitMovement.CurrentFloor;

        // 目标格子无效或不可行走时，清掉听觉数据防止无限重试
        if (!GridManager.Instance.IsValid(targetGrid) ||
            !GridManager.Instance.IsWalkable(targetGrid, targetFloor))
        {
            Debug.LogWarning($"[InvestigationExecutor] Invalid target {targetGrid}, clearing heard position");
            context.Blackboard.Remove("lastHeardPosition");
            context.Blackboard.Remove("lastHeardFloor");
            yield break;
        }

        // 已经在目标位置，直接调查
        if (unitMovement.CurrentGridPosition == targetGrid)
        {
            yield return new WaitForSeconds(config.investigationDuration);
            context.Blackboard.Remove("lastHeardPosition");
            context.Blackboard.Remove("lastHeardFloor");
            yield break;
        }

        unitMovement.MoveToGrid(targetGrid, targetFloor);

        // 等一帧确保 IsMoving 有机会变成 true
        yield return null;

        // MoveToGrid 静默失败（无路可走）
        if (!unitMovement.IsMoving)
        {
            Debug.LogWarning($"[InvestigationExecutor] MoveToGrid failed for {targetGrid}, clearing heard position");
            context.Blackboard.Remove("lastHeardPosition");
            context.Blackboard.Remove("lastHeardFloor");
            yield break;
        }

        while (unitMovement.IsMoving)
            yield return null;

        yield return new WaitForSeconds(config.investigationDuration);

        // 调查完成，清掉目标
        context.Blackboard.Remove("lastHeardPosition");
        context.Blackboard.Remove("lastHeardFloor");
    }
}