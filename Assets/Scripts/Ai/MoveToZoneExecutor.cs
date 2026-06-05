using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MoveToZoneExecutor — 导航到目标区域
/// 从 Blackboard 读取 targetZoneId，向 ZoneManager 查询可行走格子，然后导航过去
/// 用于任务层的 Mobile 行为模式（护送、运输、占领）
/// </summary>
public class MoveToZoneExecutor : IActionExecutor
{
    private Transform owner;
    private EnemyConfig config;
    private UnitMovement unitMovement;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner       = owner;
        this.config      = config;
        this.unitMovement = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        // 从 Blackboard 读取目标区域 ID
        if (!context.Blackboard.TryGetValue("targetZoneId", out var zoneIdObj) ||
            zoneIdObj == null || string.IsNullOrEmpty(zoneIdObj.ToString()))
        {
            Debug.LogWarning("[MoveToZoneExecutor] Blackboard 里没有 targetZoneId，跳过");
            yield break;
        }

        string zoneId = zoneIdObj.ToString();

        // 已经在目标区域内：在区域内随机选一个格子移动（区域内执行任务，不退回全局游走）
        if (ZoneManager.Instance != null &&
            ZoneManager.Instance.IsUnitInZone(zoneId,
                unitMovement.CurrentGridPosition, unitMovement.CurrentFloor))
        {
            Debug.Log($"[MoveToZoneExecutor] 已在目标区域 [{zoneId}] 内，区域内移动");
            yield return MoveInsideZone(zoneId);
            yield break;
        }

        // 不在区域内：从 ZoneManager 获取区域内目标格子，导航过去
        if (!ZoneManager.Instance.TryGetRandomCellInZone(zoneId, out Vector2Int targetCell, out int targetFloor))
        {
            Debug.LogWarning($"[MoveToZoneExecutor] 区域 [{zoneId}] 无可行走格子");
            yield break;
        }

        int currentFloor = unitMovement.CurrentFloor;

        // 跨楼层处理
        if (currentFloor != targetFloor)
        {
            yield return MoveToFloor(targetFloor);
            yield break;
        }

        // 同楼层寻路
        Vector2Int currentGrid = unitMovement.CurrentGridPosition;
        List<Vector2Int> path = PathfindingService.FindPath(currentGrid, targetCell, currentFloor);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[MoveToZoneExecutor] 找不到到区域 [{zoneId}] 的路径");
            yield break;
        }

        // 根据 AP 决定走几步
        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int availableSteps = turnUnit != null ? turnUnit.RemainingActionPoints : path.Count;
        int stepsToTake    = Mathf.Min(availableSteps, path.Count);
        Vector2Int finalStep = path[stepsToTake - 1];

        Debug.Log($"[MoveToZoneExecutor] 移动 {stepsToTake} 步 → 区域 [{zoneId}]");

        // 传入 stepsToTake 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        unitMovement.MoveToGrid(finalStep, currentFloor, stepsToTake);
        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
        if (unitMovement.IsMoving)
            Debug.LogWarning("[MoveToZoneExecutor] Execute 移动超时（10s），强制中断");
    }

    /// <summary>
    /// 已在 zone 内时：随机选 zone 内一个非当前位置的可行走格子并移动（受 AP 限制）
    /// 行为与 PatrolInZoneExecutor 在区域内的逻辑一致
    /// </summary>
    private IEnumerator MoveInsideZone(string zoneId)
    {
        Vector2Int currentPos = unitMovement.CurrentGridPosition;
        int        floor      = unitMovement.CurrentFloor;

        // 收集同 ID 所有 Box 的可行走格子（ZoneManager 已支持多 Box）
        var zones = ZoneManager.Instance.GetZones(zoneId);
        var candidates = new List<Vector2Int>();
        foreach (var zone in zones)
        {
            if (zone.Cells == null || zone.Floor != floor) continue;
            foreach (var c in zone.Cells)
            {
                if (c != currentPos && GridManager.Instance.IsWalkable(c, floor))
                    candidates.Add(c);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.Log($"[MoveToZoneExecutor] 区域 [{zoneId}] 内无其他可行走格，待命");
            yield break;
        }

        Vector2Int target = candidates[Random.Range(0, candidates.Count)];
        List<Vector2Int> path = PathfindingService.FindPath(currentPos, target, floor);
        if (path == null || path.Count == 0) yield break;

        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int steps    = Mathf.Min(turnUnit != null ? turnUnit.RemainingActionPoints : path.Count, path.Count);
        if (steps <= 0) yield break;

        unitMovement.MoveToGrid(path[steps - 1], floor, steps);
        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
        if (unitMovement.IsMoving)
            Debug.LogWarning("[MoveToZoneExecutor] MoveInsideZone 移动超时（10s），强制中断");
    }

    private IEnumerator MoveToFloor(int targetFloor)
    {
        if (FloorManager.Instance == null) yield break;

        int       currentFloor = unitMovement.CurrentFloor;
        FloorData floorData    = FloorManager.Instance.GetFloor(currentFloor);
        if (floorData?.connections == null)
        {
            Debug.LogWarning($"[MoveToZoneExecutor] 楼层 {currentFloor} 没有配置 connections，无法前往楼层 {targetFloor}");
            yield break;
        }

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
            Debug.LogWarning($"[MoveToZoneExecutor] 楼层 {currentFloor} 没有通往楼层 {targetFloor} 的连接点");
            yield break;
        }

        // AP 限制下移动到连接点
        List<Vector2Int> path = PathfindingService.FindPath(currentPos, nearest.gridPosition, currentFloor);
        if (path == null || path.Count == 0) yield break;

        var turnUnit  = owner.GetComponent<TurnBasedUnit>();
        int steps     = Mathf.Min(turnUnit != null ? turnUnit.RemainingActionPoints : path.Count, path.Count);
        bool canReach = steps >= path.Count;

        // 传入 steps 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        unitMovement.MoveToGrid(path[steps - 1], currentFloor, steps);
        float moveDeadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < moveDeadline) yield return null;
        if (unitMovement.IsMoving)
        {
            Debug.LogWarning("[MoveToZoneExecutor] MoveToFloor 移动超时（10s），强制中断");
            yield break;
        }

        // 到达连接点才切换楼层，使用 MoveToFloorCoroutine
        if (canReach && unitMovement.CurrentGridPosition == nearest.gridPosition)
        {
            Debug.Log($"[MoveToZoneExecutor] 到达连接点，切换到楼层 {targetFloor}");
            yield return unitMovement.MoveToFloorCoroutine(nearest.gridPosition, targetFloor, nearest);
        }
    }
}

