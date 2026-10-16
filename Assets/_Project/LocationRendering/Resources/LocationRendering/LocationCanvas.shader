// ПР-12М: «Холст» окна «Обработка кадра» — свои эффекты на весь кадр места
// (LocationCanvasPass). Проход 0 — до обработки кадра URP (её цвет ложится
// поверх): живописность (фильтр Кувахары) и туман по высоте экрана.
// Проход 1 — после неё: сведение к палитре и бумага (их не перекрашивает).
// Значения задаёт LocationCanvasPass перед кадром.
Shader "Hidden/KingdomSurvival/LocationCanvas"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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

    half4 FragBefore(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        half3 color = source.rgb;
        if (_KsPainterly.z > 0.5)
        {
            int radius = clamp((int)_KsPainterly.x, 1, 8);
            color = lerp(color, KsKuwahara(uv, radius), saturate(_KsPainterly.y));
        }
        color = KsFogApply(color, uv);
        return half4(color, source.a);
    }

    half4 FragAfter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        half3 color = source.rgb;
        if (_KsPalette.y > 0)
            color = KsPaletteApply(color, uv);
        if (_KsPaper.x > 0)
            color = KsPaperApply(color, uv);
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
