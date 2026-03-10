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

        Dictionary<Vector2Int, PathNode> allNodes = new Dictionary<Vector2Int, PathNode>();
        List<PathNode> openSet = new List<PathNode>();
        HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();

        PathNode startNode = GetOrCreateNode(start, allNodes);
        PathNode endNode = GetOrCreateNode(end, allNodes);

        startNode.gCost = 0;
        startNode.hCost = CalculateDistance(start, end);
        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            PathNode currentNode = GetLowestFCostNode(openSet);

            if (currentNode.position == end)
            {
                return ReconstructPath(currentNode);
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode.position);

            // GetNeighbors 默认 ignoreOccupied=false，路径会绕开其他单位
            foreach (Vector2Int neighborPos in GridManager.Instance.GetNeighbors(currentNode.position, floor))
            {
                if (closedSet.Contains(neighborPos)) continue;

                PathNode neighborNode = GetOrCreateNode(neighborPos, allNodes);
                GridCell cell = GridManager.Instance.GetCell(neighborPos, floor);

                int tentativeGCost = currentNode.gCost + cell.moveCost;

                if (tentativeGCost < neighborNode.gCost)
                {
                    neighborNode.parent = currentNode;
                    neighborNode.gCost = tentativeGCost;
                    neighborNode.hCost = CalculateDistance(neighborPos, end);

                    if (!openSet.Contains(neighborNode))
                    {
                        openSet.Add(neighborNode);
                    }
                }
            }
        }

        return null;
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

    private static PathNode GetOrCreateNode(Vector2Int pos, Dictionary<Vector2Int, PathNode> nodes)
    {
        if (!nodes.ContainsKey(pos))
        {
            nodes[pos] = new PathNode(pos);
        }
        return nodes[pos];
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