using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Events;

public class VisionPerception : IPerceptionModule
{
    private Transform owner;
    private EnemyConfig config;
    private LayerMask obstacleLayer;

    private bool wasSeenLastFrame = false;

    // 上一帧看到玩家时的楼层，用于检测楼层变化
    private int lastSeenFloor = -1;

    public event Action<PerceptionEvent> OnPerceptionEvent;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner = owner;
        this.config = config;
        this.obstacleLayer = LayerMask.GetMask("Obstacle", "Wall");
    }

    private Transform FindPlayer()
    {
        return GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    private int GetFloor(Transform t)
    {
        return Mathf.FloorToInt(t.position.y / 4f);
    }

    public void UpdatePerception()
    {
        var player = FindPlayer();
        if (player == null)
        {
            if (wasSeenLastFrame)
            {
                wasSeenLastFrame = false;
                lastSeenFloor = -1;
                OnPerceptionEvent?.Invoke(new PerceptionEvent
                {
                    Type = PerceptionType.VisualLost
                });
            }
            return;
        }

        int currentPlayerFloor = GetFloor(player);

        // 楼层变化检测独立于视野
        // 只要上一帧看到过玩家，就检测楼层是否变化
        if (wasSeenLastFrame && lastSeenFloor != -1 && lastSeenFloor != currentPlayerFloor)
        {
            Vector3 connectionPos = FindNearestConnectionPosition(lastSeenFloor, currentPlayerFloor);
            Debug.Log($"[VisionPerception] Firing PlayerChangedFloor {lastSeenFloor}→{currentPlayerFloor} | connectionPos:{connectionPos}");

            OnPerceptionEvent?.Invoke(new PerceptionEvent
            {
                Type               = PerceptionType.PlayerChangedFloor,
                Target             = player,
                Position           = player.position,
                FromFloor          = lastSeenFloor,
                ToFloor            = currentPlayerFloor,
                ConnectionPosition = connectionPos,
                Floor              = currentPlayerFloor
            });

            lastSeenFloor = currentPlayerFloor;
        }

        bool canSee = CanSeeTarget(player);

        if (canSee)
        {
            lastSeenFloor = currentPlayerFloor;
            wasSeenLastFrame = true;

            OnPerceptionEvent?.Invoke(new PerceptionEvent
            {
                Type       = PerceptionType.VisualContact,
                Target     = player,
                Position   = player.position,
                Confidence = CalculateVisibility(player),
                Floor      = currentPlayerFloor
            });
        }
        else if (wasSeenLastFrame)
        {
            wasSeenLastFrame = false;

            OnPerceptionEvent?.Invoke(new PerceptionEvent
            {
                Type      = PerceptionType.VisualLost,
                Floor     = lastSeenFloor,
                Position  = owner.position
            });
        }
    }

    /// <summary>
    /// 找到从 fromFloor 到 toFloor 最近的楼层连接点
    /// 作为敌人追击时的移动目标
    /// </summary>
    private Vector3 FindNearestConnectionPosition(int fromFloor, int toFloor)
    {
        if (FloorManager.Instance == null) return owner.position;

        FloorData floorData = FloorManager.Instance.GetFloor(fromFloor);
        if (floorData?.connections == null) return owner.position;

        float nearestDist = float.MaxValue;
        Vector3 nearestPos = owner.position;

        foreach (var connection in floorData.connections)
        {
            if (connection.toFloor != toFloor) continue;

            Vector3 worldPos = FloorManager.Instance.GridToWorld(connection.gridPosition, fromFloor);
            float dist = new Vector2(
                worldPos.x - owner.position.x,
                worldPos.z - owner.position.z).magnitude;

            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestPos  = worldPos;
            }
        }

        return nearestPos;
    }

    private bool CanSeeTarget(Transform target)
    {
        int ownerFloor  = GetFloor(owner);
        int targetFloor = GetFloor(target);

        if (ownerFloor != targetFloor && !config.canSeeAcrossFloors)
            return false;

        float distance = new Vector2(
            target.position.x - owner.position.x,
            target.position.z - owner.position.z).magnitude;

        if (distance > config.visionRange) return false;

        // ── 近距感知区：视野范围 30% 内，360° 察觉，跳过锥形角度检测 ──────────
        // 玩家静止站在敌人附近时，即使不在正面视野锥内也能被察觉（仍需通过射线检测）
        bool inProximityZone = distance <= config.visionRange * 0.3f;

        if (!inProximityZone)
        {
            Vector3 dirToTarget = (target.position - owner.position).normalized;
            float angle = Vector3.Angle(owner.forward, dirToTarget);
            if (angle > config.visionAngle / 2f) return false;
        }

        // 烟雾遮挡
        if (SmokeZoneManager.IsLineOfSightSmoked(owner.position, target.position))
            return false;

        // 射线检测（障碍物遮挡）
        // 用 IDetectable.DetectionPosition 作为目标点：
        // 玩家下蹲时高度降低，矮障碍物可遮挡视线
        var detectable = target.GetComponent<IDetectable>();
        Vector3 targetPos = detectable != null ? detectable.DetectionPosition : target.position;

        Vector3 dir = (targetPos - owner.position).normalized;
        return !Physics.Raycast(owner.position, dir,
            Vector3.Distance(owner.position, targetPos), obstacleLayer);
    }

    private float CalculateVisibility(Transform target)
    {
        float distance = new Vector2(
            target.position.x - owner.position.x,
            target.position.z - owner.position.z).magnitude;

        float distanceFactor = 1f - (distance / config.visionRange);

        Vector3 dirToTarget = (target.position - owner.position).normalized;
        float angle = Vector3.Angle(owner.forward, dirToTarget);
        float angleFactor = 1f - (angle / (config.visionAngle / 2f));

        return (distanceFactor + angleFactor) / 2f;
    }
}