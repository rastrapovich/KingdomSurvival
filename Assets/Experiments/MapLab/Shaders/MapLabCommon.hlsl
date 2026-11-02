// Общая часть шейдеров MapLab: художественный свет по нормали, бумага,
// насыщенность и сэмплирование палитры. Не PBR: нормаль лишь делит форму
// на тёплый свет и холодную цветную тень.
#ifndef MAPLAB_COMMON_INCLUDED
#define MAPLAB_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Тон 0..1 по нормали с мягким переходом и необязательными диапазонами.
float MapLabTone(float3 normalWS, float3 lightDir, float softness, float bands)
{
    float d = dot(normalize(normalWS), lightDir);
    float wrap = 0.25 + softness * 0.9;
    float w = saturate((d + wrap) / (1.0 + wrap));
    if (bands > 0.5)
    {
        float scaled = w * bands;
        float cell = floor(scaled);
        w = (cell + smoothstep(0.3, 0.7, scaled - cell)) / bands;
    }
    return w;
}

// Освещение относительно ровной земли: ровное место сохраняет цвет краски,
// склон к свету теплеет и светлеет, склон от света уходит в холодную тень.
float3 MapLabLight(float3 albedo, float3 normalWS, float3 lightDir, float strength, float softness, float bands, float3 shadowTint, float3 lightTint)
{
    float w = MapLabTone(normalWS, lightDir, softness, bands);
    float w0 = MapLabTone(float3(0, 1, 0), lightDir, softness, bands);
    float k = w - w0;
    float3 c = albedo;
    float lit = saturate(k * 2.2) * strength;
    c = lerp(c, c * lightTint * 1.22, lit);
    float shade = saturate(-k * 1.6) * strength * 0.85;
    c = lerp(c, c * shadowTint * 1.5, shade);
    return c;
}

float3 MapLabShadow(float3 c, float amount, float3 shadowTint)
{
    return lerp(c, c * shadowTint * 1.45, saturate(amount));
}

float3 MapLabSaturate(float3 c, float saturation)
{
    float l = dot(c, float3(0.299, 0.587, 0.114));
    return max(0, lerp(l.xxx, c, saturation));
}

// Палитра — текстура 32 × 1 (sRGB), выборка по центру ячейки.
float3 MapLabPalette(TEXTURE2D_PARAM(paletteTex, paletteSampler), float index)
{
    return SAMPLE_TEXTURE2D_LOD(paletteTex, paletteSampler, float2((index + 0.5) / 32.0, 0.5), 0).rgb;
}

#endif
