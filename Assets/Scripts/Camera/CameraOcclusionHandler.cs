using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 相机遮挡处理系统（类似僵尸毁灭工程）
/// 职责：
/// 1. 检测玩家和相机之间的遮挡物
/// 2. 自动隐藏或半透明遮挡物
/// 3. 离开后恢复原状
/// 特点：
/// - 实时检测
/// - 平滑过渡
/// - 自动恢复
/// </summary>
public class CameraOcclusionHandler : MonoBehaviour
{
    [Header("目标")]
    [SerializeField] private Transform player;              // 玩家
    [SerializeField] private Camera targetCamera;           // 相机

    [Header("遮挡检测")]
    [SerializeField] private LayerMask occlusionLayers;     // 可遮挡的层（墙壁、楼层等）
    [SerializeField] private float rayRadius = 0.5f;        // 射线粗细
    [SerializeField] private int raysPerFrame = 5;          // 每帧射线数量

    [Header("透明度设置")]
    [SerializeField] private float targetAlpha = 0.3f;      // 遮挡物目标透明度
    [SerializeField] private float fadeSpeed = 8f;          // 渐变速度

    [Header("高度检测")]
    [SerializeField] private bool onlyCheckAbovePlayer = true;  // 只检测玩家上方

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    // 运行时数据
    private Dictionary<Renderer, MaterialData> occludedObjects;  // 被遮挡的物体
    private HashSet<Renderer> currentOccluders;                  // 当前帧的遮挡物

    // 材质数据
    private class MaterialData
    {
        public Material[] originalMaterials;
        public Material[] fadeMaterials;
        public float currentAlpha;
        public bool isTransparent;

