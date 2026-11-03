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
        _Stage05 ("Этап 05: спокойная основа и штрихи течения", Float) = 0
        _Base ("Этап 05: основа (серо-синяя)", Color) = (0.4, 0.48, 0.53, 1)
        _WaterInk ("Этап 05: синяя тушь", Color) = (0.2, 0.27, 0.36, 1)
        _Strokes05 ("Этап 05: штрихи реки, озера", Vector) = (0.55, 0.22, 0, 0)
        _ShoreTex ("Этап 05: высота над водой (общая граница берега)", 2D) = "white" {}
        _ShoreRect ("Этап 05: мир → uv берега", Vector) = (0, 0, 0.004, 0.004)
        _DepthPush ("Этап 05: сдвиг к камере", Float) = 0
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
                float3 channel : TEXCOORD2;   // этап 05: x — вдоль русла, y — поперёк (мир), z — 1 река / 0 озеро
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 flow : TEXCOORD1;
                float3 channel : TEXCOORD2;
            };

            TEXTURE2D(_HeightTex); SAMPLER(sampler_HeightTex);
            TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex);
            TEXTURE2D(_WaterTex); SAMPLER(sampler_WaterTex);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
            TEXTURE2D(_ShoreTex); SAMPLER(sampler_ShoreTex);

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
                float _Stage05;
                float4 _Base;
                float4 _WaterInk;
                float4 _Strokes05;
                float4 _ShoreRect;
                float _DepthPush;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS);
                // Этап 05: край воды задаёт общая граница берега (clip ниже), а не
                // пересечение плоскости с треугольниками рельефа — поверхность чуть
                // ближе к камере, чтобы граница не зависела от диагоналей сетки.
                float3 toCamera = normalize(UNITY_MATRIX_V[2].xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS + toCamera * _DepthPush);
                o.flow = input.flow;
                o.channel = input.channel;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise21(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            // Короткие штрихи в координатах (along, across): ряды поперёк, в ряду —
            // штрихи разной длины по ячейкам; группы — по медленному шуму.
            // Ширина края — в пикселях (fwidth), без муара вдали.
            float StrokeField(float2 q, float rowSpacing, float cellLength, float chance, out float inkMask)
            {
                float row = floor(q.y / rowSpacing);
                float rowShift = Hash21(float2(row, 7.1)) * cellLength;
                float u = (q.x + rowShift) / cellLength;
                float cell = floor(u);
                float h = Hash21(float2(cell, row));
                float h2 = Hash21(float2(cell + 31.7, row - 3.3));
                float group = Noise21(float2(q.x / (cellLength * 3.1), row * 0.37));
                float present = step(h, chance * lerp(0.35, 1.6, group));
                float lengthT = lerp(0.18, 0.42, h2);
                float fu = abs(frac(u) - 0.5);
                float fv = abs(frac(q.y / rowSpacing) - 0.5) * rowSpacing;
                float2 w = fwidth(q);
                float halfW = lerp(0.045, 0.085, h2);
                float along = saturate((lengthT - fu) * cellLength / max(1e-3, w.x * 1.2));
                float across = saturate((halfW - fv) / max(1e-3, w.y * 1.0));
                float s = present * along * across;
                // Редкие длинные тонкие линии синей туши.
                inkMask = step(0.965, Hash21(float2(cell * 0.5, row + 91.0))) * saturate((0.46 - fu) * cellLength / max(1e-3, w.x * 1.2)) * saturate((0.03 - fv) / max(1e-3, w.y));
                return s;
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

                if (_Stage05 > 0.5)
                {
                    float above = SAMPLE_TEXTURE2D(_ShoreTex, sampler_ShoreTex, (world - _ShoreRect.xy) * _ShoreRect.zw).r;
                    clip(-above);
                    float deep05 = smoothstep(0.05, 2.2, depth);
                    float3 c05 = _Base.rgb * lerp(1.08, 0.86, deep05);
                    // Мелководье у кромки — светлее и теплее по общей границе.
                    float shallow = 1.0 - smoothstep(0.02, 0.45, -above);
                    c05 = lerp(c05, lerp(_Base.rgb, _ShoreColor.rgb, 0.35) * 1.1, shallow * 0.55);
                    float river = i.channel.z;
                    float ink05 = 0;
                    float s05;
                    if (river > 0.5)
                    {
                        float2 q = float2(i.channel.x, i.channel.y);
                        s05 = StrokeField(q, 0.95, 3.2, 0.42, ink05) * _Strokes05.x;
                    }
                    else
                    {
                        // Озеро: общее медленное направление, реже и спокойнее; середина пустая.
                        float2 dir = normalize(float2(0.94, 0.34));
                        float2 q = float2(dot(world, dir), dot(world, float2(-dir.y, dir.x)));
                        s05 = StrokeField(q, 1.3, 4.2, 0.22, ink05) * _Strokes05.y * lerp(1.0, 0.35, deep05);
                        ink05 *= 0.6;
                    }
                    if (mode != 0)
                        s05 = 0;
                    c05 = lerp(c05, lerp(c05, _Stroke.rgb, 0.75), saturate(s05) * 0.55);
                    c05 = lerp(c05, _WaterInk.rgb, saturate(ink05) * 0.7 * _Outline);
                    float4 shade05 = SAMPLE_TEXTURE2D(_ShadeTex, sampler_ShadeTex, uv);
                    c05 = MapLabShadow(c05, (1.0 - shade05.r) * _CastShadow * 0.7, _ShadowTint.rgb);
                    float4 p05 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, world / 23.0 + float2(0.11, 0.53));
                    c05 *= 1.0 + (p05.a - 0.5) * _Paper * 0.1;
                    return float4(MapLabSaturate(c05, _Saturation), 1);
                }

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
