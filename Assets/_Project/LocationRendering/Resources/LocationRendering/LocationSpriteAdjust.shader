// ПР-12Р: спрайт под 2D-светом с цветом экземпляра — сдвиг тона,
// насыщенность и яркость (в пространстве HSV, до освещения). Копия прохода
// Sprite-Lit-Default URP 17.6 (свет, нормали, маска) с этой правкой.
// Экземпляр рисунка (SpriteRenderer) — _KsHsv из блока свойств.
// Ковёр раскидки (_KS_PARTICLE, сетка из прямоугольников-«частиц»): своё у
// каждого прямоугольника в UV — TEXCOORD0.zw = тон (обороты), насыщенность;
// TEXCOORD1.xyz = яркость, отражение ±1, поворот (радианы против часовой);
// кадр — _MainTex и прямоугольник кадра _KsUvRect у группы. Спрайтовые
// свойства (цвет, отражение рендерера) у сетки не задаются.
// Свет по отдельности (_KsLight: x — солнце, y — огонь): общий свет места
// (солнце, смена суток) лежит в стиле смешивания 0, местные источники — в
// стилях 1–3 (обычный местный свет — стиль 2 «умножение с маской», без
// маски — как стиль 0). Солнце выключено — рисунок как при полном свете
// (не темнеет ночью); огонь выключен — местные источники не освещают.
// _KsLight.z = 1 — деталь земли: в проход нормалей не пишет, свет с
// нормалями ложится на неё так же, как на землю под ней.
// Реакция на проход (отдельный рисунок): _KsBend — смещение верха в мире и
// опускание, _KsBendShape — опора по Y и 1/высота растения в пространстве
// рисунка, закреплённая доля, степень изгиба; _KsBendShape.y = 0 — без прогиба.
// Эффекты рисунков окна «Обработка кадра» (глобальные значения задаёт
// LocationWorldRenderer перед кадром, без них — нули, эффектов нет):
// ветер, обводка изнутри, обесцвечивание дальнего плана, растворение.
// Свой у рисунка — _KsStyle: x — ветер (0 нет, 1 дерево, 2 растительность),
// y — роль (0 предмет, 1 человек, 2 деталь земли), w — растворение 0..1.
Shader "Kingdom Survival/Location Sprite Adjust"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        _KsHsv("Hue (turns), Saturation, Brightness", Vector) = (0, 1, 1, 0)
        _KsUvRect("Particle UV rect (offset, scale)", Vector) = (0, 0, 1, 1)
        _KsLight("Sun, fire light share, ground-lit", Vector) = (1, 1, 0, 0)
        _KsBend("Bend: top offset xy (world), drop (world)", Vector) = (0, 0, 0, 0)
        _KsBendShape("Bend shape: root y, 1/height (object), pinned, curve", Vector) = (0, 0, 0, 0)
        _KsStyle("Style: wind kind, role, -, dissolve", Vector) = (0, 0, 0, 0)
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

    // NOTE: одинаковая раскладка во всех проходах (SRP batcher).
    CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        float4 _KsHsv;
        float4 _KsUvRect;
        float4 _KsLight;
        float4 _KsBend;
        float4 _KsBendShape;
        float4 _KsStyle;
    CBUFFER_END

    // Эффекты окна «Обработка кадра» — глобальные (вне блока материала).
    // _KsWindParams: x — размах (мир), y — скорость, z — порывы, w — высота полного размаха (мир);
    // _KsWindShare: x — доля деревьев, y — растительности, z — направление −1..1, w — время (с).
    float4 _KsWindParams;
    float4 _KsWindShare;
    // _KsOutlineParams: x — толщина (пиксели рисунка), y — у людей, z — у предметов, w — включена.
    float4 _KsOutlineColor;
    float4 _KsOutlineParams;
    // _KsDistanceParams: x, y — начало и полная сила (доля высоты экрана), z — обесцвечивание, w — дымка;
    // _KsDistanceHaze: цвет дымки, a = 1 — включено.
    float4 _KsDistanceParams;
    float4 _KsDistanceHaze;
    // _KsDissolveParams: x — ширина края, y — частота шума (на единицу рисунка); _KsDissolveEdge — цвет края.
    float4 _KsDissolveParams;
    float4 _KsDissolveEdge;

    // Ветер: верх рисунка ходит от опоры (вес — высота над опорой к
    // полной высоте размаха), фаза — от положения опоры: соседние деревья
    // качаются вразнобой, порывы проходят волной.
    float KsWindShareOf(float kind)
    {
        return kind > 1.5 ? _KsWindShare.y : (kind > 0.5 ? _KsWindShare.x : 0);
    }

    float3 KsWind(float3 world, float3 origin, float weight, float share)
    {
        if (share <= 0 || _KsWindParams.x <= 0 || weight <= 0)
            return world;
        float t = _KsWindShare.w * _KsWindParams.y;
        float phase = origin.x * 0.37 + origin.y * 0.23;
        float sway = sin(t * 1.7 + phase * 2.1) * 0.6 + sin(t * 0.83 + phase * 1.3 + 1.7) * 0.4;
        float g = 0.5 + 0.5 * sin(t * 0.37 + origin.x * 0.11 - origin.y * 0.05);
        float gust = _KsWindParams.z * g * g;
        float direction = clamp(_KsWindShare.z, -1, 1);
        float push = direction * (0.5 + gust) + sway * (1 - abs(direction) * 0.6) * (0.4 + gust);
        float offset = _KsWindParams.x * share * weight * push;
        world.x += offset;
        world.y -= abs(offset) * 0.2 * weight;
        return world;
    }

    // Дальний план: доля по высоте опоры рисунка на экране (выше — дальше).
    float KsDistanceFactor(float3 baseWS)
    {
        if (_KsDistanceHaze.a < 0.5 || _KsStyle.y > 1.5)
            return 0;
        float4 clip = TransformWorldToHClip(baseWS);
        float y = clip.y / max(clip.w, 1e-5) * _ProjectionParams.x * 0.5 + 0.5;
        return smoothstep(_KsDistanceParams.x, max(_KsDistanceParams.y, _KsDistanceParams.x + 1e-3), y);
    }

    half3 KsDistance(half3 rgb, float f)
    {
        if (f <= 0)
            return rgb;
        half l = dot(rgb, half3(0.299, 0.587, 0.114));
        rgb = lerp(rgb, half3(l, l, l), saturate(_KsDistanceParams.z) * f);
        return lerp(rgb, half3(_KsDistanceHaze.rgb), saturate(_KsDistanceParams.w) * f);
    }

    float KsHash(float2 p)
    {
        return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
    }

    float KsValueNoise(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        f = f * f * (3 - 2 * f);
        return lerp(lerp(KsHash(i), KsHash(i + float2(1, 0)), f.x), lerp(KsHash(i + float2(0, 1)), KsHash(i + float2(1, 1)), f.x), f.y);
    }

    // Растворение: 0 — целый, 1 — исчез. Возвращает долю края (0..1) и
    // обнуляет альфу растворённого. Шум — по точке рисунка (кадр анимации не дрожит).
    half KsDissolve(inout half alpha, float2 objectPos)
    {
        float amount = _KsStyle.w;
        if (amount <= 0)
            return 0;
        float scale = max(_KsDissolveParams.y, 1);
        float n = KsValueNoise(objectPos * scale) * 0.65 + KsValueNoise(objectPos * scale * 2.7 + 13.1) * 0.35;
        float edge = max(_KsDissolveParams.x, 1e-3);
        float cut = amount * (1 + edge);
        if (n < cut - edge)
        {
            alpha = 0;
            return 0;
        }
        return n < cut ? 1 - (cut - n) / edge : 0;
    }

    // Обводка изнутри: край там, где рядом (на толщину) рисунок прозрачен.
    half KsOutlineMask(TEXTURE2D_PARAM(source, sourceSampler), float2 uv, half alpha)
    {
        if (_KsOutlineParams.w < 0.5 || alpha < 0.5 || _KsStyle.y > 1.5)
            return 0;
        float on = _KsStyle.y > 0.5 ? _KsOutlineParams.y : _KsOutlineParams.z;
        if (on < 0.5)
            return 0;
        float width, height;
        source.GetDimensions(width, height);
        float2 d = max(_KsOutlineParams.x, 0.5) / float2(max(width, 1), max(height, 1));
        float2 e = d * 0.7071;
        half a = SAMPLE_TEXTURE2D(source, sourceSampler, uv + float2(d.x, 0)).a;
        a = min(a, SAMPLE_TEXTURE2D(source, sourceSampler, uv - float2(d.x, 0)).a);
        a = min(a, SAMPLE_TEXTURE2D(source, sourceSampler, uv + float2(0, d.y)).a);
        a = min(a, SAMPLE_TEXTURE2D(source, sourceSampler, uv - float2(0, d.y)).a);
        a = min(a, SAMPLE_TEXTURE2D(source, sourceSampler, uv + e).a);
        a = min(a, SAMPLE_TEXTURE2D(source, sourceSampler, uv - e).a);
        a = min(a, SAMPLE_TEXTURE2D(source, sourceSampler, uv + float2(e.x, -e.y)).a);
        a = min(a, SAMPLE_TEXTURE2D(source, sourceSampler, uv + float2(-e.x, e.y)).a);
        return saturate((0.5 - a) * 3) * _KsOutlineColor.a;
    }

    // Реакция на проход у отдельного рисунка (не ковра: у ковра вершины
    // гнутся на CPU тем же законом — LocationGrassBend). Вес по высоте над
    // опорой в долях высоты растения: ниже закреплённой части — 0, к верху —
    // квадратично. Смещение — в мире, опускание — вдоль оси рисунка. Одна и
    // та же функция во всех проходах: цвет и нормаль совпадают по форме.
    // curve — степень изгиба (w пусто — 2, дуга).
    float KsBendWeight(float t, float pinned, float curve)
    {
        pinned = clamp(pinned, 0, 0.95);
        float s = saturate((t - pinned) / (1 - pinned));
        return pow(s, curve > 0 ? curve : 2);
    }

    // v — высота вершины в прямоугольнике частицы ковра 0..1 (ветер ковра).
    float3 KsObjectToWorld(float3 positionOS, float v)
    {
        float3 world = TransformObjectToWorld(positionOS);
        float share = KsWindShareOf(_KsStyle.x);
    #if !defined(_KS_PARTICLE)
        if (_KsBendShape.y > 0)
        {
            float weight = KsBendWeight((positionOS.y - _KsBendShape.x) * _KsBendShape.y, _KsBendShape.z, _KsBendShape.w);
            float3 up = TransformObjectToWorldDir(float3(0, 1, 0));
            world.xy += (_KsBend.xy - up.xy * _KsBend.z) * weight;
        }
        if (share > 0)
        {
            float3 origin = TransformObjectToWorld(float3(0, _KsBendShape.y > 0 ? _KsBendShape.x : 0, 0));
            float lift = saturate((world.y - origin.y) / max(_KsWindParams.w, 0.01));
            world = KsWind(world, origin, pow(lift, 1.5), share);
        }
    #else
        if (share > 0)
            world = KsWind(world, world, v * v * 0.6, share);
    #endif
        return world;
    }

    // Опора рисунка в мире — для дальнего плана (у ковра — сама вершина).
    float3 KsBaseWorld(float3 positionWS)
    {
    #if defined(_KS_PARTICLE)
        return positionWS;
    #else
        return TransformObjectToWorld(float3(0, 0, 0));
    #endif
    }

    half3 KsAdjust(half3 rgb, float3 hsv)
    {
        if (abs(hsv.x) > 1e-4 || abs(hsv.y - 1) > 1e-4)
        {
            float3 c = RgbToHsv(rgb);
            c.x = frac(c.x + hsv.x);
            c.y = saturate(c.y * hsv.y);
            rgb = HsvToRgb(c);
        }
        return rgb * hsv.z;
    }

    float2 KsUv(float2 uv, float flip)
    {
    #if defined(_KS_PARTICLE)
        if (flip < 0) uv.x = 1 - uv.x;
        return uv * _KsUvRect.zw + _KsUvRect.xy;
    #else
        return uv;
    #endif
    }

    // Просвечивание (галочка ассета «Просвечивает, когда за ним человек»):
    // круги вокруг закрытых предметом людей. Вне блока материала — задаются
    // только блоком свойств рисунка; не заданы — нули, кругов нет.
    // _KsHoles[i]: xy — центр (мир), z — радиус (мир), w — сила 0..1 (плавное
    // появление); _KsHoleShape: x — непрозрачность в круге, y — мягкий край
    // (доля радиуса).
    float4 _KsHoles[6];
    float4 _KsHoleShape;

    half KsHoleKeep(float2 world)
    {
        half keep = 1;
        [unroll] for (int i = 0; i < 6; i++)
        {
            float4 hole = _KsHoles[i];
            if (hole.z <= 0) continue;
            float d = distance(world, hole.xy) / hole.z;
            float inside = 1 - smoothstep(1 - max(_KsHoleShape.y, 1e-3), 1, d);
            keep = min(keep, lerp(1, _KsHoleShape.x, inside * saturate(hole.w)));
        }
        return keep;
    }

    // Чужая тень солнца на рисунке (LocationWorldRenderer.SunShade): сетка
    // тёмности 0..1 по рамке рисунка в мире, до 128 точек (по 4 в векторе),
    // между точками — плавно. Только блоком свойств; нет карты — без тени.
    // _KsShadeRect: xy — левый нижний угол (мир), zw — размер; _KsShadeGrid:
    // x — столбцов, y — рядов, z = 1 — карта есть; _KsShadeTint — цвет теней.
    float4 _KsShadeMap[32];
    float4 _KsShadeRect;
    float4 _KsShadeGrid;
    float4 _KsShadeTint;

    float KsShadeAt(int column, int row)
    {
        int i = row * (int)_KsShadeGrid.x + column;
        return _KsShadeMap[i >> 2][i & 3];
    }

    half3 KsShade(half3 rgb, float2 world)
    {
        if (_KsShadeGrid.z < 0.5)
            return rgb;
        int2 cells = max(int2(_KsShadeGrid.xy), int2(1, 1));
        float2 p = saturate((world - _KsShadeRect.xy) / max(_KsShadeRect.zw, 1e-4)) * cells - 0.5;
        float2 base = floor(p);
        float2 f = p - base;
        int2 c0 = clamp(int2(base), int2(0, 0), cells - 1);
        int2 c1 = clamp(int2(base) + 1, int2(0, 0), cells - 1);
        float s = lerp(lerp(KsShadeAt(c0.x, c0.y), KsShadeAt(c1.x, c0.y), f.x),
                       lerp(KsShadeAt(c0.x, c1.y), KsShadeAt(c1.x, c1.y), f.x), f.y);
        return rgb * lerp(half3(1, 1, 1), half3(_KsShadeTint.rgb), saturate(s));
    }
    ENDHLSL

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile_local _ _KS_PARTICLE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 uv           : TEXCOORD0;
            #if defined(_KS_PARTICLE)
                float3 custom       : TEXCOORD1;
            #endif
                float3 normal       : NORMAL;
                half4 color         : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color         : COLOR;
                float4 adjust       : TEXCOORD4;
                float2 ksWorld      : TEXCOORD5;
                // x — дальний план, zw — точка рисунка (растворение).
                float4 ksFx         : TEXCOORD6;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            // Свет одного стиля смешивания (маска стиля — как в URP).
            void KsShapeLight(half4 light, half4 mask, half4 maskFilter, half4 invertedFilter, half2 factors, out half4 modulate, out half4 additive)
            {
                if (any(maskFilter))
                {
                    half4 processedMask = (1 - invertedFilter) * mask + invertedFilter * (1 - mask);
                    light *= dot(processedMask, maskFilter);
                }
                modulate = light * factors.x;
                additive = light * factors.y;
            }

            // Копия CombinedShapeLightShared URP 17.6: стиль 0 (солнце) и
            // стили 1–3 (огонь и другие местные источники) — со своими долями.
            half4 KsCombinedLight(in SurfaceData2D surfaceData, in InputData2D inputData, half2 share)
            {
                #if defined(DEBUG_DISPLAY)
                half4 debugColor = 0;
                if (CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
                    return debugColor;
                #endif
                half alpha = surfaceData.alpha;
                half4 color = half4(surfaceData.albedo, alpha);
                const half4 mask = surfaceData.mask;
                const half2 lightingUV = inputData.lightingUV;
                if (alpha == 0.0)
                    discard;

            #if !USE_SHAPE_LIGHT_TYPE_0 && !USE_SHAPE_LIGHT_TYPE_1 && !USE_SHAPE_LIGHT_TYPE_2 && !USE_SHAPE_LIGHT_TYPE_3
                // Света нет вовсе — рисунок как есть (как в URP).
                return color;
            #endif
                half4 modulate = 0, additive = 0, m, a;
            #if USE_SHAPE_LIGHT_TYPE_0
                KsShapeLight(SAMPLE_TEXTURE2D(_ShapeLightTexture0, sampler_ShapeLightTexture0, lightingUV), mask,
                    _ShapeLightMaskFilter0, _ShapeLightInvertedFilter0, _ShapeLightBlendFactors0, m, a);
                // Без солнца — полный свет: рисунок не меняется со временем суток.
                modulate += lerp(half4(1, 1, 1, 1), m, share.x);
                additive += a * share.x;
            #else
                modulate += (1 - share.x);
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
                KsShapeLight(SAMPLE_TEXTURE2D(_ShapeLightTexture1, sampler_ShapeLightTexture1, lightingUV), mask,
                    _ShapeLightMaskFilter1, _ShapeLightInvertedFilter1, _ShapeLightBlendFactors1, m, a);
                modulate += m * share.y; additive += a * share.y;
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
                KsShapeLight(SAMPLE_TEXTURE2D(_ShapeLightTexture2, sampler_ShapeLightTexture2, lightingUV), mask,
                    _ShapeLightMaskFilter2, _ShapeLightInvertedFilter2, _ShapeLightBlendFactors2, m, a);
                modulate += m * share.y; additive += a * share.y;
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
                KsShapeLight(SAMPLE_TEXTURE2D(_ShapeLightTexture3, sampler_ShapeLightTexture3, lightingUV), mask,
                    _ShapeLightMaskFilter3, _ShapeLightInvertedFilter3, _ShapeLightBlendFactors3, m, a);
                modulate += m * share.y; additive += a * share.y;
            #endif
                half4 finalOutput = _HDREmulationScale * (color * modulate + additive);
                finalOutput.a = alpha;
                return max(0, finalOutput);
            }

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            Varyings LitVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #if defined(_KS_PARTICLE)
                float3 position = input.positionOS;
                o.color = input.color * _Color;
                o.adjust = float4(_KsHsv.x + input.uv.z, _KsHsv.y * input.uv.w, _KsHsv.z * input.custom.x, input.custom.y);
            #else
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.color = input.color * _Color * unity_SpriteColor;
                o.adjust = float4(_KsHsv.xyz, 1);
            #endif
                float3 positionWS = KsObjectToWorld(position, input.uv.y);
                o.positionCS = TransformWorldToHClip(positionWS);
            #if defined(DEBUG_DISPLAY)
                o.positionWS = positionWS;
                o.normalWS = TransformObjectToWorldDir(input.normal);
            #endif
                o.ksWorld = positionWS.xy;
                o.ksFx = float4(KsDistanceFactor(KsBaseWorld(positionWS)), 0, input.positionOS.xy);
                o.uv = KsUv(input.uv.xy, o.adjust.w);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                texel.rgb = KsAdjust(texel.rgb, input.adjust.xyz);
                half4 main = input.color * texel;
                main.a *= KsHoleKeep(input.ksWorld);
                main.rgb = KsShade(main.rgb, input.ksWorld);
                main.rgb = lerp(main.rgb, half3(_KsOutlineColor.rgb), KsOutlineMask(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), input.uv, texel.a));
                main.rgb = KsDistance(main.rgb, input.ksFx.x);
                half edge = KsDissolve(main.a, input.ksFx.zw);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
            #if defined(DEBUG_DISPLAY)
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                surfaceData.normalWS = input.normalWS;
            #endif
                half4 lit = KsCombinedLight(surfaceData, inputData, half2(_KsLight.xy));
                // Край растворения светится сам (тлеет и ночью).
                lit.rgb = lerp(lit.rgb, half3(_KsDissolveEdge.rgb), edge);
                return lit;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #pragma vertex NormalsVertex
            #pragma fragment NormalsFragment

            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _KS_PARTICLE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 uv           : TEXCOORD0;
            #if defined(_KS_PARTICLE)
                float3 custom       : TEXCOORD1;
            #endif
                float3 normal       : NORMAL;
                float4 tangent      : TANGENT;
                half4 color         : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4 color         : COLOR;
                float flip          : TEXCOORD4;
                float2 ksWorld      : TEXCOORD5;
                float2 ksObject     : TEXCOORD6;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            Varyings NormalsVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #if defined(_KS_PARTICLE)
                // Плоскость частицы смотрит в камеру; базис — по повороту частицы,
                // как у спрайта (нормаль от экрана, касательная вправо).
                float3 position = input.positionOS;
                o.color = input.color * _Color;
                o.flip = input.custom.y;
                float angle = input.custom.z;
                o.normalWS = half3(0, 0, -1);
                o.tangentWS = half3(cos(angle), sin(angle), 0);
                o.bitangentWS = half3(-sin(angle), cos(angle), 0);
            #else
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.color = input.color * _Color * unity_SpriteColor;
                o.flip = 1;
                o.normalWS = TransformObjectToWorldDir(input.normal);
                o.tangentWS = TransformObjectToWorldDir(input.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * input.tangent.w;
            #endif
                float3 positionWS = KsObjectToWorld(position, input.uv.y);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = KsUv(input.uv.xy, o.flip);
                o.ksWorld = positionWS.xy;
                o.ksObject = input.positionOS.xy;
                return o;
            }

            half4 NormalsFragment(Varyings input) : SV_Target
            {
                // Детали земли (_KsLight.z = 1) свою нормаль не пишут: под ними
                // остаётся нормаль земли, и огонь освещает их как землю.
                if (_KsLight.z > 0.5)
                    discard;
                half4 mainTex = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                mainTex.a *= KsHoleKeep(input.ksWorld);
                KsDissolve(mainTex.a, input.ksObject);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));
            #if defined(_KS_PARTICLE)
                // Отражённый рисунок — отражённая нормаль по горизонтали.
                normalTS.x *= input.flip < 0 ? -1 : 1;
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz));
                return half4(0.5 * (normalWS + 1), mainTex.a);
            #else
                SetUpSpriteInstanceProperties();
                return NormalsRenderingShared(mainTex, normalTS, input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _KS_PARTICLE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 uv           : TEXCOORD0;
            #if defined(_KS_PARTICLE)
                float3 custom       : TEXCOORD1;
            #endif
                half4 color         : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                half4 color         : COLOR;
                float4 adjust       : TEXCOORD1;
                float2 ksWorld      : TEXCOORD2;
                float4 ksFx         : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings UnlitVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #if defined(_KS_PARTICLE)
                float3 position = input.positionOS;
                o.color = input.color * _Color;
                o.adjust = float4(_KsHsv.x + input.uv.z, _KsHsv.y * input.uv.w, _KsHsv.z * input.custom.x, input.custom.y);
            #else
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.color = input.color * _Color * unity_SpriteColor;
                o.adjust = float4(_KsHsv.xyz, 1);
            #endif
                float3 positionWS = KsObjectToWorld(position, input.uv.y);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.ksWorld = positionWS.xy;
                o.ksFx = float4(KsDistanceFactor(KsBaseWorld(positionWS)), 0, input.positionOS.xy);
                o.uv = KsUv(input.uv.xy, o.adjust.w);
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                texel.rgb = KsAdjust(texel.rgb, input.adjust.xyz);
                half4 color = input.color * texel;
                color.a *= KsHoleKeep(input.ksWorld);
                color.rgb = KsShade(color.rgb, input.ksWorld);
                color.rgb = lerp(color.rgb, half3(_KsOutlineColor.rgb), KsOutlineMask(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), input.uv, texel.a));
                color.rgb = KsDistance(color.rgb, input.ksFx.x);
                half edge = KsDissolve(color.a, input.ksFx.zw);
                color.rgb = lerp(color.rgb, half3(_KsDissolveEdge.rgb), edge);
                return color;
            }
            ENDHLSL
        }
    }
}
