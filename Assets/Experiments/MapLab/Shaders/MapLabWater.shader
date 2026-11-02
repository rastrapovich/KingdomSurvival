// Вода MapLab: матовая сине-серая поверхность, глубина — по карте высот земли,
// редкие нарисованные штрихи вдоль течения, светлее и теплее у берега.
Shader "Kingdom Survival/MapLab/Water"
{
    Properties
    {
        _HeightTex ("Terrain height (R, ед.)", 2D) = "black" {}
        _ShadeTex ("Shade", 2D) = "white" {}
        _WaterTex ("Water strokes", 2D) = "black" {}
        _DetailTex ("Detail", 2D) = "gray" {}
        _Deep ("Deep", Color) = (0.23, 0.33, 0.44, 1)
        _Shallow ("Shallow", Color) = (0.35, 0.45, 0.55, 1)
        _Stroke ("Stroke", Color) = (0.64, 0.7, 0.76, 1)
        _ShoreColor ("Shore", Color) = (0.49, 0.45, 0.28, 1)
        _InkColor ("Ink", Color) = (0.17, 0.15, 0.13, 1)
        _ShadowTint ("Shadow tint", Color) = (0.28, 0.31, 0.48, 1)
        _MapSize ("Map size", Float) = 256
        _StrokeAmount ("Stroke amount", Range(0, 1)) = 0.6
        _CastShadow ("Cast shadow", Range(0, 1)) = 0.55
        _Outline ("Outline", Range(0, 1)) = 0.55
        _Paper ("Paper grain", Range(0, 1)) = 0.25
        _Saturation ("Saturation", Float) = 1
        _DebugMode ("Debug mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "MapLabCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 flow : TEXCOORD1;
            };

            TEXTURE2D(_HeightTex); SAMPLER(sampler_HeightTex);
            TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex);
            TEXTURE2D(_WaterTex); SAMPLER(sampler_WaterTex);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Deep;
                float4 _Shallow;
                float4 _Stroke;
                float4 _ShoreColor;
                float4 _InkColor;
                float4 _ShadowTint;
                float _MapSize;
                float _StrokeAmount;
                float _CastShadow;
                float _Outline;
                float _Paper;
                float _Saturation;
                float _DebugMode;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.flow = input.flow;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 world = i.positionWS.xz;
                float2 uv = world / _MapSize;
                float ground = SAMPLE_TEXTURE2D(_HeightTex, sampler_HeightTex, uv).r;
                float depth = i.positionWS.y - ground;
                int mode = (int)round(_DebugMode);
                if (mode == 6)
                    return float4(lerp(float3(0.5, 0.75, 0.95), float3(0.05, 0.15, 0.5), saturate(depth / 2.5)), 1);
                if (mode != 0 && mode != 9)
                    return float4(0.3, 0.45, 0.7, 1);

                float2 flow = normalize(i.flow + float2(1e-4, 0));
                float2 perp = float2(-flow.y, flow.x);
                float along = dot(world, flow);
                float across = dot(world, perp);
                float wobble = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, world / 41.0).g - 0.5;
                float4 strokes = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, float2(along / 42.0, (across + wobble * 4.0) / 24.0));
                float4 strokes2 = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, float2(along / 30.0 + 0.31, (across - wobble * 3.0) / 15.0 + 0.57));

                float deep = smoothstep(0.05, 1.8, depth);
                float3 c = lerp(_Shallow.rgb, _Deep.rgb, deep);
                c *= 1.0 + (strokes.g - 0.5) * 0.12;
                // Спокойная середина, больше штрихов у берега и на течении.
                float calm = lerp(1.0, 0.18, smoothstep(0.5, 2.0, depth));
                // Штрихи собираются редкими пятнами, середина озера остаётся спокойным цветом.
                float patch = smoothstep(0.42, 0.68, SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, world / 97.0 + float2(0.5, 0.2)).g);
                // Этап 02: реже и мягче; акценты — у берега, на мелях и сужениях.
                float accent = 1.0 - smoothstep(0.15, 0.9, depth);
                float s = saturate(max(strokes.r, strokes2.r * 0.7) * 1.5 - 0.55) * _StrokeAmount * calm * lerp(0.08, 1.0, patch) * lerp(0.55, 1.25, accent);
                c = lerp(c, lerp(c, _Stroke.rgb, 0.8), s * 0.6);
                // Кромка: тёплая отмель и тонкая линия у самой земли.
                float shoal = 1.0 - smoothstep(0.0, 0.35, depth);
                c = lerp(c, lerp(_Shallow.rgb, _ShoreColor.rgb, 0.45) * 1.08, shoal * 0.6);
                float edge = 1.0 - smoothstep(0.0, 0.07, depth);
                c = lerp(c, _InkColor.rgb, edge * _Outline * 0.55);

                float4 shade = SAMPLE_TEXTURE2D(_ShadeTex, sampler_ShadeTex, uv);
                c = MapLabShadow(c, (1.0 - shade.r) * _CastShadow * 0.8, _ShadowTint.rgb);
                float4 p = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, world / 23.0 + float2(0.11, 0.53));
                c *= 1.0 + (p.a - 0.5) * _Paper * 0.15;
                c = MapLabSaturate(c, _Saturation);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
