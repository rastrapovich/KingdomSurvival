// Затемнение краёв и углов места (LocationEdgeShade): прямоугольник во весь
// рисунок места поверх всего — земли, предметов, людей. От каждого края
// внутрь на ширину _EdgeShape.x (мир) темнеет к _EdgeColor на силу
// _EdgeParams.x; углы — по скруглённому прямоугольнику с радиусом
// _EdgeShape.y; спад — степень _EdgeParams.y (1 — ровно, больше — тьма
// прижата к краю). Сила сторон — _EdgeSides (лево, право, низ, верх).
// Свет на него не действует. Лёгкий шум убирает полосы градиента.
Shader "Kingdom Survival/Location Edge Shade"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _EdgeColor ("Color", Color) = (0, 0, 0, 1)
        _EdgeParams ("Intensity, falloff power", Vector) = (0.7, 2, 0, 0)
        _EdgeShape ("Width (world), corner radius (world), half size (world)", Vector) = (3, 3, 10, 6)
        _EdgeSides ("Left, right, bottom, top", Vector) = (1, 1, 1, 1)
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex EdgeVertex
            #pragma fragment EdgeFragment

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColor;
                float4 _EdgeParams;
                float4 _EdgeShape;
                float4 _EdgeSides;
                half4 _Color;
            CBUFFER_END

            Varyings EdgeVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                return o;
            }

            // Знаковое расстояние до скруглённого прямоугольника (внутри — отрицательное).
            float RoundBox(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
            }

            half4 EdgeFragment(Varyings input) : SV_Target
            {
                float2 halfSize = _EdgeShape.zw;
                float2 p = (input.uv - 0.5) * 2 * halfSize;
                float width = max(_EdgeShape.x, 1e-4);
                float radius = clamp(_EdgeShape.y, 0, min(halfSize.x, halfSize.y));
                float inside = -RoundBox(p, halfSize, radius);
                float f = 1 - saturate(inside / width);
                float shade = pow(smoothstep(0, 1, f), max(_EdgeParams.y, 0.01));
                // Сила стороны — той, к которой точка ближе (плавно между ними).
                float sideX = p.x < 0 ? _EdgeSides.x : _EdgeSides.y;
                float sideY = p.y < 0 ? _EdgeSides.z : _EdgeSides.w;
                float toX = halfSize.x - abs(p.x), toY = halfSize.y - abs(p.y);
                float side = lerp(sideY, sideX, smoothstep(-0.5 * width, 0.5 * width, toY - toX));
                float alpha = saturate(_EdgeParams.x) * saturate(side) * shade;
                // Шум против полос градиента (пол-шага яркости).
                float noise = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                alpha = saturate(alpha + (noise - 0.5) / 255.0);
                return half4(_EdgeColor.rgb, alpha * _EdgeColor.a);
            }
            ENDHLSL
        }
    }
}
