// ПР-12М: окклюзия на самом предмете — затемнение низа рисунка у земли со
// стороны тени. Рисуется тем же спрайтом поверх предмета и умножает его
// цвет к цвету теней _OccColor (чёрный — просто темнее). Высота затемнения
// _OccShape.w (ед. мира) отсчитывается от нижнего края силуэта в каждом
// столбце рисунка (_OccUv: UV на единицу мира поперёк, низ кадра, UV на
// единицу мира по высоте; просветы между травинками — сплошной пучок), только у
// земли (_OccBase.y — опора); поперёк — по направлению тени _OccShape.xy
// (полуширина основания _OccShape.z). Сила — _OccBase.z.
Shader "Kingdom Survival/Location Occlusion Object"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _OccBase ("Base (x, y), strength", Vector) = (0, 0, 0.3, 0)
        _OccShape ("Shadow direction (x, y), half width, fade height", Vector) = (0, 0, 1, 1)
        _KsBend("Bend: top offset xy (world), drop (world)", Vector) = (0, 0, 0, 0)
        _OccUv("Sprite uv: u per world unit, min y, max y, v per world unit", Vector) = (0, 0, 1, 1)
        _OccColor("Occlusion color", Color) = (0, 0, 0, 1)
        _OccMask("Base frame (occlusion field)", 2D) = "white" {}
        _OccRect("Current frame uv rect", Vector) = (0, 0, 1, 1)
        _OccBaseRect("Base frame uv rect", Vector) = (0, 0, 1, 1)
        _KsBendShape("Bend shape: root y, 1/height (object), pinned, curve", Vector) = (0, 0, 0, 0)
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
            TEXTURE2D(_OccMask);
            SAMPLER(sampler_OccMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _OccBase;
                float4 _OccShape;
                float4 _KsBend;
                float4 _OccUv;
                half4 _OccColor;
                float4 _OccRect;
                float4 _OccBaseRect;
                float4 _KsBendShape;
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
                // Гнущийся рисунок (реакция на проход): затемнение гнётся вместе
                // с ним тем же законом, что в Location Sprite Adjust, а тёмный
                // низ отсчитывается от несогнутой точки — «прилип» к растению.
                float3 bent = world;
                if (_KsBendShape.y > 0)
                {
                    float s = saturate(((position.y - _KsBendShape.x) * _KsBendShape.y - clamp(_KsBendShape.z, 0, 0.95)) / (1 - clamp(_KsBendShape.z, 0, 0.95)));
                    float weight = pow(s, _KsBendShape.w > 0 ? _KsBendShape.w : 2);
                    float3 up = TransformObjectToWorldDir(float3(0, 1, 0));
                    bent.xy += (_KsBend.xy - up.xy * _KsBend.z) * weight;
                }
                o.positionCS = TransformWorldToHClip(bent);
                o.uv = input.uv;
                o.world = world.xy;
                return o;
            }

            half4 OcclusionFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                float2 offset = input.world - _OccBase.xy;
                // Высота затемнения (ед. мира) — от нижнего края силуэта в этом
                // столбце рисунка, а не от горизонтали опоры: у камня темнеет
                // округлый низ, у травы — низ пучка, без ровной полосы.
                float fade = max(1e-4, _OccShape.w);
                float stepWorld = fade / 8.0;
                float stepV = stepWorld * _OccUv.w;
                float spread = 2 * stepWorld * _OccUv.x;
                // Непрерывно: каждый шаг вниз добавляет столько, насколько под ним
                // непрозрачно подряд, — без скачков на шаг, когда травинка сдвигается.
                // Точка этого кадра → та же точка первого кадра (поле неподвижно).
                float2 baseUv = _OccBaseRect.xy + (input.uv - _OccRect.xy) / max(1e-6, _OccRect.zw) * _OccBaseRect.zw;
                float edge = 0;
                float run = 1;
                [unroll] for (int i = 1; i <= 8; i++)
                {
                    float v = baseUv.y - stepV * i;
                    // Три выборки поперёк (на два шага в стороны): просветы между травинками
                    // — сплошной пучок, край не скачет от кадра к кадру анимации.
                    half below = 0;
                    if (v >= _OccUv.y)
                    {
                        below = max(SAMPLE_TEXTURE2D_LOD(_OccMask, sampler_OccMask, float2(baseUv.x, v), 0).a,
                            max(SAMPLE_TEXTURE2D_LOD(_OccMask, sampler_OccMask, float2(baseUv.x - spread, v), 0).a,
                                SAMPLE_TEXTURE2D_LOD(_OccMask, sampler_OccMask, float2(baseUv.x + spread, v), 0).a));
                    }
                    run *= saturate((below - 0.1) / 0.5);
                    edge += stepWorld * run;
                }
                float height = 1 - smoothstep(0, fade, edge);
                // Только у земли: нижний край высоко над опорой (крона) не темнеет.
                float ground = 1 - smoothstep(0.5 * fade, 2.5 * fade, offset.y);
                // Направление тени с выраженностью 0..1: без тени — ровно по низу,
                // с полной — сторона тени тёмная, сторона к свету светлая; между —
                // плавно.
                float2 direction = _OccShape.xy;
                float side = saturate(1 - 0.5 * length(direction) + 0.5 * dot(offset, direction) / max(1e-4, _OccShape.z));
                half dark = saturate(_OccBase.z) * height * ground * side * alpha;
                // К цвету теней: чёрный — просто темнее (цвет предмета сохраняется).
                return half4(lerp(half3(1, 1, 1), _OccColor.rgb, dark), 1);
            }
            ENDHLSL
        }
    }
}
