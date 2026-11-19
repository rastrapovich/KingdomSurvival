// Общая часть шейдеров MapLab: художественный свет по нормали, бумага,
// насыщенность и сэмплирование палитры. Не PBR: нормаль лишь делит форму
// на тёплый свет и холодную цветную тень.
#ifndef MAPLAB_COMMON_INCLUDED
#define MAPLAB_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "MapLabGrade.hlsl"

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

// Коррекция цвета ассета автором (окно «Генератор карты» → Ассеты):
// a.x — яркость, a.y — поворот тона, градусов, a.z — насыщенность. Нейтрально (1, 0, 1).
float3 MapLabColorAdjust(float3 c, float4 a)
{
    if (abs(a.x - 1.0) + abs(a.y) + abs(a.z - 1.0) < 1e-4)
        return c;
    // Поворот тона в YIQ — яркость и альфа не меняются.
    float3 yiq = float3(dot(c, float3(0.299, 0.587, 0.114)), dot(c, float3(0.596, -0.274, -0.322)), dot(c, float3(0.211, -0.523, 0.312)));
    float h = radians(a.y);
    float cs = cos(h), sn = sin(h);
    yiq.yz = float2(yiq.y * cs - yiq.z * sn, yiq.y * sn + yiq.z * cs) * a.z;
    float3 rgb = float3(dot(yiq, float3(1.0, 0.956, 0.621)), dot(yiq, float3(1.0, -0.272, -0.647)), dot(yiq, float3(1.0, -1.106, 1.703)));
    return max(0.0, rgb * a.x);
}

// Уровни, как в Photoshop (19.11.2026), в sRGB поверх MapLabColorAdjust.
// Кодировка — ноль без изменений: l = (вход чёрная, 1 − вход белая,
// гамма − 1, выход чёрная), lo.x = 1 − выход белая (доли 0..1).
float3 MapLabLevels(float3 c, float4 l, float4 lo)
{
    if (dot(abs(l), float4(1, 1, 1, 1)) + abs(lo.x) < 1e-5)
        return c;
    float inB = l.x, inW = 1.0 - l.y, g = max(0.01, 1.0 + l.z), outB = l.w, outW = 1.0 - lo.x;
    float3 s = pow(max(c, 0.0), 1.0 / 2.2);
    s = saturate((s - inB) / max(inW - inB, 1e-3));
    s = pow(s, 1.0 / g);
    s = lerp(outB.xxx, outW.xxx, s);
    return pow(saturate(s), 2.2);
}

#endif
