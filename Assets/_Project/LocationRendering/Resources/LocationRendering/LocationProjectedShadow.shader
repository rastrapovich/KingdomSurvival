// ПР-12М: отброшенная тень-силуэт для мест (солнце и местные источники).
// Тот же спрайт, что у предмета или фигуры, ложится на землю от точки опоры:
// чем выше точка рисунка над землёй (_GroundY), тем дальше она уходит по
// _ShadowVector (сдвиг на единицу высоты). Края смягчаются выборкой рядом.
// Опора выше низа рисунка — _Lift (см. вершинную программу).
Shader "Kingdom Survival/Location Projected Shadow"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _ShadowVector ("Shadow Vector", Vector) = (0.5, 0.3, 0, 0)
        _GroundY ("Ground Y", Float) = 0
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.5)
        _Blur ("Blur", Float) = 0.004
        _UVRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        _Lift ("Base lift share", Float) = 0
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        // Без наложения: в альфу кадра — доля света по минимуму, землю
        // затемняет один раз LocationShadowResolve.
        ColorMask A
        BlendOp Min
        Blend One One
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
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
                float height : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowVector;
                float _GroundY;
                half4 _ShadowColor;
                float _Blur;
                float4 _UVRect;
                float _Lift;
                half4 _Color;
            CBUFFER_END

            Varyings ShadowVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                float3 positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                float3 world = TransformObjectToWorld(positionOS);
                // Часть рисунка ниже точки опоры не прижимается к земле (полоска),
                // а отсекается во фрагменте.
                // _Lift — доля высоты рисунка между его низом (_GroundY) и опорой
                // (камень: опора в середине основания). Низ прижат к земле, верх
                // ложится туда же, куда лёг бы от опоры: одна тень, прилегающая к
                // предмету снизу, и в полдень, и при низком солнце. 0 — от опоры.
                // Опора поднята на заметную долю высоты (от 15 до 30 % и выше) —
                // низкий широкий предмет (камень): почти весь рисунок — его верх,
                // уходящий вглубь, а не высота. Тогда тень по вертикали не
                // сплющивается: её верх идёт от верха предмета (и выше — по
                // солнцу), а не от опоры. У деревьев и людей опора внизу — тень
                // как от столба.
                float height = world.y - _GroundY;
                float rise = 1 - _Lift;
                float depth = smoothstep(0.15, 0.3, _Lift);
                world.x += height * rise * _ShadowVector.x;
                world.y = _GroundY + height * (lerp(_Lift, 1, depth) + rise * _ShadowVector.y);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.height = height;
                return o;
            }

            half Alpha(float2 uv)
            {
                // Только свой кадр: соседние кадры страницы атласа не просачиваются.
                uv = clamp(uv, _UVRect.xy, _UVRect.zw);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                clip(input.height);
                float b = _Blur;
                half a = Alpha(input.uv) * 0.36
                       + (Alpha(input.uv + float2(b, 0)) + Alpha(input.uv - float2(b, 0))
                        + Alpha(input.uv + float2(0, b)) + Alpha(input.uv - float2(0, b))) * 0.12
                       + (Alpha(input.uv + float2(b, b)) + Alpha(input.uv - float2(b, b))
                        + Alpha(input.uv + float2(b, -b)) + Alpha(input.uv - float2(b, -b))) * 0.04;
                return half4(0, 0, 0, 1 - a * _ShadowColor.a);
            }
            ENDHLSL
        }
    }
}
