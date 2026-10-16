using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Reflection;
using System.IO;
public static class RoomCameraValidation
{
    static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    public static void Validate()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            var camera=Camera.main;
            Require(!camera.GetComponent<CameraWallDistance>().enabled,"Automatic collision zoom still enabled");
            Require(!camera.GetComponent<RoomCameraDirector>().enabled,"Room zoom still enabled");
            var player=GameObject.FindGameObjectWithTag("Player").transform;
            var floors=UnityEngine.Object.FindObjectOfType<FloorManager>();if(floors!=null)Invoke(floors,"Awake");
            var floorVisibility=UnityEngine.Object.FindObjectOfType<FloorVisibilityController>();
            if(floorVisibility!=null){Invoke(floorVisibility,"Awake");Invoke(floorVisibility,"Start");}
            foreach(var slab in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                if(slab.bounds.size.y<.25f && Mathf.Max(slab.bounds.size.x,slab.bounds.size.z)>2f &&
                    slab.bounds.center.y>3f && slab.bounds.center.y<5f)
                    Require(!slab.enabled,"Upper-storey slab still blocks ground-floor camera");
            var controller=camera.GetComponent<IsometricCameraController>();Invoke(controller,"Start");
            var fog=camera.GetComponent<PlayerLightFog>();Invoke(fog,"OnEnable");
            var handler=camera.GetComponent<CameraOcclusionHandler>();Invoke(handler,"Awake");
            try
            {
                ValidateVariant(handler);
                ValidateHeightFilter(handler,player);
                ValidateMoveEndView(camera,player);
                float fov=camera.fieldOfView;
                for(int i=0;i<200;i++)
                {
                    player.position=new Vector3(3.1f+(i%2==0?.12f:-.12f),.11f,6);
                    controller.SnapToTarget();Invoke(controller,"LateUpdate");
                    Require(Vector3.Distance(camera.transform.position-player.position,new Vector3(2,5.5f,-4.8f))<.001f,"Doorway changed camera distance");
                    Require(Quaternion.Angle(camera.transform.rotation,Quaternion.Euler(45,-15,0))<.001f && Mathf.Abs(fov-camera.fieldOfView)<.001f,"Doorway changed angle or FOV");
                }
                player.position=new Vector3(4,.11f,6);controller.SnapToTarget();Settle(handler);Invoke(fog,"LateUpdate");Capture(camera,"inside-stall");
                var report=new System.Collections.Generic.List<string>();
                foreach(var mesh in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                    if(mesh.bounds.IntersectRay(new Ray(camera.transform.position,(player.position-camera.transform.position).normalized),out float d) && d<Vector3.Distance(camera.transform.position,player.position))
                        foreach(var material in mesh.sharedMaterials)if(material!=null)report.Add(mesh.name+" "+material.name+" shader="+material.shader.name+" bounds="+mesh.bounds+" layer="+mesh.gameObject.layer);
                File.WriteAllLines("Artifacts/Visibility/blocking-materials.txt",report);
                player.position=new Vector3(1.88f,.11f,7.76f);controller.SnapToTarget();Settle(handler);Invoke(fog,"LateUpdate");Capture(camera,"foreground-wall");
                File.WriteAllText("Artifacts/Visibility/room-validation.txt","PASS: independent material copied from Material Variant; destination shader applied; source material unchanged; renderer restored; 200 doorway positions without zoom, rotation or FOV changes; local cutaway renders.");
                Debug.Log("LOCAL_TRANSPARENCY_VARIANT_VALIDATION_OK");
            }
            finally{Invoke(handler,"OnDisable");Invoke(fog,"OnDisable");}
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
    static void ValidateMoveEndView(Camera camera,Transform player)
    {
        var view=camera.GetComponent<MoveEndCameraView>();
        Require(view!=null && view.enabled,"Move-end view missing");
        var movement=player.GetComponent<UnitMovement>();
        var original=player.position;
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
        var moveFlag=typeof(UnitMovement).GetField("isMoving",Flags);
        var complete=typeof(UnitMovement).GetField("OnMoveComplete",Flags);
        var offset=new Vector3(2,5.5f,-4.8f);var rotation=Quaternion.Euler(45,-15,0);
        bool wasMoving=(bool)moveFlag.GetValue(movement);
        try
        {
            player.position=new Vector3(100,.11f,100);camera.transform.SetPositionAndRotation(player.position+offset,rotation);
            wall.transform.position=player.position+offset*.4f;
            wall.transform.rotation=Quaternion.LookRotation(offset);
            wall.transform.localScale=new Vector3(3,3,.15f);Physics.SyncTransforms();
            view.Bind(movement);
            int count=view.EvaluationCount;
            moveFlag.SetValue(movement,true);
            ((Action)complete.GetValue(movement))?.Invoke();
            view.Tick(player,offset,rotation,1);
            Require(view.EvaluationCount==count,"Evaluated during movement");
            moveFlag.SetValue(movement,false);view.Tick(player,offset,rotation,1);
            Require(view.EvaluationCount==count+1 && view.IsTopView,"Move completion did not select top view");
            Require(Vector3.Dot(camera.transform.forward,Vector3.down)>.999f,"Top view not vertical");
            wall.SetActive(false);
            for(int i=0;i<200;i++)view.Tick(player,offset,rotation,.016f);
            Require(view.EvaluationCount==count+1 && view.IsTopView,"Idle frames re-evaluated or changed view");
            ((Action)complete.GetValue(movement))?.Invoke();view.Tick(player,offset,rotation,1);
            Require(view.EvaluationCount==count+2 && !view.IsTopView,"Next completed move did not restore view");
            Require(Quaternion.Angle(camera.transform.rotation,rotation)<.001f,"Oblique view not restored");
            File.WriteAllText("Artifacts/Visibility/move-end-view-validation.txt","PASS: actual OnMoveComplete event; no scan during movement; one scan at completion; blocked selects top; 200 idle frames retain view with no scan; next clear completion restores oblique.");
        }
        finally
        {
            moveFlag.SetValue(movement,wasMoving);view.Bind(null);Invoke(view,"OnDisable");
            player.position=original;UnityEngine.Object.DestroyImmediate(wall);camera.GetComponent<IsometricCameraController>().SnapToTarget();
        }
    }
    static void ValidateVariant(CameraOcclusionHandler handler)
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        var original=new Material(shader);original.SetColor("_BaseColor",Color.cyan);
        var variant=new Material(shader);var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
        object entry=null;
        try
        {
            var parent=typeof(Material).GetProperty("parent");
            Require(parent!=null,"Material Variant API unavailable");parent.SetValue(variant,original);
            wall.GetComponent<Renderer>().sharedMaterial=variant;
            entry=typeof(CameraOcclusionHandler).GetMethod("BeginFade",Flags).Invoke(handler,new object[]{wall.GetComponent<Renderer>()});
            var converted=wall.GetComponent<Renderer>().sharedMaterial;
            Require(converted.shader==shader && converted.GetFloat("_Surface")==1f,"Whole-model transparent material missing");
            Require(parent.GetValue(converted)==null,"Converted material is still a variant");
            Require(converted.GetColor("_BaseColor")==variant.GetColor("_BaseColor"),"Variant appearance lost");
            Require(variant.shader==shader && parent.GetValue(variant)==original,"Source variant changed");
            typeof(CameraOcclusionHandler).GetMethod("EndFade",Flags).Invoke(handler,new object[]{wall.GetComponent<Renderer>(),entry});entry=null;
            Require(wall.GetComponent<Renderer>().sharedMaterial==variant,"Source material not restored");
        }
        finally
        {
            if(entry!=null)typeof(CameraOcclusionHandler).GetMethod("EndFade",Flags).Invoke(handler,new object[]{wall.GetComponent<Renderer>(),entry});
            UnityEngine.Object.DestroyImmediate(wall);UnityEngine.Object.DestroyImmediate(variant);UnityEngine.Object.DestroyImmediate(original);
        }
    }
    static void ValidateHeightFilter(CameraOcclusionHandler handler,Transform player)
    {
        var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
        var saved=player.position;
        try
        {
            player.position=new Vector3(100,.11f,100);
            var body=player.GetComponentInChildren<MeshRenderer>().bounds;
            var renderer=obstacle.GetComponent<Renderer>();
            var allowed=typeof(CameraOcclusionHandler).GetMethod("AllowFade",Flags);
            obstacle.transform.localScale=new Vector3(2,body.size.y*.45f,1);
            obstacle.transform.position=new Vector3(101,body.min.y+obstacle.transform.localScale.y*.5f,100);
            Require(!(bool)allowed.Invoke(handler,new object[]{renderer}),"Waist-high furniture faded");
            UnityEngine.Object.DestroyImmediate(obstacle.GetComponent<Collider>());
            Require(!(bool)allowed.Invoke(handler,new object[]{renderer}),"Mesh-only low furniture faded");
            obstacle.transform.position+=Vector3.up*3;
            Require(!(bool)allowed.Invoke(handler,new object[]{renderer}),"Raised short object passed height filter");
            obstacle.transform.localScale=new Vector3(2,body.size.y+1,1);
            obstacle.transform.position=new Vector3(101,body.min.y+obstacle.transform.localScale.y*.5f,100);
            Require((bool)allowed.Invoke(handler,new object[]{renderer}),"Tall wall excluded");
            var begin=typeof(CameraOcclusionHandler).GetMethod("BeginFade",Flags);
            var original=renderer.sharedMaterial;
            object entry=begin.Invoke(handler,new object[]{renderer});
            try
            {
                var faded=renderer.sharedMaterial;
                MaterialFadeUtil.SetAlpha(faded,.15f);
                Require(faded.GetFloat("_Surface")==1f && Mathf.Abs(faded.GetColor("_BaseColor").a-.15f)<.001f,"Whole model alpha incorrect");
                Require(!faded.HasProperty("_OcclusionOpacity"),"Circular portal still used");
            }
            finally{typeof(CameraOcclusionHandler).GetMethod("EndFade",Flags).Invoke(handler,new object[]{renderer,entry});}
            Require(renderer.sharedMaterial==original,"Fade did not restore original material");
            File.WriteAllText("Artifacts/Visibility/tall-model-fade-validation.txt","PASS: waist-high, mesh-only low and raised short models excluded; tall model included; uniform whole-model alpha; no circular portal; source material restored.");
        }
        finally{player.position=saved;UnityEngine.Object.DestroyImmediate(obstacle);}
    }
    static void Settle(CameraOcclusionHandler handler)
    {
        Invoke(handler,"LateUpdate");
        var entries=(System.Collections.IDictionary)typeof(CameraOcclusionHandler).GetField("_faded",Flags).GetValue(handler);
        foreach(var entry in entries.Values)entry.GetType().GetField("alpha").SetValue(entry,.08f);
        Invoke(handler,"LateUpdate");
    }
    static void Invoke(MonoBehaviour target,string method)=>target.GetType().GetMethod(method,Flags).Invoke(target,null);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Capture(Camera camera,string name)
    {
        var previous=camera.targetTexture;var active=RenderTexture.active;
        var target=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        bool async=ShaderUtil.allowAsyncCompilation;
        try{ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes("Artifacts/Visibility/"+name+".png",pixels.EncodeToPNG());}
        finally{ShaderUtil.allowAsyncCompilation=async;camera.targetTexture=previous;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);}
    }
}
