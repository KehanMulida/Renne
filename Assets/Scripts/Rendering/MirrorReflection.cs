using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 真·平面镜（planar reflection）。
/// 效果：把"当前观察相机"沿镜面所在平面翻转，重新渲染场景成一张 RenderTexture，
/// 设为全局贴图 _PlanarReflectionTexture；镜面材质（Mirror.shader）按【屏幕坐标】采样。
///
/// 为什么用屏幕坐标：倒影按屏幕像素对齐世界 → 镜面 Quad 任意拉伸/缩放都不会让倒影变形；
/// 且反射随观察视角产生正确视差（真镜子，而非固定一点透视）。
///
/// 用法：挂在镜面物体（或空管理器）上。镜面平面 = 本 Transform 位置 + up 为法线：
///   - 地面镜：物体 up 朝 +Y（Unity Plane 默认）
///   - 墙面镜：把物体旋转到 up 朝墙外
/// </summary>
[ExecuteAlways]
public class MirrorReflection : MonoBehaviour
{
    public enum SurfaceNormal { Up, Forward, Down, Back, Right, Left }

    [Header("镜面朝向")]
    [Tooltip("镜面反射法线用哪个轴：地面 Plane 用 Up；墙面 Quad 用 Forward（Quad 正面朝 +Z）。倒影方向不对就换一个")]
    public SurfaceNormal surfaceNormal = SurfaceNormal.Up;

    [Header("反射范围（建议只勾需要的层，省一半开销）")]
    [Tooltip("反射相机渲染哪些层。只勾角色/关键道具，别勾 UI/雾/特效")]
    public LayerMask reflectLayers = ~0;

    [Header("质量 / 性能")]
    [Tooltip("反射贴图相对屏幕的分辨率倍率。0.5 = 半分辨率（1/4 像素量）")]
    [Range(0.15f, 1f)] public float resolutionMultiplier = 0.5f;
    [Tooltip("反射里是否渲染阴影（关掉省开销）")]
    public bool renderShadows = false;
    [Tooltip("每隔几帧更新一次反射（相机/物体较静时设 2~3 可省开销）")]
    [Range(1, 6)] public int updateEveryNFrames = 1;
    [Tooltip("裁剪平面偏移，消除镜面边缘反射漏光")]
    public float clipPlaneOffset = 0.03f;

    [Header("离屏优化")]
    [Tooltip("镜面不在当前相机视野内时跳过反射渲染（强烈建议开）")]
    public bool onlyRenderWhenVisible = true;
    [Tooltip("用于可见性判断的镜面 Renderer；留空则自动取本物体的 Renderer")]
    public Renderer mirrorRenderer;

    private Camera _reflectionCamera;
    private RenderTexture _rt;
    private int _frame;
    private readonly Plane[] _frustum = new Plane[6];
    private static bool _isRendering; // 防递归
    private static readonly int ReflectionTexID = Shader.PropertyToID("_PlanarReflectionTexture");

    void OnEnable()
    {
        if (mirrorRenderer == null) mirrorRenderer = GetComponent<Renderer>();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }
    void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; Cleanup(); }

    private void Cleanup()
    {
        if (_reflectionCamera != null)
        {
            if (Application.isPlaying) Destroy(_reflectionCamera.gameObject); else DestroyImmediate(_reflectionCamera.gameObject);
            _reflectionCamera = null;
        }
        if (_rt != null)
        {
            if (Application.isPlaying) Destroy(_rt); else DestroyImmediate(_rt);
            _rt = null;
        }
    }

    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (_isRendering) return;
        if (cam.cameraType == CameraType.Reflection || cam.cameraType == CameraType.Preview) return;
        if (cam == _reflectionCamera) return;

        // 离屏优化：镜面不在当前相机视野内 → 直接不渲染反射
        if (onlyRenderWhenVisible && mirrorRenderer != null)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, _frustum);
            if (!GeometryUtility.TestPlanesAABB(_frustum, mirrorRenderer.bounds))
                return;
        }

        // 更新节流：非本帧则复用上一张反射贴图
        if (updateEveryNFrames > 1 && (_frame++ % updateEveryNFrames != 0))
        {
            if (_rt != null) Shader.SetGlobalTexture(ReflectionTexID, _rt);
            return;
        }

        EnsureResources(cam);
        UpdateReflectionCamera(cam);

        _isRendering = true;
        GL.invertCulling = true;    // 镜像翻转了绕序，需反向剔除
