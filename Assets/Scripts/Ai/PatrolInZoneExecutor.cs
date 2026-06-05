using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PatrolInZoneExecutor — 在指定 Zone 内巡逻
///
/// 逻辑：
/// 1. 若不在目标 Zone 内 → 先移动到 Zone
/// 2. 若已在 Zone 内 → 在 Zone 内随机选一个可行走格子移动
///
/// 用于任务层的巡逻行为：Enemy 被分配到某个区域后，在该区域内持续巡逻
/// </summary>
public class PatrolInZoneExecutor : IActionExecutor
{
    private Transform    owner;
    private EnemyConfig  config;
    private UnitMovement unitMovement;

    // 上次巡逻的目标格，避免反复走同一个格子
    private Vector2Int lastPatrolCell = Vector2Int.zero;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner        = owner;
        this.config       = config;
        this.unitMovement = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        // 读取目标 Zone ID
        if (!context.Blackboard.TryGetValue("targetZoneId", out var zoneIdObj) ||
            string.IsNullOrEmpty(zoneIdObj?.ToString()))
        {
            Debug.LogWarning("[PatrolInZoneExecutor] Blackboard 里没有 targetZoneId");
            yield break;
        }

        string zoneId        = zoneIdObj.ToString();
        Vector2Int currentPos = unitMovement.CurrentGridPosition;
        int currentFloor      = unitMovement.CurrentFloor;

        bool alreadyInZone = ZoneManager.Instance != null &&
                             ZoneManager.Instance.IsUnitInZone(zoneId, currentPos, currentFloor);