/// <summary>
/// HoldZoneExecutor — 在目标区域内守卫
/// 若已在区域内则原地不动，若不在则移动到区域边缘
/// 用于任务层的 Defensive 行为模式
/// </summary>
public class HoldZoneExecutor : IActionExecutor
{
    private Transform owner;
    private EnemyConfig config;
    private UnitMovement unitMovement;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner        = owner;
        this.config       = config;
        this.unitMovement = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        if (!context.Blackboard.TryGetValue("targetZoneId", out var zoneIdObj) ||
            string.IsNullOrEmpty(zoneIdObj?.ToString()))
        {
            yield break;
        }

        string zoneId = zoneIdObj.ToString();

        // 已在区域内 → 原地待命
        if (ZoneManager.Instance != null &&
            ZoneManager.Instance.IsUnitInZone(zoneId,
                unitMovement.CurrentGridPosition, unitMovement.CurrentFloor))
        {
            Debug.Log($"[HoldZoneExecutor] 在区域 [{zoneId}] 内守卫，待命");
            yield break;
        }

        // 不在区域内 → 移动到区域
        if (!ZoneManager.Instance.TryGetRandomCellInZone(zoneId,
            out Vector2Int targetCell, out int targetFloor))
        {
            Debug.LogWarning($"[HoldZoneExecutor] 区域 [{zoneId}] 无可行走格子");
            yield break;
        }

        Vector2Int currentGrid = unitMovement.CurrentGridPosition;
        int currentFloor = unitMovement.CurrentFloor;

        if (currentFloor != targetFloor) yield break; // 跨楼层守卫暂不处理

        List<Vector2Int> path = PathfindingService.FindPath(currentGrid, targetCell, currentFloor);
        if (path == null || path.Count == 0) yield break;

        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int steps = Mathf.Min(
            turnUnit != null ? turnUnit.RemainingActionPoints : path.Count,
            path.Count);

        // 传入 steps 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        unitMovement.MoveToGrid(path[steps - 1], currentFloor, steps);
        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
        if (unitMovement.IsMoving)
            Debug.LogWarning("[HoldZoneExecutor] Execute 移动超时（10s），强制中断");
    }
}
