using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RenneAutoTest
{
    // ══════════════════════════════════════════════════════════════════════════
    // AutoTestPathProbe —— 寻路「直线度」受控基准
    //
    // 为什么需要它：想知道「转向惩罚有没有让敌人少拐弯」，直接比较两次运行的日志
    // 是不靠谱的 —— 两次跑的随机种子、巡逻目标、敌人走的地方全不一样，
    // 路径样本根本不可比，差异淹没在噪声里。
    //
    // 这里改成真正受控的 A/B：同一张地图、同一批起终点，只切换
    // PathfindingService.TurnPenalty，其余完全不变。差异就只能来自这一个变量。
    //
    // ⚠ 最关键的指标不是「拐弯少了多少」，而是 **两边总步数必须完全相等**。
    //   转向惩罚最常见的翻车方式，就是敌人为了走直线而绕远路多耗 AP。
    //   总步数一旦不等，说明 StepCost 的约束算错了，必须回头查。
    // ══════════════════════════════════════════════════════════════════════════

    public static class AutoTestPathProbe
    {
        /// <summary>
        /// 在当前场景的网格上跑一轮受控对比，结果写进 AutoTest 的 console.log。
        /// </summary>
        /// <param name="samples">采样多少对起终点</param>
        /// <param name="floor">在哪一层测</param>
        public static void Run(int samples = 200, int floor = 0)
        {
            var grid = GridManager.Instance;
            if (grid == null)
            {
                AutoTestSession.WriteConsoleLine("[寻路基准] GridManager 不存在，跳过");
                return;
            }

            // 收集可走格子
            var walkable = new List<Vector2Int>();
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                {
                    var p = new Vector2Int(x, y);
                    if (grid.IsWalkable(p, floor, ignoreOccupied: true)) walkable.Add(p);
                }

            if (walkable.Count < 2)
            {
                AutoTestSession.WriteConsoleLine(
                    $"[寻路基准] 楼层 {floor} 可走格子只有 {walkable.Count} 个，跳过");
                return;
            }

            // 固定种子：每次运行抽到的是同一批起终点，跨运行也可比
            var rng   = new System.Random(20261006);
            var pairs = new List<(Vector2Int a, Vector2Int b)>(samples);
            int guard = samples * 40;

            while (pairs.Count < samples && guard-- > 0)
            {
                var a = walkable[rng.Next(walkable.Count)];
                var b = walkable[rng.Next(walkable.Count)];
                // 太近的没有可比性（没有多条等长路径可选）
                if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) < 4) continue;
                pairs.Add((a, b));
            }

            int savedPenalty = PathfindingService.TurnPenalty;

            var off = MeasureAll(pairs, floor, penalty: 0);
            var on  = MeasureAll(pairs, floor, penalty: savedPenalty > 0 ? savedPenalty : 1);

            PathfindingService.TurnPenalty = savedPenalty;

            var sb = new StringBuilder();
            sb.AppendLine("──────── 寻路直线度基准（同一地图 / 同一批起终点）────────");
            sb.AppendLine($"  有效样本 {off.paths} 对（楼层 {floor}，可走格 {walkable.Count}）");
            sb.AppendLine($"  {"",-14}{"总步数",8}{"总拐弯",8}{"拐弯/步",10}{"全直线",10}");
            sb.AppendLine($"  {"TurnPenalty=0",-14}{off.steps,8}{off.turns,8}{Pct(off.turns, off.steps),10}{off.straight + "/" + off.paths,10}");
            sb.AppendLine($"  {"TurnPenalty>0",-14}{on.steps,8}{on.turns,8}{Pct(on.turns, on.steps),10}{on.straight + "/" + on.paths,10}");

            if (off.steps != on.steps)
            {
                sb.AppendLine($"  ❌ 总步数不相等（{off.steps} → {on.steps}）！");
                sb.AppendLine("     转向惩罚压过了路径长度，敌人在绕远路多耗 AP。");
                sb.AppendLine("     需要调大 PathfindingService.StepCost 或调小 TurnPenalty。");
            }
            else if (off.turns > 0)
            {
                float cut = (off.turns - on.turns) / (float)off.turns;
                sb.AppendLine($"  ✅ 总步数不变（{on.steps}），拐弯减少 {off.turns - on.turns} 次（{cut:P1}）");
            }
            sb.Append("──────────────────────────────────────────────────────");

            AutoTestSession.WriteConsoleLine(sb.ToString());
            Debug.Log("[寻路基准]\n" + sb);
        }

        private static string Pct(int turns, int steps)
            => steps > 0 ? (turns / (float)steps).ToString("P1") : "—";

        private struct Stats { public int paths, steps, turns, straight; }

        private static Stats MeasureAll(List<(Vector2Int a, Vector2Int b)> pairs, int floor, int penalty)
        {
            PathfindingService.TurnPenalty = penalty;

            var s = new Stats();
            foreach (var (a, b) in pairs)
            {
                var path = PathfindingService.FindPath(a, b, floor);
                if (path == null || path.Count < 2) continue;

                int turns = CountTurns(a, path);
                s.paths++;
                s.steps += path.Count;
                s.turns += turns;
                if (turns == 0) s.straight++;
            }
            return s;
        }

        /// <summary>数这条路径拐了几次弯。path 不含起点，所以要把起点补上再比方向。</summary>
        private static int CountTurns(Vector2Int start, List<Vector2Int> path)
        {
            int turns = 0;
            Vector2Int prev = start;
            Vector2Int lastDir = Vector2Int.zero;

            foreach (var step in path)
            {
                Vector2Int dir = step - prev;
                if (lastDir != Vector2Int.zero && dir != lastDir) turns++;
                lastDir = dir;
                prev    = step;
            }
            return turns;
        }
    }
}
