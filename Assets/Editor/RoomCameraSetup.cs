using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class RoomCameraSetup
{
    public static void Apply()
    {
        System.IO.Directory.CreateDirectory("Artifacts/Visibility");
        if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Stop Play before room setup");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/Test.unity")throw new System.InvalidOperationException("Open Test scene first");
        if(!System.IO.File.Exists("Artifacts/Visibility/BeforeRoomCamera.unity"))
            EditorSceneManager.SaveScene(scene,"Artifacts/Visibility/BeforeRoomCamera.unity",true);
        var camera=Camera.main;
        var controller=new SerializedObject(camera.GetComponent<IsometricCameraController>());
        controller.FindProperty("lockAboveTarget").boolValue=false;
        controller.FindProperty("offset").vector3Value=new Vector3(2,5.5f,-4.8f);
        controller.FindProperty("angleX").floatValue=45;
        controller.FindProperty("angleY").floatValue=-15;
        controller.FindProperty("allowPan").boolValue=false;
        controller.FindProperty("allowZoom").boolValue=false;
        controller.ApplyModifiedPropertiesWithoutUndo();
        var player=GameObject.FindGameObjectWithTag("Player").transform;
        camera.transform.SetPositionAndRotation(player.position+new Vector3(2,5.5f,-4.8f),Quaternion.Euler(45,-15,0));
        var director=camera.GetComponent<RoomCameraDirector>()??camera.gameObject.AddComponent<RoomCameraDirector>();
        if(camera.GetComponent<MoveEndCameraView>()==null)camera.gameObject.AddComponent<MoveEndCameraView>();
        if(camera.GetComponent<CameraWallDistance>()==null)camera.gameObject.AddComponent<CameraWallDistance>();
        camera.GetComponent<CameraWallDistance>().enabled=false;
        director.enabled=false;
        var occlusion=new SerializedObject(camera.GetComponent<CameraOcclusionHandler>());
        occlusion.FindProperty("lateralSpread").floatValue=.15f;
        occlusion.FindProperty("localFadeRadius").floatValue=1.2f;
        occlusion.FindProperty("releaseDelay").floatValue=.3f;
        occlusion.FindProperty("targetAlpha").floatValue=.15f;
        occlusion.FindProperty("heightMargin").floatValue=.05f;
        occlusion.ApplyModifiedPropertiesWithoutUndo();
        var root=GameObject.Find("Washroom Stall Camera Trial");
        if(root==null)root=new GameObject("Washroom Stall Camera Trial");
        var zone=root.GetComponent<RoomCameraZone>()??root.AddComponent<RoomCameraZone>();
        zone.room=new Bounds(new Vector3(4.39f,.3f,5.99f),new Vector3(2.56f,4f,1.38f));
        zone.distanceScale=1f;
        if(zone.visibilityCurtain==null)
        {
            const string path="Assets/Render/RoomVisibilityCurtain.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){ material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",Color.black);AssetDatabase.CreateAsset(material,path); }
            zone.visibilityCurtain=new GameObject("Opaque upper wall visibility curtains");
            zone.visibilityCurtain.transform.SetParent(root.transform);
            var b=zone.room;
            // Extend existing solid partitions visually upwards; no colliders or gameplay changes.
            Curtain(zone,new Vector3(b.min.x,12.5f,b.center.z),new Vector3(.04f,20,b.size.z),material);
            Curtain(zone,new Vector3(b.max.x,12.5f,b.center.z),new Vector3(.04f,20,b.size.z),material);
            Curtain(zone,new Vector3(b.center.x,12.5f,b.min.z),new Vector3(b.size.x,20,.04f),material);
            Curtain(zone,new Vector3(b.center.x,12.5f,b.max.z),new Vector3(b.size.x,20,.04f),material);
        }
        zone.visibilityCurtain.SetActive(false);
        director.zones=new[]{zone};
        EditorUtility.SetDirty(director);EditorUtility.SetDirty(zone);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        RoomCameraValidation.Validate();
    }
    static void Curtain(RoomCameraZone zone,Vector3 position,Vector3 size,Material material)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name="Upper wall mask";
        Object.DestroyImmediate(obj.GetComponent<Collider>());obj.layer=2;
        obj.transform.SetParent(zone.visibilityCurtain.transform);obj.transform.position=position;obj.transform.localScale=size;
        var renderer=obj.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
}
