using UnityEngine;

/// <summary>Room selection uses nested entry/exit bounds and locks throughout each transition.</summary>
[RequireComponent(typeof(Camera))]
public class RoomCameraDirector : MonoBehaviour
{
    public RoomCameraZone[] zones;
    public RoomCameraZone ActiveRoom { get; private set; }
    public bool ControlsCamera => ActiveRoom!=null || transitioning;
    public bool SuppressWallFade => false;
    public int SwitchCount { get; private set; }
    Camera lens;
    CameraOcclusionHandler fading;
    bool transitioning, savedFade;
    float elapsed, normalFov, duration;
    Vector3 startPosition;
    float startFov;
    void Awake()
    {
        lens=GetComponent<Camera>();normalFov=lens.fieldOfView;
        fading=GetComponent<CameraOcclusionHandler>();
        savedFade=fading!=null && fading.enabled;
    }
    public void Tick(Transform player,Vector3 normalOffset,Quaternion normalRotation,float delta)
    {
        if(lens==null)Awake();
        if(player==null)return;
        // Complete the current move before considering another boundary, including teleports.
        if(!transitioning)
        {
            RoomCameraZone next=ActiveRoom;
            if(next!=null && !next.Contains(player.position,true))next=null;
            if(next==null && ActiveRoom==null && zones!=null)
                foreach(var zone in zones)
                    if(zone!=null && zone.isActiveAndEnabled && zone.Contains(player.position,false)) {next=zone;break;}
            if(next!=ActiveRoom)
            {
                duration=(next!=null?next:ActiveRoom).transitionSeconds;
                ActiveRoom=next;elapsed=0;transitioning=true;SwitchCount++;
                startPosition=transform.position;startFov=lens.fieldOfView;
            }
        }
        bool shouldFade=savedFade && !SuppressWallFade;
        if(fading!=null && fading.enabled!=shouldFade)fading.enabled=shouldFade;
        if(zones!=null)foreach(var zone in zones)
            if(zone!=null && zone.visibilityCurtain!=null)
                zone.visibilityCurtain.SetActive(false);
        if(!ControlsCamera)return;
        Vector3 end=player.position+normalOffset*(ActiveRoom!=null?ActiveRoom.distanceScale:1f);
        Quaternion rotation=normalRotation;
        float fov=normalFov;
        elapsed+=Mathf.Max(0,delta);
        float t=transitioning?Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/Mathf.Max(.1f,duration))):1;
        transform.SetPositionAndRotation(Vector3.Lerp(startPosition,end,t),rotation);
        lens.fieldOfView=Mathf.Lerp(startFov,fov,t);
        if(elapsed>=duration)transitioning=false;
    }
    void OnDisable()
    {
        if(zones!=null)foreach(var z in zones)if(z!=null && z.visibilityCurtain!=null)z.visibilityCurtain.SetActive(false);
        if(fading!=null)fading.enabled=savedFade;
        if(lens!=null)lens.fieldOfView=normalFov;
        ActiveRoom=null;transitioning=false;
    }
}
