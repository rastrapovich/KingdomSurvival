// Диагностические линии MapLab (каркас сетки).
Shader "Kingdom Survival/MapLab/Lines"
{
    Properties
    {
        _Color ("Color", Color) = (0.1, 0.1, 0.1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+20" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            float4 Vert(float3 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            float4 Frag() : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
