using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// URP 材质淡出工具（共享）。
///
/// 为什么需要它：项目用的是 URP/Lit，而旧代码里切换透明用的是 Built-in RP 的那一套
/// （`_ALPHABLEND_ON` 关键字 + `_Color` 主色 + renderQueue 3000）。URP/Lit **不认**这些，
/// 结果材质根本没变透明。URP 需要的是 `_Surface` / `_SURFACE_TYPE_TRANSPARENT` /
/// `_BaseColor`。
///
/// `CameraOcclusionHandler` 和 `FloorVisibilityController` 都要做"把物体变半透明"，
/// 统一走这里，避免两处各写一份、各错一份。
/// </summary>
public static class MaterialFadeUtil
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int SurfaceId   = Shader.PropertyToID("_Surface");
    private static readonly int BlendId     = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendId  = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId  = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId    = Shader.PropertyToID("_ZWrite");

    /// <summary>把材质切到 URP 的 Transparent(Alpha) 模式。</summary>
    public static void SetTransparent(Material m)
    {
        if (m == null) return;

        m.SetOverrideTag("RenderType", "Transparent");

        if (m.HasProperty(SurfaceId)) m.SetFloat(SurfaceId, 1f);   // 0=Opaque 1=Transparent
        if (m.HasProperty(BlendId))   m.SetFloat(BlendId,   0f);   // 0=Alpha
        if (m.HasProperty(SrcBlendId)) m.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
        if (m.HasProperty(DstBlendId)) m.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty(ZWriteId))   m.SetFloat(ZWriteId,   0f);

        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        m.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>把材质切回 URP 的 Opaque 模式。</summary>
    public static void SetOpaque(Material m)
    {
        if (m == null) return;

        m.SetOverrideTag("RenderType", "Opaque");

        if (m.HasProperty(SurfaceId))  m.SetFloat(SurfaceId, 0f);
        if (m.HasProperty(SrcBlendId)) m.SetFloat(SrcBlendId, (float)BlendMode.One);
        if (m.HasProperty(DstBlendId)) m.SetFloat(DstBlendId, (float)BlendMode.Zero);
        if (m.HasProperty(ZWriteId))   m.SetFloat(ZWriteId,   1f);

        m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        m.renderQueue = (int)RenderQueue.Geometry;
    }

    /// <summary>设置材质 alpha。URP/Lit 主色是 _BaseColor，不是 _Color。</summary>
    public static void SetAlpha(Material m, float alpha)
    {
        if (m == null) return;

        if (m.HasProperty(BaseColorId))
        {
            Color c = m.GetColor(BaseColorId);
            c.a = alpha;
            m.SetColor(BaseColorId, c);
        }
        else
        {
            Color c = m.color;
            c.a = alpha;
            m.color = c;
        }
    }
}
