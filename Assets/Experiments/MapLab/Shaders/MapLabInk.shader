// Тушевая линия этапа 03 (кромки дорог и троп): лента на рельефе, ширина —
// общий профиль пера (мир на контрольном масштабе, ограничение в экранных
// пикселях), рваный край от мировых координат, разрывы — в вершинах.
// Без записи глубины; рисуется после земли и до карточек, поэтому дерево и
// дом перед дорогой закрывают линию, а сама линия не висит над землёй:
// вершины лежат на рельефе и лишь чуть сдвинуты к камере.
Shader "Kingdom Survival/MapLab/Ink"
{
    Properties
    {
        _Color ("Перо (sRGB)", Color) = (0.33, 0.25, 0.16, 1)
        _Width ("Толщина, ед. мира", Float) = 0.085
        _MinPx ("Наименьшая толщина, пикс.", Float) = 1.1
        _MaxPx ("Наибольшая толщина, пикс.", Float) = 4.5
        _Opacity ("Непрозрачность", Range(0, 1)) = 0.85
        _Grain ("Рваность края", Range(0, 1)) = 0.35
        _DepthPush ("Сдвиг к камере, ед.", Float) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+40" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "MapLabCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;     // x — сторона −1..1, y — непрозрачность вершины
                float2 side : TEXCOORD1;   // направление поперёк линии (мир XZ)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Width;
                float _MinPx;
                float _MaxPx;
                float _Opacity;
                float _Grain;
                float _DepthPush;
            CBUFFER_END

            // Экспорт с суперсэмплингом рисует в больший кадр: пределы в пикселях
            // масштабируются, чтобы итог совпадал с экраном.
            float _MapLabPixelScale;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(input.positionOS);
                float3 side = float3(input.side.x, 0, input.side.y);
                float4 c0 = TransformWorldToHClip(p);
                float4 c1 = TransformWorldToHClip(p + side);
                float2 d = (c1.xy / c1.w - c0.xy / c0.w) * 0.5 * _ScreenParams.xy;
                float pxPerUnit = max(1e-3, length(d));
                float scale = _MapLabPixelScale > 0.0 ? _MapLabPixelScale : 1.0;
                float widthPx = clamp(_Width * pxPerUnit, _MinPx * scale, _MaxPx * scale);
                float halfWidth = 0.5 * widthPx / pxPerUnit;
                float3 toCamera = normalize(UNITY_MATRIX_V[2].xyz);
                p += side * input.uv.x * halfWidth + toCamera * _DepthPush;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = input.uv;
                o.world = p.xz;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float rough = abs(i.uv.x) + (ValueNoise(i.world * 9.0) - 0.5) * _Grain * 0.7 + (ValueNoise(i.world * 31.0) - 0.5) * _Grain * 0.35;
                float aa = max(1e-3, fwidth(i.uv.x)) * 1.2;
                float cover = saturate((1.0 - rough) / aa);
                float a = cover * saturate(i.uv.y) * _Opacity;
                clip(a - 0.004);
                return float4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
