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
    public float Confidence;
    public int Floor;

    // PlayerChangedFloor 专用
    // 玩家从哪层去了哪层，以及最近的楼层连接点位置
    public int FromFloor;
    public int ToFloor;
    public Vector3 ConnectionPosition; // 楼梯/电梯的世界坐标
}

public enum PerceptionType
{
    VisualContact,        // 看到玩家
    VisualLost,           // 失去视野
    SoundHeard,           // 听到声音
    PlayerChangedFloor,   // 看到玩家切换楼层
    Nothing
}