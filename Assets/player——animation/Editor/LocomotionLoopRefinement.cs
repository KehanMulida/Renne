using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LocomotionLoopRefinement
{
    const string Root=PlayerAnimationSetup.Root;
    static readonly string[] Names={"Rene_Cautious_Walk_15F","Rene_Crouch_Sneak_15F","Rene_Prone_Crawl_15F"};
    static LocomotionLoopRefinement(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string request="Artifacts/PlayerAnimation/locomotion_refine.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string operation=File.ReadAllText(request);File.Delete(request);
        try{if(operation.Contains("restore"))RestoreBaseline();Apply();}catch(Exception error){File.WriteAllText("Artifacts/PlayerAnimation/locomotion_refinement.txt","FAIL\n"+error);Debug.LogException(error);}
    }
    sealed class Contact
    {
        public string side;public bool hand;public float start,length,intercept,cap;public Vector3[] original;
        public float U(float phase){return Mathf.Repeat(phase-start,1)/length;}
        public float Weight(float phase)
        {
            float u=U(phase);if(u>1)return 0;
            return Smooth(u/.17f)*Smooth((1-u)/.17f)*Smooth(phase/.09f)*Smooth((1-phase)/.09f);
        }
        public Vector3 Goal(float phase,Vector3 position)
        {
            float unwrapped=start+Mathf.Repeat(phase-start,1);
            position.z+=Weight(phase)*Limit(intercept-unwrapped-position.z,cap);return position;
        }
    }
    static float Smooth(float x){x=Mathf.Clamp01(x);return x*x*(3-2*x);}
    static float Limit(float value,float cap)
    {
        float a=Mathf.Abs(value);if(a<=cap*.8f)return value;if(a>=cap*1.2f)return Mathf.Sign(value)*cap;
        float u=(a-cap*.8f)/(cap*.4f);return Mathf.Sign(value)*(cap*.8f+cap*.4f*u-cap*.2f*u*u);
    }
    static string Hash(string path){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));}
    public static void Apply()
    {
        string backup="Artifacts/PlayerAnimation/LocomotionLoops_Before";Directory.CreateDirectory(backup);
        var untouched=Directory.GetFiles(Root+"/Animations","*.anim").Where(p=>!Names.Contains(Path.GetFileNameWithoutExtension(p))).ToDictionary(p=>p,p=>Hash(p));
        var originals=Names.ToDictionary(name=>Root+"/Animations/"+name+".anim",name=>UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/"+name+".anim")));
        var report=new StringBuilder("PASS\nThree locomotion loops only. Current speeds and two footfalls per cell retained.\n");
        try
        {
        for(int index=0;index<Names.Length;index++)
        {
            string name=Names[index],path=Root+"/Animations/"+name+".anim";
            string copy=backup+"/"+name+".anim";if(!File.Exists(copy))File.Copy(path,copy);
            RefineClip(AssetDatabase.LoadAssetAtPath<AnimationClip>(path),index,report);
            File.WriteAllText("Artifacts/PlayerAnimation/locomotion_progress.txt",report.ToString());
            WalkLoopPolish.PolishClip(name,"locomotion_"+index);
            VerifyLoop(AssetDatabase.LoadAssetAtPath<AnimationClip>(path),report);
        }
        foreach(var entry in untouched)if(Hash(entry.Key)!=entry.Value)throw new InvalidOperationException("Unrelated clip changed: "+entry.Key);
        report.AppendLine("Unrelated animation files unchanged: "+untouched.Count);
        File.WriteAllText("Artifacts/PlayerAnimation/locomotion_refinement.txt",report.ToString());
        LocomotionContactAudit.Run();
        }
        catch(Exception error)
        {
            File.WriteAllText("Artifacts/PlayerAnimation/locomotion_failed_detail.txt",report+"\n"+error);
            foreach(var entry in originals)
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(entry.Key);
                EditorUtility.CopySerialized(entry.Value,clip);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
            }
            throw;
        }
        finally{foreach(var entry in originals)UnityEngine.Object.DestroyImmediate(entry.Value);}
    }
    static void RestoreBaseline()
    {
        foreach(string name in Names)
        {
            string path="Artifacts/PlayerAnimation/LocomotionLoops_Before/"+name+".anim";
            if(!File.Exists(path))continue;
            var objects=UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(Path.GetFullPath(path));
            var backup=objects.OfType<AnimationClip>().First();
            var current=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/"+name+".anim");
            EditorUtility.CopySerialized(backup,current);EditorUtility.SetDirty(current);AssetDatabase.SaveAssetIfDirty(current);
            foreach(var o in objects)UnityEngine.Object.DestroyImmediate(o);
        }
    }
    static GameObject Preview()
    {
        var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Rene_PlayerVisual.prefab"));
        go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);go.GetComponent<Animator>().enabled=false;return go;
    }
    static void RefineClip(AnimationClip clip,int type,StringBuilder report)
    {
        var go=Preview();var bones=go.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var renderer=go.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();
        int n=Mathf.RoundToInt(clip.length*30);var contacts=new List<Contact>();
        try
        {
            foreach(string side in new[]{".l",".r"})
            {
                bool hand=type==2;var original=new Vector3[n];var heights=new float[n];
                var weights=renderer.sharedMesh.boneWeights;
                var footIndices=hand?new int[0]:Enumerable.Range(0,weights.Length).Where(v=>FootWeight(renderer,weights[v],side)>.5f).ToArray();
                for(int i=0;i<n;i++)
                {
                    clip.SampleAnimation(go,clip.length*i/n);original[i]=bones[(hand?"hand":"foot")+side].position;
                    if(hand)heights[i]=original[i].y;
                    else
                    {
                        renderer.BakeMesh(mesh);var vs=mesh.vertices;
                        heights[i]=footIndices.Min(v=>renderer.transform.TransformPoint(vs[v]).y);
                    }
                }
                float threshold=heights.Min()+(hand?.022f:.014f);bool[] mask=heights.Select(y=>y<threshold).ToArray();
                int bestStart=-1,bestLength=0;
                for(int i=0;i<n;i++)if(mask[i]&&!mask[(i+n-1)%n])
                {int count=0;while(count<n&&mask[(i+count)%n])count++;if(count>bestLength){bestLength=count;bestStart=i;}}
                if(bestLength<n*.3f || bestLength>n*.9f)throw new InvalidOperationException("No reliable support interval: "+clip.name+side);
                float start=bestStart/(float)n,length=bestLength/(float)n,intercept=0;
                for(int j=0;j<bestLength;j++)intercept+=original[(bestStart+j)%n].z+start+j/(float)n;
                intercept/=bestLength;
                contacts.Add(new Contact{side=side,hand=hand,start=start,length=length,intercept=intercept,cap=type==0?.12f:type==1?.16f:.10f,original=original});
            }
            var affected=new HashSet<string>();
            foreach(var c in contacts)
                foreach(string name in c.hand?new[]{"arm_stretch","c_arm_twist_offset","forearm_stretch","hand"}:new[]{"thigh_stretch","thigh_twist","leg_stretch","foot"})affected.Add(name+c.side);
            var curves=new Dictionary<EditorCurveBinding,List<Keyframe>>();
            foreach(string name in affected)
            {
                string p=AnimationUtility.CalculateTransformPath(bones[name],go.transform);
                foreach(string property in new[]{"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"})curves.Add(EditorCurveBinding.FloatCurve(p,typeof(Transform),property),new List<Keyframe>());
            }
            float maxReachError=0,maxCorrection=0;
            var previousRotations=new Dictionary<string,Quaternion>();
            for(int i=0;i<=n;i++)
            {
                float phase=i/(float)n,time=phase*clip.length;clip.SampleAnimation(go,time);
                foreach(var c in contacts)
                {
                    var end=bones[(c.hand?"hand":"foot")+c.side];Vector3 target=c.Goal(phase,end.position);
                    maxCorrection=Mathf.Max(maxCorrection,Vector3.Distance(target,end.position));
                    maxReachError=Mathf.Max(maxReachError,Solve(bones,c,target));
                }
                var rotations=new Dictionary<string,Quaternion>();
                foreach(string name in affected)
                {
                    var q=bones[name].localRotation;
                    if(previousRotations.ContainsKey(name)&&Quaternion.Dot(previousRotations[name],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                    rotations[name]=q;previousRotations[name]=q;
                }
                foreach(var entry in curves)
                {
                    var transform=go.transform.Find(entry.Key.path);string property=entry.Key.propertyName;int axis="xyzw".IndexOf(property.Last());
                    float value=property.StartsWith("m_LocalPosition")?transform.localPosition[axis]:rotations[transform.name][axis];
                    if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("Invalid IK pose");
                    entry.Value.Add(new Keyframe(time,value));
                }
            }
            var draft=UnityEngine.Object.Instantiate(clip);draft.name=clip.name;
            foreach(var entry in curves)
            {
                var keys=entry.Value.ToArray();
                for(int i=0;i<keys.Length;i++)
                {int a=Mathf.Max(0,i-1),b=Mathf.Min(keys.Length-1,i+1);float tangent=(keys[b].value-keys[a].value)/(keys[b].time-keys[a].time);keys[i].inTangent=tangent;keys[i].outTangent=tangent;}
                AnimationUtility.SetEditorCurve(draft,entry.Key,new AnimationCurve(keys));
            }
            draft.EnsureQuaternionContinuity();
            foreach(var c in contacts)
            {
                float before=Slip(clip,go,bones,c,n),after=Slip(draft,go,bones,c,n);
                report.AppendLine(clip.name+" "+c.side+": core support slip "+before.ToString("F5")+" -> "+after.ToString("F5")+" m/cycle; reduction="+(100*(1-after/Mathf.Max(.0001f,before))).ToString("F1")+"%");
                if(after>before*.8f)throw new InvalidOperationException("Support correction did not improve enough: "+clip.name+c.side);
            }
            if(maxReachError>.06f)throw new InvalidOperationException("Excessive IK reach error "+maxReachError);
            EditorUtility.CopySerialized(draft,clip);EditorUtility.SetDirty(clip);UnityEngine.Object.DestroyImmediate(draft);
            report.AppendLine("Max support adjustment="+maxCorrection.ToString("F4")+"m; max IK reach clamp="+maxReachError.ToString("F5")+"m");
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);}
    }
    static float FootWeight(SkinnedMeshRenderer renderer,BoneWeight w,string side)
    {
        int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};float sum=0;
        for(int i=0;i<4;i++)if(renderer.bones[ids[i]].name=="foot"+side||renderer.bones[ids[i]].name=="toes_01"+side)sum+=values[i];return sum;
    }
    static float Solve(Dictionary<string,Transform> bones,Contact c,Vector3 target)
    {
        Transform upper=bones[(c.hand?"arm_stretch":"thigh_stretch")+c.side],extra=bones[(c.hand?"c_arm_twist_offset":"thigh_twist")+c.side],lower=bones[(c.hand?"forearm_stretch":"leg_stretch")+c.side],end=bones[(c.hand?"hand":"foot")+c.side];
        Vector3 a=upper.position,b=lower.position,e=end.position;
        if((target-e).sqrMagnitude<1e-12f)return 0;
        float l1=Vector3.Distance(a,b),l2=Vector3.Distance(b,e),originalDistance=Vector3.Distance(a,e);
        // Preserve the source contact height and avoid straightening further than the original pose.
        float reach=Mathf.Max(originalDistance,(l1+l2)*.975f);
        float dz=Mathf.Sqrt(Mathf.Max(0,reach*reach-Mathf.Pow(target.x-a.x,2)-Mathf.Pow(target.y-a.y,2)));
        target.z=Mathf.Clamp(target.z,a.z-dz,a.z+dz);
        float wanted=Vector3.Distance(a,target);
        float distance=Mathf.Clamp(wanted,Mathf.Abs(l1-l2)+.001f,Mathf.Max(originalDistance,(l1+l2)*.997f));
        Vector3 axis=(target-a).normalized;target=a+axis*distance;
        Vector3 pole=b-a-axis*Vector3.Dot(b-a,axis);
        if(pole.sqrMagnitude<1e-8f)pole=Vector3.Cross(axis,Vector3.right);
        pole.Normalize();float along=(l1*l1+distance*distance-l2*l2)/(2*distance);
        Vector3 joint=a+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
        Quaternion up=Quaternion.FromToRotation(b-a,joint-a),down=Quaternion.FromToRotation(e-b,target-joint);
        Vector3 extraPosition=extra.position;Quaternion extraRotation=extra.rotation,lowerRotation=lower.rotation,endRotation=end.rotation;
        upper.rotation=up*upper.rotation;
        extra.SetPositionAndRotation(a+up*(extraPosition-a),up*extraRotation);
        lower.SetPositionAndRotation(joint,down*lowerRotation);end.SetPositionAndRotation(target,endRotation);
        return Mathf.Abs(wanted-distance);
    }
    static float Slip(AnimationClip clip,GameObject go,Dictionary<string,Transform> bones,Contact c,int n)
    {
        float sum=0;int count=0;float h=1f/(n*4);
        for(int i=0;i<n;i++)
        {
            float phase=i/(float)n,u=c.U(phase);if(u<.28f||u>.72f||phase<.11f||phase>.89f)continue;
            var end=bones[(c.hand?"hand":"foot")+c.side];clip.SampleAnimation(go,(phase-h)*clip.length);float z0=end.position.z;
            clip.SampleAnimation(go,(phase+h)*clip.length);float z1=end.position.z;
            sum+=Mathf.Abs((z1-z0)/(2*h)+1);count++;
        }
        if(count==0)throw new InvalidOperationException("No support samples");return sum/count;
    }
    static void VerifyLoop(AnimationClip clip,StringBuilder report)
    {
        var go=Preview();var bones=go.GetComponentsInChildren<Transform>();
        try
        {
            clip.SampleAnimation(go,0);var p=bones.Select(b=>b.position).ToArray();var q=bones.Select(b=>b.rotation).ToArray();
            clip.SampleAnimation(go,clip.length);float gap=0,angle=0;
            for(int i=0;i<bones.Length;i++){gap=Mathf.Max(gap,Vector3.Distance(p[i],bones[i].position));angle=Mathf.Max(angle,Quaternion.Angle(q[i],bones[i].rotation));}
            if(gap>.00002f||angle>.06f)throw new InvalidOperationException("Loop seam mismatch: "+clip.name+" "+gap+" "+angle);
            report.AppendLine("PASS seam: "+clip.name+", max bone position gap="+gap.ToString("F7")+"m, angle="+angle.ToString("F4")+"deg");
            foreach(float phase in new[]{.25f,.5f,.75f})
            {
                clip.SampleAnimation(go,clip.length*phase);
                PlayerAnimationValidation.RenderPose(go,"refined_"+clip.name+"_"+Mathf.RoundToInt(phase*100));
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
}
