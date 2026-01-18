using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(FogMeshBuilder))]
[RequireComponent(typeof(FogOfWarRenderer))]
public class FogOfWarManager : MonoBehaviour
{
    public PlayerVision visionSource;
    public int rayCount = 300;

    private FogMeshBuilder meshBuilder;
    private FogOfWarRenderer renderer;

    private float updateInterval = 0.05f;
    private float lastUpdate;

    void Awake()
    {
        meshBuilder = GetComponent<FogMeshBuilder>();
        renderer = GetComponent<FogOfWarRenderer>();
    }

    void Update()
    {
        if (visionSource == null) return;

        if (Time.time - lastUpdate > updateInterval)
        {
            lastUpdate = Time.time;
            UpdateFogMesh();
        }
    }

    void UpdateFogMesh()
    {
        List<Vector3> visiblePts = visionSource.GetVisiblePoints(rayCount);
        Mesh fogMesh = meshBuilder.BuildMesh(visiblePts, visionSource.EyePos);

        renderer.ApplyMesh(fogMesh);
    }
}
