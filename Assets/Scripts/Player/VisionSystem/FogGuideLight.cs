using UnityEngine;

/// <summary>短距离环境引导灯。PlayerLightFog 保留此标记灯，换楼层后熄灭照明。</summary>
[RequireComponent(typeof(Light))]
public sealed class FogGuideLight : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float mountHeight = 1.9f;
    [Range(0f, 0.15f)] [SerializeField] private float flickerAmount = 0.04f;
    private Light source;
    private float baseIntensity;

    private void OnEnable()
    {
        source = GetComponent<Light>();
        baseIntensity = source.intensity;
        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found.transform;
        }
    }

    private void LateUpdate()
    {
        if (source == null) return;
        bool sameFloor = player == null || FloorManager.Instance == null ||
            FloorManager.Instance.GetFloorFromWorldY(player.position.y) ==
            FloorManager.Instance.GetFloorFromWorldY(transform.position.y - mountHeight);
        float flicker = 1f - flickerAmount * Mathf.PerlinNoise(transform.position.x, Time.time * 1.7f);
        source.intensity = sameFloor ? baseIntensity * flicker : 0f;
    }

    private void OnDisable()
    {
        if (source != null) source.intensity = baseIntensity;
    }
}
