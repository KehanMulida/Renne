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
        Vector2Int currentGrid = unitMovement.CurrentGridPosition;
        int currentFloor = unitMovement.CurrentFloor;

        // 敌人开始移动时触发 QTE（战斗模式下）
        // 玩家可在敌人走动期间移动 2 格或使用道具
        if (CombatModeManager.Instance != null &&
            CombatModeManager.Instance.IsInCombatMode &&
            config.qteWindowDuration > 0f)
        {
            Vector3 moveDir = (context.TargetPosition - owner.position).normalized;
            CombatModeManager.Instance.NotifyEnemyAction(owner.gameObject, moveDir, config.qteWindowDuration);
        }

        // Debug.Log($"[MovementExecutor] Execute | currentFloor:{currentFloor}");

        Vector2Int targetGrid = GridManager.Instance.WorldToGrid(context.TargetPosition);
        // Debug.Log($"[MovementExecutor] Moving to {targetGrid} on floor {currentFloor}");

        if (GridManager.Instance.IsOccupied(targetGrid, currentFloor))
            targetGrid = FindNearestEmptyAdjacentTile(targetGrid, currentGrid);

        if (targetGrid == currentGrid)
        {
            // Debug.Log("[MovementExecutor] Already at target");
            yield break;
        }

        List<Vector2Int> fullPath = PathfindingService.FindPath(currentGrid, targetGrid, currentFloor);

        if (fullPath == null || fullPath.Count == 0)
        {
            Debug.LogWarning($"[MovementExecutor] No path found to {targetGrid}");
            yield break;
        }

        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int availableSteps = turnUnit != null ? turnUnit.RemainingActionPoints : fullPath.Count;

        int stepsToTake = GetOptimalSteps(fullPath, availableSteps, context);

        if (stepsToTake <= 0)
        {
            // Debug.Log("[MovementExecutor] Already in weapon range, skip movement");
            yield break;
        }

        Vector2Int finalStep = fullPath[stepsToTake - 1];
        // Debug.Log($"[MovementExecutor] Moving {stepsToTake} steps to {finalStep}");

        // 传入 stepsToTake 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        unitMovement.MoveToGrid(finalStep, currentFloor, stepsToTake);

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

        // 当前距离（XZ平面，避免Y差异干扰射程判断）
        float currentDist = new Vector2(
            owner.position.x - targetWorldPos.x,
            owner.position.z - targetWorldPos.z).magnitude;

        // 不同楼层时不判断射程，直接走向楼层连接点
        int ownerFloor  = unitMovement.CurrentFloor;
        int targetFloor = context.Blackboard.ContainsKey("lastSeenFloor")
            ? (int)context.Blackboard["lastSeenFloor"] : ownerFloor;

        if (ownerFloor != targetFloor)
        {
            // Debug.Log($"[MovementExecutor] Different floor ({ownerFloor}→{targetFloor}), moving to connection");
            return maxSteps;
        }

        // Debug.Log($"[MovementExecutor] Chase | currentDist:{currentDist:F1} | weaponRange:{weaponRangeWorld:F1} | maxSteps:{maxSteps}");

        // 已经在射程内，不需要移动
        if (currentDist <= weaponRangeWorld)
        {
            // Debug.Log($"[MovementExecutor] Already in range, staying put");
            return 0;
        }

        // 从路径中找第一个进入射程的格子
        for (int i = 0; i < maxSteps; i++)
        {
            Vector3 stepWorldPos = FloorManager.Instance != null
                ? FloorManager.Instance.GridToWorld(path[i], unitMovement.CurrentFloor)
                : GridManager.Instance.GridToWorld(path[i]);

            float dist = new Vector2(
                stepWorldPos.x - targetWorldPos.x,
                stepWorldPos.z - targetWorldPos.z).magnitude;

            // Debug.Log($"[MovementExecutor] Step {i + 1}: grid{path[i]} dist:{dist:F1}");

            if (dist <= weaponRangeWorld)
            {
                // Debug.Log($"[MovementExecutor] Stop at step {i + 1} (dist:{dist:F1} <= range:{weaponRangeWorld:F1})");
                return i + 1;
            }
        }

        // Debug.Log($"[MovementExecutor] Cannot reach weapon range in {maxSteps} steps, moving max");
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

}