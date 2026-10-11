// ПР-12М: окклюзия на самом предмете — затемнение низа рисунка со стороны
// тени. Рисуется тем же спрайтом поверх предмета и умножает его цвет: к
// земле (_OccBase.y) темнее, выше высоты _OccShape.w — без затемнения;
// поперёк — по направлению тени _OccShape.xy (длина — выраженность 0..1):
// сторона тени темнее, сторона к свету светлее (полуширина основания
// _OccShape.z). Без тени — ровно по низу. Сила — _OccBase.z.
Shader "Kingdom Survival/Location Occlusion Object"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _OccBase ("Base (x, y), strength", Vector) = (0, 0, 0.3, 0)
        _OccShape ("Shadow direction (x, y), half width, fade height", Vector) = (0, 0, 1, 1)
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend DstColor Zero
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _OccBase;
                float4 _OccShape;
                half4 _Color;
            CBUFFER_END

            Varyings OcclusionVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                float3 world = TransformObjectToWorld(position);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.world = world.xy;
                return o;
            }

            half4 OcclusionFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                float2 offset = input.world - _OccBase.xy;
                float height = saturate(1 - max(0, offset.y) / max(1e-4, _OccShape.w));
                // Направление тени с выраженностью 0..1: без тени — ровно по низу,
                // с полной — сторона тени тёмная, сторона к свету светлая; между —
                // плавно.
                float2 direction = _OccShape.xy;
                float side = saturate(1 - 0.5 * length(direction) + 0.5 * dot(offset, direction) / max(1e-4, _OccShape.z));
                half dark = saturate(_OccBase.z) * height * height * side * alpha;
                return half4((1 - dark).xxx, 1);
            }
            ENDHLSL
        }
    }
}
