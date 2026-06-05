using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FollowLeaderExecutor — 编队跟随执行器（并行巡逻模式）
///
/// 核心设计：跟随者每回合都移动，而不是站在原地等 PatrolInZone 接管。
///
/// 行为逻辑：
///   ① 已在 formationDistance 内  → 在 Leader 周围随机选一个合法格移动（并行巡逻）
///   ② 超出 formationDistance     → 向离自己最近的 Leader 周围合法格靠拢（追上队形）
///
/// 区域约束：若 targetZoneId 不为空，候选格必须在区域内（防止跑出巡逻区）。
/// 降级条件：Leader 不存在/已死亡/不同楼层/找不到任何候选格 → yield break。
///
/// 平滑移动设计（三级选格优先级）：
///   Tier-1 — 不在历史中 且 与上次移动方向同向 → 直线推进，看起来最流畅
///   Tier-2 — 不在历史中（任意方向）           → 自然转向，探索新区域
///   Tier-3 — 历史格（fallback）               → 仅在真正无路时才折返
/// 历史深度 HistorySize=3 确保至少走过 3 格后才能回头，消除 A↔B 振荡。
/// </summary>
public class FollowLeaderExecutor : IActionExecutor
{
    private Transform    owner;
    private EnemyConfig  config;
    private UnitMovement unitMovement;

    // 缓存 Leader 引用
    private EnemyAIController _cachedLeader;
    private string            _cachedLeaderId;

    // 移动历史队列：滚动保存最近 HistorySize 步，用于抑制来回折返
    private readonly Queue<Vector2Int> _recentCells = new Queue<Vector2Int>();
    private const int HistorySize = 3;

    // 上次移动方向（单位向量，分量 ∈ {-1,0,1}），用于给选格加方向动量
    private Vector2Int _moveDir = Vector2Int.zero;

    // ── 初始化 ──────────────────────────────────────────

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner    = owner;
        this.config   = config;
        unitMovement  = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    // ── 执行 ────────────────────────────────────────────

