using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EnemyDialogueValidation
{
    public static void Validate()
    {
        var dialogues = new Dictionary<string, DialogueData>();
        int lines = 0, ambient = 0, hooks = 0;
        foreach (string path in Directory.GetFiles("Assets/Resources/Dialogues", "mission_*.json"))
        {
            var data = JsonUtility.FromJson<DialogueData>(File.ReadAllText(path));
            Require(data != null && data.id == Path.GetFileNameWithoutExtension(path), "JSON ID mismatch: " + path);
            Require(!dialogues.ContainsKey(data.id), "Duplicate ID: " + data.id);
            Require(data.lines != null && data.lines.Count == 2, "Expected two lines: " + data.id);
            Require(data.clearRadius <= data.keywordRadius && data.keywordRadius <= data.audibleRadius, "Hearing radius order");
            foreach (var line in data.lines)
                Require(!string.IsNullOrWhiteSpace(line.text) && data.speakers.Contains(line.speaker), "Missing speaker/text: " + data.id);
            if (data.TriggerKind == DialogueTrigger.Ambient)
            {
                ambient++;
                Require(data.missionIds != null && data.missionIds.Count == 1, "Missing task gate: " + data.id);
                Require(data.MatchesMission(data.missionIds[0]), "Matching task was rejected");
                Require(!data.MatchesMission(null) && !data.MatchesMission("unrelated_task"), "Unrelated task was accepted");
            }
            else
            {
                hooks++;
                Require(data.IsRealtime && data.priority == 100, "Task event must use realtime/mission priority");
                Require(data.MatchesMission(null), "Start/settle dialogue must not require a current task");
            }
            lines += data.lines.Count;
            dialogues.Add(data.id, data);
        }

        int boundAssets = 0;
        foreach (var path in Directory.GetFiles("Assets/Data/Mission", "*.asset"))
        {
            var mission = AssetDatabase.LoadAssetAtPath<MissionData>(path.Replace('\\','/'));
            if (mission == null || string.IsNullOrEmpty(mission.missionId)) continue;
            foreach (var key in new[] { mission.dialogueOnStart, mission.dialogueOnSuccess, mission.dialogueOnFail })
            {
                Require(!string.IsNullOrEmpty(key) && dialogues.TryGetValue(key, out _), "Missing hook for " + path);
                foreach (var actor in dialogues[key].speakers)
                    Require(mission.assignedEnemies.Contains(actor), "Task dialogue uses unassigned enemy: " + path);
                Require(Resources.Load<TextAsset>("Dialogues/" + key) != null, "Dialogue not loadable as Resources asset");
            }
            boundAssets++;
        }
        Require(dialogues.Count == 45 && ambient == 18 && hooks == 27 && lines == 90, "Expansion counts mismatch");
        Require(boundAssets == 10, "Expected nine task IDs across ten configured assets");
        Require(new DialogueData().MatchesMission(null), "Existing ambient dialogues changed behavior");
        Require(File.Exists("docs/EnemyAI对白完整记录.md") && File.Exists("docs/enemy_dialogue_catalog.json"), "Catalog files missing");
        ValidateActualTaskSelection();
        Debug.Log($"ENEMY_DIALOGUE_VALIDATION_OK: {dialogues.Count} dialogues, {lines} lines, {ambient} task-gated ambient, {hooks} events, {boundAssets} bound assets");
    }

    private static void ValidateActualTaskSelection()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        GameObject host = null;
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            var ai = Array.Find(UnityEngine.Object.FindObjectsOfType<EnemyAIController>(), enemy => enemy.GetComponent<DialogueSpeaker>() != null);
            Require(ai != null, "Scene has no speaking enemy");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var queue = (List<MissionContext>)typeof(EnemyAIController).GetField("_missionQueue", flags).GetValue(ai);
            host = new GameObject("Temporary Dialogue Task Selection Check");
            var director = host.AddComponent<AmbientChatDirector>();
            var idle = (List<DialogueSpeaker>)typeof(AmbientChatDirector).GetField("_idle", flags).GetValue(director);
            idle.Add(ai.GetComponent<DialogueSpeaker>());
            var method = typeof(AmbientChatDirector).GetMethod("TryFindCast", flags);
            var task = AssetDatabase.LoadAssetAtPath<MissionData>("Assets/Data/Mission/patrol.asset");
            var otherTask = AssetDatabase.LoadAssetAtPath<MissionData>("Assets/Data/Mission/patrol _2.asset");
            var data = new DialogueData { speakers = new List<string> { "A" }, missionIds = new List<string> { task.missionId } };
            queue.Clear();
            Require(!(bool)method.Invoke(director, new object[] { data, null }), "Idle enemy spoke task-specific dialogue");
            queue.Add(new MissionContext(otherTask, ai.EnemyId));
            queue.Add(new MissionContext(task, ai.EnemyId));
            Require(!(bool)method.Invoke(director, new object[] { data, null }), "Queued task incorrectly allowed dialogue");
            queue.Reverse();
            Require((bool)method.Invoke(director, new object[] { data, null }), "Actual current task did not allow dialogue");
            data.missionIds = null;
            queue.Clear();
            Require((bool)method.Invoke(director, new object[] { data, null }), "Existing non-task dialogue stopped working");
            Debug.Log("ENEMY_TASK_CAST_VALIDATION_OK: no task rejected, queued task rejected, current task accepted, legacy dialogue accepted");
        }
        finally
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
