using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovementExecutor : IActionExecutor
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
        Vector2Int currentGrid = unitMovement.CurrentGridPosition;
        int currentFloor = unitMovement.CurrentFloor;
        
        // 从 blackboard 读取目标楼层
        int targetFloor = currentFloor;
        if (context.Blackboard.ContainsKey("lastSeenFloor"))
        {
            targetFloor = (int)context.Blackboard["lastSeenFloor"];
        }
        
        // 如果需要跨楼层
        if (currentFloor != targetFloor)
        {
            yield return MoveToFloor(targetFloor);
            yield break;
        }
        
        // 同楼层移动
        if (GridManager.Instance.IsOccupied(targetGrid, currentFloor))
        {
            targetGrid = FindNearestEmptyAdjacentTile(targetGrid, currentGrid);
        }
        
        if (targetGrid == currentGrid)
        {
            yield break;
        }
        
        List<Vector2Int> fullPath = PathfindingService.FindPath(currentGrid, targetGrid, currentFloor);
        
        if (fullPath == null || fullPath.Count == 0)
        {
            Debug.LogWarning("[MovementExecutor] No path found");
            yield break;
        }
        
        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int availableSteps = turnUnit != null ? turnUnit.RemainingActionPoints : fullPath.Count;
        int stepsToTake = Mathf.Min(availableSteps, fullPath.Count);
        Vector2Int finalStep = fullPath[stepsToTake - 1];
        
        Debug.Log($"[MovementExecutor] Moving {stepsToTake} steps towards {targetGrid}, AP: {availableSteps}");
        
        unitMovement.MoveToGrid(finalStep, currentFloor);

        // 等一帧确保 IsMoving 有机会变成 true
        yield return null;

        // MoveToGrid 静默失败（路径被阻挡或目标被占据）
        if (!unitMovement.IsMoving)
        {
            Debug.LogWarning($"[MovementExecutor] MoveToGrid failed for {finalStep}");
            yield break;
        }

        while (unitMovement.IsMoving)
            yield return null;
    }

    private Vector2Int FindNearestEmptyAdjacentTile(Vector2Int target, Vector2Int from)
    {
        Vector2Int[] directions = {
            new Vector2Int(0, 1), new Vector2Int(1, 0),
            new Vector2Int(0, -1), new Vector2Int(-1, 0)
        };
        
        Vector2Int bestTile = from;
        float bestDistance = float.MaxValue;
        
        foreach (var dir in directions)
        {
            Vector2Int adjacent = target + dir;
            if (GridManager.Instance.IsWalkable(adjacent, unitMovement.CurrentFloor) &&
                !GridManager.Instance.IsOccupied(adjacent, unitMovement.CurrentFloor))
            {
                float dist = Vector2Int.Distance(adjacent, from);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestTile = adjacent;
                }
            }
        }
        
        return bestTile;
    }

    private IEnumerator MoveToFloor(int targetFloor)
    {
        var connection = GetNearestFloorConnection(targetFloor);
        if (connection == null) yield break;
        
        unitMovement.MoveToGrid(connection.gridPosition, unitMovement.CurrentFloor);
        
        while (unitMovement.IsMoving)
        {
            yield return null;
        }

        if (connection.connectionType == FloorConnectionType.Stairs)
        {
            yield return UseStairs(connection);
        }
        else if (connection.connectionType == FloorConnectionType.Elevator)
        {
            yield return UseElevator(connection);
        }
    }

    private FloorConnection GetNearestFloorConnection(int toFloor)
    {
        if (FloorManager.Instance == null) return null;

        FloorData floorData = FloorManager.Instance.GetFloor(unitMovement.CurrentFloor);
        if (floorData == null || floorData.connections == null) return null;

        FloorConnection nearestConnection = null;
        float nearestDistance = float.MaxValue;

        foreach (var connection in floorData.connections)
        {
            if (connection.toFloor == toFloor)
            {
                float distance = Vector2Int.Distance(unitMovement.CurrentGridPosition, connection.gridPosition);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestConnection = connection;
                }
            }
        }

        return nearestConnection;
    }

    private IEnumerator UseStairs(FloorConnection connection)
    {
        unitMovement.SetFloor(connection.toFloor);
        yield return new WaitForSeconds(1f);
    }

    private IEnumerator UseElevator(FloorConnection connection)
    {
        unitMovement.SetFloor(connection.toFloor);
        yield return new WaitForSeconds(2f);
    }
}