// Поверхность карты MapLab. Краска (CPU, мировые координаты) × свет по нормали
// × запечённые падающие тени и затенение × мелкая фактура и бумага + тонкая линия.
// Unlit-проход без LightMode — рисуется действующим Renderer2D.
Shader "Kingdom Survival/MapLab/Terrain"
{
    Properties
    {
        _PaintTex ("Paint (RGB краска, A линия)", 2D) = "gray" {}
        _ShadeTex ("Shade (R свет, G затенение)", 2D) = "white" {}
        _MaskA ("Masks A (лес, поле, дорога, уступ)", 2D) = "black" {}
        _MaskB ("Masks B (вода, берег, поселение, камень)", 2D) = "black" {}
        _DetailTex ("Detail", 2D) = "gray" {}
        _LightDir ("Light direction", Vector) = (-0.55, 0.72, 0.42, 0)
        _ShadowTint ("Shadow tint", Color) = (0.28, 0.31, 0.48, 1)
        _LightTint ("Light tint", Color) = (1, 0.94, 0.82, 1)
        _InkColor ("Ink", Color) = (0.17, 0.15, 0.13, 1)
        _PaperColor ("Paper", Color) = (0.9, 0.86, 0.76, 1)
        _LightStrength ("Light strength", Range(0, 1)) = 0.75
        _ShadowSoftness ("Shadow softness", Range(0, 1)) = 0.55
        _ToneBands ("Tone bands", Float) = 0
        _CastShadow ("Cast shadow", Range(0, 1)) = 0.55
        _AO ("Occlusion", Range(0, 1)) = 0.5
        _Outline ("Outline", Range(0, 1)) = 0.55
        _Micro ("Micro texture", Range(0, 1)) = 0.35
        _Paper ("Paper grain", Range(0, 1)) = 0.25
        _BrushScale ("Brush scale (units)", Float) = 1.6
        _Saturation ("Saturation", Float) = 1
        _DebugMode ("Debug mode", Float) = 0
        _ShowPaint ("Show paint", Float) = 1
        _ShowLight ("Show relief light", Float) = 1
        _HeightRange ("Height min, max", Vector) = (0, 12, 0, 0)
        _Exaggeration ("Relief exaggeration (light)", Float) = 2.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "MapLabCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            TEXTURE2D(_PaintTex); SAMPLER(sampler_PaintTex);
            TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex);
            TEXTURE2D(_MaskA); SAMPLER(sampler_MaskA);
            TEXTURE2D(_MaskB); SAMPLER(sampler_MaskB);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PaintTex_ST;
                float4 _LightDir;
                float4 _ShadowTint;
                float4 _LightTint;
                float4 _InkColor;
                float4 _PaperColor;
                float _LightStrength;
                float _ShadowSoftness;
                float _ToneBands;
                float _CastShadow;
                float _AO;
                float _Outline;
                float _Micro;
                float _Paper;
                float _BrushScale;
                float _Saturation;
                float _DebugMode;
                float _ShowPaint;
                float _ShowLight;
                float4 _HeightRange;
                float _Exaggeration;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }

            float3 Ramp(float t)
            {
                t = saturate(t);
                return lerp(lerp(float3(0.15, 0.25, 0.55), float3(0.45, 0.7, 0.35), saturate(t * 2)), float3(0.95, 0.9, 0.75), saturate(t * 2 - 1));
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 L = normalize(_LightDir.xyz);
                float4 paint = SAMPLE_TEXTURE2D(_PaintTex, sampler_PaintTex, i.uv);
                float4 shade = SAMPLE_TEXTURE2D(_ShadeTex, sampler_ShadeTex, i.uv);
                float4 maskA = SAMPLE_TEXTURE2D(_MaskA, sampler_MaskA, i.uv);
                float4 maskB = SAMPLE_TEXTURE2D(_MaskB, sampler_MaskB, i.uv);

                int mode = (int)round(_DebugMode);
                if (mode == 1)
                    return float4(Ramp((i.positionWS.y - _HeightRange.x) / max(0.01, _HeightRange.y - _HeightRange.x)), 1);
                if (mode == 2)
                {
                    float slope = degrees(acos(saturate(n.y)));
                    return float4(Ramp(slope / 50.0), 1);
                }
                if (mode == 3)
                    return float4(n * 0.5 + 0.5, 1);
                if (mode == 4)
                    return float4(saturate(float3(maskA.g + maskB.a * 0.6, maskA.r * 0.8 + maskA.g * 0.6, maskB.r + maskA.b)), 1);
                if (mode == 5)
                    return float4(lerp(float3(0.9, 0.88, 0.8), float3(0.05, 0.35, 0.15), maskA.r), 1);
                if (mode == 6)
                    return float4(lerp(lerp(float3(0.85, 0.82, 0.72), float3(0.85, 0.6, 0.3), maskB.g), float3(0.15, 0.35, 0.8), maskB.r), 1);
                if (mode == 7)
                    return float4(lerp(float3(0.75, 0.75, 0.72) * (0.6 + 0.4 * n.y), float3(0.85, 0.15, 0.1), maskA.b), 1);
                if (mode == 8)
                    return float4(lerp(float3(0.75, 0.75, 0.72) * (0.6 + 0.4 * n.y), float3(0.6, 0.2, 0.75), maskA.a), 1);

                float3 albedo = _ShowPaint > 0.5 ? paint.rgb : float3(0.58, 0.58, 0.56);
                float3 c = albedo;
                if (_ShowLight > 0.5)
                {
                    float3 shapeNormal = normalize(float3(n.x * _Exaggeration, n.y, n.z * _Exaggeration));
                    c = MapLabLight(c, shapeNormal, L, _LightStrength, _ShadowSoftness, _ToneBands, _ShadowTint.rgb, _LightTint.rgb);
                    c = MapLabShadow(c, (1.0 - shade.r) * _CastShadow * 0.9, _ShadowTint.rgb);
                    c *= lerp(1.0, shade.g, _AO);
                }

                // Мелкая фактура мазка и бумаги в мировых координатах (не плывёт с камерой).
                // На общем виде гасится: затухание по размеру пикселя в мире.
                float2 world = i.positionWS.xz;
                float pixelWorld = max(fwidth(world.x), fwidth(world.y));
                float fade = 1.0 - smoothstep(_BrushScale * 0.25, _BrushScale * 0.9, pixelWorld);
                float4 d1 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, world / (_BrushScale * 9.0));
                float4 d2 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, world / (_BrushScale * 3.1) + float2(0.37, 0.71));
                float micro = (d1.r - 0.5) * 0.6 + (d2.r - 0.5) * 0.4 * fade;
                c *= 1.0 + micro * _Micro * 0.35;
                float4 p = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, world / 23.0 + float2(0.11, 0.53));
                float paper = (p.a - 0.5) * 0.7 + (d2.g - 0.5) * 0.3 * fade;
                c *= 1.0 + paper * _Paper * 0.22;
                c = lerp(c, _PaperColor.rgb * c * 1.6, _Paper * 0.05);

                c = lerp(c, _InkColor.rgb, saturate(paint.a * _Outline));
                c = MapLabSaturate(c, _Saturation);
                if (mode == 9)
                    c = lerp(float3(0.62, 0.62, 0.6), c, 0.25);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
