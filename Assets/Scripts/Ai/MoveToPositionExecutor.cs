using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MoveToPositionExecutor — 通用"移动到指定格子"执行器
///
/// 任何系统需要让 AI 移动到具体位置时，向 Blackboard 写入：
///   "targetGridPosition" (Vector2Int) — 目标格坐标
///   "targetPositionFloor" (int)       — 目标楼层（可选，缺省用当前楼层）
///
/// 到达目标格后自动清除这两个 Blackboard key，BT 自然回落到下一个任务行为。
/// </summary>
public class MoveToPositionExecutor : IActionExecutor
{
    private Transform     owner;
    private UnitMovement  unitMovement;
    private TurnBasedUnit turnUnit;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner    = owner;
        unitMovement  = owner.GetComponent<UnitMovement>();
        turnUnit      = owner.GetComponent<TurnBasedUnit>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        // ── 读取目标坐标 ─────────────────────────────────────────────────
        if (!context.Blackboard.TryGetValue("targetGridPosition", out var posObj) ||
            posObj is not Vector2Int targetCell)
        {
            yield break;
        }

        int targetFloor = unitMovement.CurrentFloor;
        if (context.Blackboard.TryGetValue("targetPositionFloor", out var floorObj) &&
            floorObj is int f)
            targetFloor = f;

        Vector2Int currentPos = unitMovement.CurrentGridPosition;

        // 已经在目标格，清除 key 即可
        if (currentPos == targetCell && unitMovement.CurrentFloor == targetFloor)
        {
            ClearTargetKeys(context.Blackboard);
            yield break;
        }

        // ── 跨楼层处理 ───────────────────────────────────────────────────
        if (unitMovement.CurrentFloor != targetFloor)
        {
            yield return MoveToFloor(targetFloor);
            yield break;
        }

        // ── 目标格被占用时找最近相邻格 ───────────────────────────────────
        if (GridManager.Instance.IsOccupied(targetCell, targetFloor))
        {
            Vector2Int fallback = FindNearestEmpty(targetCell, currentPos, targetFloor);
            if (fallback == currentPos)
            {
                // 周围全满，无法靠近，清除目标防止卡死
                ClearTargetKeys(context.Blackboard);
                yield break;
            }
            targetCell = fallback;
        }

        // ── 寻路移动 ─────────────────────────────────────────────────────
        List<Vector2Int> path = PathfindingService.FindPath(currentPos, targetCell, targetFloor);
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[MoveToPositionExecutor] 无法寻路到 {targetCell}，清除目标");
            ClearTargetKeys(context.Blackboard);
            yield break;
        }

        int steps = Mathf.Min(
            turnUnit != null ? turnUnit.RemainingActionPoints : path.Count,
            path.Count);

        unitMovement.MoveToGrid(path[steps - 1], targetFloor, steps);

        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
        if (unitMovement.IsMoving)
            Debug.LogWarning("[MoveToPositionExecutor] 移动超时（10s），强制中断");

        // 到达目标格则清除，否则下回合继续
        if (unitMovement.CurrentGridPosition == targetCell)
            ClearTargetKeys(context.Blackboard);
    }

    private void ClearTargetKeys(Dictionary<string, object> blackboard)
    {
        blackboard.Remove("targetGridPosition");
        blackboard.Remove("targetPositionFloor");
    }

    private IEnumerator MoveToFloor(int targetFloor)
    {
        int currentFloor = unitMovement.CurrentFloor;
        FloorData floorData = FloorManager.Instance?.GetFloor(currentFloor);
        if (floorData?.connections == null) yield break;

        FloorConnection nearest = null;
        float nearestDist = float.MaxValue;
        Vector2Int currentPos = unitMovement.CurrentGridPosition;

        foreach (var conn in floorData.connections)
        {
            if (conn.toFloor != targetFloor) continue;
            float d = Vector2Int.Distance(currentPos, conn.gridPosition);
            if (d < nearestDist) { nearestDist = d; nearest = conn; }
        }
        if (nearest == null) yield break;

        List<Vector2Int> path = PathfindingService.FindPath(currentPos, nearest.gridPosition, currentFloor);
        if (path == null || path.Count == 0) yield break;

        int steps = Mathf.Min(
            turnUnit != null ? turnUnit.RemainingActionPoints : path.Count,
            path.Count);

        bool canReach = steps >= path.Count;
        unitMovement.MoveToGrid(path[steps - 1], currentFloor, steps);

        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;

        if (canReach && unitMovement.CurrentGridPosition == nearest.gridPosition)
            yield return unitMovement.MoveToFloorCoroutine(nearest.gridPosition, targetFloor, nearest);
    }

    private Vector2Int FindNearestEmpty(Vector2Int target, Vector2Int from, int floor)
    {
        Vector2Int[] dirs = {
            new Vector2Int(0,1), new Vector2Int(1,0),
            new Vector2Int(0,-1), new Vector2Int(-1,0)
        };
        Vector2Int best = from;
        float bestDist = float.MaxValue;
        foreach (var dir in dirs)
        {
            Vector2Int adj = target + dir;
            if (!GridManager.Instance.IsWalkable(adj, floor)) continue;
            if (GridManager.Instance.IsOccupied(adj, floor)) continue;
            float d = Vector2Int.Distance(adj, from);
            if (d < bestDist) { bestDist = d; best = adj; }
        }
        return best;
    }
}
