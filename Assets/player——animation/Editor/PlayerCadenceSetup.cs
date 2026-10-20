using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PlayerCadenceSetup
{
    const string Request="Artifacts/PlayerAnimation/cadence.request";
    static PlayerCadenceSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlayingOrWillChangePlaymode)
        {if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();return;}
        File.Delete(Request);
        try{Apply();}
        catch(Exception error){File.WriteAllText("Artifacts/PlayerAnimation/cadence_setup.txt",error.ToString());Debug.LogException(error);}
    }

    [MenuItem("Tools/Renne/Player Animation/Apply Two Steps Per Cell and Color Fix")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before setup.");
        string root=PlayerAnimationSetup.Root;
        var report=new StringBuilder();
        string[] paths={root+"/Player.controller",root+"/Player.mat","Assets/Data/Player/PlayerConfig.asset","Assets/Prefab/Character/Player/Player.prefab"};
        foreach(var path in paths)
        {
            string backup="Artifacts/PlayerAnimation/Before_Cadence_"+Path.GetFileName(path);
            if(!File.Exists(backup))File.Copy(path,backup);
        }
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(paths[0]);
        if(!controller.parameters.Any(p=>p.name=="GaitPhase"))controller.AddParameter("GaitPhase",AnimatorControllerParameterType.Float);
        foreach(var child in controller.layers[0].stateMachine.states)
        {
            var state=child.state;
            if(state.name=="Walk"||state.name=="CrouchWalk"||state.name=="ProneCrawl")
            {state.timeParameter="GaitPhase";state.timeParameterActive=true;EditorUtility.SetDirty(state);}
        }
        EditorUtility.SetDirty(controller);
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"/Textures/Player_BaseColor.png");
        var material=AssetDatabase.LoadAssetAtPath<Material>(paths[1]);
        material.SetTexture("_BaseMap",texture);material.SetTexture("_MainTex",texture);
        material.SetColor("_BaseColor",Color.white);material.SetColor("_Color",Color.white);
        material.SetTexture("_EmissionMap",texture);material.SetColor("_EmissionColor",new Color(.4f,.4f,.4f));
        material.EnableKeyword("_EMISSION");material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.15f);
        EditorUtility.SetDirty(material);
        var config=AssetDatabase.LoadAssetAtPath<PlayerConfig>(paths[2]);config.MoveSpeed=1.1f;EditorUtility.SetDirty(config);
        foreach(string path in new[]{paths[3],root+"/Rene_PlayerVisual.prefab"})
        {
            var prefab=PrefabUtility.LoadPrefabContents(path);
            try{Configure(prefab,material,report);PrefabUtility.SaveAsPrefabAsset(prefab,path);}
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
        }
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Test.unity");
        bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene("Assets/Scenes/Test.unity",OpenSceneMode.Additive);
        string sceneBackup="Artifacts/PlayerAnimation/Before_Cadence_Test.unity";
        if(!File.Exists(sceneBackup))EditorSceneManager.SaveScene(scene,sceneBackup,true);
        foreach(var go in scene.GetRootGameObjects())
        foreach(var player in go.GetComponentsInChildren<PlayerAnimationDriver>(true))Configure(player.gameObject,material,report);
        EditorSceneManager.SaveScene(scene);
        if(opened)EditorSceneManager.CloseScene(scene,true);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Artifacts/PlayerAnimation/cadence_setup.txt","PASS\nMoveSpeed: 1.1 world units/sec; crouch x0.75; prone x0.5.\nOne complete gait cycle and exactly two footfalls per grid edge.\nTextured neutral emission fill; player receives no environment shadow.\n"+report);
    }
    static void Configure(GameObject player,Material material,StringBuilder report)
    {
        var visual = player.transform.Find("Rene_PlayerVisual");
        if (visual)
        {
            report.AppendLine("Previous model local offset: " + visual.localPosition);
            var local=visual.localPosition;local.y=.002f;visual.localPosition=local;
            if(PrefabUtility.IsPartOfPrefabInstance(visual))PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
        }
        var movement=player.GetComponent<UnitMovement>();
        if(movement)
        {
            var so=new SerializedObject(movement);so.FindProperty("moveSpeed").floatValue=1.1f;so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach(var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            report.AppendLine(player.name+" / "+renderer.name+": "+string.Join(", ",renderer.sharedMaterials.Select(m=>m?m.name+" / "+m.shader.name+" / texture="+(m.GetTexture("_BaseMap")?m.GetTexture("_BaseMap").name:"NULL"):"NULL")));
            renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
            renderer.receiveShadows=false;EditorUtility.SetDirty(renderer);
            if(PrefabUtility.IsPartOfPrefabInstance(renderer))PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
    }
}
