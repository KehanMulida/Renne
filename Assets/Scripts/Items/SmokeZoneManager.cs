using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 烟雾区域管理器（单例）
/// 跟踪场景中所有活跃烟雾区，供 VisionPerception 查询视线是否被遮挡。
/// </summary>
public class SmokeZoneManager : MonoBehaviour
{
    private static SmokeZoneManager _instance;

    private readonly List<SmokeZone> _activeZones = new List<SmokeZone>();

    public static SmokeZoneManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("SmokeZoneManager");
                _instance = go.AddComponent<SmokeZoneManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Register(SmokeZone zone)
    {
        _activeZones.RemoveAll(z => z == null); // 顺便清理已销毁的残留引用
        _activeZones.Add(zone);
    }

    public void Unregister(SmokeZone zone) => _activeZones.Remove(zone);

    /// <summary>
    /// 检查从 from 到 to 的连线是否穿过任意烟雾区。
    /// VisionPerception 调用此方法判断视线是否被烟雾遮挡。
    /// </summary>
    public static bool IsLineOfSightSmoked(Vector3 from, Vector3 to)
    {
        if (_instance == null) return false;
        foreach (var zone in _instance._activeZones)
        {
            if (zone == null) continue;
            if (zone.LinePassesThrough(from, to)) return true;
        }
        return false;
    }

    /// <summary>
    /// 在世界坐标 pos 生成烟雾区（由 ThrowableProjectile 调用）。
    /// 持续回合数在 [minTurns, maxTurns] 内随机。
    /// </summary>
    public static void SpawnSmoke(Vector3 pos, int radiusCells, int minTurns, int maxTurns)
    {
        float worldRadius = radiusCells *
            (GridManager.Instance != null ? GridManager.Instance.CellSize : 1f);

        int turns = UnityEngine.Random.Range(minTurns, maxTurns + 1);

        var go = new GameObject($"SmokeZone(T{turns})");
        go.transform.position = pos;
        var zone = go.AddComponent<SmokeZone>();
        zone.Initialize(worldRadius, turns);
        Debug.Log($"[SmokeZone] 生成 r={worldRadius:F1}  持续 {turns} 回合");
    }
}