        if (!alreadyInZone)
        {
            // ── 不在 Zone 内：先走过去 ────────────────────────
            Debug.Log($"[PatrolInZoneExecutor] 不在区域 [{zoneId}]，移动过去");

            if (!ZoneManager.Instance.TryGetRandomCellInZone(zoneId,
                out Vector2Int entryCell, out int targetFloor))
            {
                Debug.LogWarning($"[PatrolInZoneExecutor] 区域 [{zoneId}] 无可行走格子");
                yield break;
            }

            // 跨楼层：先走到楼梯连接点，消耗 AP，下回合继续
            if (currentFloor != targetFloor)
            {
                // 找当前楼层最近的连接点，走过去就结束本回合
                // 下回合 MoveToFloor 会继续处理楼层切换
                var turnUnit = owner.GetComponent<TurnBasedUnit>();
                if (turnUnit != null && turnUnit.RemainingActionPoints <= 0)
                    yield break; // AP 不足，等下回合

                yield return MoveToFloor(targetFloor);
                yield break;
            }

            yield return MoveToCell(entryCell, currentFloor);
        }
        else
        {
            // ── 已在 Zone 内：随机选一个 Zone 内的格子巡逻 ──
            ZoneMarker zone = ZoneManager.Instance.GetZone(zoneId);
            if (zone == null || zone.Cells == null || zone.Cells.Count == 0)
                yield break;

            // 从 Zone 内的格子里随机选，排除当前位置和上次目标
            List<Vector2Int> candidates = new List<Vector2Int>();
            foreach (var cell in zone.Cells)
            {
                if (cell == currentPos)   continue; // 不选当前格
                if (cell == lastPatrolCell) continue; // 不选上次走过的格
                if (!GridManager.Instance.IsWalkable(cell, currentFloor)) continue;
                candidates.Add(cell);
            }

            // 如果候选格为空（Zone 太小），放开限制重新选
            if (candidates.Count == 0)
            {
                foreach (var cell in zone.Cells)
                {
                    if (cell != currentPos && GridManager.Instance.IsWalkable(cell, currentFloor))
                        candidates.Add(cell);
                }
            }

            if (candidates.Count == 0)
            {
                Debug.Log($"[PatrolInZoneExecutor] 区域 [{zoneId}] 没有其他可行走格子，待命");
                yield break;
            }

            // 随机选一个目标格
            Vector2Int patrolTarget = candidates[Random.Range(0, candidates.Count)];
            lastPatrolCell = patrolTarget;

            Debug.Log($"[PatrolInZoneExecutor] 在区域 [{zoneId}] 内巡逻 → {patrolTarget}");

            yield return MoveToCell(patrolTarget, currentFloor);
        }
    }

    // ── 工具方法 ──────────────────────────────────────────

    private IEnumerator MoveToCell(Vector2Int targetCell, int floor)
    {
        Vector2Int currentPos = unitMovement.CurrentGridPosition;

        if (targetCell == currentPos) yield break;

        // 如果目标格被占用，找相邻格
        if (GridManager.Instance.IsOccupied(targetCell, floor))
        {
            targetCell = FindNearestEmpty(targetCell, currentPos, floor);
            if (targetCell == currentPos) yield break;
        }

        List<Vector2Int> path = PathfindingService.FindPath(currentPos, targetCell, floor);
        if (path == null || path.Count == 0) yield break;

        // 根据 AP 决定走几步
        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int steps    = Mathf.Min(
            turnUnit != null ? turnUnit.RemainingActionPoints : path.Count,
            path.Count);

        // 传入 steps 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        unitMovement.MoveToGrid(path[steps - 1], floor, steps);
        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
        if (unitMovement.IsMoving)
            Debug.LogWarning("[PatrolInZoneExecutor] MoveToCell 移动超时（10s），强制中断");
    }

    private IEnumerator MoveToFloor(int targetFloor)
    {
        if (FloorManager.Instance == null) yield break;

        int       currentFloor = unitMovement.CurrentFloor;
        FloorData floorData    = FloorManager.Instance.GetFloor(currentFloor);
        if (floorData?.connections == null)
        {
            Debug.LogWarning($"[PatrolInZoneExecutor] 楼层 {currentFloor} 没有配置 connections，无法前往楼层 {targetFloor}");
            yield break;
        }

        // 找最近的楼层连接点
        FloorConnection nearest     = null;
        float           nearestDist = float.MaxValue;
        Vector2Int      currentPos  = unitMovement.CurrentGridPosition;

        foreach (var conn in floorData.connections)
        {
            if (conn.toFloor != targetFloor) continue;
            float d = Vector2Int.Distance(currentPos, conn.gridPosition);
            if (d < nearestDist) { nearestDist = d; nearest = conn; }
        }

        if (nearest == null)
        {
            Debug.LogWarning($"[PatrolInZoneExecutor] 楼层 {currentFloor} 没有通往楼层 {targetFloor} 的连接点");
            yield break;
        }

        // 寻路到连接点，受 AP 限制
        List<Vector2Int> path = PathfindingService.FindPath(currentPos, nearest.gridPosition, currentFloor);
        if (path == null || path.Count == 0) yield break;

        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int steps = Mathf.Min(
            turnUnit != null ? turnUnit.RemainingActionPoints : path.Count,
            path.Count);

        // 提前判断本回合是否能到达连接点（移动前判断，避免 AP 被消耗后再判断导致漏切层）
        bool canReachThisTurn = steps >= path.Count;

        // 传入 steps 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        unitMovement.MoveToGrid(path[steps - 1], currentFloor, steps);
        float moveDeadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < moveDeadline) yield return null;
        if (unitMovement.IsMoving)
        {
            Debug.LogWarning("[PatrolInZoneExecutor] MoveToFloor 移动超时（10s），强制中断");
            yield break;
        }

        // 到达连接点 → 用 MoveToFloorCoroutine 切换楼层（与 FloorChaseExecutor 保持一致）
        if (canReachThisTurn && unitMovement.CurrentGridPosition == nearest.gridPosition)
        {
            Debug.Log($"[PatrolInZoneExecutor] 到达连接点，切换到楼层 {targetFloor}");
            yield return unitMovement.MoveToFloorCoroutine(nearest.gridPosition, targetFloor, nearest);
        }
        // 未能到达：本回合 AP 不足，下回合 Execute 会重新尝试
    }

    private Vector2Int FindNearestEmpty(Vector2Int target, Vector2Int from, int floor)
    {
        Vector2Int[] dirs = {
            new Vector2Int(0,1), new Vector2Int(1,0),
            new Vector2Int(0,-1), new Vector2Int(-1,0)
        };

        Vector2Int best     = from;
        float      bestDist = float.MaxValue;

        foreach (var dir in dirs)
        {
            Vector2Int adj = target + dir;
            if (GridManager.Instance.IsWalkable(adj, floor) &&
                !GridManager.Instance.IsOccupied(adj, floor))
            {
                float d = Vector2Int.Distance(adj, from);
                if (d < bestDist) { bestDist = d; best = adj; }
            }
        }
        return best;
    }
}