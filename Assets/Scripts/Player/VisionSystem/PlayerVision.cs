using UnityEngine;
using System.Collections.Generic;

public interface IVisionObstacle
{
    float GetVisionBlockPercent();   // 0 = 不遮挡，1 = 完全遮挡
    float ObstacleHeight { get; }
}

public class PlayerVision : MonoBehaviour
{
    [Header("Vision Settings")]
    public float visionRadius = 10f;
    public float viewAngle = 360f; 
    public float eyeHeight = 1.6f;

    [Header("Layers")]
    public LayerMask obstacleLayers;

    private static readonly Vector3[] sampleOffsets =
    {
        new Vector3(0, 0.1f, 0),
        new Vector3(0, 0.9f, 0),
        new Vector3(0, 1.6f, 0)
    };

    private PlayerController _pc;
    void Awake() { _pc = GetComponentInParent<PlayerController>(); }

    // 探头时眼位横移到探出点（绕过掩体看另一侧）；由 PlayerController.PeekOffset 驱动
    public Vector3 EyePos => transform.position
        + (_pc != null ? _pc.PeekOffset : Vector3.zero)
        + Vector3.up * eyeHeight;

    // ==================================================================
    // 1) 可见性检测
    // ==================================================================
    public bool CanSee(Vector3 targetPos)
    {
        Vector3 eye = EyePos;
        Vector3 dir = targetPos - eye;
        float dist = dir.magnitude;
        if (dist > visionRadius) return false;

        // FOV 角度检测
        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > viewAngle * 0.5f) return false;

        dir.Normalize();

        // 多点检测
        foreach (var offset in sampleOffsets)
        {
            Vector3 checkPoint = targetPos + offset;

            if (Physics.Raycast(eye, (checkPoint - eye).normalized, out RaycastHit hit, dist, obstacleLayers))
            {
                if (hit.collider.TryGetComponent<IVisionObstacle>(out var obs))
                {
                    if (obs.GetVisionBlockPercent() >= 1f || obs.ObstacleHeight >= eyeHeight)
                        continue;
                }
                else continue;
            }

            return true;
        }

        return false;
    }

    public bool CanSee(GameObject obj)
    {
        Renderer r = obj.GetComponent<Renderer>();
        Vector3 pos = r ? r.bounds.center : obj.transform.position;
        return CanSee(pos);
    }

    // ==================================================================
    // 2) 获取 360° 可见点（给 FOW 用）
    // ==================================================================
    public List<Vector3> GetVisiblePoints(int rayCount)
    {
        List<Vector3> pts = new List<Vector3>(rayCount);

        for (int i = 0; i < rayCount; i++)
        {
            float angle = transform.eulerAngles.y + (i / (float)rayCount) * viewAngle;
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            Vector3 target = EyePos + dir * visionRadius;

            if (Physics.Raycast(EyePos, dir, out RaycastHit hit, visionRadius, obstacleLayers))
                target = hit.point;

            pts.Add(target);
        }

        return pts;
    }

}