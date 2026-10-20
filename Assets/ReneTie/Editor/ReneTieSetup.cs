using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Renne.CharacterEffects;

public static class ReneTieSetup
{
    [MenuItem("Tools/Renne/Create tie physics prefab")]
    public static void CreatePrefab()
    {
        const string path = "Assets/ReneTie/Rene_Player_TieUnity.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.optimizeGameObjects = false;
        importer.isReadable = true;
        importer.importAnimation = false;
        importer.SaveAndReimport();
        if (!AssetDatabase.IsValidFolder("Assets/ReneTie/Textures"))
            AssetDatabase.CreateFolder("Assets/ReneTie", "Textures");
        importer.ExtractTextures("Assets/ReneTie/Textures");
        AssetDatabase.Refresh();
        var obj = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        try
        {
            obj.name = "Rene_Player_TiePhysics";
            var sim = obj.AddComponent<ReneTieDynamics>();
            sim.FindBones();
            if (!sim.Initialize()) throw new Exception("Missing exported tie chain");
            var texture = AssetDatabase.FindAssets("t:Texture2D", new[]{"Assets/ReneTie/Textures"})
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();
            const string materialPath = "Assets/ReneTie/ReneTieCharacter.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!mat)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) shader = Shader.Find("Standard");
                mat = new Material(shader);
                mat.mainTexture = texture;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
                AssetDatabase.CreateAsset(mat, materialPath);
            }
            foreach (var renderer in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var bounds = renderer.localBounds; bounds.Expand(.4f); renderer.localBounds = bounds;
                renderer.sharedMaterial = mat;
            }
            PrefabUtility.SaveAsPrefabAsset(obj,"Assets/ReneTie/Rene_Player_TiePhysics.prefab");
            AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); }
    }

}
