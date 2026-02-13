using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;   
public interface IPerceptionModule
{
    void Initialize(Transform owner, EnemyConfig config);
    void UpdatePerception();
    event Action<PerceptionEvent> OnPerceptionEvent;
}

public class PerceptionEvent
{
    public PerceptionType Type;
    public Vector3 Position;
    public Transform Target;
    public float Confidence; // 0-1
    public int Floor;
}

public enum PerceptionType
{
    VisualContact,
    VisualLost,
    SoundHeard,
    Nothing
}