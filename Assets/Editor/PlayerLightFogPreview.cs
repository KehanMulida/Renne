using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>可重复生成当前关卡的灯光前后对照，不保存临时场景修改。</summary>
public static class PlayerLightFogPreview
{
    [MenuItem("Tools/Renne/Render Light Fog Preview")]
    public static void Render()
    {
        if (EditorApplication.isPlaying ||
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            var floors = Object.FindObjectOfType<FloorManager>();
            var visibility = Object.FindObjectOfType<FloorVisibilityController>();
            if (floors != null) Invoke(floors, "Awake");
            if (visibility != null)
            {
                Invoke(visibility, "Awake");
                Invoke(visibility, "Start");
            }
            var camera = Camera.main;
            if (camera == null) throw new System.InvalidOperationException("Main camera missing");
            Invoke(camera.GetComponent<IsometricCameraController>(), "Start");
            var occlusion = camera.GetComponent<CameraOcclusionHandler>();
            if (occlusion != null) Invoke(occlusion, "Awake");
            Directory.CreateDirectory("Artifacts/LightFog");
            Capture(camera, "Artifacts/LightFog/before.png");
            var oldPosition = camera.transform.position;
            var oldRotation = camera.transform.rotation;
            camera.transform.SetPositionAndRotation(new Vector3(7f, 30f, 7f), Quaternion.Euler(90f, 0f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 13f;
            Capture(camera, "Artifacts/LightFog/layout.png");
            camera.orthographic = false;
            camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
            var fog = camera.GetComponent<PlayerLightFog>();
            if (fog == null) throw new System.InvalidOperationException("Light fog missing");
            Invoke(fog, "OnEnable");
            var visualBoundary=camera.GetComponent<PlayerSceneVisibility>();
            var readability=camera.GetComponent<PlayerReadability>();
            if(visualBoundary!=null)Invoke(visualBoundary,"OnEnable");
            if(readability!=null)Invoke(readability,"OnEnable");
            try
            {
                Physics.SyncTransforms();
                if (occlusion != null)
                {
                    Invoke(occlusion, "LateUpdate");
                    // Settle the transition deterministically outside Play mode.
                    var entries = (System.Collections.IDictionary)typeof(CameraOcclusionHandler)
                        .GetField("_faded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .GetValue(occlusion);
                    foreach (var entry in entries.Values)
                        entry.GetType().GetField("alpha").SetValue(entry, 0.08f);
                    Invoke(occlusion, "LateUpdate");
                    Debug.Log($"LOCAL_OCCLUSION_RENDERERS: {entries.Count}");
                }
                Capture(camera, "Artifacts/LightFog/after.png");
                var player = GameObject.FindGameObjectWithTag("Player").transform;
                var light = camera.GetComponentInChildren<Light>();
                var originalRotation = camera.transform.rotation;
                var originalPosition = player.position;
                player.position += new Vector3(2f, 0f, 0f);
                Invoke(camera.GetComponent<IsometricCameraController>(), "LateUpdate");
                Invoke(fog, "LateUpdate");
                if(visualBoundary!=null)visualBoundary.Refresh();
                if(readability!=null)Invoke(readability,"LateUpdate");
                if (Quaternion.Angle(originalRotation, camera.transform.rotation) > 0.001f ||
                    Mathf.Abs(light.transform.position.x - player.position.x) > 0.001f)
                    throw new System.InvalidOperationException("Camera rotation or light follow check failed");
                camera.GetComponent<IsometricCameraController>().SnapToTarget();
                Physics.SyncTransforms();
                if (occlusion != null) SettleOcclusion(occlusion);
                Capture(camera, "Artifacts/LightFog/moved.png");
                player.position = originalPosition;
                camera.GetComponent<IsometricCameraController>().SnapToTarget();
                Invoke(fog, "LateUpdate");
                if (GameObject.Find("Washroom Environmental Details") != null)
                {
                    player.position = new Vector3(1.65f, originalPosition.y, 4.1f);
                    camera.GetComponent<IsometricCameraController>().SnapToTarget();
                    Invoke(fog, "LateUpdate");
                    if(visualBoundary!=null)visualBoundary.Refresh();
                    if(readability!=null)Invoke(readability,"LateUpdate");
                    Capture(camera, "Artifacts/LightFog/washroom-details.png");
                    player.position = originalPosition;
                    camera.GetComponent<IsometricCameraController>().SnapToTarget();
                    Invoke(fog, "LateUpdate");
                }
                if (occlusion != null) ValidateLocalOcclusion(camera, occlusion, player);
                Debug.Log("LIGHT_FOG_VALIDATION_OK: render, fixed camera rotation, player light follow");
            }
            finally
            {
                if(readability!=null)Invoke(readability,"OnDisable");
                if(visualBoundary!=null)Invoke(visualBoundary,"OnDisable");
                if (occlusion != null) Invoke(occlusion, "OnDisable");
                Invoke(fog, "OnDisable");
            }
        }
        finally
        {
            if (previousSetup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }

    private static void Invoke(MonoBehaviour component, string method)
    {
        component.GetType().GetMethod(method,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(component, null);
    }

    private static void SettleOcclusion(CameraOcclusionHandler occlusion)
    {
        Invoke(occlusion,"LateUpdate");
        var entries=(System.Collections.IDictionary)typeof(CameraOcclusionHandler)
            .GetField("_faded",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(occlusion);
        foreach(var entry in entries.Values)entry.GetType().GetField("alpha").SetValue(entry,.08f);
        Invoke(occlusion,"LateUpdate");
    }

    private static void ValidateLocalOcclusion(Camera camera, CameraOcclusionHandler occlusion, Transform player)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Temporary Occlusion Regression Wall";
        var original = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var renderer = wall.GetComponent<Renderer>();
        renderer.sharedMaterial = original;
        var center = player.position + Vector3.up * 0.85f;
        wall.transform.position = Vector3.Lerp(center, camera.transform.position, 0.3f);
        wall.transform.rotation = Quaternion.LookRotation(camera.transform.position - center);
        wall.transform.localScale = new Vector3(4f, 3.5f, 0.1f);
        try
        {
            Physics.SyncTransforms();
            Invoke(occlusion, "LateUpdate");
            if (renderer.sharedMaterial.GetFloat("_Surface") != 1f)
                throw new System.InvalidOperationException("Local occlusion material was not applied");
            renderer.sharedMaterial.SetFloat("_OcclusionOpacity", 0.08f);
            Capture(camera, "Artifacts/LightFog/occlusion-test.png");
            foreach (var message in ShaderUtil.GetShaderMessages(renderer.sharedMaterial.shader))
                if (message.severity.ToString() == "Error")
                    throw new System.InvalidOperationException(message.message);
            Invoke(occlusion, "OnDisable");
            if (renderer.sharedMaterial != original)
                throw new System.InvalidOperationException("Original wall material was not restored");
            Debug.Log("LOCAL_OCCLUSION_VALIDATION_OK: applied, shader rendered, original material restored");
        }
        finally
        {
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(original);
        }
    }

    private static void Capture(Camera camera, string path)
    {
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            camera.targetTexture = target;
            camera.Render(); // Warm reflection/material variants before recording a frame.
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation=asyncCompilation;
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
