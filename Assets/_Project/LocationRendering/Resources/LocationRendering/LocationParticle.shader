// ПР-12М: частицы атмосферы места (LocationWorldRenderer.Atmosphere):
// своя форма без текстур — точка (пыль, мошкара, искры), штрих (дождь),
// лист (с прожилкой и тенью половины), клуб (пар); или своя картинка слоя.
// Цвет и прозрачность — цвет вершины частицы. Смешивание — обычное или
// сложение (искры светятся). Свет 2D на частицы не действует: их цвет
// подстраивает рендерер места по общему свету.
Shader "Hidden/KingdomSurvival/LocationParticle"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _KsShape("Shape: 0 dot, 1 streak, 2 leaf, 3 puff, 4 texture", Float) = 0
        [HideInInspector] _SrcBlend("Src", Float) = 5
        [HideInInspector] _DstBlend("Dst", Float) = 10
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _KsShape;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.color = input.color;
                o.uv = input.uv;
                return o;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                half alpha;
                half3 shade = half3(1, 1, 1);
                if (_KsShape > 3.5)
                {
                    half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    return half4(texel.rgb * input.color.rgb, texel.a * input.color.a);
                }
                else if (_KsShape > 2.5)
                {
                    // Клуб пара: мягкое пятно с неровным краем.
                    float r = length(p);
                    float wobble = sin(atan2(p.y, p.x) * 5 + input.uv.x * 3) * 0.08;
                    alpha = saturate(1 - smoothstep(0.35 + wobble, 1, r));
                    alpha *= alpha;
                }
                else if (_KsShape > 1.5)
                {
                    // Лист: вытянутый овал, заострённый к концам, прожилка и тень половины.
                    float2 q = float2(p.x * 1.9, p.y);
                    float body = 1 - (q.x * q.x + pow(abs(p.y), 1.6));
                    alpha = saturate(body * 6);
                    shade = p.x > 0 ? half3(0.82, 0.82, 0.82) : half3(1, 1, 1);
                    shade *= 1 - (1 - smoothstep(0, 0.07, abs(p.x))) * 0.35;
                }
                else if (_KsShape > 0.5)
                {
                    // Штрих дождя: узкий, к концам тает.
                    alpha = saturate(1 - abs(p.x) * 1.2) * saturate(1 - abs(p.y));
                }
                else
                {
                    // Точка: светлая середина, мягкий край.
                    float r = length(p);
                    alpha = saturate(1 - r);
                    alpha = alpha * alpha * (3 - 2 * alpha);
                }
                return half4(input.color.rgb * shade, alpha * input.color.a);
            }
            ENDHLSL
        }
    }
}
