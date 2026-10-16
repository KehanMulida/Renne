using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerVisibilityValidation
{
    public static void Validate()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();
        GameObject target=null,wall=null,lamp=null;
        PlayerSceneVisibility service=null;
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            var player=GameObject.FindGameObjectWithTag("Player").transform;
            // The scene now uses authored room presets, verified separately by RoomCameraValidation.
            ValidateLighting(player);
            player.position=new Vector3(100,.11f,100);
            service=Camera.main.GetComponent<PlayerSceneVisibility>();
            Require(service!=null,"Visibility service not attached");
            target=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            target.transform.position=player.position+Vector3.right*2;
            target.AddComponent<WorldItem>();
            Invoke(service,"OnEnable");Physics.SyncTransforms();
            Require(service.IsVisible(target.transform),"Open nearby target hidden");
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position=player.position+new Vector3(1,.7f,0);
            wall.transform.localScale=new Vector3(.15f,2f,3f);
            Physics.SyncTransforms();service.Refresh();
            Require(!PlayerSceneVisibility.CanReveal(target.transform),"Solid wall leaked target/UI");
            Require(target.GetComponent<Renderer>().forceRenderingOff,"Hidden mesh still rendered");
            Require(target.activeInHierarchy && target.GetComponent<Collider>().enabled,"Visibility changed collision");
            lamp=new GameObject("Visibility Test Bright Light");lamp.AddComponent<Light>().intensity=100f;
            Require(!service.IsVisible(target.transform),"Environmental light revealed hidden target");
            UnityEngine.Object.DestroyImmediate(wall.GetComponent<Collider>());
            Invoke(service,"Rescan");service.Refresh();
            Require(!service.IsVisible(target.transform),"Wall without collider leaked target");
            wall.SetActive(false);service.Refresh();
            Require(service.IsVisible(target.transform) && !target.GetComponent<Renderer>().forceRenderingOff,"Reveal did not restore target");
            target.transform.position=player.position+Vector3.right*20;
            Require(!service.IsVisible(target.transform),"Out of range target visible");
            target.transform.position=player.position+Vector3.up*3;
            Require(!service.IsVisible(target.transform),"Other floor target visible");
            Invoke(service,"OnDisable");
            Require(!target.GetComponent<Renderer>().forceRenderingOff,"Disable failed to restore state");
            Debug.Log("PLAYER_VISIBILITY_VALIDATION_OK: walls, missing colliders, hidden mesh/UI, range, floors, lights, restore; collision unchanged");
        }
        finally
        {
            if(service!=null)Invoke(service,"OnDisable");
            if(target!=null)UnityEngine.Object.DestroyImmediate(target);
            if(wall!=null)UnityEngine.Object.DestroyImmediate(wall);
            if(lamp!=null)UnityEngine.Object.DestroyImmediate(lamp);
            if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }
    private static void ValidateLighting(Transform player)
    {
        var camera=Camera.main;
        var fog=camera.GetComponent<PlayerLightFog>();
        var readability=camera.GetComponent<PlayerReadability>();
        var before=RenderSettings.ambientProbe;
        Quaternion originalRotation=camera.transform.rotation;
        Invoke(fog,"OnEnable");Invoke(readability,"OnEnable");
        try
        {
            var colors=new Color[6];
            RenderSettings.ambientProbe.Evaluate(new[] {Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back},colors);
            for(int i=1;i<colors.Length;i++)
                Require(Mathf.Abs(colors[i].r-colors[0].r)<.001f && Mathf.Abs(colors[i].b-colors[0].b)<.001f,
                    "Indoor diffuse fill depends on direction");
            var fills=System.Array.FindAll(camera.GetComponentsInChildren<Light>(),light=>light.type==LightType.Point);
            Require(fills.Length==0,"Side fills should be removed in overhead mode");
            var top=System.Array.Find(camera.GetComponentsInChildren<Light>(),light=>light.type==LightType.Spot);
            Require(top!=null,"Overhead light missing");
            camera.transform.rotation*=Quaternion.Euler(0f,90f,0f);
            Invoke(fog,"LateUpdate");
            Require(Vector3.Dot(top.transform.forward,Vector3.down)>.999f,"Camera rotation turned overhead light sideways");
            Require(Mathf.Abs(top.transform.position.x-player.position.x)<.001f &&
                Mathf.Abs(top.transform.position.z-player.position.z)<.001f,"Light is not above player");
        }
        finally
        {
            camera.transform.rotation=originalRotation;
            Invoke(readability,"OnDisable");Invoke(fog,"OnDisable");
        }
        var restored=RenderSettings.ambientProbe;
        for(int channel=0;channel<3;channel++)for(int coefficient=0;coefficient<9;coefficient++)
            Require(Mathf.Abs(before[channel,coefficient]-restored[channel,coefficient])<.001f,"Ambient lighting did not restore");
        Debug.Log("OVERHEAD_LIGHTING_VALIDATION_OK: uniform indoor diffuse, no side fills, centered downward light, rotation independence, restore");
    }

    private static void ValidateOverheadCamera(Transform player)
    {
        var camera=Camera.main;
        var controller=camera.GetComponent<IsometricCameraController>();
        Vector3 originalPosition=player.position;
        GameObject ceiling=null;
        try
        {
            player.position=new Vector3(100,.11f,100);
            ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name="Temporary Camera Ceiling Check";
            ceiling.transform.position=player.position+Vector3.up*3f;
            ceiling.transform.localScale=new Vector3(4f,.2f,4f);
            Physics.SyncTransforms();Invoke(controller,"Start");controller.SnapToTarget();
            Require(Mathf.Abs(camera.transform.position.x-player.position.x)<.001f &&
                Mathf.Abs(camera.transform.position.z-player.position.z)<.001f,"Camera is not directly overhead");
            Require(Vector3.Dot(camera.transform.forward,Vector3.down)>.999f,"Camera does not look vertically down");
            Require(camera.transform.position.y<ceiling.GetComponent<Collider>().bounds.min.y-.1f,"Camera crossed ceiling");
            UnityEngine.Object.DestroyImmediate(ceiling.GetComponent<Collider>());
            controller.SnapToTarget();
            Require(camera.transform.position.y<ceiling.GetComponent<Renderer>().bounds.min.y-.1f,"Camera crossed mesh-only ceiling");
            ceiling.SetActive(false);controller.SnapToTarget();
            Require(Mathf.Abs(camera.transform.position.y-player.position.y-6.5f)<.001f,"Camera height failed to recover");
            player.position+=new Vector3(2f,0f,-2f);Invoke(controller,"LateUpdate");
            Require(Mathf.Abs(camera.transform.position.x-player.position.x)<.001f &&
                Mathf.Abs(camera.transform.position.z-player.position.z)<.001f,"Camera drifted across a wall while following");
            Debug.Log("OVERHEAD_CAMERA_VALIDATION_OK: centered lock, fixed downward view, collider/mesh ceiling clearance, height recovery, no lateral lag");
        }
        finally
        {
            if(ceiling!=null)UnityEngine.Object.DestroyImmediate(ceiling);
            player.position=originalPosition;controller.SnapToTarget();
        }
    }
    private static void Invoke(MonoBehaviour target,string method) =>
        target.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,null);
    private static void Require(bool result,string message){if(!result)throw new InvalidOperationException(message);}
}