#pragma warning disable 618 // URP14: RenderSingleCamera 已过时但仍可用
        UniversalRenderPipeline.RenderSingleCamera(context, _reflectionCamera);
#pragma warning restore 618
        GL.invertCulling = false;   // 立即复位，避免污染主相机
        _isRendering = false;

        Shader.SetGlobalTexture(ReflectionTexID, _rt);
    }

    private void EnsureResources(Camera src)
    {
        int w = Mathf.Max(16, (int)(src.pixelWidth  * resolutionMultiplier));
        int h = Mathf.Max(16, (int)(src.pixelHeight * resolutionMultiplier));

        if (_rt == null || _rt.width != w || _rt.height != h)
        {
            if (_rt != null) { if (Application.isPlaying) Destroy(_rt); else DestroyImmediate(_rt); }
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR)
            { name = "_PlanarReflectionTexture", useMipMap = false, autoGenerateMips = false };
            _rt.Create();
        }

        if (_reflectionCamera == null)
        {
            var go = new GameObject("MirrorReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
            _reflectionCamera = go.AddComponent<Camera>();
            _reflectionCamera.enabled = false; // 手动 RenderSingleCamera
        }
    }

    private void UpdateReflectionCamera(Camera src)
    {
        _reflectionCamera.CopyFrom(src);
        _reflectionCamera.targetTexture = _rt;
        _reflectionCamera.cullingMask   = reflectLayers;
        _reflectionCamera.cameraType    = CameraType.Reflection;

        var refData = _reflectionCamera.GetUniversalAdditionalCameraData();
        if (refData != null)
        {
            refData.renderShadows        = renderShadows;
            refData.requiresColorTexture = false;
            refData.requiresDepthTexture = false;
            refData.renderType           = CameraRenderType.Base;
        }

        // 镜面平面：位置 = 本物体，法线 = 选定轴
        Vector3 pos = transform.position;
        Vector3 normal = GetNormal();
        float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
        Vector4 plane = new Vector4(normal.x, normal.y, normal.z, d);

        // 反射矩阵翻转观察相机的 view
        Matrix4x4 reflection = CalculateReflectionMatrix(plane);
        _reflectionCamera.worldToCameraMatrix = src.worldToCameraMatrix * reflection;

        // 斜裁剪近平面，避免渲染镜面背后的东西
        Vector4 clipPlane = CameraSpacePlane(_reflectionCamera, pos, normal, clipPlaneOffset);
        _reflectionCamera.projectionMatrix = src.CalculateObliqueMatrix(clipPlane);

        // 供 LOD/剔除参考的 transform（渲染以上面的矩阵为准）
        _reflectionCamera.transform.position = reflection.MultiplyPoint(src.transform.position);
        _reflectionCamera.transform.rotation = src.transform.rotation;
    }

    private Vector3 GetNormal()
    {
        switch (surfaceNormal)
        {
            case SurfaceNormal.Forward: return transform.forward;
            case SurfaceNormal.Down:    return -transform.up;
            case SurfaceNormal.Back:    return -transform.forward;
            case SurfaceNormal.Right:   return transform.right;
            case SurfaceNormal.Left:    return -transform.right;
            default:                    return transform.up;
        }
    }

    private static Matrix4x4 CalculateReflectionMatrix(Vector4 p)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y;     m.m02 = -2f * p.x * p.z;     m.m03 = -2f * p.w * p.x;
        m.m10 = -2f * p.y * p.x;     m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z;     m.m13 = -2f * p.w * p.y;
        m.m20 = -2f * p.z * p.x;     m.m21 = -2f * p.z * p.y;     m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
        m.m30 = 0f;                  m.m31 = 0f;                  m.m32 = 0f;                  m.m33 = 1f;
        return m;
    }

    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float offset)
    {
        Vector3 offsetPos = pos + normal * offset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }
}
