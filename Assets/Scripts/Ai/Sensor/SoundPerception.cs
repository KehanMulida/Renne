using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;   
using UnityEngine.Events;

/// <summary>
/// 声音感知模块
/// </summary>
public class SoundPerception : IPerceptionModule
{
    private Transform owner;
    private EnemyConfig config;
    public event Action<PerceptionEvent> OnPerceptionEvent;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner = owner;
        this.config = config;
        
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.OnSoundBroadcast += HandleSound;
        }
    }

    public void UpdatePerception() { }

    private void HandleSound(SoundEvent soundEvent)
    {
        float distance = Vector3.Distance(owner.position, soundEvent.position);
        if (distance > config.hearingRange) return;
        if (GetFloor(owner) != soundEvent.floor) return;

        // 转向声音方向
        TurnTowardsSound(soundEvent.position);

        OnPerceptionEvent?.Invoke(new PerceptionEvent
        {
            Type = PerceptionType.SoundHeard,
            Position = soundEvent.position,
            Confidence = 1f - (distance / config.hearingRange),
            Floor = soundEvent.floor
        });
    }

    private void TurnTowardsSound(Vector3 soundPosition)
    {
        Vector3 direction = soundPosition - owner.position;
        direction.y = 0; // 只水平旋转
        
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            owner.rotation = targetRotation; // 立即转向
            
            Debug.Log($"[SoundPerception] Turned towards sound at {soundPosition}");
        }
    }

    private int GetFloor(Transform target)
    {
        return Mathf.FloorToInt(target.position.y / 4f);
    }

    public void Cleanup()
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.OnSoundBroadcast -= HandleSound;
        }
    }
}