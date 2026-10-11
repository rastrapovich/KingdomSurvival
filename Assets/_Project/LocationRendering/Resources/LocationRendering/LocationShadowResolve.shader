// Тени на земле без наложения. Силуэты от солнца, свои тени огня и пятна
// окклюзии не темнят землю сами: каждая пишет в альфу кадра долю света
// (1 − темнота) по минимуму (LocationProjectedShadow, LocationOcclusion), так
// что в каждой точке остаётся только самая тёмная из них. Этот шейдер — два
// прямоугольника во весь кадр вокруг них по порядку рисования:
//   «перед тенями» — только альфа кадра = 1 (света ещё ничто не закрыло);
//   «после теней» — земля один раз затемняется к цвету тени
//                 _ShadowTint на (1 − альфа), альфа кадра снова 1.
// Цель без альфы (HDR-буфер R11G11B10) читает альфу как 1 — тогда теней нет,
// но и кадр не портится. Поэтому камеры места рисуют без HDR.
Shader "Kingdom Survival/Location Shadow Resolve"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _ShadowTint ("Shadow Tint", Color) = (0, 0, 0, 1)
        [HideInInspector] _KsColorMask ("Color mask", Float) = 1
        [HideInInspector] _KsSrc ("Src blend", Float) = 1
        [HideInInspector] _KsDst ("Dst blend", Float) = 0
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        struct Attributes
        {
            float3 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        CBUFFER_START(UnityPerMaterial)
            half4 _ShadowTint;
            half4 _Color;
        CBUFFER_END

        Varyings ResolveVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(input.positionOS);
            return o;
        }
        ENDHLSL

        // Порядок прохода выбирается материалом: у «перед» — только запись
        // альфы, у «после» — смешивание по альфе кадра.
        Pass
        {
            ColorMask [_KsColorMask]
            Blend [_KsSrc] [_KsDst], One Zero

            HLSLPROGRAM
            #pragma vertex ResolveVertex
            #pragma fragment ResolveFragment
            #pragma multi_compile_instancing

            half4 ResolveFragment(Varyings input) : SV_Target
            {
                return half4(_ShadowTint.rgb, 1);
            }
            ENDHLSL
        }
    }
}
