using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗掩体筛选（AI 侧）。纯查询、无副作用。
///
/// 性能设计：
///   1. 只有「正在和玩家交战」的 AI 才该调用（EnemyAIController 里用 hasVisualContact 把门）。
///   2. 回合制：一回合算一次，不要每帧。
///   3. 候选来源是 Physics.OverlapSphere 拿到的**真实障碍**——不遍历全网格、不碰空地；
///      动态障碍（被推走/骑走/升降）天然实时，无需预计算表与失效钩子。
///   4. 廉价筛（可走/整数距离/朝向）→ 贵检测（Linecast）只对存活者 → 最后只对前几名做寻路验证。
///
/// 关键几何约定（踩过的坑）：
///   · 搜索球心放在**玩家**身上，不是 AI 身上。否则 AI 远离时周围格子都超出攻击距离、
///     一个候选都没有，就会退化成「先直冲玩家、近了再横移到掩体」的两段式怪动作。
///   · 「障碍朝玩家」是**评分项**不是硬门槛。若硬性要求 Dot>0 同时又要求能看见玩家，
///     对一根柱子来说只有**斜角**格能同时满足，AI 就会一直跑到对角去。
///   · IsBesideCover 必须和候选判定用**同一套几何**，否则 AI 走到目的地后
///     不被判定为"已就位"，会不停重新选点来回晃。
///
/// v1 范围：找「掩体旁的射击位」——贴着障碍、且**仍能看见/打到玩家**。
///   完整的「躲到后面→探头射→缩回」需要格内贴边 + 蹲姿，见 GetStandPoint 预留的接缝。
/// </summary>
public static class CombatCover
{
    public struct CoverPick
    {
        public bool       valid;
        public Vector2Int cell;       // 射击位
        public Vector2Int coverDir;   // 由 cell 指向障碍的方向
        public float      score;
    }

    // TODO(StanceController): 接入姿态后按站/蹲取高度
    private const float StandEyeHeight = 1.2f;
    private const int   MaxObstacles   = 48;

    /// <summary>矮于此高度的障碍算半掩体：站着挡不住、需要蹲。v1 暂不选。</summary>
    private const float HalfCoverHeight = 1.0f;

    /// <summary>最多对前几名做寻路验证（寻路最贵，必须限次）。</summary>
    private const int MaxPathChecks = 4;

    private static readonly Vector2Int[] Dirs =
    { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    private static readonly Collider[]          ObsBuf = new Collider[MaxObstacles];
    private static readonly HashSet<Vector2Int> Seen   = new HashSet<Vector2Int>();
    private static readonly List<CoverPick>     Cands  = new List<CoverPick>();

    private static int _obstacleMask = 0;
    private static int ObstacleMask
    {
        get
        {
            if (_obstacleMask == 0) _obstacleMask = LayerMask.GetMask("Obstacle");
            return _obstacleMask;
        }
    }

    // ── 站位点：现在=格心+站姿眼高。以后加蹲姿/格内贴边只改这里，调用方不动 ──
    private static Vector3 GetStandPoint(Vector2Int cell, Vector2Int coverDir, int floor, bool crouch = false)
        => FloorManager.Instance.GridToWorld(cell, floor) + Vector3.up * StandEyeHeight;

    /// <summary>
    /// 某格是否算「掩体位」：四邻里有障碍，且该障碍不在背对玩家的那一侧（Dot >= 0）。
    /// 与 SelectBestCover 的候选几何**保持一致**，用作"是否已就位"的判定，避免来回晃。
    /// 只有网格查询，很便宜。
    /// </summary>
    public static bool IsBesideCover(Vector2Int cell, Vector2Int playerCell, int floor)
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;

        Vector2Int toPlayer = playerCell - cell;
        foreach (var d in Dirs)
        {
            // CanCross=false 同时覆盖两种遮挡：邻格是厚障碍、或格边上立着薄墙（厕所隔板）
            if (gm.CanCross(cell, cell + d, floor)) continue;
            if (Dot(d, toPlayer) >= 0) return true;   // 不在背面即可
        }
        return false;
    }

