using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 楼层追击执行器
/// 在回合内执行：走到楼梯 → 上楼
/// 如果当前回合 AP 不够走到楼梯，走尽量多格子，下回合继续
/// </summary>
public class FloorChaseExecutor : IActionExecutor
{
    private Transform     owner;
    private EnemyConfig   config;
    private UnitMovement  unitMovement;
    private TurnBasedUnit turnUnit;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner    = owner;
        this.config   = config;
        unitMovement  = owner.GetComponent<UnitMovement>();
        turnUnit      = owner.GetComponent<TurnBasedUnit>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        if (!context.Blackboard.ContainsKey("pendingFollowFloor"))
            yield break;

        int targetFloor   = (int)context.Blackboard["pendingFollowFloor"];
        int currentFloor  = unitMovement.CurrentFloor;

        if (currentFloor == targetFloor)
        {
            context.Blackboard.Remove("pendingFollowFloor");
            yield break;
        }

        // 找最近的楼层连接点
        FloorData floorData = FloorManager.Instance?.GetFloor(currentFloor);
        if (floorData?.connections == null)
        {
            Debug.LogWarning("[FloorChaseExecutor] No connections found");
            yield break;
        }

        FloorConnection bestConnection = null;
        float nearestDist = float.MaxValue;

        foreach (var connection in floorData.connections)
        {
            if (connection.toFloor != targetFloor) continue;

            Vector3 worldPos = FloorManager.Instance.GridToWorld(
                connection.gridPosition, currentFloor);

            float dist = new Vector2(
                worldPos.x - owner.position.x,
                worldPos.z - owner.position.z).magnitude;

            if (dist < nearestDist)
            {
                nearestDist    = dist;
                bestConnection = connection;
            }
        }

        if (bestConnection == null)
        {
            Debug.LogWarning($"[FloorChaseExecutor] No connection to floor {targetFloor}");
            yield break;
        }

        Vector2Int currentGrid    = unitMovement.CurrentGridPosition;
        Vector2Int connectionGrid = bestConnection.gridPosition;

        // 已经在连接点，直接上楼
        if (currentGrid == connectionGrid)
        {
            Debug.Log($"[FloorChaseExecutor] Already at connection, going to floor {targetFloor}");
            yield return unitMovement.MoveToFloorCoroutine(
                connectionGrid, targetFloor, bestConnection);

            context.Blackboard.Remove("pendingFollowFloor");
            yield break;
        }

        // 寻路到连接点
        List<Vector2Int> path = PathfindingService.FindPath(
            currentGrid, connectionGrid, currentFloor);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[FloorChaseExecutor] No path to connection");
            yield break;
        }

        // 检查当前回合 AP
        int availableSteps = turnUnit != null ? turnUnit.RemainingActionPoints : path.Count;
        int stepsToTake = Mathf.Min(availableSteps, path.Count);

        if (stepsToTake <= 0) yield break;

        // 判断本回合是否能走到连接点
        bool canReachThisTurn = stepsToTake >= path.Count;

        // 移动：传入 stepsToTake 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        Vector2Int finalStep = path[stepsToTake - 1];
        unitMovement.MoveToGrid(finalStep, currentFloor, stepsToTake);

        yield return null;
        if (!unitMovement.IsMoving) yield break;
        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
        if (unitMovement.IsMoving)
        {
            Debug.LogWarning("[FloorChaseExecutor] 移动超时（10s），强制中断");
            yield break;
        }

        Debug.Log($"[FloorChaseExecutor] Moved to {finalStep} | canReach:{canReachThisTurn}");

        // 到达连接点，执行上楼
        if (canReachThisTurn && unitMovement.CurrentGridPosition == connectionGrid)
        {
            Debug.Log($"[FloorChaseExecutor] Reached connection, going to floor {targetFloor}");
            yield return unitMovement.MoveToFloorCoroutine(
                connectionGrid, targetFloor, bestConnection);

            context.Blackboard.Remove("pendingFollowFloor");
        }
        // 没到达，下回合继续（pendingFollowFloor 保留）
    }
}