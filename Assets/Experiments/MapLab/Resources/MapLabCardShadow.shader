// Силуэтная тень карточки на глобальной карте: тот же рисунок и та же сетка, что
// у карточки (MapLabArtCard), но основание остаётся на земле, а верх уходит по
// свету. Проход без LightMode — его рисует действующий Renderer2D после рельефа
// (очередь выше Geometry) и до карточек (AlphaTest). Проход «SilhouetteShadow»
// карточки (арена) для карты не годится: Renderer2D рисует теги по очереди, и
// рельеф, имеющий проход без тега, ложится поверх теней с тегом Universal2D.
Shader "Kingdom Survival/MapLab/Card Shadow"
{
    Properties
    {
        _MainTex ("Рисунок (RGBA)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0.05, 0.95)) = 0.5
        _SilShadow ("xy смещение по земле на единицу высоты, z плотность, w вкл", Vector) = (0, 0, 0, 0)
        _SilShadowColor ("Цвет тени", Color) = (0.07, 0.09, 0.03, 1)
        _SilShadowLift ("Подъём над рельефом, ед.", Float) = 0.05
        _SilShadowSoft ("Мягкость края: сдвиг мипа рисунка (0 — чёткий силуэт)", Range(0, 5)) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Assets/Experiments/MapLab/Shaders/MapLabCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                float4 _SilShadow;
                float4 _SilShadowColor;
                float _SilShadowLift;
                float _SilShadowSoft;
            CBUFFER_END

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

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                if (_SilShadow.w < 0.5)
                {
                    o.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                    return o;
                }
                float3 basePoint = TransformObjectToWorld(input.positionOS);
                // Основание на месте и вдоль кадра, верх рисунка — по свету
                // (часть ниже опоры лежит на земле в точке опоры).
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                camRight = normalize(float3(camRight.x, 0, camRight.z) + 1e-5);
                float height = max(0.0, input.offset.y);
                float3 world = basePoint + camRight * input.offset.x + float3(_SilShadow.x, 0.0, _SilShadow.y) * height;
                world.y += _SilShadowLift;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                // Небольшое размытие края: силуэт берётся из более мелкого мипа рисунка.
                float a = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, i.uv, _SilShadowSoft).a;
                float ramp = 0.08 + 0.07 * _SilShadowSoft;
                a = smoothstep(_Cutoff - ramp, _Cutoff + ramp, a) * _SilShadow.z;
                clip(a - 0.004);
                return float4(_SilShadowColor.rgb, a);
            }
            ENDHLSL
        }
    }
}
