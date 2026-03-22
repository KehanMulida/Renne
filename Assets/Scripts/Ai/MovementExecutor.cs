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

        int stepsToTake = GetOptimalSteps(fullPath, availableSteps, context);

        // 已经在射程内（stepsToTake=0），不需要移动，直接结束让 BT 走 Combat 分支
        if (stepsToTake <= 0)
        {
            Debug.Log("[MovementExecutor] Already in weapon range, skip movement");
            yield break;
        }

        Vector2Int finalStep = fullPath[stepsToTake - 1];

        Debug.Log($"[MovementExecutor] Moving {stepsToTake} steps towards {targetGrid}, AP: {availableSteps}");

        unitMovement.MoveToGrid(finalStep, currentFloor);

        yield return null;

        if (!unitMovement.IsMoving)
        {
            Debug.LogWarning($"[MovementExecutor] MoveToGrid failed for {finalStep}");
            yield break;
        }

        while (unitMovement.IsMoving)
            yield return null;
    }

    /// <summary>
    /// 计算最优移动步数
    /// Chase 状态：找到路径中刚好进入武器射程的最远位置停下
    /// 其他状态：走到 AP 允许的最远位置
    /// </summary>
    private int GetOptimalSteps(List<Vector2Int> path, int availableSteps, ActionContext context)
    {
        int maxSteps = Mathf.Min(availableSteps, path.Count);

        if (!context.Blackboard.ContainsKey("hasVisualContact") ||
            !(bool)context.Blackboard["hasVisualContact"])
            return maxSteps;

        EnemyEquipment equip = owner.GetComponent<EnemyEquipment>();
        EnemyConfig config = owner.GetComponent<EnemyAIController>()?.config;
        if (config == null) return maxSteps;

        // 武器射程转换为世界单位
        float weaponRangeGrids = equip != null
            ? equip.GetAttackRange(config)
            : 1f;
        float weaponRangeWorld = weaponRangeGrids * GridManager.Instance.CellSize;

        if (!context.Blackboard.TryGetValue("lastSeenPosition", out object posObj)) return maxSteps;
        Vector3 targetWorldPos = (Vector3)posObj;

        // 当前距离
        float currentDist = Vector3.Distance(
            new Vector3(owner.position.x, targetWorldPos.y, owner.position.z),
            targetWorldPos);

        Debug.Log($"[MovementExecutor] Chase | currentDist:{currentDist:F1} | weaponRange:{weaponRangeWorld:F1} | maxSteps:{maxSteps}");

        // 已经在射程内，不需要移动
        if (currentDist <= weaponRangeWorld)
        {
            Debug.Log($"[MovementExecutor] Already in range, staying put");
            return 0;
        }

        // 从路径中找第一个进入射程的格子
        for (int i = 0; i < maxSteps; i++)
        {
            // 用 FloorManager 获取正确的世界坐标（含楼层Y）
            Vector3 stepWorldPos = FloorManager.Instance != null
                ? FloorManager.Instance.GridToWorld(path[i], unitMovement.CurrentFloor)
                : GridManager.Instance.GridToWorld(path[i]);

            // 用XZ平面距离避免Y差异干扰
            float dist = Vector3.Distance(
                new Vector3(stepWorldPos.x, targetWorldPos.y, stepWorldPos.z),
                targetWorldPos);

            Debug.Log($"[MovementExecutor] Step {i + 1}: grid{path[i]} dist:{dist:F1}");

            if (dist <= weaponRangeWorld)
            {
                Debug.Log($"[MovementExecutor] Stop at step {i + 1} (dist:{dist:F1} <= range:{weaponRangeWorld:F1})");
                return i + 1;
            }
        }

        Debug.Log($"[MovementExecutor] Cannot reach weapon range in {maxSteps} steps, moving max");
        return maxSteps;
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