using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerVisibilitySetup
{
    [MenuItem("Tools/Renne/Apply Visibility and Readability")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Stop Play mode before applying scene settings.");
        var active=EditorSceneManager.GetActiveScene();
        if(active.path!="Assets/Scenes/Test.unity")
        {
            if(active.isDirty)throw new System.InvalidOperationException("Another scene has unsaved changes; nothing was changed.");
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        }
        else if(active.isDirty)
        {
            System.IO.Directory.CreateDirectory("Artifacts/Visibility");
            EditorSceneManager.SaveScene(active,"Artifacts/Visibility/BeforeVisibilityChanges.unity",true);
        }
        var camera=Camera.main;
        var controller=new SerializedObject(camera.GetComponent<IsometricCameraController>());
        controller.FindProperty("lockAboveTarget").boolValue=false;
        controller.FindProperty("offset").vector3Value=new Vector3(2f,5.5f,-4.8f);
        controller.FindProperty("angleX").floatValue=45f;
        controller.FindProperty("angleY").floatValue=-15f;
        controller.FindProperty("allowPan").boolValue=false;
        controller.FindProperty("allowZoom").boolValue=false;
        controller.ApplyModifiedPropertiesWithoutUndo();
        var player=GameObject.FindGameObjectWithTag("Player");
        if(player!=null)camera.transform.SetPositionAndRotation(player.transform.position+new Vector3(2,5.5f,-4.8f),Quaternion.Euler(45,-15,0));
        var fog=camera.GetComponent<PlayerLightFog>();
        var settings=new SerializedObject(fog);
        settings.FindProperty("lightHeight").floatValue=4.2f;
        settings.FindProperty("intensity").floatValue=10f;
        settings.FindProperty("indoorFill").colorValue=new Color(.28f,.34f,.43f);
        settings.FindProperty("shadowStrength").floatValue=.7f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        if(camera.GetComponent<PlayerSceneVisibility>()==null)camera.gameObject.AddComponent<PlayerSceneVisibility>();
        var readability=camera.GetComponent<PlayerReadability>();
        if(readability==null)readability=camera.gameObject.AddComponent<PlayerReadability>();
        const string path="Assets/Render/PlayerReadabilityMarker.mat";
        var marker=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(marker==null)
        {
            marker=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            marker.SetColor("_BaseColor",Color.white);
            AssetDatabase.CreateAsset(marker,path);
        }
        var serialized=new SerializedObject(readability);
        serialized.FindProperty("markerMaterial").objectReferenceValue=marker;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        PlayerLightFogPreview.Render();
    }
}
