using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class WalkLoopPolish
{
    const string Root="Assets/player——animation";
    static WalkLoopPolish(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string apply="Artifacts/PlayerAnimation/walk_loop_polish.request";
        if(File.Exists(apply)&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(apply);
            try{Polish();}catch(Exception e){File.WriteAllText("Artifacts/PlayerAnimation/walk_loop_polish.txt","FAIL\n"+e);Debug.LogException(e);}return;
        }
        const string request="Artifacts/PlayerAnimation/walk_loop_analyze.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        try{Analyze();}catch(Exception e){File.WriteAllText("Artifacts/PlayerAnimation/walk_loop_analysis.txt",e.ToString());}
    }

    [MenuItem("Tools/Renne/Player Animation/Smooth Walk Loop Seam")]
    public static void Polish()
    {
        PolishClip("Rene_Cautious_Walk_15F","walk_loop");
    }
    public static void PolishClip(string clipName,string reportName)
    {
        string path=Root+"/Animations/"+clipName+".anim";
        string backup="Artifacts/PlayerAnimation/Before_Loop_"+clipName+".anim";
        if(!File.Exists(backup))File.Copy(path,backup);
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        var bindings=AnimationUtility.GetCurveBindings(clip);float duration=clip.length;
        // The source pose at time zero is retained so idle and posture transitions still match.
        float window=Mathf.Min(.2f,duration*.1f),h=2f/30;
        foreach(var group in bindings.Where(b=>b.propertyName.StartsWith("m_LocalRotation.")).GroupBy(b=>b.path))
        {
            var axes=group.ToDictionary(b=>b.propertyName.Last(),b=>AnimationUtility.GetEditorCurve(clip,b));
            var first=new Quaternion(axes['x'].Evaluate(0),axes['y'].Evaluate(0),axes['z'].Evaluate(0),axes['w'].Evaluate(0));
            var last=new Quaternion(axes['x'].Evaluate(duration),axes['y'].Evaluate(duration),axes['z'].Evaluate(duration),axes['w'].Evaluate(duration));
            if(Quaternion.Dot(first,last)<0)throw new InvalidOperationException("Quaternion hemisphere changes across seam: "+group.Key);
        }
        int changed=0;float maxValueGap=0,maxSlopeGap=0;
        foreach(var binding in bindings)
        {
            var original=AnimationUtility.GetEditorCurve(clip,binding);
            float value=original.Evaluate(0),velocity=(original.Evaluate(h)-original.Evaluate(duration-h))/(2*h);
            float startValue=original.Evaluate(window),endValue=original.Evaluate(duration-window);
            float startVelocity=Derivative(original,window),endVelocity=Derivative(original,duration-window);
            var keys=original.keys;
            int startIndex=Array.FindIndex(keys,k=>Mathf.Abs(k.time-window)<.00001f);
            int endIndex=Array.FindIndex(keys,k=>Mathf.Abs(k.time-(duration-window))<.00001f);
            if(startIndex>=0)startVelocity=keys[startIndex].outTangent;
            if(endIndex>=0)endVelocity=keys[endIndex].inTangent;
            for(int i=0;i<keys.Length;i++)
            {
                float t=keys[i].time;
                if(t>window+.00001f && t<duration-window-.00001f)continue;
                float patched,slope;
                if(t<=window+.00001f)Hermite(Mathf.Clamp(t,0,window),window,value,velocity,startValue,startVelocity,out patched,out slope);
                else Hermite(Mathf.Clamp(t-(duration-window),0,window),window,endValue,endVelocity,value,velocity,out patched,out slope);
                keys[i].value=patched;
                if(i!=startIndex)keys[i].outTangent=slope;
                if(i!=endIndex)keys[i].inTangent=slope;
                keys[i].weightedMode=WeightedMode.None;
            }
            var result=new AnimationCurve(keys);
            if(startIndex<0)result.AddKey(new Keyframe(window,startValue,startVelocity,startVelocity));
            if(endIndex<0)result.AddKey(new Keyframe(duration-window,endValue,endVelocity,endVelocity));
            result.preWrapMode=WrapMode.Loop;result.postWrapMode=WrapMode.Loop;
            AnimationUtility.SetEditorCurve(clip,binding,result);changed++;
            var final=result.keys;
            maxValueGap=Mathf.Max(maxValueGap,Mathf.Abs(final[0].value-final[final.Length-1].value));
            maxSlopeGap=Mathf.Max(maxSlopeGap,Mathf.Abs(final[0].outTangent-final[final.Length-1].inTangent));
        }
        clip.EnsureQuaternionContinuity();
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=false;
        AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
        if(maxValueGap>.00001f || maxSlopeGap>.00001f)throw new InvalidOperationException("Seam values or tangents do not match.");
        if(clipName=="Rene_Cautious_Walk_15F")Analyze();
        File.WriteAllText("Artifacts/PlayerAnimation/"+reportName+"_polish.txt","PASS\nClip: "+clipName+". First pose, duration, and middle outside seam windows retained by this seam pass.\nSeam correction window: "+window+" sec at each end.\nCurves: "+changed+"; endpoint value gap="+maxValueGap+"; endpoint slope gap="+maxSlopeGap+".\nOriginal clip backed up at "+backup+".\n");
    }
    static float Derivative(AnimationCurve curve,float time)
    {const float d=.0005f;return (curve.Evaluate(time+d)-curve.Evaluate(time-d))/(2*d);}
    static void Hermite(float time,float width,float p0,float v0,float p1,float v1,out float value,out float derivative)
    {
        float u=time/width,u2=u*u,u3=u2*u;
        value=(2*u3-3*u2+1)*p0+(u3-2*u2+u)*width*v0+(-2*u3+3*u2)*p1+(u3-u2)*width*v1;
        derivative=((6*u2-6*u)*p0+(3*u2-4*u+1)*width*v0+(-6*u2+6*u)*p1+(3*u2-2*u)*width*v1)/width;
    }
    public static void Analyze()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Rene_PlayerVisual.prefab");
        var go=UnityEngine.Object.Instantiate(prefab);go.GetComponent<Animator>().enabled=false;
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/Rene_Cautious_Walk_15F.anim");
        var result=new StringBuilder();float dt=1f/30;
        try
        {
            var bones=go.GetComponentsInChildren<Transform>();
            Vector3[][] pos=new Vector3[4][];Quaternion[][] rot=new Quaternion[4][];
            float[] times={0,dt,clip.length-dt,clip.length};
            for(int t=0;t<4;t++)
            {
                clip.SampleAnimation(go,times[t]);pos[t]=bones.Select(b=>b.position).ToArray();rot[t]=bones.Select(b=>b.rotation).ToArray();
            }
            var rows=Enumerable.Range(0,bones.Length).Select(i=>new{index=i,gap=Vector3.Distance(pos[0][i],pos[3][i]),angle=Quaternion.Angle(rot[0][i],rot[3][i]),jerk=((pos[3][i]-pos[2][i])-(pos[1][i]-pos[0][i])).magnitude/dt}).ToArray();
            result.AppendLine("Duration="+clip.length+" seconds");
            foreach(var row in rows.OrderByDescending(r=>r.gap).Take(12))result.AppendLine(bones[row.index].name+": position gap="+row.gap.ToString("F6")+"m, angle gap="+row.angle.ToString("F4")+"deg, velocity mismatch="+row.jerk.ToString("F5")+"m/s");
            foreach(var row in rows.OrderByDescending(r=>r.jerk).Take(8))result.AppendLine("Velocity: "+bones[row.index].name+" "+row.jerk.ToString("F5")+"m/s");
            File.WriteAllText("Artifacts/PlayerAnimation/walk_loop_analysis.txt",result.ToString());
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
}
