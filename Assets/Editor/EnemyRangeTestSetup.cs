using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;

public static class EnemyRangeTestSetup
{
    [MenuItem("Tools/Renne/Apply Enemy Range Test")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before applying enemy range test.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/Test.unity")throw new InvalidOperationException("Open Test scene first.");
        Directory.CreateDirectory("Artifacts/EnemyRangeTest");
        const string backup="Artifacts/EnemyRangeTest/BeforeRangeTest.unity";
        if(!File.Exists(backup))EditorSceneManager.SaveScene(scene,backup,true);
        if(!AssetDatabase.IsValidFolder("Assets/AI/Testing"))AssetDatabase.CreateFolder("Assets/AI","Testing");
        var lines=new List<string>{"# 敌人范围测试配置","","只应用于 Test 场景，原始 EnemyConfig 资源不改。测试配置位于 Assets/AI/Testing。",""};
        int enemies=0;
        foreach(var ai in UnityEngine.Object.FindObjectsOfType<EnemyAIController>(true))
        {
            if(ai.config==null)continue;
            var source=ai.config;
            string originalPath=AssetDatabase.GetAssetPath(source);
            if(originalPath.Contains("/Testing/"))
            {
                originalPath="Assets/AI/"+source.name.Replace("_RangeTest","")+".asset";
                source=AssetDatabase.LoadAssetAtPath<EnemyConfig>(originalPath);
                if(source==null)throw new InvalidOperationException("Original enemy config missing: "+originalPath);
            }
            string path="Assets/AI/Testing/"+source.name+"_RangeTest.asset";
            var test=AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
            if(test==null){test=UnityEngine.Object.Instantiate(source);test.name=source.name+"_RangeTest";AssetDatabase.CreateAsset(test,path);}
            test.visionRange=5f;
            EditorUtility.SetDirty(test);ai.config=test;EditorUtility.SetDirty(ai);
            var emitter=ai.GetComponent<SoundEmitter>();
            float oldRadius=0;
            if(emitter!=null)
            {
                var settings=new SerializedObject(emitter);
                oldRadius=settings.FindProperty("movementSoundRadius").floatValue;
                settings.FindProperty("movementSoundRadius").floatValue=18;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            lines.Add($"- {ai.name}：视距 {source.visionRange} → {test.visionRange}；视角仍为 {source.visionAngle}°；脚步声 {oldRadius} → 18。");
            enemies++;
        }
        if(enemies==0)throw new InvalidOperationException("No enemy AI found.");
        var manager=UnityEngine.Object.FindObjectOfType<DialogueManager>();
        if(manager==null)throw new InvalidOperationException("DialogueManager missing.");
        var dialogue=new SerializedObject(manager);
        dialogue.FindProperty("audibleRangeMultiplier").floatValue=3;
        dialogue.ApplyModifiedPropertiesWithoutUndo();
        var player=GameObject.FindGameObjectWithTag("Player");
        var ears=player!=null?player.GetComponent<SoundPerception>():null;
        if(ears==null)throw new InvalidOperationException("Player hearing component missing.");
        float oldHearing=ears.HearingRange;ears.HearingRange=20;EditorUtility.SetDirty(ears);
        lines.Add($"\n对话最远可察觉半径 ×3；清晰与关键词距离不变，墙体衰减和楼层限制继续生效。玩家听觉上限 {oldHearing} → 20；实际能听到的距离仍受玩家听觉上限限制。");
        lines.Add("\n测试后可把敌人的 config 换回 Assets/AI 下的原配置，脚步声按上方记录恢复，DialogueManager 的 Audible Range Multiplier 改回 1，玩家 SoundPerception 的 Hearing Range 按上方恢复。");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        // Verify the actual cached dialogue data uses the new radius, not just an inspector value.
        var raw=Resources.LoadAll<TextAsset>("Dialogues");
        int checkedCount=0;
        foreach(var asset in raw)
        {
            var original=JsonUtility.FromJson<DialogueData>(asset.text);
            if(original==null || original.lines==null || original.lines.Count==0)continue;
            var loaded=manager.Load(asset.name);
            if(loaded==null || Mathf.Abs(loaded.audibleRadius-Mathf.Max(original.keywordRadius,original.audibleRadius*3))>.001f ||
                loaded.clearRadius!=original.clearRadius || loaded.keywordRadius!=original.keywordRadius)
                throw new InvalidOperationException("Dialogue range check failed: "+asset.name);
            checkedCount++;
        }
        lines.Add($"\n验证通过：{enemies} 个敌人、{checkedCount} 段对话；场景与资源已保存。");
        Directory.CreateDirectory("docs");File.WriteAllLines("docs/敌人感知与发声测试记录.md",lines);
        File.WriteAllText("Artifacts/EnemyRangeTest/validation.txt",$"PASS: {enemies} enemy configs assigned; footsteps 18; {checkedCount} dialogue radii tripled with clarity unchanged; player hearing 20; source enemy assets retained.");
    }
}

