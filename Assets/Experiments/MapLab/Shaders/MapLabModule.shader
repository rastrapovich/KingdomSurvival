// Этап 05: модули оград и мостов — рисунок на локальной поверхности в мире
// (вертикальная грань vertical_front или горизонтальный настил horizontal_top)
// с UV и обычной глубиной, не билборд. Свет уже нарисован: шейдер даёт лишь
// мягкую разницу сторон к свету, тень рельефа под точкой и общий тон древесины.
// Край альфы: AlphaToMask при MSAA цели (резкий край с покрытием), иначе — порог.
Shader "Kingdom Survival/MapLab/Module"
{
    Properties
    {
        _MainTex ("Модуль (RGBA)", 2D) = "white" {}
        _ShadeTex ("Shade (R свет, G затенение)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0.05, 0.95)) = 0.5
        _MapSize ("Map size", Float) = 256
        _ShadowTint ("Shadow tint", Color) = (0.4, 0.42, 0.5, 1)
        _GroundShade ("Тень рельефа", Range(0, 1)) = 0.5
        _Tint ("Тон древесины", Color) = (0.66, 0.54, 0.39, 1)
        _TintStrength ("Сила тона", Range(0, 1)) = 0.15
        _LightDir ("Light dir", Vector) = (-0.62, 0.72, 0.06, 0)
        _Saturation ("Saturation", Float) = 1
        [Enum(Off, 0, On, 1)] _AlphaToMask ("AlphaToMask", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "MapLabCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float shade : TEXCOORD2;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                float _MapSize;
                float4 _ShadowTint;
                float _GroundShade;
                float4 _Tint;
                float _TintStrength;
                float4 _LightDir;
                float _Saturation;
                float _AlphaToMask;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 s = SAMPLE_TEXTURE2D_LOD(_ShadeTex, sampler_ShadeTex, world.xz / max(1.0, _MapSize), 0);
                o.shade = saturate((1.0 - s.r) * 0.9 + (1.0 - s.g) * 0.3);
                return o;
            }

            float4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float a = tex.a;
                if (_AlphaToMask > 0.5)
                {
                    // Резкий край с покрытием: альфа растягивается на ширину пикселя.
                    a = saturate((tex.a - _Cutoff) / max(fwidth(tex.a), 1e-4) + 0.5);
                    clip(a - 0.002);
                }
                else
                {
                    clip(tex.a - _Cutoff);
                    a = 1;
                }
                float3 c = tex.rgb;
                const float3 w = float3(0.299, 0.587, 0.114);
                float3 toned = _Tint.rgb * (dot(c, w) / max(1e-3, dot(_Tint.rgb, w)));
                c = lerp(c, toned, _TintStrength);
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 L = normalize(_LightDir.xyz);
                // Нарисованный свет сохраняется: только мягкая разница сторон.
                float side = abs(n.y) > 0.7 ? 1.0 : lerp(0.86, 1.04, saturate(dot(n, L) * 0.5 + 0.5));
                c *= side;
                c = MapLabShadow(c, i.shade * _GroundShade, _ShadowTint.rgb);
                c = MapLabSaturate(c, _Saturation);
                return float4(c, a);
            }
            ENDHLSL
        }
    }
}
