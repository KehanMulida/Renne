using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 寻路节点（仅用于A*算法的内部计算）
/// 职责：存储A*算法中每个节点的代价信息
/// 注意：这个类只在寻路算法内部使用，外部不应访问
/// </summary>
public class PathNode
{
    public Vector2Int position;  // 节点的网格坐标
    public PathNode parent;      // 父节点，用于回溯路径
    public int gCost;            // G成本：从起点到当前节点的实际代价
    public int hCost;            // H成本：从当前节点到终点的启发式估计代价
    public int fCost => gCost + hCost;  // F成本 = G + H，用于选择最优节点

    public PathNode(Vector2Int pos)
    {
        position = pos;
        gCost = int.MaxValue;  // 初始化为最大值，表示未访问
    }
}

/// <summary>
/// 寻路服务 - 纯静态工具类（支持多楼层）
/// 职责：
/// 1. 提供A*寻路算法
/// 2. 提供移动范围计算（Dijkstra算法）
/// 特点：
/// - 无状态，所有方法都是静态的
/// - 不依赖任何GameObject，可以在任何地方调用
/// - 只依赖GridManager获取网格数据
/// - 支持多楼层寻路（在同一楼层内）
/// 优势：
/// - 易于测试（纯函数）
/// - 可复用性强
/// - 性能好（无实例化开销）
/// </summary>
public static class PathfindingService
{
    /// <summary>
    /// A*寻路算法（支持楼层）
    /// 用途：找到从起点到终点的最短路径
    /// 参数：
    ///   start - 起始网格坐标
    ///   end - 目标网格坐标
    ///   floor - 楼层编号（默认0）
    /// 返回：
    ///   路径上的网格坐标列表（不包含起点），如果无法到达则返回null
    /// 算法特点：
    ///   - 使用曼哈顿距离作为启发式函数（适合四方向移动）
    ///   - 考虑地形移动消耗
    ///   - 保证找到最优路径
    ///   - 只在指定楼层内寻路
    /// </summary>
    public static List<Vector2Int> FindPath(Vector2Int start, Vector2Int end, int floor = 0)
    {
        // 前置检查：起点和终点必须都可行走
        if (!GridManager.Instance.IsWalkable(start, floor) || !GridManager.Instance.IsWalkable(end, floor))
        {
            return null;
        }

        // 数据结构初始化
        Dictionary<Vector2Int, PathNode> allNodes = new Dictionary<Vector2Int, PathNode>();
        List<PathNode> openSet = new List<PathNode>();
        HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();

        // 初始化起点和终点
        PathNode startNode = GetOrCreateNode(start, allNodes);
        PathNode endNode = GetOrCreateNode(end, allNodes);

        startNode.gCost = 0;
        startNode.hCost = CalculateDistance(start, end);
        openSet.Add(startNode);

        // A*主循环
        while (openSet.Count > 0)
        {
            PathNode currentNode = GetLowestFCostNode(openSet);
            
            if (currentNode.position == end)
            {
                return ReconstructPath(currentNode);
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode.position);

            // 获取相邻节点（传入楼层参数）
            foreach (Vector2Int neighborPos in GridManager.Instance.GetNeighbors(currentNode.position, floor))
            {
                if (closedSet.Contains(neighborPos))
                    continue;

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

        return null; // 无法找到路径
    }

    /// <summary>
    /// 计算移动范围（Dijkstra算法变种，支持楼层）
    /// 用途：计算单位在指定移动力下能到达的所有格子
    /// 参数：
    ///   startPos - 起始位置
    ///   maxMovePoints - 最大移动力
    ///   floor - 楼层编号（默认0）
    /// 返回：
    ///   所有可到达格子的集合
    /// 算法特点：
    ///   - 考虑地形移动消耗
    ///   - 使用广度优先搜索的变种
    ///   - 适合计算技能范围、攻击范围等
    ///   - 只在指定楼层内计算
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

            if (currentCost > maxMovePoints)
                continue;

            reachableCells.Add(current);

            // 获取相邻格子（传入楼层参数）
            foreach (Vector2Int neighbor in GridManager.Instance.GetNeighbors(current, floor))
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
    
    /// <summary>
    /// 获取或创建节点
    /// 避免重复创建相同位置的节点
    /// </summary>
    private static PathNode GetOrCreateNode(Vector2Int pos, Dictionary<Vector2Int, PathNode> nodes)
    {
        if (!nodes.ContainsKey(pos))
        {
            nodes[pos] = new PathNode(pos);
        }
        return nodes[pos];
    }

    /// <summary>
    /// 从节点列表中找到F成本最低的节点
    /// 如果F成本相同，选择H成本更小的（更接近目标）
    /// </summary>
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

    /// <summary>
    /// 计算两点之间的曼哈顿距离
    /// 曼哈顿距离 = |x1-x2| + |y1-y2|
    /// 适用于只能四方向移动的网格（不能斜向移动）
    /// </summary>
    private static int CalculateDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    /// <summary>
    /// 重建路径
    /// 从终点通过parent指针回溯到起点
    /// 返回的路径不包含起点（因为单位已经在起点了）
    /// </summary>
    private static List<Vector2Int> ReconstructPath(PathNode endNode)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        PathNode current = endNode;

        // 通过parent指针回溯
        while (current.parent != null)
        {
            path.Add(current.position);
            current = current.parent;
        }

        // 反转路径（因为是从终点往回走的）
        path.Reverse();
        return path;
    }
}