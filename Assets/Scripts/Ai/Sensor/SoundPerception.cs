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

        // 忽略自己发出的声音
        if (soundEvent.source != null && soundEvent.source == owner.gameObject) return;

        // 忽略同阵营 Enemy 发出的声音（Enemy 之间不互相感知移动声）
        if (soundEvent.source != null &&
            soundEvent.source.GetComponent<EnemyAIController>() != null) return;

        // 听觉只负责“判断位置”：上报到黑板（lastHeardPosition），供 AI 在自己回合调查。
        // 不在此转身/瞄准——感知模块不应旋转 transform。
        OnPerceptionEvent?.Invoke(new PerceptionEvent
        {
            Type = PerceptionType.SoundHeard,
            Position = soundEvent.position,
            Confidence = 1f - (distance / config.hearingRange),
            Floor = soundEvent.floor
        });
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