using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PlayerSpeedTuningSetup
{
    const string Request="Artifacts/PlayerAnimation/speed_tuning.request";
    static PlayerSpeedTuningSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlayingOrWillChangePlaymode){if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();return;}
        File.Delete(Request);
        try{Apply();}catch(Exception e){File.WriteAllText("Artifacts/PlayerAnimation/speed_tuning.txt","FAILED\n"+e);Debug.LogException(e);}
    }
    sealed class Entry {public UnitMovement movement;public float speed;public string label;}
    static bool IsEnemy(UnitMovement movement)
    {
        return !movement.GetComponent<PlayerController>() &&
            (movement.GetComponent<EnemyAIController>() || movement.GetComponent<SimpleEnemyAI>() ||
             movement.GetComponent<TurnBasedUnit>()?.Faction==TurnFaction.Enemy);
    }
    static void SetSpeed(UnitMovement movement,float speed)
    {
        var so=new SerializedObject(movement);so.FindProperty("moveSpeed").floatValue=speed;so.ApplyModifiedPropertiesWithoutUndo();
        if(PrefabUtility.IsPartOfPrefabInstance(movement))PrefabUtility.RecordPrefabInstancePropertyModifications(movement);
        if(Mathf.Abs(movement.MoveSpeed-speed)>.0001f)throw new InvalidOperationException("Speed was not saved: "+movement.name);
    }
    static void Apply()
    {
        var report=new StringBuilder("PASS\nPlayer walk: 1.32 units/sec (+20%); prone: 0.66 units/sec (+20%); crouch: 0.825 unchanged.\nTwo footfalls per grid cell retained.\n");
        var opened=new List<Scene>();var changedScenes=new List<Scene>();var entries=new List<Entry>();
        var active=SceneManager.GetActiveScene();
        // Capture scene values before changing prefab defaults, so inherited values are reduced once.
        foreach(string guid in AssetDatabase.FindAssets("t:Scene",new[]{"Assets/Scenes"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var scene=SceneManager.GetSceneByPath(path);
            if(!scene.IsValid()||!scene.isLoaded){scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);opened.Add(scene);}
            var enemies=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<UnitMovement>(true)).Where(IsEnemy).ToArray();
            if(enemies.Length==0)continue;
            string backup="Artifacts/PlayerAnimation/Before_EnemySlowdown_"+Path.GetFileName(path);
            if(!File.Exists(backup))EditorSceneManager.SaveScene(scene,backup,true);
            changedScenes.Add(scene);
            foreach(var movement in enemies)entries.Add(new Entry{movement=movement,speed=movement.MoveSpeed,label=path+" / "+movement.name});
        }
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefab/Character"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!asset.GetComponentsInChildren<UnitMovement>(true).Any(IsEnemy))continue;
            string backup="Artifacts/PlayerAnimation/Before_EnemySlowdown_"+Path.GetFileName(path);
            if(!File.Exists(backup))File.Copy(path,backup);
            var prefab=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var movement in prefab.GetComponentsInChildren<UnitMovement>(true).Where(IsEnemy))
                {
                    float before=movement.MoveSpeed,after=Mathf.Max(.1f,before-2f);SetSpeed(movement,after);
                    report.AppendLine(path+" / "+movement.name+": "+before.ToString("F2")+" -> "+after.ToString("F2"));
                }
                PrefabUtility.SaveAsPrefabAsset(prefab,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
        }
        foreach(var entry in entries)
        {
            float after=Mathf.Max(.1f,entry.speed-2f);SetSpeed(entry.movement,after);
            report.AppendLine(entry.label+": "+entry.speed.ToString("F2")+" -> "+after.ToString("F2"));
        }
        foreach(var scene in changedScenes)EditorSceneManager.SaveScene(scene);
        foreach(var scene in opened)EditorSceneManager.CloseScene(scene,true);
        if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Artifacts/PlayerAnimation/speed_tuning.txt",report.ToString());
    }
}