    public IEnumerator Execute(ActionContext context)
    {
        // ── 1. 读取 leaderId ─────────────────────────────
        if (!context.Blackboard.TryGetValue("leaderId", out var leaderIdObj))
            yield break;
        string leaderId = leaderIdObj as string;
        if (string.IsNullOrEmpty(leaderId))
            yield break;

        // ── 2. 读取 formationDistance（默认 2 格）────────
        int formationDistance = 2;
        if (context.Blackboard.TryGetValue("formationDistance", out var distObj))
        {
            if (distObj is int di)        formationDistance = Mathf.Max(1, di);
            else if (distObj is float df) formationDistance = Mathf.Max(1, Mathf.RoundToInt(df));
        }

        // ── 3. 读取区域约束（可选）────────────────────────
        string zoneId = "";
        if (context.Blackboard.TryGetValue("targetZoneId", out var zoneObj))
            zoneId = zoneObj?.ToString() ?? "";

        // ── 3.2. 读取并排模式（可选）─────────────────────
        // moveInGroup=true：GetFormationCells 只返回正交相邻格（4 方向），
        // 强制跟随者贴紧 Leader，实现并排移动效果
        bool moveInGroup = context.Blackboard.TryGetValue("moveInGroup", out var migObj)
                           && migObj is bool mig && mig;

        // ── 3.5. 跨楼层优先：目标区域在其他楼层时，先向楼梯靠拢 ────────────
        // 设计原则：队形跟随是同楼层行为；若任务 zone 在其他楼层，
        // 跟随者应先独立导航到楼层连接点完成切换，无需等待 Leader 带路。
        // 到达目标楼层后，下回合恢复正常队形逻辑（此时 zone 约束自然生效）。
        if (!string.IsNullOrEmpty(zoneId) && ZoneManager.Instance != null)
        {
            if (ZoneManager.Instance.TryGetRandomCellInZone(
                    zoneId, out _, out int zoneFloor)
                && zoneFloor != unitMovement.CurrentFloor)
            {
                Debug.Log($"[FollowLeader] 目标区域 [{zoneId}] 在楼层 {zoneFloor}，" +
                          $"当前楼层 {unitMovement.CurrentFloor}，优先导航至楼梯连接点");
                yield return MoveToFloor(zoneFloor);
                yield break;
            }
        }

        // ── 4. 找到 Leader ────────────────────────────────
        EnemyAIController leader = GetLeader(leaderId);
        if (leader == null || !leader.IsAlive)
        {
            Debug.Log($"[FollowLeader] Leader [{leaderId}] 不存在或已死亡，降级");
            yield break;
        }

        var leaderMov = leader.GetComponent<UnitMovement>();
        if (leaderMov == null) yield break;

        // ── 5. 跨楼层：不处理，让 FloorChase 分支接手 ───
        int currentFloor = unitMovement.CurrentFloor;
        if (leaderMov.CurrentFloor != currentFloor)
        {
            Debug.Log($"[FollowLeader] Leader 在不同楼层，降级");
            yield break;
        }

        var turnUnit = owner.GetComponent<TurnBasedUnit>();

        // ── 6. AP 循环：单次 Execute 消耗完本回合所有剩余 AP ─────────────────
        // 核心设计：执行器不提前返回，而是在内部持续循环直到 AP 耗尽。
        // 这样 BTAction.isExecuting 在整个过程中保持 true，
        // WaitForMovementComplete 看到 IsActionRunning=true 就不会重入 ExecuteSingleAction，
        // 消除每步之间因行为树重新评估而产生的可见停顿。
        int loopGuard = 20; // 防止逻辑错误导致死循环
        while (loopGuard-- > 0)
        {
            // 每次迭代重新检查执行条件和 AP
            if (!CanExecute()) break;
            int availableAP = turnUnit != null ? turnUnit.RemainingActionPoints : 1;
            if (availableAP <= 0) break;

            // 每步重新采样位置（跟随者上一步已经移动，Leader 也可能已经移动）
            Vector2Int followerPos = unitMovement.CurrentGridPosition;
            Vector2Int leaderPos   = leaderMov.CurrentGridPosition;

            // 跨楼层：Leader 可能在上一步切换了楼层
            if (leaderMov.CurrentFloor != currentFloor)
            {
                Debug.Log($"[FollowLeader] Leader 已切换楼层，停止本回合移动");
                break;
            }

            // ── 收集候选格 ───────────────────────────────
            var candidates = GetFormationCells(leaderPos, formationDistance, currentFloor, zoneId, moveInGroup);
            if (candidates.Count == 0)
            {
                Debug.Log($"[FollowLeader] 无可用候选格，停止本回合移动");
                break;
            }

            // ── 选目标格（三级优先级）────────────────────
            Vector2Int target;
            int dist = ManhattanDist(followerPos, leaderPos);

            if (dist <= formationDistance)
            {
                // 队形内：同向(forward) > 非历史(side) > 历史(fallback)
                var forward  = new List<Vector2Int>(candidates.Count);
                var side     = new List<Vector2Int>(candidates.Count);
                var fallback = new List<Vector2Int>(candidates.Count);

                foreach (var c in candidates)
                {
                    if (_recentCells.Contains(c)) { fallback.Add(c); continue; }
                    side.Add(c);
                    if (_moveDir == Vector2Int.zero)
                        forward.Add(c);
                    else
                    {
                        Vector2Int d = c - followerPos;
                        if (d.x * _moveDir.x + d.y * _moveDir.y > 0) forward.Add(c);
                    }
                }

                var pool = forward.Count > 0 ? forward :
                           side.Count   > 0 ? side     : fallback;
                target = pool[Random.Range(0, pool.Count)];
                Debug.Log($"[FollowLeader] 队形内巡逻 → {target} AP:{availableAP} " +
                          $"(前:{forward.Count} 侧:{side.Count} 历:{fallback.Count})");
            }
            else
            {
                // 队形外：向最近候选格靠拢
                target = candidates[0];
                int minD = ManhattanDist(candidates[0], followerPos);
                for (int i = 1; i < candidates.Count; i++)
                {
                    int d = ManhattanDist(candidates[i], followerPos);
                    if (d < minD) { minD = d; target = candidates[i]; }
                }
                Debug.Log($"[FollowLeader] 追上队形 dist={dist}>{formationDistance} → {target} AP:{availableAP}");
            }

            // ── 寻路 + AP 限制 ───────────────────────────
            var path = PathfindingService.FindPath(followerPos, target, currentFloor);
            if (path == null || path.Count == 0)
            {
                Debug.LogWarning($"[FollowLeader] 无路径到 {target}，停止本回合移动");
                break;
            }

            int stepsToTake = Mathf.Min(availableAP, path.Count);
            if (stepsToTake <= 0) break;

            Vector2Int finalStep = path[stepsToTake - 1];

            // 更新方向动量（分量限制在 {-1,0,1}）
            Vector2Int delta = finalStep - followerPos;
            if (delta != Vector2Int.zero)
                _moveDir = new Vector2Int(
                    Mathf.Clamp(delta.x, -1, 1),
                    Mathf.Clamp(delta.y, -1, 1));

            // 写入移动历史（滚动窗口）
            _recentCells.Enqueue(finalStep);
            while (_recentCells.Count > HistorySize)
                _recentCells.Dequeue();

            // ── 执行移动，等待完成 ───────────────────────
            // 传入 stepsToTake 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
            unitMovement.MoveToGrid(finalStep, currentFloor, stepsToTake);
            yield return null; // 等一帧让 IsMoving 状态更新
            if (!unitMovement.IsMoving)
            {
                Debug.LogWarning($"[FollowLeader] MoveToGrid 启动失败，停止本回合移动");
                break;
            }
            while (unitMovement.IsMoving)
                yield return null;

            // 移动后楼层变化检查
            if (unitMovement.CurrentFloor != currentFloor) break;

            // 循环继续 → 消耗下一点 AP，无需重返行为树
        }
        // Execute 返回 → BTAction.isExecuting = false
        // → WaitForMovementComplete 的 WaitUntil 条件满足，正常推进回合结束判断
    }

