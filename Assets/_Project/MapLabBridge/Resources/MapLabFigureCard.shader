// 12У-H: фигура героя в сцене живой карты MapLab. Карточка строится так же,
// как карточки деревьев и домов (MapLabArtCard: тот же наклон и та же
// глубина по опоре), поэтому дерево, чей ствол ближе к зрителю, закрывает
// фигуру, а фигура перед стволом закрывает дерево. Второй материал с
// ZTest Greater рисует контур там, где фигура закрыта. Плоская тень —
// та же карточка с нулевыми смещениями (лежит на земле).
Shader "Kingdom Survival/MapLab/Figure Card"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.35
        _CardLean ("Card lean", Range(0, 1)) = 0.3
        _DepthMargin ("Depth margin", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 offset : TEXCOORD1;   // x — вправо по кадру, y — вверх от опоры (мир)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _CardLean;
                float _DepthMargin;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 basePoint = TransformObjectToWorld(input.positionOS);
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 camUp = normalize(UNITY_MATRIX_V[1].xyz);
                float3 toCamera = normalize(UNITY_MATRIX_V[2].xyz);
                float3 forward = -toCamera;
                camRight = normalize(float3(camRight.x, 0, camRight.z) + 1e-5);
                float3 up = normalize(lerp(float3(0, 1, 0), camUp, _CardLean));
                float screenScale = 1.0 / max(0.25, dot(up, camUp));
                float y = input.offset.y;
                float sinPitch = max(0.05, -forward.y);
                float cosPitch = length(forward.xz);
                float need = max(0.0, cosPitch / sinPitch - screenScale * dot(up, forward));
                float push = max(0.0, -y) * need + _DepthMargin;
                float3 world = basePoint + camRight * input.offset.x + up * y * screenScale + toCamera * push;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                return tex * _Color;
            }
            ENDHLSL
        }
    }
}
