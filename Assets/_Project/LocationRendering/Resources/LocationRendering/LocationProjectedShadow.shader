// ПР-12М: отброшенная тень-силуэт для мест (солнце и местные источники).
// Тот же спрайт, что у предмета или фигуры, ложится на землю от точки опоры:
// чем выше точка рисунка над землёй (_GroundY), тем дальше она уходит по
// _ShadowVector (сдвиг на единицу высоты), а ширина рисунка ложится вдоль
// _ShadowSide от точки опоры (_GroundX, _GroundY). При свете спереди сторона —
// (1, 0), как у прямой проекции; сбоку и сзади силуэт поворачивается и
// ложится на землю, а не сплющивается в линию. Края смягчаются выборкой рядом.
Shader "Kingdom Survival/Location Projected Shadow"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _ShadowVector ("Shadow Vector", Vector) = (0.5, 0.3, 0, 0)
        _ShadowSide ("Shadow Side", Vector) = (1, 0, 0, 0)
        _GroundX ("Ground X", Float) = 0
        _GroundY ("Ground Y", Float) = 0
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.5)
        _Blur ("Blur", Float) = 0.004
        _UVRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
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

            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float height : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowVector;
                float4 _ShadowSide;
                float _GroundX;
                float _GroundY;
                half4 _ShadowColor;
                float _Blur;
                float4 _UVRect;
                half4 _Color;
            CBUFFER_END

            Varyings ShadowVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                float3 positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                float3 world = TransformObjectToWorld(positionOS);
                // Часть рисунка ниже точки опоры не прижимается к земле (полоска),
                // а отсекается во фрагменте.
                float height = world.y - _GroundY;
                float side = world.x - _GroundX;
                world.xy = float2(_GroundX, _GroundY) + height * _ShadowVector.xy + side * _ShadowSide.xy;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.height = height;
                return o;
            }

            half Alpha(float2 uv)
            {
                // Только свой кадр: соседние кадры страницы атласа не просачиваются.
                uv = clamp(uv, _UVRect.xy, _UVRect.zw);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                clip(input.height);
                float b = _Blur;
                half a = Alpha(input.uv) * 0.36
                       + (Alpha(input.uv + float2(b, 0)) + Alpha(input.uv - float2(b, 0))
                        + Alpha(input.uv + float2(0, b)) + Alpha(input.uv - float2(0, b))) * 0.12
                       + (Alpha(input.uv + float2(b, b)) + Alpha(input.uv - float2(b, b))
                        + Alpha(input.uv + float2(b, -b)) + Alpha(input.uv - float2(b, -b))) * 0.04;
                return half4(_ShadowColor.rgb, a * _ShadowColor.a);
            }
            ENDHLSL
        }
    }
}
