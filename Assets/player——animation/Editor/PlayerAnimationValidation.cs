using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerAnimationValidation
{
    const string Root = PlayerAnimationSetup.Root;
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Renne/Player Animation/Validate Imported Animations")]
    public static void Validate()
    {
        var report = new StringBuilder();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Rene_PlayerVisual.prefab");
        Require(prefab, "Missing visual prefab");
        var go = UnityEngine.Object.Instantiate(prefab); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var animator = go.GetComponent<Animator>();
        var mesh = new Mesh();
        try
        {
            var all = AssetDatabase.FindAssets("t:AnimationClip", new[]{Root + "/Animations"})
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).ToArray();
            Require(all.Length == 27, "Expected 24 exported clips and 3 stationary poses, got " + all.Length);
            Require(all.All(c => !c.name.Contains("Knockdown")), "Knockdown clip present");
            foreach(var clip in all)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Require(bindings.Length>100,"Missing baked skeleton in " + clip.name);
                foreach(var binding in bindings)
                {
                    Require(string.IsNullOrEmpty(binding.path) || go.transform.Find(binding.path), "Unbound path in " + clip.name + ": " + binding.path);
                    var curve=AnimationUtility.GetEditorCurve(clip,binding);
                    Require(curve.keys.All(k => !float.IsNaN(k.value) && !float.IsInfinity(k.value)),"Invalid key in " + clip.name);
                }
                clip.SampleAnimation(go,clip.length*.5f);
                var renderer=go.GetComponentInChildren<SkinnedMeshRenderer>();renderer.BakeMesh(mesh);
                var vs=mesh.vertices.Select(v=>renderer.transform.TransformPoint(v)).ToArray();
                float low=vs.Min(v=>v.y),high=vs.Max(v=>v.y);
                report.AppendLine("DIAGNOSTIC " +clip.name+" " +low+".."+high);
                File.WriteAllText("Artifacts/PlayerAnimation/pose_diagnostic.txt",report.ToString());
                Require(high-low>.15f && high-low<2.5f,"Broken export height: " + clip.name + " " +(high-low));
                Require(vs.All(v=>v.magnitude<4f),"Exploded skin: " + clip.name);
                report.AppendLine(clip.name + ": " + bindings.Length + " curves, duration=" + clip.length.ToString("F3") + ", y=" + low.ToString("F4") + ".." + high.ToString("F4"));
                if (clip.name=="StandIdle" || clip.name=="CrouchIdle" || clip.name=="Rene_Prone_Crawl_15F" || clip.name=="Rene_Throw_Right_15F" || clip.name=="Rene_Crouch_Pickup_Right_15F")
                    RenderPose(go,clip.name);
            }
            animator.Rebind();animator.Update(0);
            // Exercise the actual controller graph, including direct stand/prone requests routed via crouch.
            CheckState(animator,0,false,"StandIdle",.3f);
            CheckState(animator,0,true,"Walk",.5f);
            CheckState(animator,1,false,"CrouchIdle",1.6f);
            CheckState(animator,1,true,"CrouchWalk",.5f);
            CheckState(animator,2,false,"ProneIdle",2.7f);
            CheckState(animator,2,true,"ProneCrawl",.5f);
            CheckState(animator,0,false,"StandIdle",4.5f);
            CheckState(animator,2,false,"ProneIdle",4.5f);
            CheckState(animator,0,false,"StandIdle",4.5f);
            animator.SetTrigger("Throw"); Step(animator,.4f);Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Throw"),"Throw trigger not connected");Step(animator,3f);
            animator.SetTrigger("Pickup");Step(animator,.4f);Require(animator.GetCurrentAnimatorStateInfo(0).IsName("StandPickup"),"Standing pickup not connected");Step(animator,3.5f);
            CheckState(animator,1,false,"CrouchIdle",1.6f);
            animator.SetTrigger("Pickup");Step(animator,.4f);Require(animator.GetCurrentAnimatorStateInfo(0).IsName("CrouchPickup"),"Crouched pickup not connected");Step(animator,3.5f);
            report.AppendLine("CONTROLLER_GRAPH_PASS: stand/crouch/prone idle+movement, all four transitions, throw, both pickups, direct stand/prone routing.");
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);}
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        var player=GameObject.FindGameObjectWithTag("Player");
        Require(player && player.GetComponent<PlayerAnimationDriver>(),"Test scene player not connected");
        Require(!player.GetComponent<MeshRenderer>() && !player.GetComponent<MeshFilter>(),"Capsule visual still present");
        Require(player.GetComponent<CapsuleCollider>().enabled,"Player collision lost");
        Require(player.GetComponentInChildren<Animator>().runtimeAnimatorController,"Player controller unassigned");
        report.AppendLine("SCENE_PLAYER_PASS: existing player root/components retained; capsule renderer hidden; collision and animator present.");
        File.WriteAllText("Artifacts/PlayerAnimation/validation.txt",report.ToString());
        Debug.Log("PLAYER_ANIMATION_VALIDATION_PASS");
    }
    static void Step(Animator animator,float seconds) { for(int i=0;i<Mathf.CeilToInt(seconds*60);i++)animator.Update(1f/60); }
    public static void RenderPose(GameObject go,string name)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
        var cameraObject=new GameObject("Animation validation camera");
        var lightObject=new GameObject("Animation validation light");
        var poseObject=new GameObject("Static evaluated pose");poseObject.layer=31;
        var skin=go.GetComponentInChildren<SkinnedMeshRenderer>();var poseMesh=new Mesh();skin.BakeMesh(poseMesh);
        var vertices=poseMesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]=skin.transform.TransformPoint(vertices[i]);
        poseMesh.vertices=vertices;poseMesh.RecalculateBounds();poseMesh.RecalculateNormals();
        poseObject.AddComponent<MeshFilter>().sharedMesh=poseMesh;
        poseObject.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
        skin.enabled=false;
        var target=new RenderTexture(640,640,24);var image=new Texture2D(640,640,TextureFormat.RGB24,false);
        int originalLayer=go.layer;
        foreach(var t in go.GetComponentsInChildren<Transform>())t.gameObject.layer=31;
        try
        {
            var camera=cameraObject.AddComponent<Camera>();camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.19f,.22f);
            var center=poseMesh.bounds.center;
            camera.orthographic=true;camera.orthographicSize=Mathf.Max(.8f,poseMesh.bounds.size.magnitude*.6f);camera.nearClipPlane=.01f;
            camera.transform.position=center+new Vector3(3,2,4);camera.transform.LookAt(center);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;
            light.transform.rotation=Quaternion.Euler(35,-30,0);light.cullingMask=1<<31;
            camera.targetTexture=target;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes("Artifacts/PlayerAnimation/"+name+".png",image.EncodeToPNG());
        }
        finally
        {
            foreach(var t in go.GetComponentsInChildren<Transform>())t.gameObject.layer=originalLayer;
            skin.enabled=true;UnityEngine.Object.DestroyImmediate(poseObject);UnityEngine.Object.DestroyImmediate(poseMesh);
            UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
            target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
        }
    }
    static void CheckState(Animator animator,int stance,bool moving,string state,float seconds)
    {
        animator.SetInteger("Stance",stance);animator.SetBool("IsMoving",moving);Step(animator,seconds);
        Require(animator.GetCurrentAnimatorStateInfo(0).IsName(state),"Expected controller state " +state+", got hash " +animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
    }
}
