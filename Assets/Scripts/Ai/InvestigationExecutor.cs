using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InvestigationExecutor : IActionExecutor
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
        Vector2Int targetGrid = GridManager.Instance.WorldToGrid(context.TargetPosition);
        // 声音感知已保证同楼层，保险起见仍做 fallback
        int targetFloor = context.TargetFloor != -1 ? context.TargetFloor : unitMovement.CurrentFloor;

        // 只检查静态障碍（ignoreOccupied:true）
        // 关键：玩家站在声音来源格时 IsOccupied=true，若此处不忽略占据，
        // IsWalkable 会返回 false，executor 直接退出，
        // 永远到不了下面"找相邻格"的逻辑，导致敌人听到声音但不移动
        if (!GridManager.Instance.IsValid(targetGrid) ||
            !GridManager.Instance.IsWalkable(targetGrid, targetFloor, ignoreOccupied: true))
        {
            Debug.LogWarning($"[InvestigationExecutor] Target {targetGrid} is invalid or static-blocked, skipping");
            yield break;
        }

        // ── 关键修复：目标格子被占用时（玩家站在声音来源处），改走相邻最近空格 ──
        // 若不处理，MoveToGrid 会对被占用格直接失败，导致 Enemy 每回合原地不动
        if (GridManager.Instance.IsOccupied(targetGrid, unitMovement.CurrentFloor))
        {
            Vector2Int adjacent = FindNearestEmptyAdjacentTile(
                targetGrid, unitMovement.CurrentGridPosition, unitMovement.CurrentFloor);

            if (adjacent == unitMovement.CurrentGridPosition)
            {
                // 周围没有可达空格（极罕见），原地结束调查（不等待，直接清除）
                Debug.Log($"[InvestigationExecutor] Target {targetGrid} occupied & no adjacent reachable, aborting");
                ClearSoundKeys(context.Blackboard);
                yield break;
            }

            Debug.Log($"[InvestigationExecutor] Target {targetGrid} occupied → reroute to adjacent {adjacent}");
            targetGrid = adjacent;
        }

        // 已经在目标位置：调查完成，清除记忆（不需要再等待，AP 已经反映了行动代价）
        if (unitMovement.CurrentGridPosition == targetGrid)
        {
            Debug.Log($"[InvestigationExecutor] Already at {targetGrid}, investigation done");
            ClearSoundKeys(context.Blackboard);
            yield break;
        }

        // ── AP 步数限制（与 MovementExecutor 对齐，禁止一次跨越全图）──────────────
        int availableSteps = turnUnit != null ? turnUnit.RemainingActionPoints : 1;

        List<Vector2Int> path = PathfindingService.FindPath(
            unitMovement.CurrentGridPosition, targetGrid, targetFloor);

        if (path == null || path.Count == 0)
        {
            // 路径不存在：本次 action 放弃移动，但保留声音记忆（让倒计时自然耗尽）
            // 不清除 soundKeys——若清除则敌人会立刻停止调查、原地发呆
            Debug.LogWarning($"[InvestigationExecutor] No path to {targetGrid}, skipping this action");
            yield break;
        }

        int stepsToTake = Mathf.Min(availableSteps, path.Count);

        // 安全防护：AP 为 0 时直接退出（理论上 CanAct 已拦截，此处双重保险）
        if (stepsToTake <= 0)
        {
            yield break;
        }

        Vector2Int finalStep = path[stepsToTake - 1];

        Debug.Log($"[InvestigationExecutor] Moving {stepsToTake}/{path.Count} steps to {finalStep} (AP:{availableSteps})");

        unitMovement.MoveToGrid(finalStep, targetFloor, stepsToTake);

        // 等一帧确保 IsMoving 有机会变成 true
        yield return null;

        // MoveToGrid 静默失败（目标格在 FindPath 后被占用等竞态情况）
        // 同样保留声音记忆而不清除，下一回合重试
        if (!unitMovement.IsMoving)
        {
            Debug.LogWarning($"[InvestigationExecutor] MoveToGrid failed for {finalStep}, will retry next turn");
            yield break;
        }

        while (unitMovement.IsMoving)
            yield return null;

        // ── 到达调查位置 ───────────────────────────────────────────────────────────
        // 回合制游戏中，"调查"的时间代价已经体现在 AP 消耗和 soundInvestigateCountdown 回合数上。
        // 不需要额外的实时等待（investigationDuration = 5s 会让整个回合卡住）。
        // 若已抵达目标位置，清除声音记忆；若还没到，下回合继续移动（countdown 控制）。
        if (unitMovement.CurrentGridPosition == targetGrid ||
            Vector2Int.Distance(unitMovement.CurrentGridPosition, targetGrid) <= 1)
        {
            // 到达目标或相邻格：调查完成
            Debug.Log($"[InvestigationExecutor] Reached investigation point {unitMovement.CurrentGridPosition}, done");
            ClearSoundKeys(context.Blackboard);
        }
        else
        {
            // 还未到达（AP 不足）：保留声音记忆，下回合继续
            Debug.Log($"[InvestigationExecutor] Moving toward {targetGrid}, {Vector2Int.Distance(unitMovement.CurrentGridPosition, targetGrid):F0} cells away");
        }
    }

    // ── 辅助方法 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 清除 Blackboard 中声音调查相关的所有 key
    /// </summary>
    private static void ClearSoundKeys(Dictionary<string, object> blackboard)
    {
        blackboard.Remove("lastHeardPosition");
        blackboard.Remove("lastHeardFloor");
        blackboard.Remove("soundInvestigateCountdown");
    }

    /// <summary>
    /// 在 <paramref name="target"/> 的四邻格中找到最近的、可行走且未被占用的格子
    /// 找不到时返回 <paramref name="from"/>（当前格）作为失败标记
    /// </summary>
    private Vector2Int FindNearestEmptyAdjacentTile(Vector2Int target, Vector2Int from, int floor)
    {
        Vector2Int[] dirs = {
            new Vector2Int(0, 1), new Vector2Int(1, 0),
            new Vector2Int(0, -1), new Vector2Int(-1, 0)
        };

        Vector2Int best = from;   // fallback = 当前格，调用方以此判断失败
        float bestDist  = float.MaxValue;

        foreach (var dir in dirs)
        {
            Vector2Int adj = target + dir;
            if (!GridManager.Instance.IsWalkable(adj, floor)) continue;
            if (GridManager.Instance.IsOccupied(adj, floor))  continue;

            float d = Vector2Int.Distance(adj, from);
            if (d < bestDist)
            {
                bestDist = d;
                best     = adj;
            }
        }

        return best;
    }
}