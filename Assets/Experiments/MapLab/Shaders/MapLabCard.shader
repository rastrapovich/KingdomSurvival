// Карточки растительности MapLab: опорная точка у основания, разворот к
// фиксированной камере в вершинном шейдере (ствол вертикален, наклон карточки
// к камере — _CardLean), alpha clip + запись глубины. Свет запечён в атласе
// (сверху слева), тон — из палитры, поэтому смена палитры не пересобирает меши.
Shader "Kingdom Survival/MapLab/Card"
{
    Properties
    {
        _MainTex ("Atlas (R тон, G линия, B вариация, A силуэт)", 2D) = "white" {}
        _PaletteTex ("Palette", 2D) = "white" {}
        _DetailTex ("Detail", 2D) = "gray" {}
        _CardLean ("Card lean", Range(0, 1)) = 0.25
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _LightStrength ("Light strength", Range(0, 1)) = 0.75
        _Outline ("Outline", Range(0, 1)) = 0.55
        _Paper ("Paper grain", Range(0, 1)) = 0.25
        _Saturation ("Saturation", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

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
                float2 offset : TEXCOORD1;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float heightFraction : TEXCOORD3;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_PaletteTex); SAMPLER(sampler_PaletteTex);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _CardLean;
                float _Cutoff;
                float _LightStrength;
                float _Outline;
                float _Paper;
                float _Saturation;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 basePoint = TransformObjectToWorld(input.positionOS);
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 camUp = normalize(UNITY_MATRIX_V[1].xyz);
                camRight = normalize(float3(camRight.x, 0, camRight.z) + 1e-5);
                float3 up = normalize(lerp(float3(0, 1, 0), camUp, _CardLean));
                // Высота карточки на экране не зависит от наклона камеры.
                float screenScale = 1.0 / max(0.25, dot(up, camUp));
                float3 world = basePoint + camRight * input.offset.x + up * input.offset.y * screenScale;
                o.positionWS = world;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.color = input.color;
                o.heightFraction = input.offset.y;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 atlas = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(atlas.a - _Cutoff);
                float kind = i.color.b;
                float indexA = 22, indexB = 23;          // лиственные: мшистый / дымчатый
                if (kind > 0.2 && kind < 0.4) { indexA = 24; indexB = 23; }   // хвойные
                else if (kind > 0.4 && kind < 0.6) { indexA = 26; indexB = 22; } // кусты
                else if (kind > 0.6) { indexA = 27; indexB = 2; }               // камыш
                float3 toneA = MapLabPalette(TEXTURE2D_ARGS(_PaletteTex, sampler_PaletteTex), indexA);
                float3 toneB = MapLabPalette(TEXTURE2D_ARGS(_PaletteTex, sampler_PaletteTex), indexB);
                float3 forestShadow = MapLabPalette(TEXTURE2D_ARGS(_PaletteTex, sampler_PaletteTex), 25);
                float3 lightTint = MapLabPalette(TEXTURE2D_ARGS(_PaletteTex, sampler_PaletteTex), 29);
                float3 dry = MapLabPalette(TEXTURE2D_ARGS(_PaletteTex, sampler_PaletteTex), 2);
                float3 ink = MapLabPalette(TEXTURE2D_ARGS(_PaletteTex, sampler_PaletteTex), 30);
                float t = saturate(i.color.r * 0.8 + (atlas.b - 0.5) * 0.45);
                float3 baseColor = lerp(toneA, toneB, t) * (i.color.g * 1.25);
                float3 lit = lerp(baseColor * lightTint * 1.45, dry, 0.12);
                float3 shadow = lerp(baseColor * 0.32, forestShadow, 0.55);
                float v = atlas.r;
                float strength = lerp(0.35, 1.0, _LightStrength);
                float3 c = lerp(lerp(baseColor, shadow, strength), lerp(baseColor, lit, strength), smoothstep(0.12, 0.95, v));
                float4 p = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.positionWS.xz / 23.0 + float2(0.11, 0.53));
                c *= 1.0 + (p.a - 0.5) * _Paper * 0.15;
                c = lerp(c, ink, saturate(atlas.g * _Outline * 1.1));
                c = MapLabSaturate(c, _Saturation);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
