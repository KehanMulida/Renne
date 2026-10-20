using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>角色柔和补光、脚下选中圈，以及真实交互物的近距离标记。</summary>
[DefaultExecutionOrder(950)]
public sealed class PlayerReadability : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Material markerMaterial;
    private Camera view;
    private LineRenderer ring;
    private Material ringMaterial, pickupMaterial, interactMaterial;
    private readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
    private readonly List<Material> clones = new List<Material>();
    private readonly Dictionary<Transform, LineRenderer> markers = new Dictionary<Transform, LineRenderer>();
    private float nextScan;
    private MeshRenderer[] groundCandidates;
    private readonly RaycastHit[] groundHits=new RaycastHit[16];

    private void OnEnable()
    {
        view=GetComponent<Camera>();
        if(player==null) {var found=GameObject.FindGameObjectWithTag("Player");if(found!=null)player=found.transform;}
        if(player==null || markerMaterial==null) return;
        foreach(var renderer in player.GetComponentsInChildren<Renderer>())
        {
            var shared=renderer.sharedMaterials;
            originals[renderer]=shared;
            var materials=new Material[shared.Length];
            for(int i=0;i<shared.Length;i++)
            {
                if(shared[i]==null) continue;
                var material=new Material(shared[i]);
                if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.12f);
                if(material.HasProperty("_SpecColor"))material.SetColor("_SpecColor",Color.black);
                material.SetFloat("_SpecularHighlights",0f);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                if(material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    bool character = renderer is SkinnedMeshRenderer;
                    if (character && material.HasProperty("_BaseMap") && material.HasProperty("_EmissionMap"))
                        material.SetTexture("_EmissionMap", material.GetTexture("_BaseMap"));
                    material.SetColor("_EmissionColor", character ? new Color(.4f,.4f,.4f) : new Color(.018f,.026f,.04f));
                }
                if (renderer is SkinnedMeshRenderer) renderer.receiveShadows = false;
                materials[i]=material;clones.Add(material);
            }
            renderer.sharedMaterials=materials;
        }
        ringMaterial=Tint(new Color(.25f,.7f,.85f));
        pickupMaterial=Tint(new Color(.2f,.85f,1f));
        interactMaterial=Tint(new Color(.95f,.72f,.3f));
        ring=Line("Player Selection Ring",ringMaterial,48,.018f);
        Scan();LateUpdate();
    }

    private Material Tint(Color color)
    {
        var material=new Material(markerMaterial);material.SetColor("_BaseColor",color);return material;
    }
    private LineRenderer Line(string name,Material material,int count,float width)
    {
        var obj=new GameObject(name);obj.transform.SetParent(player,false);
        var line=obj.AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=true;
        line.loop=true;line.positionCount=count;line.widthMultiplier=width;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
        return line;
    }
    private void Scan()
    {
        groundCandidates=FindObjectsOfType<MeshRenderer>();
        foreach(var item in FindObjectsOfType<WorldItem>())
            if(!item.IsPickedUp && !markers.ContainsKey(item.transform))
                markers[item.transform]=Line("Pickup Marker",pickupMaterial,4,.017f);
        foreach(var item in FindObjectsOfType<SceneItemInstance>())
            if(item.Data!=null && item.Data.PlayerCanInteract && !markers.ContainsKey(item.transform))
                markers[item.transform]=Line("Interaction Marker",interactMaterial,6,.017f);
        nextScan=Time.unscaledTime+1f;
    }
    private void LateUpdate()
    {
        if(ring==null || player==null)return;
        if(Time.unscaledTime>=nextScan)Scan();
        float ground=GroundHeight();
        for(int i=0;i<48;i++)
        {float a=i*Mathf.PI*2/48;ring.SetPosition(i,new Vector3(player.position.x+Mathf.Cos(a)*.57f,ground+.018f,player.position.z+Mathf.Sin(a)*.57f));}
        Transform closestPickup=null,closestInteraction=null;
        float pickupDistance=2.8f,interactionDistance=2.8f;
        foreach(var pair in markers)
        {
            var target=pair.Key;if(target==null || !PlayerSceneVisibility.CanReveal(target))continue;
            float distance=Vector3.Distance(target.position,player.position);
            bool pickup=target.GetComponent<WorldItem>()!=null;
            if(pickup && distance<pickupDistance){pickupDistance=distance;closestPickup=target;}
            if(!pickup && distance<interactionDistance){interactionDistance=distance;closestInteraction=target;}
        }
        foreach(var pair in markers)
        {
            var target=pair.Key;var line=pair.Value;
            bool visible=target!=null && target.gameObject.activeInHierarchy &&
                (target==closestPickup || target==closestInteraction) && PlayerSceneVisibility.CanReveal(target);
            if(target!=null)
            {
                var item=target.GetComponent<SceneItemInstance>();
                if(item!=null && item.IsDestroyed)visible=false;
            }
            line.enabled=visible;if(!visible)continue;
            var renderer=target.GetComponentInChildren<MeshRenderer>();
            Vector3 point=renderer!=null?renderer.bounds.center:target.position;
            point.y=renderer!=null?renderer.bounds.max.y+.18f:point.y+.5f;
            for(int i=0;i<line.positionCount;i++)
            {
                float a=i*Mathf.PI*2/line.positionCount;
                line.SetPosition(i,point+(view.transform.right*Mathf.Sin(a)+view.transform.up*Mathf.Cos(a))*.1f);
            }
        }
    }
    private float GroundHeight()
    {
        float best=player.position.y-1f;
        int count=Physics.RaycastNonAlloc(player.position+Vector3.up*.2f,Vector3.down,groundHits,3f,1<<6,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
            if(groundHits[i].point.y<=player.position.y+.1f)best=Mathf.Max(best,groundHits[i].point.y);
        if(best>player.position.y-.99f)return best;
        if(groundCandidates==null)return best;
        foreach(var renderer in groundCandidates)
        {
            if(renderer==null || !renderer.enabled)continue;
            var b=renderer.bounds;
            if(b.size.y>.4f || b.size.x<1f || b.size.z<1f || b.max.y>player.position.y+.1f)continue;
            if(player.position.x>=b.min.x && player.position.x<=b.max.x && player.position.z>=b.min.z && player.position.z<=b.max.z)
                best=Mathf.Max(best,b.max.y);
        }
        return best;
    }
    private void OnDisable()
    {
        foreach(var pair in originals)if(pair.Key!=null)pair.Key.sharedMaterials=pair.Value;
        originals.Clear();foreach(var material in clones)Release(material);clones.Clear();
        foreach(var line in markers.Values)if(line!=null)Release(line.gameObject);markers.Clear();
        if(ring!=null)Release(ring.gameObject);

        Release(ringMaterial);Release(pickupMaterial);Release(interactMaterial);
    }
    private static void Release(Object obj)
    {if(obj==null)return;if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}
}
