using UnityEngine;

/// <summary>Choose a view once per completed player move; animation never re-evaluates occlusion.</summary>
public class MoveEndCameraView : MonoBehaviour
{
    public LayerMask obstacleLayers=129;
    [Min(2)] public float topHeight=6.5f;
    [Min(.1f)] public float transitionSeconds=.55f;
    public bool IsTopView { get; private set; }
    public int EvaluationCount { get; private set; }
    public bool ControlsCamera=>IsTopView || transitioning;
    UnitMovement movement;
    bool pending,transitioning;
    float elapsed;
    Vector3 fromPosition;
    Quaternion fromRotation;
    readonly RaycastHit[] hits=new RaycastHit[128];

    void OnEnable()
    {
        var player=GameObject.FindGameObjectWithTag("Player");
        Bind(player!=null?player.GetComponent<UnitMovement>():null);
    }
    public void Bind(UnitMovement unit)
    {
        if(movement!=null){movement.OnMoveComplete-=RequestEvaluation;movement.OnInitialized-=RequestEvaluation;}
        movement=unit;
        if(movement!=null)
        {
            movement.OnMoveComplete+=RequestEvaluation;movement.OnInitialized+=RequestEvaluation;
            pending=movement.IsInitialized;
        }
    }
    void RequestEvaluation(){pending=true;}
    void OnDisable()
    {
        Bind(null);pending=false;IsTopView=false;transitioning=false;
    }
    public void Tick(Transform player,Vector3 normalOffset,Quaternion normalRotation,float delta)
    {
        if(player==null)return;
        if(pending && (movement==null || !movement.IsMoving))
        {
            pending=false;
            Physics.SyncTransforms();
            int blocked=CountBlocked(player,player.position+normalOffset);
            EvaluationCount++;
            // Two of three body samples enter top view; fully clear sight restores oblique.
            // A single grazing sample retains the previous view until the next move finishes.
            bool next=blocked>=2 || (IsTopView && blocked!=0);
            if(next!=IsTopView)
            {
                IsTopView=next;transitioning=true;elapsed=0;
                fromPosition=transform.position;fromRotation=transform.rotation;
            }
        }
        if(!ControlsCamera)return;
        Vector3 end=player.position+(IsTopView?Vector3.up*topHeight:normalOffset);
        Quaternion rotation=IsTopView?Quaternion.Euler(90,normalRotation.eulerAngles.y,0):normalRotation;
        elapsed+=Mathf.Max(0,delta);
        float t=transitioning?Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/transitionSeconds)):1;
        transform.SetPositionAndRotation(Vector3.Lerp(fromPosition,end,t),Quaternion.Slerp(fromRotation,rotation,t));
        if(elapsed>=transitionSeconds)transitioning=false;
    }
    int CountBlocked(Transform player,Vector3 cameraPosition)
    {
        var body=player.GetComponentInChildren<MeshRenderer>();
        var bounds=body!=null?body.bounds:new Bounds(player.position+Vector3.up*.8f,new Vector3(.7f,1.6f,.7f));
        var meshes=FindObjectsOfType<MeshRenderer>();
        int blocked=0;
        for(int sample=0;sample<3;sample++)
        {
            Vector3 aim=bounds.center;aim.y=Mathf.Lerp(bounds.min.y+.15f,bounds.max.y-.15f,sample*.5f);
            Vector3 delta=aim-cameraPosition;float distance=delta.magnitude;
            if(distance<.001f)continue;
            var ray=new Ray(cameraPosition,delta/distance);
            bool hitWall=false;
            int count=Physics.RaycastNonAlloc(ray,hits,distance,obstacleLayers,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                if(Ignore(hits[i].collider.transform,player))continue;
                var r=hits[i].collider.GetComponent<Renderer>();
                if(r!=null && (!r.enabled || r.forceRenderingOff))continue;
                if(r!=null && (r.bounds.size.y<=bounds.size.y+.05f || r.bounds.max.y<=bounds.max.y+.05f))continue;
                hitWall=true;break;
            }
            if(!hitWall)foreach(var mesh in meshes)
            {
                if(!mesh.enabled || mesh.forceRenderingOff || Ignore(mesh.transform,player) || mesh.GetComponent<Collider>()!=null ||
                    (obstacleLayers.value & (1<<mesh.gameObject.layer))==0 ||
                    mesh.bounds.size.y<=bounds.size.y+.05f || mesh.bounds.max.y<=bounds.max.y+.05f)continue;
                if(mesh.bounds.Contains(cameraPosition) || (mesh.bounds.IntersectRay(ray,out float d) && d<distance-.05f))
                {hitWall=true;break;}
            }
            if(hitWall)blocked++;
        }
        return blocked;
    }
    static bool Ignore(Transform obj,Transform player)=>obj==player || obj.IsChildOf(player) ||
        obj.GetComponentInParent<EnemyAIController>()!=null || obj.GetComponentInParent<WorldItem>()!=null;
}
