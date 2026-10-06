// ПР-12М: тень от огня с учётом высоты. Предмет — вертикальная карточка с
// его рисунком, стоящая на опоре (_Foot) лицом к огню; огонь — точка на
// высоте (_Light.z) над своим местом (_Light.xy). Для точки земли луч от огня
// пересекает карточку: там, где рисунок непрозрачен, — тень, где прозрачен —
// луч идёт дальше (свет между ногами продолжается за фигурой). Части выше
// огня на землю не падают, части чуть ниже — до края света (_Light.w).
// Земля — плоскость экрана; высота — вертикаль рисунка. Темнота слабеет к
// краю света. Рисуется прямоугольником, накрывающим тень.
Shader "Kingdom Survival/Location Light Shadow"
{
    Properties
    {
        _MainTex ("Square", 2D) = "white" {}
        _CasterTex ("Caster Texture", 2D) = "white" {}
        _UVRect ("Caster UV Rect", Vector) = (0, 0, 1, 1)
        // x, y — сдвиг рисунка по ширине от опоры у u = 0 и u = 1; z, w — высота у v = 0 и v = 1.
        _SpriteRect ("Sprite Rect From Foot", Vector) = (-0.5, 0.5, 0, 1)
        _Foot ("Foot", Vector) = (0, 0, 0, 0)
        _Light ("Light (xy ground, z height, w radius)", Vector) = (0, 0, 1.5, 3)
        _Blur ("Blur", Float) = 0.01
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.5)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 world : TEXCOORD0;
            };

            TEXTURE2D(_CasterTex);
            SAMPLER(sampler_CasterTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _UVRect;
                float4 _SpriteRect;
                float4 _Foot;
                float4 _Light;
                float _Blur;
                half4 _ShadowColor;
            CBUFFER_END

            Varyings ShadowVertex(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.world = world.xy;
                return o;
            }

            half Alpha(float2 uv)
            {
                uv = clamp(uv, _UVRect.xy, _UVRect.zw);
                return SAMPLE_TEXTURE2D(_CasterTex, sampler_CasterTex, uv).a;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                float2 light = _Light.xy;
                float2 toFoot = _Foot.xy - light;
                float distance = length(toFoot);
                clip(distance - 1e-3);
                float2 d = toFoot / distance;
                // Карточка лицом к огню: огонь спереди (снизу) — ширина как на экране.
                float2 side = float2(d.y, -d.x);
                float2 fromLight = input.world - light;
                float along = dot(fromLight, d);
                // Только за карточкой и в радиусе света.
                clip(along - distance);
                float reach = length(fromLight);
                clip(_Light.w - reach);
                float t = distance / along;
                float height = _Light.z * (1 - t);
                float width = dot(light + fromLight * t - _Foot.xy, side);
                float u = (width - _SpriteRect.x) / (_SpriteRect.y - _SpriteRect.x);
                float v = (height - _SpriteRect.z) / max(_SpriteRect.w - _SpriteRect.z, 1e-4);
                clip(u);
                clip(1 - u);
                clip(v);
                clip(1 - v);
                float2 uv = lerp(_UVRect.xy, _UVRect.zw, float2(u, v));
                // Полутень растёт с удалением от предмета.
                float b = _Blur * (1 + (1 / t - 1) * 0.5);
                half a = Alpha(uv) * 0.4
                       + (Alpha(uv + float2(b, 0)) + Alpha(uv - float2(b, 0))
                        + Alpha(uv + float2(0, b)) + Alpha(uv - float2(0, b))) * 0.15;
                // К краю света тень слабеет вместе со светом.
                half fade = saturate(1 - reach / _Light.w);
                return half4(_ShadowColor.rgb, a * _ShadowColor.a * sqrt(fade));
            }
            ENDHLSL
        }
    }
}
