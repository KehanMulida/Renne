using UnityEngine;

/// <summary>
/// 掩体/墙角判定（供探头、廖枪使用）。
/// 纯网格几何：不依赖 CoverConfig，只看邻格是否阻挡。
/// 规则（玩家在格 cell）：
///   1. 某方向 d 的邻格不可走 → 该方向有掩体（墙/障碍）。
///   2. 垂直侧 s 满足「cell+s 可走 且 cell+d+s 可走」→ 该侧存在可探墙角开口。
/// </summary>
public static class CoverUtil
{
    public struct CoverPeek
    {
        public bool       hasCover;
        public Vector2Int coverDir;   // 指向掩体（墙）的方向
        public Vector2Int sideA;      // 可探的垂直侧之一（无则 zero）
        public Vector2Int sideB;      // 另一垂直侧（无则 zero）

        public bool HasAnyPeek => sideA != Vector2Int.zero || sideB != Vector2Int.zero;
    }

    private static readonly Vector2Int[] Dirs =
    { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    /// <summary>玩家所在格是否贴掩体且有可探墙角。找到第一个满足的掩体方向即返回。</summary>
    public static bool TryGetCover(Vector2Int cell, int floor, out CoverPeek result)
    {
        result = default;
        var gm = GridManager.Instance;
        if (gm == null) return false;

        foreach (var d in Dirs)
        {
            Vector2Int wall = cell + d;
            // 掩体 = 该方向邻格「有效但不可走」（墙/障碍/scene item）
            if (!gm.IsValid(wall) || gm.IsWalkable(wall, floor, ignoreOccupied: true))
                continue;

            Vector2Int perp1 = new Vector2Int(-d.y, d.x);
            Vector2Int perp2 = new Vector2Int(d.y, -d.x);

            Vector2Int sideA = CanPeek(gm, cell, d, perp1, floor) ? perp1 : Vector2Int.zero;
            Vector2Int sideB = CanPeek(gm, cell, d, perp2, floor) ? perp2 : Vector2Int.zero;

            if (sideA != Vector2Int.zero || sideB != Vector2Int.zero)
            {
                result = new CoverPeek { hasCover = true, coverDir = d, sideA = sideA, sideB = sideB };
                return true;
            }
        }
        return false;
    }

    // 侧向能探：玩家能侧身探入 side（cell+side 可走），且墙那侧过去开阔（cell+coverDir+side 可走）
    private static bool CanPeek(GridManager gm, Vector2Int cell, Vector2Int coverDir, Vector2Int side, int floor)
    {
        Vector2Int lean   = cell + side;
        Vector2Int beyond = cell + coverDir + side;
        return gm.IsValid(lean)   && gm.IsWalkable(lean,   floor, ignoreOccupied: true)
            && gm.IsValid(beyond) && gm.IsWalkable(beyond, floor, ignoreOccupied: true);
    }

    /// <summary>在两个可探侧里，选与期望方向（世界向量投影到网格）更一致的一侧。</summary>
    public static Vector2Int PickSide(CoverPeek cover, Vector2 desiredGridDir)
    {
        if (cover.sideA == Vector2Int.zero) return cover.sideB;
        if (cover.sideB == Vector2Int.zero) return cover.sideA;
        float dotA = Vector2.Dot(desiredGridDir, cover.sideA);
        float dotB = Vector2.Dot(desiredGridDir, cover.sideB);
        return dotA >= dotB ? cover.sideA : cover.sideB;
    }
}
