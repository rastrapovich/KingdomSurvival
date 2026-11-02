// Пучки травы этапа 04: тонкие сужающиеся штрихи, развёрнутые к камере,
// цвет — в вершинах (местный луг, часть штрихов темнее). Толщина — в мире с
// пределами в пикселях; на общем виде (мало пикселей на единицу) штрихи
// плавно гаснут, вблизи — читаются. Без записи глубины, после земли и пера.
Shader "Kingdom Survival/MapLab/Grass"
{
    Properties
    {
        _Width ("Толщина у корня, ед.", Float) = 0.045
        _MinPx ("Наименьшая толщина, пикс.", Float) = 0.8
        _MaxPx ("Наибольшая толщина, пикс.", Float) = 2.6
        _FadePx ("Появление: пикселей на единицу (начало, полная)", Vector) = (11, 24, 0, 0)
        _Opacity ("Непрозрачность", Range(0, 1)) = 0.9
        _DepthPush ("Сдвиг к камере, ед.", Float) = 0.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+45" "RenderPipeline" = "UniversalPipeline" }

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
                float2 uv : TEXCOORD0;     // x — сторона −1..1, y — доля высоты штриха
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float fade : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Width;
                float _MinPx;
                float _MaxPx;
                float4 _FadePx;
                float _Opacity;
                float _DepthPush;
            CBUFFER_END

            float _MapLabPixelScale;

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(input.positionOS);
                float3 side = normalize(UNITY_MATRIX_V[0].xyz);
                float4 c0 = TransformWorldToHClip(p);
                float4 c1 = TransformWorldToHClip(p + side);
                float pxPerUnit = max(1e-3, length((c1.xy / c1.w - c0.xy / c0.w) * 0.5 * _ScreenParams.xy));
                float scale = _MapLabPixelScale > 0.0 ? _MapLabPixelScale : 1.0;
                float widthPx = clamp(_Width * pxPerUnit, _MinPx * scale, _MaxPx * scale) * (1.0 - input.uv.y * 0.8);
                float halfWidth = 0.5 * widthPx / pxPerUnit;
                float3 toCamera = normalize(UNITY_MATRIX_V[2].xyz);
                p += side * input.uv.x * halfWidth + toCamera * _DepthPush;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = input.uv;
                o.color = input.color;
                o.fade = smoothstep(_FadePx.x * scale, _FadePx.y * scale, pxPerUnit);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float aa = max(1e-3, fwidth(i.uv.x)) * 1.2;
                float cover = saturate((1.0 - abs(i.uv.x)) / aa);
                float a = cover * i.fade * _Opacity;
                clip(a - 0.004);
                return float4(i.color.rgb, a);
            }
            ENDHLSL
        }
    }
}
