using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ColdSceneLightingSetup
{
    // Explicit setup command, never runs automatically when importing scripts.
    public static void Apply()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        if (GameObject.Find("Cold Environment Guide Lights") == null)
        {
            var root = new GameObject("Cold Environment Guide Lights");
            Directory.CreateDirectory("Assets/Render/GuideLights");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.08f, 0.22f, 0.3f));
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.12f, 0.8f, 1.1f));
            AssetDatabase.CreateAsset(material, "Assets/Render/GuideLights/ColdGuidePanel.mat");
            Create(root.transform, "Washroom Entry Guide", new Vector3(5.19f, 1.9f, 4.42f),
                new Color(0.25f, 0.85f, 1f), 0.9f, 2.4f, material);
            Create(root.transform, "Washbasin Fault Light", new Vector3(0.2f, 1.9f, 11.8f),
                new Color(0.45f, 0.65f, 1f), 0.7f, 2f, material);
            Create(root.transform, "Corridor Guide", new Vector3(6.7f, 1.9f, 7.8f),
                new Color(0.2f, 0.8f, 0.85f), 0.65f, 2.2f, material);
        }
        // Anchor the entry marker to the existing door frame.
        GameObject.Find("Washroom Entry Guide").transform.position = new Vector3(5.19f, 1.9f, 4.42f);
        foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            if (renderer.name.ToLowerInvariant().Contains("door"))
                Debug.Log($"GUIDE_ANCHOR {renderer.name} {renderer.bounds.center} {renderer.bounds.size}");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        PlayerLightFogPreview.Render();
    }

    private static void Create(Transform root, string name, Vector3 position,
        Color color, float intensity, float range, Material panelMaterial)
    {
        var lamp = new GameObject(name);
        lamp.transform.SetParent(root);
        lamp.transform.position = position;
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.Soft;
        light.shadowBias = 0.02f;
        light.shadowNormalBias = 0.1f;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        lamp.AddComponent<FogGuideLight>();
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Guide Light Strip";
        panel.transform.SetParent(lamp.transform, false);
        panel.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        panel.transform.localScale = new Vector3(0.28f, 0.055f, 0.045f);
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.GetComponent<Renderer>().sharedMaterial = panelMaterial;
        panel.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
