using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PlayerCapsuleVisualSetup
{
    const string Request = "Artifacts/PlayerAnimation/remove_capsule.request";
    static PlayerCapsuleVisualSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception error)
        {
            File.WriteAllText("Artifacts/PlayerAnimation/capsule_cleanup.txt", "FAILED\n" + error);
            Debug.LogException(error);
        }
    }

    public static void RemoveRootVisual(GameObject player)
    {
        var renderer = player.GetComponent<MeshRenderer>();
        if (renderer) UnityEngine.Object.DestroyImmediate(renderer);
        var filter = player.GetComponent<MeshFilter>();
        if (filter) UnityEngine.Object.DestroyImmediate(filter);
    }

    [MenuItem("Tools/Renne/Player Animation/Remove Capsule Visual Only")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before removing the capsule visual.");
        Directory.CreateDirectory("Artifacts/PlayerAnimation");
        const string prefabPath = "Assets/Prefab/Character/Player/Player.prefab";
        const string backup = "Artifacts/PlayerAnimation/Before_CapsuleCleanup_Player.prefab";
        if (!File.Exists(backup)) File.Copy(prefabPath, backup);
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            RemoveRootVisual(prefab);
            if (!prefab.GetComponent<CapsuleCollider>()) throw new InvalidOperationException("Player collision is missing.");
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        var test = SceneManager.GetSceneByPath("Assets/Scenes/Test.unity");
        bool opened = !test.IsValid() || !test.isLoaded;
        if (opened) test = EditorSceneManager.OpenScene("Assets/Scenes/Test.unity", OpenSceneMode.Additive);
        int count = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || string.IsNullOrEmpty(scene.path) || !scene.path.StartsWith("Assets/")) continue;
            bool changed = false;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var player in root.GetComponentsInChildren<PlayerController>(true))
            {
                if (!player.GetComponent<UnitMovement>()) continue;
                string sceneBackup = "Artifacts/PlayerAnimation/Before_CapsuleCleanup_" + Path.GetFileName(scene.path);
                if (!File.Exists(sceneBackup)) EditorSceneManager.SaveScene(scene, sceneBackup, true);
                RemoveRootVisual(player.gameObject);
                if (player.GetComponent<MeshRenderer>() || player.GetComponent<MeshFilter>() ||
                    !player.GetComponent<CapsuleCollider>() || !player.GetComponentInChildren<Animator>())
                    throw new InvalidOperationException("Player visual/collision verification failed: " + player.name);
                count++; changed = true;
            }
            if (changed) EditorSceneManager.SaveScene(scene);
        }
        if (opened) EditorSceneManager.CloseScene(test, true);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Artifacts/PlayerAnimation/capsule_cleanup.txt", "PASS\nScene players: " + count +
            "\nRoot MeshFilter and MeshRenderer removed. CapsuleCollider, character Animator, and gameplay components retained.\nPlayer prefab updated.\n");
        Debug.Log("PLAYER_CAPSULE_VISUAL_REMOVED");
    }
}
