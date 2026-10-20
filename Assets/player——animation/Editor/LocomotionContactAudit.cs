using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LocomotionContactAudit
{
    [Serializable] public class BoneRow { public string name,parent,path;public float[] localPosition,localRotation; }
    [Serializable] public class Frame { public float phase;public float[] leftFoot,rightFoot,leftHand,rightHand;public float leftSole,rightSole; }
    [Serializable] public class ClipRow { public string name;public float duration;public Frame[] frames; }
    [Serializable] public class Audit { public BoneRow[] bones;public ClipRow[] clips; }
    static LocomotionContactAudit(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string request="Artifacts/PlayerAnimation/contact_audit.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);try{Run();}catch(Exception e){File.WriteAllText("Artifacts/PlayerAnimation/contact_audit_error.txt",e.ToString());Debug.LogException(e);}
    }
    static float[] V(Vector3 v){return new[]{v.x,v.y,v.z};}
    public static void Run()
    {
        var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAnimationSetup.Root+"/Rene_PlayerVisual.prefab"));
        go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);go.GetComponent<Animator>().enabled=false;
        var bones=go.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var renderer=go.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();
        var weights=renderer.sharedMesh.boneWeights;
        int[] left=FootVertices(renderer,weights,".l"),right=FootVertices(renderer,weights,".r");
        var report=new Audit();var clips=new List<ClipRow>();
        try
        {
            report.bones=go.GetComponentsInChildren<Transform>().Select(t=>new BoneRow{name=t.name,parent=t.parent?t.parent.name:"",path=AnimationUtility.CalculateTransformPath(t,go.transform),localPosition=V(t.localPosition),localRotation=new[]{t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w}}).ToArray();
            foreach(string name in new[]{"Rene_Cautious_Walk_15F","Rene_Crouch_Sneak_15F","Rene_Prone_Crawl_15F"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerAnimationSetup.Root+"/Animations/"+name+".anim");var frames=new List<Frame>();
                for(int i=0;i<=120;i++)
                {
                    float phase=i/120f;clip.SampleAnimation(go,phase*clip.length);renderer.BakeMesh(mesh);var vs=mesh.vertices;
                    frames.Add(new Frame{phase=phase,leftFoot=V(bones["foot.l"].position),rightFoot=V(bones["foot.r"].position),leftHand=V(bones["hand.l"].position),rightHand=V(bones["hand.r"].position),leftSole=left.Min(v=>renderer.transform.TransformPoint(vs[v]).y),rightSole=right.Min(v=>renderer.transform.TransformPoint(vs[v]).y)});
                }
                clips.Add(new ClipRow{name=name,duration=clip.length,frames=frames.ToArray()});
            }
            report.clips=clips.ToArray();File.WriteAllText("Artifacts/PlayerAnimation/contact_audit.json",JsonUtility.ToJson(report,true));
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);}
    }
    static int[] FootVertices(SkinnedMeshRenderer renderer,BoneWeight[] weights,string side)
    {
        var ids=new HashSet<int>(renderer.bones.Select((b,i)=>new{b,i}).Where(x=>x.b.name=="foot"+side || x.b.name=="toes_01"+side).Select(x=>x.i));
        return Enumerable.Range(0,weights.Length).Where(i=>Weight(weights[i],ids)>.5f).ToArray();
    }
    static float Weight(BoneWeight w,HashSet<int> ids){return (ids.Contains(w.boneIndex0)?w.weight0:0)+(ids.Contains(w.boneIndex1)?w.weight1:0)+(ids.Contains(w.boneIndex2)?w.weight2:0)+(ids.Contains(w.boneIndex3)?w.weight3:0);}
}
