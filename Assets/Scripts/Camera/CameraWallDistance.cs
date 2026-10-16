using UnityEngine;

/// <summary>Continuous boom contraction, independent of room selection. Never rotates the camera.</summary>
public class CameraWallDistance : MonoBehaviour
{
    public LayerMask wallLayers=129;
    [Min(.05f)] public float radius=.2f;
    [Min(.01f)] public float padding=.12f;
    [Min(0)] public float releaseDelay=.35f;
    [Min(.1f)] public float recoverySpeed=3f;
    [Min(1f)] public float minimumViewDistance=3.2f;
    readonly RaycastHit[] hits=new RaycastHit[128];
    MeshRenderer[] geometry;
    float heldDistance=float.PositiveInfinity, clearTime;
    public float LastSafeDistance { get; private set; }
    public bool NeedsCutaway { get; private set; }

    public Vector3 Resolve(Transform player,Vector3 desired,float delta)
    {
        if(player==null)return desired;
        if(geometry==null)geometry=FindObjectsOfType<MeshRenderer>();
        Vector3 origin=player.position,boom=desired-origin;
        float length=boom.magnitude;
        if(length<.001f)return desired;
        Vector3 direction=boom/length;
        var lens=GetComponent<Camera>();
        float nearRadius=lens!=null?lens.nearClipPlane*Mathf.Tan(lens.fieldOfView*Mathf.Deg2Rad*.5f)*Mathf.Sqrt(1+lens.aspect*lens.aspect):0;
        float clearance=Mathf.Max(radius,nearRadius);
        float safe=length;
        int count=Physics.SphereCastNonAlloc(origin,clearance,direction,hits,length,wallLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var hit=hits[i];
            if(Ignore(hit.collider.transform,player))continue;
            var renderer=hit.collider.GetComponent<Renderer>();
            if(renderer!=null && (!renderer.enabled || renderer.forceRenderingOff))continue;
            safe=Mathf.Min(safe,Mathf.Max(.05f,hit.distance-padding));
        }
        // Imported partitions can have render meshes without colliders.
        foreach(var mesh in geometry)
        {
            if(mesh==null || !mesh.enabled || !mesh.gameObject.activeInHierarchy || mesh.forceRenderingOff ||
                (wallLayers.value & (1<<mesh.gameObject.layer))==0 || Ignore(mesh.transform,player) ||
                mesh.GetComponent<Collider>()!=null)continue;
            var bounds=mesh.bounds;
            if(bounds.size.y<.8f || Mathf.Max(bounds.size.x,bounds.size.z)<.8f || bounds.max.y<origin.y-.25f)continue;
            bounds.Expand(clearance*2);
            if(bounds.Contains(origin))continue;
            if(bounds.IntersectRay(new Ray(origin,direction),out float distance))
                safe=Mathf.Min(safe,Mathf.Max(.05f,distance-padding));
        }
        LastSafeDistance=safe;
        // A capsule-width stall cannot fit a readable third-person boom at this angle.
        // Retain a readable distance and ask the foreground wall cutaway to reveal the player.
        NeedsCutaway=safe<minimumViewDistance;
        safe=Mathf.Min(length,Mathf.Max(safe,minimumViewDistance));
        if(safe<heldDistance) {heldDistance=safe;clearTime=0;}
        else if(safe>heldDistance+.08f)
        {
            clearTime+=Mathf.Max(0,delta);
            if(clearTime>=releaseDelay)
                heldDistance=Mathf.Lerp(heldDistance,safe,1-Mathf.Exp(-recoverySpeed*Mathf.Max(0,delta)));
        }
        else {if(clearTime>=releaseDelay)heldDistance=safe;clearTime=0;}
        return origin+direction*Mathf.Min(heldDistance,safe);
    }
    static bool Ignore(Transform obj,Transform player)=>obj==player || obj.IsChildOf(player) ||
        obj.GetComponentInParent<EnemyAIController>()!=null || obj.GetComponentInParent<WorldItem>()!=null;
    public void ResetDistance(){heldDistance=float.PositiveInfinity;clearTime=0;geometry=null;NeedsCutaway=false;}
}