    // ── 工具方法 ─────────────────────────────────────────

    /// <summary>
    /// 收集 Leader 周围 maxDist 内所有合法候选格：
    /// - Manhattan 距离 ≤ maxDist
    /// - 可行走且未被占用
    /// - 不是 Leader 自己的格子
    /// - 若 zoneId 非空且目标区域在当前楼层，必须在区域内
    ///
    /// 注意：若区域约束导致当前楼层全部候选格被过滤（常见于目标区域在其他楼层），
    /// 会自动放开区域约束，保证跟随者可以先移动到正确楼层，届时约束自然生效。
    /// </summary>
    /// <param name="moveInGroup">
    /// true = 并排模式：只返回正交相邻格（|dx|+|dy|==1），跟随者贴紧 Leader 侧旁
    /// false = 常规编队：返回 Manhattan 距离 ≤ maxDist 的所有合法格
    /// </param>
    private List<Vector2Int> GetFormationCells(
        Vector2Int leaderPos, int maxDist, int floor, string zoneId, bool moveInGroup = false)
    {
        var cells = new List<Vector2Int>();

        for (int dx = -maxDist; dx <= maxDist; dx++)
        {
            for (int dy = -maxDist; dy <= maxDist; dy++)
            {
                int manDist = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (manDist > maxDist) continue;
                if (dx == 0 && dy == 0) continue; // 不踩 Leader 的格子

                // 并排模式：只保留正交相邻格（4 方向，排除对角和更远格）
                if (moveInGroup && manDist != 1) continue;

                Vector2Int c = new Vector2Int(leaderPos.x + dx, leaderPos.y + dy);

                if (!GridManager.Instance.IsValid(c))           continue;
                if (!GridManager.Instance.IsWalkable(c, floor)) continue;
                if (GridManager.Instance.IsOccupied(c, floor))  continue;

                // 区域约束：有 zoneId 时只保留区域内的格子
                if (!string.IsNullOrEmpty(zoneId)
                    && ZoneManager.Instance != null
                    && !ZoneManager.Instance.IsUnitInZone(zoneId, c, floor))
                    continue;

                cells.Add(c);
            }
        }

        // ── 区域约束降级：若约束过滤后无候选格，说明目标区域不在当前楼层 ──
        // 此时放开区域约束，让跟随者可以正常跟随 Leader 移动（如上楼途中）。
        // 到达目标楼层后，区域约束会在下一次迭代中自然重新生效。
        if (cells.Count == 0 && !string.IsNullOrEmpty(zoneId))
        {
            Debug.Log($"[FollowLeader] 区域 [{zoneId}] 在楼层 {floor} 无有效格，放开区域约束跟随队形");
            for (int dx = -maxDist; dx <= maxDist; dx++)
            {
                for (int dy = -maxDist; dy <= maxDist; dy++)
                {
                    int manDist = Mathf.Abs(dx) + Mathf.Abs(dy);
                    if (manDist > maxDist) continue;
                    if (dx == 0 && dy == 0) continue;
                    if (moveInGroup && manDist != 1) continue; // 降级时同样保持并排约束

                    Vector2Int c = new Vector2Int(leaderPos.x + dx, leaderPos.y + dy);

                    if (!GridManager.Instance.IsValid(c))           continue;
                    if (!GridManager.Instance.IsWalkable(c, floor)) continue;
                    if (GridManager.Instance.IsOccupied(c, floor))  continue;

                    cells.Add(c);
                }
            }
        }

        return cells;
    }

