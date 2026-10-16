using UnityEngine;

/// <summary>Authored world-space room bounds. No raycast or occlusion score selects a camera.</summary>
public class RoomCameraZone : MonoBehaviour
{
    public Bounds room = new Bounds(new Vector3(4.37f,.3f,6f),new Vector3(2.5f,4f,1.38f));
    [Min(.01f)] public float entryInset = .18f;
    [Min(.05f)] public float exitBuffer = .45f;
    [Min(.1f)] public float transitionSeconds = .65f;
    public GameObject visibilityCurtain;
    [Range(.35f,1f)] public float distanceScale = 1f;

    public bool Contains(Vector3 point, bool retaining)
    {
        var b=room;
        // Height never receives horizontal hysteresis: another floor cannot retain this room.
        var size=b.size;
        float margin=retaining ? exitBuffer : -entryInset;
        size.x=Mathf.Max(.01f,size.x+2*margin); size.z=Mathf.Max(.01f,size.z+2*margin);
        b.size=size;
        return b.Contains(point);
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.color=Color.cyan;Gizmos.DrawWireCube(room.center,room.size);
        Gizmos.color=Color.yellow;
        Gizmos.DrawWireCube(room.center,room.size+new Vector3(exitBuffer*2,0,exitBuffer*2));
    }
}
