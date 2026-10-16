using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 寻路节点（仅用于A*算法的内部计算）
/// </summary>
public class PathNode
{
    public Vector2Int position;
    public PathNode parent;
    public int gCost;
    public int hCost;
    public int fCost => gCost + hCost;

    /// <summary>从 parent 走到本节点的方向。用于判断「这一步是不是拐了弯」</summary>
    public Vector2Int dirFromParent;

    public PathNode(Vector2Int pos)
    {
        position = pos;
        gCost = int.MaxValue;
    }
}

/// <summary>
/// 寻路服务 - 纯静态工具类（支持多楼层）
/// 修改点：
/// 1. FindPath 终点会检查占据状态，避免移动到已有单位的格子
/// 2. CalculateMovementRange 使用 ignoreOccupied=true，让范围显示不受占据影响
///    但 FindPath 中路径节点仍然绕开占据格子
/// </summary>
public static class PathfindingService
{
    // ══════════════════════════════════════════════════════════════════════
    // 直线偏好（转向惩罚）
    //
    // 问题：4 向均匀网格上，两点之间的最短路径有非常多条（任意单调阶梯都一样长）。
    // 原来选中哪条纯粹取决于 GetNeighbors 的遍历顺序（上→下→左→右），
    // 结果必然走成阶梯，表现为敌人一回合内左拐右拐。
    //
    // 解法：每拐一次弯加一点点代价，让 A* 在等长路径里挑拐弯最少的那条。
    //
    // ⚠ 惩罚必须「小到不足以改变路径长度」，否则敌人会为了少拐弯而多走格子、
    //   白白多耗 AP。所以把每步代价放大 StepCost 倍，转向惩罚固定为 1：
    //   只要一条路径上的拐弯数 < StepCost，就绝不可能压过"多走一格"的代价。
    //   换句话说，路径长度永远优先，拐弯数只在长度打平时才起作用。
    //
    // 放大只发生在 A* 内部（gCost/hCost 都放大，保持启发函数可采纳）。
    // FindPath 返回的是坐标列表，不含代价，所以 AP 计算完全不受影响。
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>每格移动代价的内部放大倍数。必须大于单条路径上可能出现的最大拐弯数</summary>
    private const int StepCost = 64;

    /// <summary>每拐一次弯的附加代价。相对 StepCost 极小，只用于等长路径之间的取舍</summary>
    public static int TurnPenalty = 1;

    /// <summary>
    /// A*寻路算法（支持楼层）
    /// 注意：
    /// - 起点忽略占据检查（单位自己站着的格子）
    /// - 终点检查占据状态（不能走到有其他单位的格子）
    /// - 路径中间格子也会绕开被占据的格子（通过 GetNeighbors 实现）
    /// </summary>
    public static List<Vector2Int> FindPath(Vector2Int start, Vector2Int end, int floor = 0)
    {
        // 起点只检查静态障碍物（单位站在自己格子上是正常的）
        if (!GridManager.Instance.IsWalkable(start, floor, ignoreOccupied: true))
        {
            Debug.LogWarning($"[Pathfinding] Start position {start} is not walkable");
            return null;
        }

        // 终点检查静态障碍物和占据状态
        if (!GridManager.Instance.IsWalkable(end, floor, ignoreOccupied: false))
        {
            Debug.LogWarning($"[Pathfinding] End position {end} is not walkable or occupied");
            return null;
        }

        // ⚠ 搜索状态是「坐标 + 进入方向」，不是单纯的坐标。
        // 因为转向代价取决于"从哪个方向走进这个格子"：同一个格子从北边进来、
        // 还是从西边进来，后续能不能直着走下去完全不同。
        // 只按坐标存的话，格子第一次被弯弯绕绕地到达就被关闭了，
        // 后来更直的路线再也进不来，转向惩罚只能部分生效。
        // 代价是节点数最多 ×5（4 个方向 + 起点的"无方向"），路径都很短，可以忽略。
        var allNodes  = new Dictionary<(Vector2Int pos, int dir), PathNode>();
        var openSet   = new List<PathNode>();
        var closedSet = new HashSet<(Vector2Int pos, int dir)>();

        PathNode startNode = GetOrCreateNode(start, DirNone, allNodes);

        startNode.gCost = 0;
        startNode.hCost = CalculateDistance(start, end) * StepCost;
        startNode.dirFromParent = Vector2Int.zero;   // 起点没有"来的方向"，第一步不算拐弯
        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            PathNode currentNode = GetLowestFCostNode(openSet);

            // 第一次弹出落在终点的状态就是最优解（启发可采纳，代价非负）
            if (currentNode.position == end)
            {
                return ReconstructPath(currentNode);
            }

            openSet.Remove(currentNode);
            int currentDir = DirIndex(currentNode.dirFromParent);
            closedSet.Add((currentNode.position, currentDir));

            // GetNeighbors 默认 ignoreOccupied=false，路径会绕开其他单位
            foreach (Vector2Int neighborPos in GridManager.Instance.GetNeighbors(currentNode.position, floor))
            {
                // 这一步朝哪个方向走；和上一步方向不同就是拐了个弯
                Vector2Int stepDir  = neighborPos - currentNode.position;
                int        stepDirI = DirIndex(stepDir);

                if (closedSet.Contains((neighborPos, stepDirI))) continue;

                PathNode neighborNode = GetOrCreateNode(neighborPos, stepDirI, allNodes);
                GridCell cell = GridManager.Instance.GetCell(neighborPos, floor);

                bool turned = currentNode.dirFromParent != Vector2Int.zero &&
                              stepDir != currentNode.dirFromParent;

                int tentativeGCost = currentNode.gCost
                                   + cell.moveCost * StepCost
                                   + (turned ? TurnPenalty : 0);

                if (tentativeGCost < neighborNode.gCost)
                {
                    neighborNode.parent        = currentNode;
                    neighborNode.dirFromParent = stepDir;
                    neighborNode.gCost         = tentativeGCost;
                    neighborNode.hCost         = CalculateDistance(neighborPos, end) * StepCost;

                    if (!openSet.Contains(neighborNode))
                    {
                        openSet.Add(neighborNode);
                    }
                }
            }
        }

