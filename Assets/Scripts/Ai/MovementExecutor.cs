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
        
        // 如果目标格子被占据，找相邻格子
        if (GridManager.Instance.IsWalkable(targetGrid, unitMovement.CurrentFloor))
        {
            targetGrid = FindNearestEmptyAdjacentTile(targetGrid, currentGrid);
        }
        
        if (targetGrid == currentGrid)
        {
            yield break; // 已经在最近位置
        }
        
        unitMovement.MoveToGrid(targetGrid, context.TargetFloor != -1 ? context.TargetFloor : unitMovement.CurrentFloor);
        
        while (unitMovement.IsMoving)
        {
            yield return null;
        }
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
                !GridManager.Instance.IsOccupied(adjacent))
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