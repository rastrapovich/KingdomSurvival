// Рисованная стена уступа (пакет скал ревизии 06): рисунок на непрерывной
// ленте вдоль обрыва, свет уже нарисован — второго освещения нет, только
// мягкий тон по стороне к свету и тень рельефа. Стены, обращённые от
// камеры, не рисуются (по нормали наружу всего куска): там видна площадка.
// Подошва и травяная кромка лежат на земле — их чуть ближе к камере, чтобы
// не мерцать с рельефом. Мягкая альфа рисунка (растворение подошвы и
// кромки) — смешиванием; стена рисуется после карточек (они уже в кадре).
Shader "Kingdom Survival/MapLab/Cliff"
{
    Properties
    {
        _MainTex ("Стена (RGBA)", 2D) = "white" {}
        _ShadeTex ("Shade (R свет, G затенение)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0.05, 0.95)) = 0.5
        _MapSize ("Map size", Float) = 256
        _ShadowTint ("Shadow tint", Color) = (0.62, 0.58, 0.5, 1)
        _GroundShade ("Тень рельефа", Range(0, 1)) = 0.45
        _LightDir ("Light dir", Vector) = (-0.62, 0.72, 0.06, 0)
        _Saturation ("Saturation", Float) = 1
        _DepthPush ("Сдвиг кромки и подошвы к камере", Float) = 0.08
        [Enum(Off, 0, On, 1)] _AlphaToMask ("AlphaToMask", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest+20" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
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
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 outward : TEXCOORD1;   // наружу от стены (мир XZ)
                float4 color : COLOR;         // a — растворение концов
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float facing : TEXCOORD1;
                float tone : TEXCOORD2;
                float shade : TEXCOORD3;
                float fade : TEXCOORD4;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                float _MapSize;
                float4 _ShadowTint;
                float _GroundShade;
                float4 _LightDir;
                float _Saturation;
                float _DepthPush;
                float _AlphaToMask;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                float3 toCamera = normalize(UNITY_MATRIX_V[2].xyz);
                float3 n = TransformObjectToWorldNormal(input.normalOS);
                // Лежащие на земле полосы — чуть к камере (без мерцания с рельефом).
                float flat = step(0.7, n.y);
                o.positionCS = TransformWorldToHClip(world + toCamera * _DepthPush * flat);
                o.uv = input.uv;
                float3 outward = normalize(float3(input.outward.x, 0, input.outward.y) + 1e-5);
                o.facing = dot(outward, toCamera);
                float3 L = normalize(_LightDir.xyz);
                o.tone = lerp(0.9, 1.03, saturate(dot(outward, normalize(float3(L.x, 0, L.z) + 1e-5)) * 0.5 + 0.5));
                float4 s = SAMPLE_TEXTURE2D_LOD(_ShadeTex, sampler_ShadeTex, world.xz / max(1.0, _MapSize), 0);
                o.shade = saturate((1.0 - s.r) * 0.8 + (1.0 - s.g) * 0.25);
                o.fade = input.color.a;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                // Стена смотрит от камеры — не рисуется целиком (и кромка, и подошва).
                clip(i.facing + 0.06);
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                tex.a *= i.fade;
                // Почти прозрачное не пишет глубину (за ним видны дальние объекты).
                clip(tex.a - 0.04);
                float a = saturate((tex.a - 0.04) / 0.96);
                float3 c = tex.rgb * i.tone;
                c = MapLabShadow(c, i.shade * _GroundShade, _ShadowTint.rgb);
                c = MapLabSaturate(c, _Saturation);
                return float4(c, a);
            }
            ENDHLSL
        }
    }
}