        return null;
    }

    private const int DirNone = 0;

    /// <summary>方向 → 0~4 的编号，用作搜索状态的一部分。(0,0) = 起点的"无方向"</summary>
    private static int DirIndex(Vector2Int d)
    {
        if (d == Vector2Int.up)    return 1;
        if (d == Vector2Int.down)  return 2;
        if (d == Vector2Int.left)  return 3;
        if (d == Vector2Int.right) return 4;
        return DirNone;
    }

    /// <summary>
    /// 计算移动范围（Dijkstra算法，支持楼层）
    /// 注意：使用 GetNeighborsIgnoreOccupied，让范围显示不受其他单位位置影响
    /// 这样玩家可以看到完整的可移动范围，只是实际点击时终点不能有其他单位
    /// </summary>
    public static HashSet<Vector2Int> CalculateMovementRange(Vector2Int startPos, int maxMovePoints, int floor = 0)
    {
        HashSet<Vector2Int> reachableCells = new HashSet<Vector2Int>();
        Dictionary<Vector2Int, int> costSoFar = new Dictionary<Vector2Int, int>();
        Queue<Vector2Int> frontier = new Queue<Vector2Int>();

        frontier.Enqueue(startPos);
        costSoFar[startPos] = 0;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            int currentCost = costSoFar[current];

            if (currentCost > maxMovePoints) continue;

            reachableCells.Add(current);

            // 忽略占据状态来计算范围（让玩家看到完整移动范围）
            foreach (Vector2Int neighbor in GridManager.Instance.GetNeighborsIgnoreOccupied(current, floor))
            {
                GridCell cell = GridManager.Instance.GetCell(neighbor, floor);
                int newCost = currentCost + cell.moveCost;

                if (newCost <= maxMovePoints)
                {
                    if (!costSoFar.ContainsKey(neighbor) || newCost < costSoFar[neighbor])
                    {
                        costSoFar[neighbor] = newCost;
                        frontier.Enqueue(neighbor);
                    }
                }
            }
        }

        return reachableCells;
    }

    // ============ 私有辅助方法 ============

    private static PathNode GetOrCreateNode(
        Vector2Int pos, int dir, Dictionary<(Vector2Int pos, int dir), PathNode> nodes)
    {
        var key = (pos, dir);
        if (!nodes.TryGetValue(key, out var node))
        {
            node = new PathNode(pos);
            nodes[key] = node;
        }
        return node;
    }

    private static PathNode GetLowestFCostNode(List<PathNode> nodes)
    {
        PathNode lowest = nodes[0];
        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].fCost < lowest.fCost ||
                (nodes[i].fCost == lowest.fCost && nodes[i].hCost < lowest.hCost))
            {
                lowest = nodes[i];
            }
        }
        return lowest;
    }

    private static int CalculateDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static List<Vector2Int> ReconstructPath(PathNode endNode)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        PathNode current = endNode;

        while (current.parent != null)
        {
            path.Add(current.position);
            current = current.parent;
        }

        path.Reverse();
        return path;
    }
}