// Твёрдые объекты MapLab (скалы, срубы, кровли, ограды, мост, валуны):
// цвет из палитры по индексу, узор поверхности, свет по плоской нормали,
// тонкая линия по рёбрам граней постоянной экранной толщины.
Shader "Kingdom Survival/MapLab/Solid"
{
    Properties
    {
        _PaletteTex ("Palette", 2D) = "white" {}
        _PaintTex ("Terrain paint", 2D) = "gray" {}
        _MapSize ("Map size", Float) = 256
        _DetailTex ("Detail", 2D) = "gray" {}
        _LightDir ("Light direction", Vector) = (-0.55, 0.72, 0.42, 0)
        _ShadowTint ("Shadow tint", Color) = (0.28, 0.31, 0.48, 1)
        _LightTint ("Light tint", Color) = (1, 0.94, 0.82, 1)
        _InkColor ("Ink", Color) = (0.17, 0.15, 0.13, 1)
        _LightStrength ("Light strength", Range(0, 1)) = 0.75
        _ShadowSoftness ("Shadow softness", Range(0, 1)) = 0.55
        _ToneBands ("Tone bands", Float) = 0
        _Outline ("Outline", Range(0, 1)) = 0.55
        _Paper ("Paper grain", Range(0, 1)) = 0.25
        _Saturation ("Saturation", Float) = 1
        _DebugMode ("Debug mode", Float) = 0
        _RockTex ("Rock color (этап 02)", 2D) = "gray" {}
        _RockMean ("Rock mean", Color) = (0.5, 0.5, 0.5, 1)
        _RockScale ("Rock scale", Float) = 14
        _RockStrength ("Rock strength", Range(0, 1)) = 0
        _Stage04 ("Этап 04: дерево и камень в общем рисунке", Float) = 0
        _WoodColor ("Этап 04: древесина", Color) = (0.62, 0.5, 0.36, 1)
        _StoneColor ("Этап 04: камень уступов", Color) = (0.6, 0.58, 0.52, 1)
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
                float2 face : TEXCOORD0;
                float2 size : TEXCOORD1;
                float4 meta : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 face : TEXCOORD2;
                float2 size : TEXCOORD3;
                float4 meta : TEXCOORD4;
            };

            TEXTURE2D(_PaletteTex); SAMPLER(sampler_PaletteTex);
            TEXTURE2D(_PaintTex); SAMPLER(sampler_PaintTex);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
            TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _LightDir;
                float4 _ShadowTint;
                float4 _LightTint;
                float4 _InkColor;
                float _LightStrength;
                float _ShadowSoftness;
                float _ToneBands;
                float _Outline;
                float _Paper;
                float _Saturation;
                float _DebugMode;
                float _MapSize;
                float4 _RockMean;
                float _RockScale;
                float _RockStrength;
                float _Stage04;
                float4 _WoodColor;
                float4 _StoneColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.face = input.face;
                o.size = input.size;
                o.meta = input.meta;
                return o;
            }

            float Lines(float x, float period, float width)
            {
                float f = abs(frac(x / period) - 0.5) * 2.0;
                return 1.0 - smoothstep(1.0 - width * 2.0, 1.0 - width, f);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float index = i.meta.x * 32.0 - 0.5;
                float3 c = MapLabPalette(TEXTURE2D_ARGS(_PaletteTex, sampler_PaletteTex), index) * i.meta.y;
                int pattern = (int)floor(i.meta.z * 8.0 + 0.5);
                float2 f = i.face;
                float4 d = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, f / float2(2.0, 6.0));
                float3 nRaw = normalize(i.normalWS);
                int paletteIndex = (int)round(index);
                bool stage04 = _Stage04 > 0.5;
                bool wood = stage04 && (paletteIndex == 17 || paletteIndex == 18);
                bool stone = stage04 && pattern == 4;
                float inkWeight = 1.0;
                if (wood)
                {
                    // Выцветшее охристое дерево: светлая широкая плоскость, тень —
                    // от общего света; пара выбранных стыков вместо мелкой фактуры.
                    c = _WoodColor.rgb * (paletteIndex == 18 ? 0.9 : 1.0) * i.meta.y * (1.0 + (d.g - 0.5) * 0.08);
                    if (pattern == 5)
                        c *= 1.0 - 0.07 * Lines(f.x, 1.7, 0.05);
                    inkWeight = 0.8;
                }
                else if (stone)
                {
                    // Крупные неравные плоскости тёплого серо-оливкового камня, слабые
                    // вертикальные штрихи, редкие трещины пером, травяные наплывы
                    // сверху и осыпь у основания; горизонтальные полки — как земля.
                    float plane = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.positionWS.xz / 13.0 + float2(0.3, 0.1)).g;
                    c = _StoneColor.rgb * i.meta.y * (0.88 + plane * 0.24);
                    c *= 1.0 + (SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, f / float2(2.4, 9.0)).b - 0.5) * 0.12;
                    float3 ground = SAMPLE_TEXTURE2D(_PaintTex, sampler_PaintTex, i.positionWS.xz / _MapSize).rgb;
                    if (abs(nRaw.y) > 0.6)
                        c = ground * 0.9;
                    else if (i.size.y > 0)
                    {
                        float top = i.size.y - f.y;
                        float shelf = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.positionWS.xz / 5.0 + float2(0.7, 0.2)).g;
                        float overhang = (1.0 - smoothstep(0.12 + shelf * 0.55, 0.2 + shelf * 0.55, top)) * smoothstep(0.42, 0.5, shelf);
                        c = lerp(c, ground * 0.92, overhang);
                        float scree = 1.0 - smoothstep(0.25 + plane * 0.45, 0.4 + plane * 0.45, f.y);
                        c = lerp(c, c * float3(1.03, 0.98, 0.92) * 0.9, scree * 0.6);
                        float crackMask = step(0.6, SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, float2(floor(f.x / 2.9) * 0.173, i.positionWS.y * 0.01 + 0.4)).r);
                        float crack = Lines(f.x + (d.b - 0.5) * 1.1, 2.9, 0.018) * crackMask * smoothstep(0.3, 0.8, f.y) * (1.0 - smoothstep(0.5, 0.9, 1.0 - top / max(0.5, i.size.y)));
                        c = lerp(c, _InkColor.rgb, crack * 0.55 * _Outline);
                    }
                    inkWeight = 0.75;
                }
                else if (pattern == 1)        // бревенчатый сруб
                    c *= 1.0 - 0.28 * Lines(f.y, 0.34, 0.18) + (d.g - 0.5) * 0.12;
                else if (pattern == 2)   // солома: штрихи по скату
                    c *= 1.0 + (SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, f / float2(0.9, 5.0)).b - 0.5) * 0.42 - 0.12 * Lines(f.y, 0.55, 0.08);
                else if (pattern == 3)   // драница: ряды и стыки
                {
                    float row = floor(f.y / 0.38);
                    c *= 1.0 - 0.22 * Lines(f.y, 0.38, 0.1) - 0.15 * Lines(f.x + row * 0.21, 0.42, 0.06) + (d.g - 0.5) * 0.1;
                }
                else if (pattern == 4)   // камень: вертикальные штрихи + слабая вариация цвета Rock_Color
                {
                    c *= 1.0 + (SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, f / float2(1.6, 7.0)).b - 0.5) * 0.5 + (d.g - 0.5) * 0.15;
                    if (_RockStrength > 0.001)
                    {
                        float3 r1 = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, f / _RockScale).rgb;
                        float3 r2 = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, f.yx / (_RockScale * 0.43) + float2(0.31, 0.67)).rgb;
                        float3 rock = lerp(r1, r2, 0.4) / max(_RockMean.rgb, 0.02);
                        c = lerp(c, c * clamp(rock, 0.6, 1.5), _RockStrength);
                    }
                }
                else if (pattern == 6)   // крышка уступа: цвет живописной поверхности
                    c = SAMPLE_TEXTURE2D(_PaintTex, sampler_PaintTex, i.positionWS.xz / _MapSize).rgb * i.meta.y;
                else if (pattern == 5)   // доски
                    c *= 1.0 - 0.2 * Lines(f.x, 0.3, 0.12) + (d.g - 0.5) * 0.1;

                float3 n = normalize(i.normalWS);
                // Крышка уступа продолжает землю: свет как у ровной площадки, без «стеклянных» граней.
                if (pattern == 6 || (stone && abs(nRaw.y) > 0.6))
                    n = float3(0, 1, 0);
                float3 L = normalize(_LightDir.xyz);
                if ((int)round(_DebugMode) == 3)
                    return float4(n * 0.5 + 0.5, 1);
                // Стены и камень — с усиленным делением на свет и тень. Этап 04:
                // тень камня и дерева теплее (без лиловых провалов).
                float wall = 1.0 - saturate(abs(n.y) * 1.3);
                float3 shadowTint = (wood || stone) ? lerp(_ShadowTint.rgb, float3(0.66, 0.62, 0.58), 0.55) : _ShadowTint.rgb;
                c = MapLabLight(c, n, L, saturate(_LightStrength * (1.0 + wall * 0.15) * ((wood || stone) ? 0.8 : 1.0)), _ShadowSoftness * (1.0 - wall * 0.2), _ToneBands, shadowTint, _LightTint.rgb);

                // Линия по рёбрам грани: толщина около пикселя при любом приближении.
                float2 size = i.size;
                float e;
                if (size.y < 0)
                    e = f.y;
                else
                    e = min(min(f.x, abs(size.x) - f.x), min(f.y, size.y - f.y));
                float px = max(fwidth(e), 1e-4);
                float edge = 1.0 - saturate(e / (px * 1.3) - 0.4);
                c = lerp(c, _InkColor.rgb, edge * i.meta.w * _Outline * inkWeight);
                float4 p = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.positionWS.xz / 23.0 + float2(0.11, 0.53));
                c *= 1.0 + (p.a - 0.5) * _Paper * 0.15;
                c = MapLabSaturate(c, _Saturation);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
