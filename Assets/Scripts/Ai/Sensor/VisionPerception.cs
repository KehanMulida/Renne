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
     //   Debug.Log("[VisionPerception] UpdatePerception called");
        
        var player = FindPlayer();
     //   Debug.Log($"[VisionPerception] Player found: {player != null}");
     //   bool canSee = CanSeeTarget(player);
  //  Debug.Log($"[Vision] CanSeeTarget result: {canSee}");  // ← 添加这行
        if (player == null) return;

        if (CanSeeTarget(player))
        {
            Debug.Log("[VisionPerception] Can see player! Invoking event...");
            
            var evt = new PerceptionEvent
            {
                Type = PerceptionType.VisualContact,
                Target = player,
                Position = player.position,
                Confidence = CalculateVisibility(player),
                Floor = GetFloor(player)
            };
            
          //  Debug.Log($"[VisionPerception] Event created: {evt.Type}, Subscribers: {OnPerceptionEvent?.GetInvocationList().Length ?? 0}");
            
            OnPerceptionEvent?.Invoke(evt);
            
        //    Debug.Log("[VisionPerception] Event invoked");
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