        public MaterialData(Renderer renderer)
        {
            originalMaterials = renderer.materials;
            fadeMaterials = new Material[originalMaterials.Length];
            currentAlpha = 1f;
            isTransparent = false;

            // 创建材质副本
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                fadeMaterials[i] = new Material(originalMaterials[i]);
            }
        }
    }

    void Awake()
    {
        occludedObjects = new Dictionary<Renderer, MaterialData>();
        currentOccluders = new HashSet<Renderer>();

        if (targetCamera == null)
            targetCamera = Camera.main;
        
        if (player == null)
        {
            // 尝试自动查找玩家
            UnitMovement playerUnit = FindObjectOfType<UnitMovement>();
            if (playerUnit != null && playerUnit.GetComponent<TurnBasedUnit>()?.Faction == TurnFaction.Player)
            {
                player = playerUnit.transform;
            }
        }

        Debug.Log($"[CameraOcclusion] Initialized - Player: {player?.name}, Camera: {targetCamera?.name}");
    }

    void Start()
    {
        if (player == null)
        {
            Debug.LogError("[CameraOcclusion] No player assigned!");
        }
        if (targetCamera == null)
        {
            Debug.LogError("[CameraOcclusion] No camera found!");
        }
        
        Debug.Log($"[CameraOcclusion] Occlusion Layers: {occlusionLayers.value}");
    }

    void LateUpdate()
    {
        if (player == null || targetCamera == null) return;

        currentOccluders.Clear();
        DetectOcclusion();
        UpdateTransparency();
    }

    /// <summary>
    /// 检测遮挡物
    /// </summary>
    private void DetectOcclusion()
    {
        Vector3 cameraPos = targetCamera.transform.position;
        Vector3 playerPos = player.position + Vector3.up * 0.5f;

        Vector3 direction = playerPos - cameraPos;
        float distance = direction.magnitude;

        if (enableDebugLog && Time.frameCount % 60 == 0)  // 每60帧输出一次
        {
            Debug.Log($"[CameraOcclusion] Checking from camera to player, distance: {distance:F2}m");
        }

        // 多条射线检测
        int hitCount = 0;
        for (int i = 0; i < raysPerFrame; i++)
        {
            Vector3 offset = Random.insideUnitSphere * rayRadius;
            offset.y = Mathf.Abs(offset.y);

            Vector3 startPos = cameraPos + offset;
            Vector3 dir = playerPos - startPos;
            float dist = dir.magnitude;

            RaycastHit[] hits = Physics.RaycastAll(startPos, dir.normalized, dist, occlusionLayers);

            foreach (RaycastHit hit in hits)
            {
                // 只处理玩家上方的物体
                if (onlyCheckAbovePlayer && hit.transform.position.y <= player.position.y)
                    continue;

                Renderer renderer = hit.collider.GetComponent<Renderer>();
                if (renderer != null && renderer.gameObject != player.gameObject)
                {
                    currentOccluders.Add(renderer);
                    hitCount++;

                    // 如果是新的遮挡物，注册
                    if (!occludedObjects.ContainsKey(renderer))
                    {
                        occludedObjects[renderer] = new MaterialData(renderer);
                        
                        if (enableDebugLog)
                        {
                            Debug.Log($"[CameraOcclusion] New occluder: {renderer.gameObject.name}");
                        }
                    }
                }
            }
        }

        if (enableDebugLog && hitCount > 0 && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[CameraOcclusion] Found {currentOccluders.Count} occluders this frame");
        }
    }

    /// <summary>
    /// 更新透明度
    /// </summary>
    private void UpdateTransparency()
    {
        List<Renderer> toRemove = new List<Renderer>();

        foreach (var kvp in occludedObjects)
        {
            Renderer renderer = kvp.Key;
            MaterialData data = kvp.Value;

            if (renderer == null)
            {
                toRemove.Add(renderer);
                continue;
            }

            // 判断是否仍在遮挡
            bool isOccluding = currentOccluders.Contains(renderer);

            // 目标透明度
            float targetA = isOccluding ? targetAlpha : 1f;

            // 平滑过渡
            data.currentAlpha = Mathf.Lerp(data.currentAlpha, targetA, Time.deltaTime * fadeSpeed);

            // 应用透明度
            ApplyAlpha(renderer, data, data.currentAlpha);

            // 完全恢复后移除记录
            if (!isOccluding && Mathf.Approximately(data.currentAlpha, 1f))
            {
                RestoreOriginalMaterials(renderer, data);
                toRemove.Add(renderer);
            }
        }

        // 清理
        foreach (var r in toRemove)
        {
            occludedObjects.Remove(r);
        }
    }

    /// <summary>
    /// 应用透明度到材质
    /// </summary>
    private void ApplyAlpha(Renderer renderer, MaterialData data, float alpha)
    {
        // 首先确保材质设置正确
        if (!data.isTransparent && alpha < 0.99f)
        {
            // 需要变透明，设置所有材质为透明模式
            for (int i = 0; i < data.fadeMaterials.Length; i++)
            {
                SetMaterialTransparent(data.fadeMaterials[i]);
            }
            data.isTransparent = true;
            
            if (enableDebugLog)
            {
                Debug.Log($"[CameraOcclusion] Set {renderer.gameObject.name} to transparent mode");
            }
        }
        else if (data.isTransparent && alpha >= 0.99f)
        {
            // 需要变不透明，恢复材质模式
            for (int i = 0; i < data.fadeMaterials.Length; i++)
            {
                SetMaterialOpaque(data.fadeMaterials[i]);
            }
            data.isTransparent = false;
        }

        // 然后应用透明度
        for (int i = 0; i < data.fadeMaterials.Length; i++)
        {
            Material mat = data.fadeMaterials[i];
            Color color = mat.color;
            color.a = alpha;
            mat.color = color;
            
            // 同时设置_Color属性（某些Shader需要）
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }

        // 应用材质到Renderer
        renderer.materials = data.fadeMaterials;
    }

    /// <summary>
    /// 设置材质为透明模式（Fade）
    /// </summary>
    private void SetMaterialTransparent(Material mat)
    {
        // 设置渲染模式为Transparent（而非Fade）
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        
        // 启用透明混合
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        
        mat.renderQueue = 3000;
        
        if (enableDebugLog)
        {
            Debug.Log($"[CameraOcclusion] Material '{mat.name}' set to transparent");
        }
    }

    /// <summary>
    /// 设置材质为不透明模式
    /// </summary>
    private void SetMaterialOpaque(Material mat)
    {
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        
        // 禁用所有透明关键字
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        
        mat.renderQueue = -1;
    }

    /// <summary>
    /// 恢复原始材质
    /// </summary>
    private void RestoreOriginalMaterials(Renderer renderer, MaterialData data)
    {
        renderer.materials = data.originalMaterials;
    }

    void OnDestroy()
    {
        // 清理：恢复所有材质
        foreach (var kvp in occludedObjects)
        {
            if (kvp.Key != null)
            {
                RestoreOriginalMaterials(kvp.Key, kvp.Value);
            }
        }
    }

    void OnDrawGizmos()
    {
        if (player == null || targetCamera == null) return;

        // 绘制检测射线
        Gizmos.color = Color.cyan;
        Vector3 cameraPos = targetCamera.transform.position;
        Vector3 playerPos = player.position + Vector3.up * 0.5f;
        Gizmos.DrawLine(cameraPos, playerPos);
    }
}