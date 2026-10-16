using System.Collections;
using UnityEngine;

/// <summary>
/// 战斗走位执行器：把 AI 挪到已选好的「掩体旁射击位」（blackboard 的 coverTarget）。
///
/// 分工说明：
///   · **掩体筛选不在这里**——在 EnemyAIController.EvaluateState 里做，且只对交战中的 AI、
///     每回合一次。选中才写 coverTarget，没选中就不写。
///     这样 BT 的 Combat_TakeCover 分支可以用「coverTarget 是否存在」当条件：
///     没掩体时该分支直接 Failure、干净地落到开火分支，而不是空跑一个 Action 浪费整个回合。
///   · 这里只负责「走过去」，与 MovementExecutor 分开是为了避开它 "move" 专用的
///     巡逻点随机化副作用，并给 Phase 4（到位后贴边/下蹲/探头）留一个落点。
/// </summary>
public class CombatMoveExecutor : IActionExecutor
{
    private UnitMovement  unitMovement;
    private TurnBasedUnit turnUnit;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        unitMovement = owner.GetComponent<UnitMovement>();
        turnUnit     = owner.GetComponent<TurnBasedUnit>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        var gm = GridManager.Instance;
        if (gm == null || unitMovement == null) yield break;

        Vector2Int targetGrid = gm.WorldToGrid(context.TargetPosition);
        int floor = context.TargetFloor != -1 ? context.TargetFloor : unitMovement.CurrentFloor;

        Vector2Int aiCell = unitMovement.CurrentGridPosition;
        if (targetGrid == aiCell) yield break;               // 已在位

        if (!gm.IsValid(targetGrid) || !gm.IsWalkable(targetGrid, floor, ignoreOccupied: true))
            yield break;

        var path = PathfindingService.FindPath(aiCell, targetGrid, floor);
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[CombatMove] 到掩体位 {targetGrid} 无路径");
            yield break;
        }

        int availableSteps = turnUnit != null ? turnUnit.RemainingActionPoints : 1;
        int stepsToTake    = Mathf.Min(availableSteps, path.Count);
        if (stepsToTake <= 0) yield break;

        Vector2Int finalStep = path[stepsToTake - 1];
        Debug.Log($"[CombatMove] 前往掩体位 {targetGrid}：本回合走 {stepsToTake}/{path.Count} 步到 {finalStep}");

        unitMovement.MoveToGrid(finalStep, floor, stepsToTake);

        yield return null;                        // 等一帧让 IsMoving 生效
        if (!unitMovement.IsMoving) yield break;  // MoveToGrid 静默失败，下回合重试

        while (unitMovement.IsMoving) yield return null;
    }
}
