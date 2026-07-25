using UnityEngine;

/// <summary>
/// 单个烟雾区域实例。
/// 持续回合数在投掷时随机确定（smokeMinTurns ~ smokeMaxTurns）。
/// 每次任意单位回合结束（TurnSystem.OnTurnEnd）扣一层，归零后自动销毁。
/// VisionPerception 通过 SmokeZoneManager.IsLineOfSightSmoked 查询视线遮挡。
/// </summary>
public class SmokeZone : MonoBehaviour
{
    private float _radius;
    private int   _remainingTurns;

    public void Initialize(float worldRadius, int turns)
    {
        _radius         = worldRadius;
        _remainingTurns = turns;

        SmokeZoneManager.Instance.Register(this);

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnEnd += OnTurnEnd;
        else
            Debug.LogError("[SmokeZone] TurnSystem.Instance 为 null，烟雾区将永不消散！");
    }

    void OnDestroy()
    {
        SmokeZoneManager.Instance?.Unregister(this);

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnEnd -= OnTurnEnd;
    }

    private void OnTurnEnd(TurnData _)
    {
        _remainingTurns--;
        Debug.Log($"[SmokeZone] 剩余 {_remainingTurns} 回合");
        if (_remainingTurns <= 0)
            Destroy(gameObject);
    }

    /// <summary>
    /// 检查线段 from→to 是否经过本烟雾球体（点到线段距离公式）。
    /// </summary>
    public bool LinePassesThrough(Vector3 from, Vector3 to)
    {
        Vector3 center = transform.position;
        Vector3 seg    = to - from;
        float   segLen = seg.magnitude;

        if (segLen < 0.001f)
            return Vector3.Distance(from, center) < _radius;

        Vector3 segDir  = seg / segLen;
        float   t       = Mathf.Clamp(Vector3.Dot(center - from, segDir), 0f, segLen);
        Vector3 closest = from + segDir * t;

        return (closest - center).sqrMagnitude < _radius * _radius;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.25f);
        Gizmos.DrawSphere(transform.position, _radius);
        UnityEditor.Handles.Label(transform.position + Vector3.up * (_radius + 0.3f),
            $"Smoke T{_remainingTurns}");
    }
#endif
}
