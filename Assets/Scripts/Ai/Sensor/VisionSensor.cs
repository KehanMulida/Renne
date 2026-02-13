using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisionSensor : MonoBehaviour
{
    [Header("FOV 设置")]
    public float viewDistance = 8f;
    public float viewAngle = 100f;
    public float eyeHeight = 1.6f;

    [Header("遮挡检测")]
    public LayerMask obstacleMask;

    [Header("玩家层")]
    public LayerMask playerMask;

   // [Header("输出结果")]
    public bool PlayerVisible { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }

    private Transform eye;

    void Awake()
    {
        eye = transform;
    }

    void Update()
    {
        DetectPlayer();
    }

    private void DetectPlayer()
    {
        PlayerVisible = false;

        Vector3 origin = eye.position + Vector3.up * eyeHeight;

        Collider[] hits = Physics.OverlapSphere(origin, viewDistance, playerMask);
        if (hits.Length == 0) return;

        Transform player = hits[0].transform;
        Vector3 dir = (player.position - origin).normalized;

        // 检查角度
        if (Vector3.Angle(eye.forward, dir) > viewAngle * 0.5f)
            return;

        float dist = Vector3.Distance(origin, player.position);

        // 遮挡检测
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, obstacleMask))
            return;

        // 成功看到玩家
        PlayerVisible = true;
        LastSeenPosition = player.position;
    }

    public void PerformDetection()
    {
        DetectPlayer();
    }
    // 替换整个OnDrawGizmos()和OnDrawGizmosSelected()方法

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        
        // 根据是否看到玩家改变颜色
        Color baseColor = PlayerVisible ? Color.red : Color.yellow;
        
        // 绘制扇形视野（简化版）
        DrawVisionCone(origin, transform.forward, viewAngle, viewDistance, baseColor);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying 
            ? transform.position + Vector3.up * eyeHeight
            : transform.position + Vector3.up * eyeHeight;

        // 选中时显示详细的扇形
        Color detailColor = Application.isPlaying && PlayerVisible ? Color.red : Color.cyan;
        DrawDetailedVisionCone(origin, transform.forward, viewAngle, viewDistance, detailColor);

        // 绘制视线高度
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, origin);
        Gizmos.DrawWireSphere(origin, 0.2f);

        // 如果看到玩家，绘制连线
        if (Application.isPlaying && PlayerVisible)
        {
            Collider[] hits = Physics.OverlapSphere(origin, viewDistance, playerMask);
            if (hits.Length > 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(origin, hits[0].transform.position);
            }
        }
    }

    /// <summary>
    /// 绘制简化的扇形视野
    /// </summary>
    private void DrawVisionCone(Vector3 origin, Vector3 forward, float angle, float distance, Color color)
    {
        int segments = 20;  // 扇形分段数
        float halfAngle = angle * 0.5f;

        Gizmos.color = new Color(color.r, color.g, color.b, 0.2f);

        // 绘制扇形的边缘线
        Vector3 leftBoundary = Quaternion.Euler(0, -halfAngle, 0) * forward * distance;
        Vector3 rightBoundary = Quaternion.Euler(0, halfAngle, 0) * forward * distance;

        Gizmos.color = color;
        Gizmos.DrawLine(origin, origin + leftBoundary);
        Gizmos.DrawLine(origin, origin + rightBoundary);

        // 绘制扇形弧线
        Vector3 previousPoint = origin + leftBoundary;
        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = -halfAngle + (angle * i / segments);
            Vector3 direction = Quaternion.Euler(0, currentAngle, 0) * forward;
            Vector3 point = origin + direction * distance;
            
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }

    /// <summary>
    /// 绘制详细的扇形视野（选中时）
    /// </summary>
    private void DrawDetailedVisionCone(Vector3 origin, Vector3 forward, float angle, float distance, Color color)
    {
        int segments = 30;
        int rings = 3;  // 距离环数
        float halfAngle = angle * 0.5f;

        // 绘制距离环
        Gizmos.color = new Color(color.r, color.g, color.b, 0.3f);
        for (int ring = 1; ring <= rings; ring++)
        {
            float ringDistance = distance * ring / rings;
            DrawArc(origin, forward, angle, ringDistance, segments);
        }

        // 绘制射线示意（8条）
        Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
        for (int i = 0; i < 8; i++)
        {
            float rayAngle = -halfAngle + (angle * i / 7f);
            Vector3 direction = Quaternion.Euler(0, rayAngle, 0) * forward;
            Vector3 endPoint = origin + direction * distance;

            // 检测遮挡
            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance, obstacleMask))
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(origin, hit.point);
            }
            else
            {
                Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
                Gizmos.DrawLine(origin, endPoint);
            }
        }

        // 绘制边界
        Gizmos.color = color;
        Vector3 leftBoundary = Quaternion.Euler(0, -halfAngle, 0) * forward * distance;
        Vector3 rightBoundary = Quaternion.Euler(0, halfAngle, 0) * forward * distance;
        Gizmos.DrawLine(origin, origin + leftBoundary);
        Gizmos.DrawLine(origin, origin + rightBoundary);
    }

    /// <summary>
    /// 绘制圆弧
    /// </summary>
    private void DrawArc(Vector3 center, Vector3 forward, float angle, float radius, int segments)
    {
        float halfAngle = angle * 0.5f;
        Vector3 previousPoint = center + Quaternion.Euler(0, -halfAngle, 0) * forward * radius;

        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = -halfAngle + (angle * i / segments);
            Vector3 direction = Quaternion.Euler(0, currentAngle, 0) * forward;
            Vector3 point = center + direction * radius;
            
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }

}

