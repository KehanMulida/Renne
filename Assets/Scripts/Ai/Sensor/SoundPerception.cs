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
        
        // 订阅SoundManager的OnSoundBroadcast事件
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.OnSoundBroadcast += HandleSound;
        }
    }

    // 匹配OnSoundBroadcast的Action<SoundEvent>签名
    private void HandleSound(SoundEvent soundEvent)
    {
        float distance = Vector3.Distance(owner.position, soundEvent.position);
        if (distance > config.hearingRange) return;
        if (GetFloor(owner) != soundEvent.floor) return;

        OnPerceptionEvent?.Invoke(new PerceptionEvent
        {
            Type = PerceptionType.SoundHeard,
            Position = soundEvent.position,
            Confidence = 1f - (distance / config.hearingRange),
            Floor = soundEvent.floor
        });
    }
    public void UpdatePerception()
    {
        // 声音感知是事件驱动的，不需要主动轮询
        // 留空即可
        return;
    }

    public void Cleanup()
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.OnSoundBroadcast -= HandleSound;
        }
    }

    private int GetFloor(Transform target)
    {
        return Mathf.FloorToInt(target.position.y / 4f);
    }
}