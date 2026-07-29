Shader "Custom/Mirror"
{
    // 配合 MirrorReflection.cs（planar reflection）：采样其设置的全局反射贴图 _PlanarReflectionTexture。
    // 关键：按【屏幕坐标】采样 → 镜面 Quad 任意拉伸/缩放都不变形，且反射随视角正确变化（真镜子）。
    Properties
    {
        _BaseColor          ("Tint 色调", Color) = (1,1,1,1)
        _ReflectionStrength ("反射强度", Range(0,1)) = 1
        [Normal] _BumpMap   ("扰动法线(水面用,可留空)", 2D) = "bump" {}
        _BumpStrength       ("扰动强度(0=镜面)", Range(0,0.2)) = 0.0
        _BumpTiling         ("扰动平铺", Float) = 1
        _BumpScroll         ("扰动流动 xy", Vector) = (0.02,0.01,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PlanarReflectionTexture); SAMPLER(sampler_PlanarReflectionTexture);
            TEXTURE2D(_BumpMap);                 SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _ReflectionStrength;
                float4 _BumpMap_ST;
                float  _BumpStrength;
                float  _BumpTiling;
                float4 _BumpScroll;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; float2 uv : TEXCOORD1; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.screenPos  = ComputeScreenPos(p.positionCS); // 屏幕坐标 → 不随 quad 拉伸
                OUT.uv         = IN.uv * _BumpTiling;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 suv = IN.screenPos.xy / IN.screenPos.w;

                // 可选水波扰动：法线贴图偏移屏幕采样坐标
                if (_BumpStrength > 0.0001)
                {
                    float2 duv = IN.uv + _BumpScroll.xy * _Time.y;
                    float3 n = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, duv));
                    suv += n.xy * _BumpStrength;
                }

                half3 refl = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, suv).rgb;
                half3 col  = refl * _ReflectionStrength * _BaseColor.rgb;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
