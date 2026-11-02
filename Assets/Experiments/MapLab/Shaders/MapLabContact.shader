// Контактная тень опоры (фишка отряда): мягкое пятно, лежащее на рельефе.
// Умножение на цвет под ним, без записи глубины; рисуется после земли и до
// карточек, поэтому не ложится поверх самой фигурки.
Shader "Kingdom Survival/MapLab/Contact"
{
    Properties
    {
        _Color ("Тон тени", Color) = (0.36, 0.38, 0.5, 1)
        _Strength ("Сила", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+60" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Off
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "MapLabCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;   // -1..1 по пятну
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Strength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                o.uv = input.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                float a = (1.0 - smoothstep(0.35, 1.0, r)) * _Strength;
                return float4(lerp(float3(1, 1, 1), _Color.rgb * 1.3, a), 1);
            }
            ENDHLSL
        }
    }
}
