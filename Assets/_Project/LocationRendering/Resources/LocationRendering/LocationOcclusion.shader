// ПР-12М: окклюзия в тени — мягкое пятно у основания предмета или человека.
// Темнит то, что под ним, умножением: земля только темнеет (не светлеет и
// не перекрашивается); с другими тенями не складывается — берётся самая
// тёмная (LocationShadowResolve). Сила — альфа
// цвета рендерера, форма — альфа рисунка пятна.
Shader "Kingdom Survival/Location Occlusion"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        // Без наложения: доля света по минимуму в альфу кадра (LocationShadowResolve).
        ColorMask A
        BlendOp Min
        Blend One One
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex OcclusionVertex
            #pragma fragment OcclusionFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings OcclusionVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                o.color = input.color * unity_SpriteColor;
                return o;
            }

            half4 OcclusionFragment(Varyings input) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * saturate(input.color.a);
                return half4(0, 0, 0, 1 - a);
            }
            ENDHLSL
        }
    }
}
