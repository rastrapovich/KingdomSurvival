// ПР-12М: «Холст» окна «Обработка кадра» — свои эффекты на весь кадр места
// (LocationCanvasPass). Проход 0 — до обработки кадра URP (её цвет ложится
// поверх): живописность (фильтр Кувахары) и туман по высоте экрана.
// Проход 1 — после неё: сведение к палитре и бумага (их не перекрашивает).
// Значения задаёт LocationCanvasPass перед кадром.
Shader "Hidden/KingdomSurvival/LocationCanvas"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // x, y — 1 / размер; z, w — размер кадра (пиксели).
    float4 _KsCanvasTexel;
    // x — радиус, y — сила, z — включено.
    float4 _KsPainterly;
    float4 _KsFogColor;
    // x — плотность, y — начало, z — полная сила (доли высоты от края), w — 1 — снизу.
    float4 _KsFog;
    // x — клочья, y — их размер, z — скорость, w — время (с).
    float4 _KsFogNoise;
    // x — уровней, y — сила, z — дизеринг, w — цветов палитры (0 — по уровням).
    float4 _KsPalette;
    float4 _KsPaletteColors[64];
    // x — сила, y — масштаб, z — волокна, w — 1 — своя текстура.
    float4 _KsPaper;
    TEXTURE2D(_KsPaperTex);
    // Бумага повторяется по кадру, что бы ни стояло в импорте текстуры.
    SAMPLER(sampler_linear_repeat);

    float KsHash(float2 p)
    {
        return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
    }

    float KsNoise(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        f = f * f * (3 - 2 * f);
        return lerp(lerp(KsHash(i), KsHash(i + float2(1, 0)), f.x), lerp(KsHash(i + float2(0, 1)), KsHash(i + float2(1, 1)), f.x), f.y);
    }

    float KsFbm(float2 p)
    {
        float sum = 0, amplitude = 0.5;
        [unroll] for (int i = 0; i < 4; i++)
        {
            sum += KsNoise(p) * amplitude;
            p = p * 2.03 + 17.17;
            amplitude *= 0.5;
        }
        return sum / 0.9375;
    }

    half3 KsSample(float2 uv)
    {
        return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
    }

    // Фильтр Кувахары: из четырёх квадрантов вокруг точки — средний цвет
    // самого ровного. Края остаются, мелочь сливается в мазки.
    half3 KsKuwahara(float2 uv, int radius)
    {
        float3 mean[4] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0) };
        float3 square[4] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0) };
        float2 texel = _KsCanvasTexel.xy;
        [loop] for (int y = -radius; y <= radius; y++)
        {
            [loop] for (int x = -radius; x <= radius; x++)
            {
                float3 c = KsSample(uv + float2(x, y) * texel);
                float3 c2 = c * c;
                if (x <= 0 && y <= 0) { mean[0] += c; square[0] += c2; }
                if (x >= 0 && y <= 0) { mean[1] += c; square[1] += c2; }
                if (x <= 0 && y >= 0) { mean[2] += c; square[2] += c2; }
                if (x >= 0 && y >= 0) { mean[3] += c; square[3] += c2; }
            }
        }
        float n = (radius + 1) * (radius + 1);
        float best = 1e9;
        float3 result = KsSample(uv);
        [unroll] for (int k = 0; k < 4; k++)
        {
            float3 m = mean[k] / n;
            float3 v = abs(square[k] / n - m * m);
            float variance = v.r + v.g + v.b;
            if (variance < best) { best = variance; result = m; }
        }
        return result;
    }

    half3 KsFogApply(half3 color, float2 uv)
    {
        if (_KsFog.x <= 0)
            return color;
        float h = _KsFog.w > 0.5 ? 1 - uv.y : uv.y;
        float amount = smoothstep(_KsFog.y, max(_KsFog.z, _KsFog.y + 1e-3), h) * _KsFog.x;
        if (_KsFogNoise.x > 0)
        {
            float aspect = _KsCanvasTexel.z / max(_KsCanvasTexel.w, 1);
            float2 p = float2(uv.x * aspect, uv.y) * max(_KsFogNoise.y, 0.1) + float2(1, 0.3) * _KsFogNoise.w * _KsFogNoise.z;
            float n = KsFbm(p);
            amount *= lerp(1, saturate(n * 1.8 - 0.25), saturate(_KsFogNoise.x));
        }
        return lerp(color, half3(_KsFogColor.rgb), saturate(amount));
    }

    // Порядок Байера 4×4 — ровный дизеринг без шума.
    float KsBayer(float2 pixel)
    {
        int2 p = int2(fmod(pixel, 4));
        const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        return m[p.y * 4 + p.x] / 16.0 - 0.46875;
    }

    // Палитра и бумага считаются в гамме экрана (sRGB): в линейном буфере
    // тёмные тона сжаты, и ступени палитры и фактура проваливали бы тени.
    half3 KsToDisplay(half3 c)
    {
    #if defined(UNITY_COLORSPACE_GAMMA)
        return c;
    #else
        return LinearToSRGB(max(c, 0));
    #endif
    }

    half3 KsFromDisplay(half3 c)
    {
    #if defined(UNITY_COLORSPACE_GAMMA)
        return c;
    #else
        return SRGBToLinear(max(c, 0));
    #endif
    }

    half3 KsPaletteApply(half3 color, float2 uv)
    {
        float2 pixel = uv * _KsCanvasTexel.zw;
        float dither = KsBayer(pixel) * _KsPalette.z;
        half3 display = saturate(KsToDisplay(color));
        half3 result;
        int count = (int)_KsPalette.w;
        if (count > 0)
        {
            // Цвета палитры — в sRGB, как в рисунке-палитре.
            float3 c = saturate(display + dither * 0.12);
            float best = 1e9;
            result = display;
            [loop] for (int i = 0; i < count; i++)
            {
                float3 d = (c - _KsPaletteColors[i].rgb) * float3(0.30, 0.59, 0.11);
                float e = dot(d, d);
                if (e < best) { best = e; result = _KsPaletteColors[i].rgb; }
            }
        }
        else
        {
            float levels = max(_KsPalette.x - 1, 1);
            result = floor(display * levels + 0.5 + dither) / levels;
        }
        return lerp(color, KsFromDisplay(saturate(result)), saturate(_KsPalette.y));
    }

    // Волокно: тонкая извилистая линия там, где вытянутый шум проходит
    // середину; редкое — под крупной маской.
    float KsFiber(float2 p, float2 axis)
    {
        float2 r = float2(dot(p, axis), dot(p, float2(-axis.y, axis.x)));
        float stroke = 1 - smoothstep(0.0, 0.04, abs(KsNoise(r * float2(5, 90)) - 0.5));
        float mask = saturate(KsNoise(r * 2.3 + 7.1) * 2.2 - 1.1);
        return stroke * mask;
    }

    half3 KsPaperApply(half3 color, float2 uv)
    {
        // Масштаб — от пикселей кадра: фактура одинаковая при любом размере окна.
        float2 p = uv * _KsCanvasTexel.zw / 512 * max(_KsPaper.y, 0.05);
        float strength = saturate(_KsPaper.x);
        half3 display = KsToDisplay(color);
        if (_KsPaper.w > 0.5)
        {
            half3 paper = KsToDisplay(SAMPLE_TEXTURE2D(_KsPaperTex, sampler_linear_repeat, p).rgb);
            return KsFromDisplay(lerp(display, display * paper * 1.06, strength));
        }
        // Своя бумага: крупная неровность, мелкое зерно и волокна в двух направлениях.
        float grain = KsFbm(p * 3) * 0.45 + KsNoise(p * 60) * 0.3 + KsNoise(p * 170) * 0.25;
        float fibers = KsFiber(p, float2(0.866, 0.5)) + KsFiber(p * 1.3 + 3.7, float2(0.5, -0.866));
        float value = grain - 0.5 - fibers * _KsPaper.z * 0.35;
        return KsFromDisplay(saturate(display * (1 + value * strength * 1.4)));
    }

    // ------------------------------------------------------------------
    // Новые эффекты (окно «Обработка кадра»): резкость, тилт-шифт, контур,
    // облака, лучи, марево — до обработки URP; выборочный цвет, градиентная
    // карта, штриховка, плёнка, рамка, состояния героя, вспышка, затемнение,
    // кинополосы — после. Значения задаёт LocationCanvasPass.
    // ------------------------------------------------------------------

    // Марево: до 8 огней — xy (доли кадра), z — ширина (доли высоты), w — сила.
    float4 _KsHaze[8];
    // x — огней, y — высота марева (доли высоты кадра), z — время × скорость.
    float4 _KsHazeParams;
    // x — сила резкости.
    float4 _KsSharpen;
    // x — фокус (доля высоты), y — полоса резкости, z — размытие тилт-шифта (пиксели), w — размытие оглушения.
    float4 _KsTilt;
    float4 _KsInkColor;
    // x — порог, y — толщина (пиксели), z — сила, w — включено.
    float4 _KsInk;
    // Камера в мире: xy — центр, zw — половина ширины и высоты кадра.
    float4 _KsWorld;
    // Облака: x — покрытие, y — темнота, z — размер (мир), w — мягкость.
    float4 _KsClouds;
    // xy — сдвиг облаков (мир), z — включено.
    float4 _KsCloudsMove;
    float4 _KsRaysColor;
    // Лучи: xy — направление (мир), z — сила, w — ширина полосы (мир).
    float4 _KsRays;
    // x — дрожание (время × скорость), y — густота, z — включено.
    float4 _KsRaysMove;

    // Выборочный цвет: x — сдвиг тона (обороты), y — насыщенность, z — светлота.
    float4 _KsSelective[6];
    // x — включено.
    float4 _KsSelectiveOn;
    TEXTURE2D(_KsGradientTex);
    // x — сила, y — включено.
    float4 _KsGradient;
    float4 _KsHatchColor;
    // x — порог, y — шаг (пиксели), z — сила, w — порог перекрёстного.
    float4 _KsHatch;
    // x — пыль, y — царапины, z — мерцание, w — время.
    float4 _KsFilm;
    TEXTURE2D(_KsBorderTex);
    float4 _KsBorderColor;
    // x — ширина (доля высоты), y — неровность, z — 1 — своя текстура, w — включено.
    float4 _KsBorder;
    float4 _KsHealthColor;
    // x — сила (уже по здоровью), y — частота пульса, z — время.
    float4 _KsHealth;
    // x — затемнение, y — обесцвечивание (уже по силе оглушения).
    float4 _KsStun;
    // rgb — цвет, a — сила.
    float4 _KsFlash;
    float4 _KsFade;
    // x — доля высоты сверху и снизу.
    float4 _KsLetterbox;
    float4 _KsLetterboxColor;

    float KsLuma(half3 c) { return dot(c, half3(0.2126, 0.7152, 0.0722)); }

    // Мир по доле кадра.
    float2 KsWorldAt(float2 uv) { return _KsWorld.xy + (uv * 2 - 1) * _KsWorld.zw; }

    // Яркость в гамме экрана (края, пороги — как видит глаз).
    float KsDisplayLuma(float2 uv) { return KsLuma(saturate(KsToDisplay(KsSample(uv)))); }

    // Марево: дрожание над огнями (смещение выборки).
    float2 KsHazeUv(float2 uv)
    {
        int count = (int)_KsHazeParams.x;
        if (count <= 0)
            return uv;
        float aspect = _KsCanvasTexel.z / max(_KsCanvasTexel.w, 1);
        float2 offset = 0;
        [loop] for (int i = 0; i < count; i++)
        {
            float4 fire = _KsHaze[i];
            float2 d = uv - fire.xy;
            d.x *= aspect;
            float above = d.y;
            if (above < -fire.z * 0.3 || above > _KsHazeParams.y) continue;
            float across = saturate(1 - abs(d.x) / max(fire.z, 1e-4));
            float up = saturate(1 - above / max(_KsHazeParams.y, 1e-4)) * saturate((above + fire.z * 0.3) / max(fire.z * 0.3, 1e-4));
            float weight = across * across * up * fire.w;
            float2 p = uv * float2(aspect, 1) * 90 + float2(0, -_KsHazeParams.z * 4);
            float2 wobble = float2(KsNoise(p), KsNoise(p + 7.3)) * 0.65 + float2(KsNoise(p * 2.3 + 3.1), KsNoise(p * 2.3 + 11.7)) * 0.35;
            offset += (wobble - 0.5) * weight * 0.035;
        }
        return uv + offset;
    }

    half3 KsSharpenApply(half3 color, float2 uv)
    {
        float2 t = _KsCanvasTexel.xy;
        half3 blur = (KsSample(uv + float2(t.x, 0)) + KsSample(uv - float2(t.x, 0)) + KsSample(uv + float2(0, t.y)) + KsSample(uv - float2(0, t.y))) * 0.25;
        return max(0, color + (color - blur) * _KsSharpen.x);
    }

    // Размытие диском: тилт-шифт (по высоте) и оглушение (везде).
    half3 KsBlurApply(half3 color, float2 uv)
    {
        float distance = abs(uv.y - _KsTilt.x);
        float tilt = _KsTilt.z * smoothstep(_KsTilt.y * 0.5, _KsTilt.y * 0.5 + 0.25, distance);
        float radius = tilt + _KsTilt.w;
        if (radius < 0.5)
            return color;
        const float2 disk[12] = {
            float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
            float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
            float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598) };
        half3 sum = color;
        [unroll] for (int i = 0; i < 12; i++)
            sum += KsSample(uv + disk[i] * radius * _KsCanvasTexel.xy);
        return sum / 13;
    }

    // Чернильный контур: оператор Собеля по яркости.
    half3 KsInkApply(half3 color, float2 uv)
    {
        float2 t = _KsCanvasTexel.xy * max(_KsInk.y, 0.5);
        float l00 = KsDisplayLuma(uv + float2(-t.x, -t.y)), l10 = KsDisplayLuma(uv + float2(0, -t.y)), l20 = KsDisplayLuma(uv + float2(t.x, -t.y));
        float l01 = KsDisplayLuma(uv + float2(-t.x, 0)), l21 = KsDisplayLuma(uv + float2(t.x, 0));
        float l02 = KsDisplayLuma(uv + float2(-t.x, t.y)), l12 = KsDisplayLuma(uv + float2(0, t.y)), l22 = KsDisplayLuma(uv + float2(t.x, t.y));
        float gx = (l20 + 2 * l21 + l22) - (l00 + 2 * l01 + l02);
        float gy = (l02 + 2 * l12 + l22) - (l00 + 2 * l10 + l20);
        float edge = sqrt(gx * gx + gy * gy);
        float threshold = lerp(0.04, 0.9, _KsInk.x);
        float ink = smoothstep(threshold, threshold + 0.08, edge) * saturate(_KsInk.z);
        return lerp(color, half3(_KsInkColor.rgb), ink);
    }

    half3 KsCloudsApply(half3 color, float2 uv)
    {
        float2 p = (KsWorldAt(uv) + _KsCloudsMove.xy) / max(_KsClouds.z, 0.5);
        float n = KsFbm(p);
        float start = 1 - saturate(_KsClouds.x);
        float shadow = smoothstep(start, start + max(_KsClouds.w, 0.02), n) * saturate(_KsClouds.y);
        return color * (1 - shadow);
    }

    half3 KsRaysApply(half3 color, float2 uv)
    {
        float2 world = KsWorldAt(uv);
        float2 direction = normalize(_KsRays.xy + float2(1e-5, 0));
        float2 across = float2(-direction.y, direction.x);
        float width = max(_KsRays.w, 0.05);
        float band = KsFbm(float2(dot(world, across) / width, _KsRaysMove.x));
        float density = saturate(_KsRaysMove.y);
        float floorLevel = (1 - density) * 0.75;
        float mask = pow(saturate((band - floorLevel) / max(1 - floorLevel, 1e-3)), 2.2);
        mask *= 0.6 + 0.4 * KsNoise(float2(dot(world, direction) / (width * 6), 3.1));
        return color + half3(_KsRaysColor.rgb) * mask * _KsRays.z * (0.35 + KsLuma(color));
    }

    // Выборочный цвет: шесть тонов круга, вес — близость тона и насыщенность.
    half3 KsSelectiveApply(half3 color)
    {
        half3 display = saturate(KsToDisplay(color));
        float3 hsv = RgbToHsv(display);
        float shift = 0, saturation = 1, lightness = 0;
        [unroll] for (int i = 0; i < 6; i++)
        {
            float d = abs(frac(hsv.x - i / 6.0 + 0.5) - 0.5) * 6;
            float w = saturate(1 - d) * saturate(hsv.y * 3);
            shift += _KsSelective[i].x * w;
            saturation *= lerp(1, _KsSelective[i].y, w);
            lightness += _KsSelective[i].z * w;
        }
        hsv.x = frac(hsv.x + shift);
        hsv.y = saturate(hsv.y * saturation);
        display = HsvToRgb(hsv);
        display = lightness >= 0 ? lerp(display, 1, lightness) : display * (1 + lightness);
        return KsFromDisplay(saturate(display));
    }

    half3 KsGradientApply(half3 color)
    {
        half3 display = saturate(KsToDisplay(color));
        half3 mapped = SAMPLE_TEXTURE2D(_KsGradientTex, sampler_LinearClamp, float2(saturate(KsLuma(display)), 0.5)).rgb;
        return KsFromDisplay(lerp(display, mapped, saturate(_KsGradient.x)));
    }

    // Штриховка: косые линии в тенях, в глубоких — перекрёстные.
    half3 KsHatchApply(half3 color, float2 uv)
    {
        float l = KsLuma(saturate(KsToDisplay(color)));
        if (l >= _KsHatch.x)
            return color;
        float2 pixel = uv * _KsCanvasTexel.zw;
        float spacing = max(_KsHatch.y, 2);
        float first = abs(frac((pixel.x + pixel.y) / spacing) - 0.5) * 2;
        float second = abs(frac((pixel.x - pixel.y) / spacing) - 0.5) * 2;
        float stroke = (1 - smoothstep(0.15, 0.45, first)) * smoothstep(_KsHatch.x, _KsHatch.x - 0.12, l);
        stroke = max(stroke, (1 - smoothstep(0.15, 0.45, second)) * smoothstep(_KsHatch.w, _KsHatch.w - 0.1, l));
        return lerp(color, half3(_KsHatchColor.rgb), stroke * saturate(_KsHatch.z));
    }

    half3 KsFilmApply(half3 color, float2 uv)
    {
        float time = _KsFilm.w;
        float frame = floor(time * 18);
        float aspect = _KsCanvasTexel.z / max(_KsCanvasTexel.w, 1);
        float2 cell = floor(uv * float2(aspect * 140, 140));
        float speck = KsHash(cell + frame * 1.37);
        if (speck > 1 - _KsFilm.x * 0.004)
            color = lerp(color, KsHash(cell + 3.1) > 0.5 ? half3(0.9, 0.88, 0.8) : half3(0.05, 0.04, 0.03), 0.8);
        float lane = KsHash(float2(frame, 7.7));
        if (lane < _KsFilm.y * 0.6)
        {
            float x = KsHash(float2(frame, 1.3));
            float scratch = 1 - smoothstep(0, 0.0015, abs(uv.x - x));
            color = lerp(color, half3(0.85, 0.83, 0.78), scratch * 0.6);
        }
        return color * (1 + (KsHash(float2(frame, 4.2)) - 0.5) * _KsFilm.z * 0.12);
    }

    half3 KsBorderApply(half3 color, float2 uv)
    {
        if (_KsBorder.z > 0.5)
        {
            half4 frameTex = SAMPLE_TEXTURE2D(_KsBorderTex, sampler_LinearClamp, uv);
            return lerp(color, frameTex.rgb, frameTex.a);
        }
        float aspect = _KsCanvasTexel.z / max(_KsCanvasTexel.w, 1);
        float2 p = float2(uv.x * aspect, uv.y);
        float d = min(min(p.x, aspect - p.x), min(p.y, 1 - p.y));
        float along = (p.x + p.y) * 40;
        float rough = (KsNoise(float2(along, 1.7)) * 0.7 + KsNoise(float2(along * 3.1, 5.3)) * 0.3) * _KsBorder.y * _KsBorder.x * 0.8;
        float edge = _KsBorder.x - rough;
        float mask = 1 - smoothstep(edge - 0.004, edge, d);
        return lerp(color, half3(_KsBorderColor.rgb), mask * _KsBorderColor.a);
    }

    half3 KsStatesApply(half3 color, float2 uv)
    {
        if (_KsStun.x > 0 || _KsStun.y > 0)
        {
            half l = KsLuma(color);
            color = lerp(color, half3(l, l, l), saturate(_KsStun.y)) * (1 - saturate(_KsStun.x));
        }
        if (_KsHealth.x > 0)
        {
            float aspect = _KsCanvasTexel.z / max(_KsCanvasTexel.w, 1);
            float2 d = (uv - 0.5) * float2(aspect, 1);
            float edge = smoothstep(0.25, 0.75, length(d) / (0.5 * sqrt(aspect * aspect + 1)) * 1.1);
            float pulse = 0.65 + 0.35 * sin(_KsHealth.z * _KsHealth.y * 6.2832);
            color = lerp(color, half3(_KsHealthColor.rgb), saturate(edge * _KsHealth.x * pulse));
        }
        color = lerp(color, half3(_KsFlash.rgb), saturate(_KsFlash.a));
        color = lerp(color, half3(_KsFade.rgb), saturate(_KsFade.a));
        if (_KsLetterbox.x > 0 && (uv.y < _KsLetterbox.x || uv.y > 1 - _KsLetterbox.x))
            color = half3(_KsLetterboxColor.rgb);
        return color;
    }

    half4 FragBefore(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = KsHazeUv(input.texcoord);
        half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        half3 color = source.rgb;
        if (_KsSharpen.x > 0)
            color = KsSharpenApply(color, uv);
        if (_KsPainterly.z > 0.5)
        {
            int radius = clamp((int)_KsPainterly.x, 1, 8);
            color = lerp(color, KsKuwahara(uv, radius), saturate(_KsPainterly.y));
        }
        if (_KsTilt.z > 0 || _KsTilt.w > 0)
            color = KsBlurApply(color, uv);
        if (_KsInk.w > 0.5)
            color = KsInkApply(color, uv);
        if (_KsCloudsMove.z > 0.5)
            color = KsCloudsApply(color, uv);
        if (_KsRaysMove.z > 0.5)
            color = KsRaysApply(color, uv);
        color = KsFogApply(color, uv);
        return half4(color, source.a);
    }

    half4 FragAfter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        half3 color = source.rgb;
        if (_KsSelectiveOn.x > 0.5)
            color = KsSelectiveApply(color);
        if (_KsGradient.y > 0.5)
            color = KsGradientApply(color);
        if (_KsHatch.z > 0)
            color = KsHatchApply(color, uv);
        if (_KsPalette.y > 0)
            color = KsPaletteApply(color, uv);
        if (_KsPaper.x > 0)
            color = KsPaperApply(color, uv);
        if (_KsFilm.x > 0 || _KsFilm.y > 0 || _KsFilm.z > 0)
            color = KsFilmApply(color, uv);
        if (_KsBorder.w > 0.5)
            color = KsBorderApply(color, uv);
        color = KsStatesApply(color, uv);
        return half4(color, source.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "Canvas Before Post"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBefore
            ENDHLSL
        }

        Pass
        {
            Name "Canvas After Post"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAfter
            ENDHLSL
        }
    }
}
