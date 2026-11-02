// Карточки нарисованных объектов этапа 02 (обычный RGBA-цвет, не packed
// atlas): RGB рисунка сохраняется, alpha управляет вырезанием. Свет и объём уже
// нарисованы — второго Lambert нет; общий тон тени рельефа под опорой и мягкий
// оттенок палитры регулируются отдельно (0 — рисунок без изменений).
//
// Глубина: карточка стоит в опоре и разворачивается к фиксированной камере
// (ствол вертикален). Часть рисунка ниже опоры (видимое основание дома,
// плинт фигурки) сдвигается к камере вдоль луча зрения ровно настолько, чтобы
// не уйти под землю. В ортографии такой сдвиг не меняет положения на экране.
Shader "Kingdom Survival/MapLab/Art Card"
{
    Properties
    {
        _MainTex ("Рисунок (RGBA)", 2D) = "white" {}
        _ShadeTex ("Shade (R свет, G затенение)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0.05, 0.95)) = 0.5
        _CardLean ("Card lean", Range(0, 1)) = 0.35
        _TintColor ("Тон палитры", Color) = (0.5, 0.5, 0.4, 1)
        _TintStrength ("Сила оттенка палитры", Range(0, 1)) = 0
        _ShadowTint ("Shadow tint", Color) = (0.4, 0.42, 0.56, 1)
        _GroundShade ("Тень рельефа под опорой", Range(0, 1)) = 0.6
        _MapSize ("Map size", Float) = 256
        _DepthMargin ("Запас глубины к камере", Float) = 0.6
        _Saturation ("Saturation", Float) = 1
        _AlphaView ("Диагностика альфы: 0 нет, 1 исходная, 2 после clip", Float) = 0
        _Ghost ("Призрачный контур", Float) = 0
        _GhostColor ("Цвет контура", Color) = (0.95, 0.85, 0.6, 0.45)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "MapLabCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 offset : TEXCOORD1;   // x — вправо по кадру, y — вверх от опоры (мир)
                float4 color : COLOR;        // r — яркость/1.25, g — сила оттенка, b — доля тени рельефа
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float shade : TEXCOORD2;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                float _CardLean;
                float4 _TintColor;
                float _TintStrength;
                float4 _ShadowTint;
                float _GroundShade;
                float _MapSize;
                float _DepthMargin;
                float _Saturation;
                float _AlphaView;
                float _Ghost;
                float4 _GhostColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 basePoint = TransformObjectToWorld(input.positionOS);
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 camUp = normalize(UNITY_MATRIX_V[1].xyz);
                float3 toCamera = normalize(UNITY_MATRIX_V[2].xyz);
                float3 forward = -toCamera;
                camRight = normalize(float3(camRight.x, 0, camRight.z) + 1e-5);
                float3 up = normalize(lerp(float3(0, 1, 0), camUp, _CardLean));
                // Экранная высота карточки не зависит от наклона камеры.
                float screenScale = 1.0 / max(0.25, dot(up, camUp));
                float y = input.offset.y;
                float sinPitch = max(0.05, -forward.y);
                float cosPitch = length(forward.xz);
                // Насколько земля перед опорой ближе к камере, чем точка
                // карточки на той же экранной высоте ниже опоры.
                float need = max(0.0, cosPitch / sinPitch - screenScale * dot(up, forward));
                float push = max(0.0, -y) * need + _DepthMargin;
                float3 world = basePoint + camRight * input.offset.x + up * y * screenScale + toCamera * push;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.color = input.color;
                float4 s = SAMPLE_TEXTURE2D_LOD(_ShadeTex, sampler_ShadeTex, basePoint.xz / max(1.0, _MapSize), 0);
                o.shade = saturate((1.0 - s.r) * 0.9 + (1.0 - s.g) * 0.35);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                if (_AlphaView > 0.5 && _AlphaView < 1.5)
                    return float4(tex.aaa, 1);
                if (_AlphaView > 1.5)
                {
                    // Отсечённое — красной штриховкой, оставшееся — исходным цветом.
                    float hatch = frac((i.positionCS.x + i.positionCS.y) / 8.0) < 0.5 ? 1 : 0.6;
                    if (tex.a < _Cutoff)
                    {
                        clip(tex.a - 0.02);
                        return float4(0.85 * hatch, 0.1, 0.1, 1);
                    }
                    return float4(tex.rgb, 1);
                }
                clip(tex.a - _Cutoff);
                if (_Ghost > 0.5)
                    return _GhostColor;
                float3 c = tex.rgb;
                const float3 w = float3(0.299, 0.587, 0.114);
                float tint = saturate(_TintStrength * i.color.g * 2.0);
                float3 toned = _TintColor.rgb * (dot(c, w) / max(1e-3, dot(_TintColor.rgb, w)));
                c = lerp(c, toned, tint * 0.5);
                c *= i.color.r * 1.25;
                c = MapLabShadow(c, i.shade * i.color.b * _GroundShade, _ShadowTint.rgb);
                c = MapLabSaturate(c, _Saturation);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
