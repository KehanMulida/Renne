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

    // 追踪上一帧是否看到玩家，用于检测 VisualLost
    private bool wasSeenLastFrame = false;

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

    private int GetFloor(Transform target)
    {
        return Mathf.FloorToInt(target.position.y / 4f);
    }

    public void UpdatePerception()
    {
        var player = FindPlayer();
        if (player == null)
        {
            // 玩家消失，如果上帧还看得到就触发 VisualLost
            if (wasSeenLastFrame)
            {
                wasSeenLastFrame = false;
                OnPerceptionEvent?.Invoke(new PerceptionEvent
                {
                    Type = PerceptionType.VisualLost
                });
            }
            return;
        }

        bool canSee = CanSeeTarget(player);

        if (canSee)
        {
            wasSeenLastFrame = true;

            OnPerceptionEvent?.Invoke(new PerceptionEvent
            {
                Type       = PerceptionType.VisualContact,
                Target     = player,
                Position   = player.position,
                Confidence = CalculateVisibility(player),
                Floor      = GetFloor(player)
            });
        }
        else if (wasSeenLastFrame)
        {
            // 上一帧看得到，这一帧看不到 → 触发 VisualLost
            wasSeenLastFrame = false;

            OnPerceptionEvent?.Invoke(new PerceptionEvent
            {
                Type = PerceptionType.VisualLost
            });
        }
    }

    private bool CanSeeTarget(Transform target)
    {
        Vector3 dirToTarget = (target.position - owner.position).normalized;
        float angle = Vector3.Angle(owner.forward, dirToTarget);
        
        if (angle > config.visionAngle / 2f) return false;
        
        float distance = Vector3.Distance(owner.position, target.position);
        if (distance > config.visionRange) return false;

        return !Physics.Raycast(owner.position, dirToTarget, distance, obstacleLayer);
    }

    private float CalculateVisibility(Transform target)
    {
        float distance = Vector3.Distance(owner.position, target.position);
        float distanceFactor = 1f - (distance / config.visionRange);
        
        Vector3 dirToTarget = (target.position - owner.position).normalized;
        float angle = Vector3.Angle(owner.forward, dirToTarget);
        float angleFactor = 1f - (angle / (config.visionAngle / 2f));
        
        return (distanceFactor + angleFactor) / 2f;
    }
}