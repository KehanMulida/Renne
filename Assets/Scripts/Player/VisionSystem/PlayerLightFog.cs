using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 以玩家为中心的实时灯光迷雾。暗处不保留探索记录；不改变 AI 的视野判定。
/// 只在运行时接管本场景灯光，停用时恢复原来的环境设置。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public sealed class PlayerLightFog : MonoBehaviour
{
    [Header("玩家视野灯光")]
    [SerializeField] private Transform player;
    [Min(1f)] [SerializeField] private float radius = 9f;
    [Min(0.1f)] [SerializeField] private float lightHeight = 4.2f;
    [Min(0f)] [SerializeField] private float intensity = 10f;
    [SerializeField] private Color lightColor = new Color(0.55f, 0.76f, 1f);
    [Tooltip("参与照明和投影的层。灯光高度应高于玩家模型。")]
    [SerializeField] private LayerMask illuminatedLayers = ~0;
    [Tooltip("视野灯位于角色附近，关闭自身投影以免巨大阴影遮黑角色和脚下。停用后恢复。")]
    [SerializeField] private bool preventPlayerShadow = true;

    [Header("迷雾暗度")]
    [SerializeField] private Color darkness = new Color(0.055f, 0.075f, 0.11f);
    [Tooltip("室内均匀漫反射环境色，让背光地砖与墙面仍可辨认。独立于目标可见性。")]
    [SerializeField] private Color indoorFill = new Color(0.28f, 0.34f, 0.43f);
    [Range(0f, 1f)] [SerializeField] private float shadowStrength = 0.7f;
    [Tooltip("关闭场景原有灯光，避免远处房间继续被照亮。")]
    [SerializeField] private bool suppressSceneLights = true;

    private Light visionLight;
    private readonly List<Light> suppressedLights = new List<Light>();
    private readonly Dictionary<Renderer, ShadowCastingMode> playerShadows =
        new Dictionary<Renderer, ShadowCastingMode>();
    private AmbientMode previousAmbientMode;
    private Color previousAmbientLight;
    private float previousAmbientIntensity;
    private float previousReflectionIntensity;
    private SphericalHarmonicsL2 previousAmbientProbe;
    private Camera viewCamera;
    private CameraClearFlags previousClearFlags;
    private Color previousBackground;
    private bool initialized;
    public float VisionRadius => radius;

    private void OnEnable()
    {
        if (initialized) return;
        if (player == null)
        {
            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null) player = taggedPlayer.transform;
        }
        if (player == null)
        {
            Debug.LogWarning("[PlayerLightFog] 请指定玩家 Transform。", this);
            return;
        }

        previousAmbientMode = RenderSettings.ambientMode;
        previousAmbientLight = RenderSettings.ambientLight;
        previousAmbientIntensity = RenderSettings.ambientIntensity;
        previousReflectionIntensity = RenderSettings.reflectionIntensity;
        previousAmbientProbe = RenderSettings.ambientProbe;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = indoorFill;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0f;
        // URP's indirect diffuse uses SH. Updating only ambientLight can leave stale/dark
        // environment coefficients, so explicitly supply uniform, direction-free light.
        var probe = new SphericalHarmonicsL2();
        probe.AddAmbientLight(indoorFill.linear);
        RenderSettings.ambientProbe = probe;

        if (preventPlayerShadow)
        {
            foreach (var playerRenderer in player.GetComponentsInChildren<Renderer>(true))
            {
                playerShadows.Add(playerRenderer, playerRenderer.shadowCastingMode);
                playerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        if (suppressSceneLights)
        {
            foreach (var sceneLight in FindObjectsOfType<Light>())
            {
                if (sceneLight.gameObject.scene != gameObject.scene || !sceneLight.enabled ||
                    sceneLight.GetComponent<FogGuideLight>() != null) continue;
                suppressedLights.Add(sceneLight);
                sceneLight.enabled = false;
            }
        }

        viewCamera = GetComponent<Camera>();
        if (viewCamera != null)
        {
            previousClearFlags = viewCamera.clearFlags;
            previousBackground = viewCamera.backgroundColor;
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = darkness;
        }

        var lightObject = new GameObject("Player Vision Light (Runtime)");
        lightObject.transform.SetParent(transform, false);
        visionLight = lightObject.AddComponent<Light>();
        visionLight.type = LightType.Spot;
        visionLight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        visionLight.lightmapBakeType = LightmapBakeType.Realtime;
        visionLight.shadows = LightShadows.Soft;
        visionLight.shadowStrength = shadowStrength;
        visionLight.shadowBias = 0.02f;
        visionLight.shadowNormalBias = 0.1f;
        visionLight.renderMode = LightRenderMode.ForcePixel;
        initialized = true;
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (visionLight == null) return;
        visionLight.enabled = player != null;
        if (player == null) return;
        visionLight.transform.position = player.position + Vector3.up * lightHeight;
        // Parent camera rotation must never turn the overhead light sideways.
        visionLight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        visionLight.range = Mathf.Sqrt(radius * radius + lightHeight * lightHeight) + .5f;
        visionLight.spotAngle = Mathf.Rad2Deg * 2f * Mathf.Atan(radius / Mathf.Max(.1f, lightHeight));
        visionLight.innerSpotAngle = visionLight.spotAngle * .65f;
        visionLight.intensity = intensity;
        visionLight.color = lightColor;
        visionLight.cullingMask = illuminatedLayers;
    }

    private void OnDisable()
    {
        if (!initialized) return;
        RenderSettings.ambientMode = previousAmbientMode;
        RenderSettings.ambientLight = previousAmbientLight;
        RenderSettings.ambientIntensity = previousAmbientIntensity;
        RenderSettings.reflectionIntensity = previousReflectionIntensity;
        RenderSettings.ambientProbe = previousAmbientProbe;
        foreach (var sceneLight in suppressedLights)
            if (sceneLight != null) sceneLight.enabled = true;
        suppressedLights.Clear();
        foreach (var entry in playerShadows)
            if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
        playerShadows.Clear();
        if (viewCamera != null)
        {
            viewCamera.clearFlags = previousClearFlags;
            viewCamera.backgroundColor = previousBackground;
        }
        if (visionLight != null)
        {
            visionLight.enabled = false;
            if (Application.isPlaying) Destroy(visionLight.gameObject);
            else DestroyImmediate(visionLight.gameObject);
        }
        initialized = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (player == null) return;
        Gizmos.color = lightColor;
        Gizmos.DrawWireSphere(player.position + Vector3.up * lightHeight, radius);
    }
}
