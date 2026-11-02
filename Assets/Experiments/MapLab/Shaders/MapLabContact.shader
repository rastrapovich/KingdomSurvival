// Контактная тень опоры (фишка отряда): мягкое пятно, лежащее на рельефе.
// Умножение на цвет под ним, без записи глубины; рисуется после земли и до
// карточек, поэтому не ложится поверх самой фигурки.
Shader "Kingdom Survival/MapLab/Contact"
{
    Properties
    {
        _Color ("Тон тени", Color) = (0.36, 0.38, 0.5, 1)
        _Strength ("Сила", Range(0, 1)) = 0.55
        _Mode ("0 — пятно этапа 02, 1 — тень этапа 04", Float) = 0
        _Foot ("Опора (xy) и пятно тени (zw), мир XZ", Vector) = (0, 0, 0, 0)
        _Shape ("Радиус, растяжение по свету, направление света (zw)", Vector) = (0.5, 1.4, 1, 0)
        _Tone ("Сила тени, плотность контакта, длина градиента", Vector) = (0.4, 0.45, 0.5, 0)
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
                float _Mode;
                float4 _Foot;
                float4 _Shape;
                float4 _Tone;
            CBUFFER_END

            // Этап 04 (uv — мир XZ): пятно у точки падения + «шейка» к опоре,
            // чёткий внешний край; внутри — градиент плотности от опоры. Та же
            // формула тона, что у мировых теней в шейдере земли.
            float4 Cast(float2 p)
            {
                float2 foot = _Foot.xy, hit = _Foot.zw;
                float r = _Shape.x;
                float2 ax = normalize(_Shape.zw + 1e-5);
                float2 pe = float2(-ax.y, ax.x);
                float2 rel = p - hit;
                float u = dot(rel, ax) / (r * _Shape.y), v = dot(rel, pe) / r;
                float dE = (sqrt(u * u + v * v) - 1.0) * r;
                float2 ab = hit - foot;
                float t = saturate(dot(p - foot, ab) / max(dot(ab, ab), 1e-4));
                float dC = length(p - (foot + ab * t)) - lerp(r * 0.55, r * 0.85, t);
                float d = min(dE, dC);
                float aa = max(fwidth(d), 1e-4);
                float cast = 1.0 - smoothstep(-aa, aa, d);
                float2 g = p - lerp(foot, hit, 0.18);
                float reach = max(1e-3, _Tone.z);
                float gu = dot(g, ax) / (reach * 1.25), gv = dot(g, pe) / (reach * 0.8);
                float q = saturate(1.0 - sqrt(gu * gu + gv * gv));
                float density = q * q * (3.0 - 2.0 * q);
                float tone = cast * saturate(_Tone.x + _Tone.y * density);
                return float4(lerp(float3(1, 1, 1), _Color.rgb, tone), 1);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                o.uv = input.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                if (_Mode > 0.5)
                    return Cast(i.uv);
                float r = length(i.uv);
                float a = (1.0 - smoothstep(0.35, 1.0, r)) * _Strength;
                return float4(lerp(float3(1, 1, 1), _Color.rgb * 1.3, a), 1);
            }
            ENDHLSL
        }
    }
}