    /// <summary>
    /// 挑一个最好的「掩体旁射击位」。找不到返回 false（调用方就按原逻辑推进/开火）。
    /// </summary>
    /// <param name="playerAimPoint">玩家身体瞄准点（建议 IDetectable.DetectionPosition）</param>
    /// <param name="maxTravel">离 AI 最远允许多少格（防止跨半张图选点）</param>
    /// <param name="debugOut">非 null 时收集所有候选（可视化用）</param>
    public static bool SelectBestCover(
        Vector2Int aiCell, Vector2Int playerCell, Vector3 playerAimPoint,
        int floor, string tactic, int maxTravel, int attackRangeCells,
        out CoverPick best, List<CoverPick> debugOut = null)
    {
        best = default;
        var gm = GridManager.Instance;
        var fm = FloorManager.Instance;
        if (gm == null || fm == null) return false;

        Seen.Clear();
        Cands.Clear();

        // ★ 球心在【玩家】身上：掩体位必须是「能打到玩家」的位置，
        //   这样 AI 从一开始就朝它推进，而不是先冲脸再横移。
        Vector3 playerWorld  = fm.GridToWorld(playerCell, floor);
        float   floorY       = fm.GetFloorWorldY(floor);
        int     searchCells  = attackRangeCells + 1;
        float   searchWorld  = searchCells * gm.CellSize;

        int n = Physics.OverlapSphereNonAlloc(playerWorld, searchWorld, ObsBuf, ObstacleMask);

        for (int i = 0; i < n; i++)
        {
            Collider col = ObsBuf[i];
            if (col == null) continue;

            float coverH = col.bounds.max.y - floorY;   // 高度白送（含动态升降）
            if (coverH < HalfCoverHeight) continue;     // v1：矮掩体要蹲才有用，暂不选

            foreach (Vector2Int c in CellsAroundBoundsClamped(col.bounds, playerCell, searchCells))
            {
                if (!Seen.Add(c)) continue;          // 多 collider 共格去重

                // ① 廉价：候选自己得站得住
                if (!gm.IsValid(c) || !gm.IsWalkable(c, floor, ignoreOccupied: true)) continue;

                // ② 廉价：整数距离——必须能打到玩家，且别离 AI 太远
                int dPl = Manhattan(c, playerCell);
                if (dPl > attackRangeCells) continue;
                int dAI = Manhattan(c, aiCell);
                if (dAI > maxTravel) continue;

                // ③ 廉价：找一条「朝玩家且有遮挡」的边。
                //    用 CanCross 判定，可同时吃到「邻格是厚障碍」和「格边立着薄墙」两种掩体。
                //    只排除背面（dot<0）；"朝玩家多少"留给评分，不做硬门槛，
                //    否则叠加"必须能看见玩家"后只剩斜角能过，AI 就会一直跑对角。
                Vector2Int toPlayer = playerCell - c;
                Vector2Int coverDir = Vector2Int.zero;
                int bestDot = -1;
                foreach (var d in Dirs)
                {
                    if (gm.CanCross(c, c + d, floor)) continue;   // 那侧通畅，不是掩体
                    int dot = Dot(d, toPlayer);
                    if (dot < 0) continue;                         // 背面的遮挡不算
                    if (dot > bestDot) { bestDot = dot; coverDir = d; }
                }
                if (bestDot < 0) continue;

                // ④ 贵检测：只对活到这里的少数格发射线——必须仍能看见/打到玩家
                Vector3 stand = GetStandPoint(c, coverDir, floor);
                if (Physics.Linecast(stand, playerAimPoint, ObstacleMask)) continue;

                // 掩体质量：遮挡方向与「指向玩家」的归一化贴合度（0~1）
                float quality = dPl > 0 ? bestDot / (float)dPl : 0f;

                float s = Score(tactic, dAI, dPl, attackRangeCells, quality);
                var pick = new CoverPick { valid = true, cell = c, coverDir = coverDir, score = s };
                Cands.Add(pick);
                debugOut?.Add(pick);
            }
        }

        if (Cands.Count == 0) return false;

        // 按分数降序，只对前几名做寻路验证（寻路最贵）
        Cands.Sort((a, b) => b.score.CompareTo(a.score));

        int checks = Mathf.Min(MaxPathChecks, Cands.Count);
        for (int i = 0; i < checks; i++)
        {
            var cand = Cands[i];
            if (cand.cell == aiCell) { best = cand; return true; }   // 已经站在最优位

            var path = PathfindingService.FindPath(aiCell, cand.cell, floor);
            if (path != null && path.Count > 0) { best = cand; return true; }
        }

        return false;   // 前几名都走不到：这回合就别硬挪了
    }

    // ── 评分：战术分层。分数越高越想去 ──
    private static float Score(string tactic, int dAI, int dPl, int attackRangeCells, float coverQuality)
    {
        // 偏好交战距离：贴脸和极限射程都不好，取射程的 ~2/3
        int preferred = Mathf.Max(1, attackRangeCells * 2 / 3);

        float s;
        float coverWeight;
        switch (tactic)
        {
            case "Aggressive":
                s = (attackRangeCells - dPl) * 1.5f;   // 越近越好
                coverWeight = 1.5f;                    // 掩体次要
                break;
            case "Retreat":
                s = dPl * 1.2f;                        // 越远越好
                coverWeight = 5f;                      // 极重视掩体
                break;
            default:                                   // Engage / Defensive / CallSupport
                s = -Mathf.Abs(dPl - preferred) * 1.5f;
                coverWeight = 4f;
                break;
        }

        s += coverQuality * coverWeight;   // 障碍越正对玩家，遮蔽越好
        s -= dAI * 0.4f;                   // 少走路；顺便抑制「每回合换掩体」的抖动
        return s;
    }

    /// <summary>
    /// 障碍 collider 周围一圈的格（bounds 外扩 1 格），裁剪到搜索框内——长墙只取附近那一截。
    /// 外扩是因为「贴着障碍站人」的格子在障碍**外面**；薄墙更是不占格子，
    /// 站位格就是它两侧那两格。
    /// </summary>
    private static IEnumerable<Vector2Int> CellsAroundBoundsClamped(
        Bounds b, Vector2Int center, int radius)
    {
        var gm = GridManager.Instance;
        Vector2Int min = gm.WorldToGrid(b.min);
        Vector2Int max = gm.WorldToGrid(b.max);

        int x0 = Mathf.Max(min.x - 1, center.x - radius);
        int x1 = Mathf.Min(max.x + 1, center.x + radius);
        int y0 = Mathf.Max(min.y - 1, center.y - radius);
        int y1 = Mathf.Min(max.y + 1, center.y + radius);

        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                yield return new Vector2Int(x, y);
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    // Unity 的 Vector2Int 没有 Dot，自己来
    private static int Dot(Vector2Int a, Vector2Int b) => a.x * b.x + a.y * b.y;
}