    /// <summary>带缓存的 Leader 查找</summary>
    private EnemyAIController GetLeader(string leaderId)
    {
        if (_cachedLeader != null
            && _cachedLeader.EnemyId == leaderId
            && _cachedLeader.IsAlive)
            return _cachedLeader;

        _cachedLeaderId = leaderId;
        _cachedLeader   = null;

        foreach (var e in Object.FindObjectsOfType<EnemyAIController>())
        {
            if (e.EnemyId == leaderId)
            {
                _cachedLeader = e;
                return e;
            }
        }
        return null;
    }

    private static int ManhattanDist(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    // ── 跨楼层导航 ───────────────────────────────────────────

    /// <summary>
    /// 向目标楼层的连接点（楼梯）导航并触发楼层切换。
    /// 逻辑与 PatrolInZoneExecutor.MoveToFloor 完全一致：
    ///   1. 在当前楼层找到通往 targetFloor 的最近连接点
    ///   2. 按 AP 预算寻路过去
    ///   3. 到达连接点则调用 MoveToFloorCoroutine 切换楼层
    /// </summary>
    private IEnumerator MoveToFloor(int targetFloor)
    {
        if (FloorManager.Instance == null) yield break;

        int        currentFloor = unitMovement.CurrentFloor;
        FloorData  floorData    = FloorManager.Instance.GetFloor(currentFloor);
        if (floorData?.connections == null)
        {
            Debug.LogWarning($"[FollowLeader] 楼层 {currentFloor} 没有配置 connections，" +
                             $"无法前往楼层 {targetFloor}");
            yield break;
        }

        // 找当前楼层通往 targetFloor 的最近连接点
        FloorConnection nearest    = null;
        float           bestDist   = float.MaxValue;
        Vector2Int      currentPos = unitMovement.CurrentGridPosition;

        foreach (var conn in floorData.connections)
        {
            if (conn.toFloor != targetFloor) continue;
            float d = Vector2Int.Distance(currentPos, conn.gridPosition);
            if (d < bestDist) { bestDist = d; nearest = conn; }
        }

        if (nearest == null)
        {
            Debug.LogWarning($"[FollowLeader] 楼层 {currentFloor} 没有通往楼层 {targetFloor} 的连接点");
            yield break;
        }

        // 寻路到连接点，受 AP 限制
        List<Vector2Int> path = PathfindingService.FindPath(
            currentPos, nearest.gridPosition, currentFloor);
        if (path == null || path.Count == 0) yield break;

        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int steps    = Mathf.Min(
            turnUnit != null ? turnUnit.RemainingActionPoints : path.Count,
            path.Count);

        bool canReachThisTurn = steps >= path.Count;

        // 传入 steps 作为 apCost，防止 MoveToGrid 内部 re-pathfind 路径不同导致 AP 超耗
        unitMovement.MoveToGrid(path[steps - 1], currentFloor, steps);
        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
        if (unitMovement.IsMoving)
        {
            Debug.LogWarning("[FollowLeader] MoveToFloor 移动超时（10s），强制中断");
            yield break;
        }

        // 到达连接点 → 切换楼层
        if (canReachThisTurn && unitMovement.CurrentGridPosition == nearest.gridPosition)
        {
            Debug.Log($"[FollowLeader] 到达连接点 {nearest.gridPosition}，" +
                      $"切换到楼层 {targetFloor}");
            yield return unitMovement.MoveToFloorCoroutine(
                nearest.gridPosition, targetFloor, nearest);
        }
        // 本回合 AP 不足到达连接点 → 下回合继续（Execute 会重新检测）
    }
}
