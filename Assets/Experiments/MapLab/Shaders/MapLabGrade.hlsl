// Вид карты (MapLabMapViews): общая цветокоррекция всего рисунка карты —
// земля, мазки, тушь, вода, деревья, дома, фигуры. Глобальные значения
// ставит MapLabViewGradeShader только на время рендера карты (живая карта
// игры, снимок, предпросмотр генератора) и сразу снимает: арена боя и
// прочие рисунки MapLab той же сессии остаются нетронутыми.
// _MapLabGrade.x == 0 (по умолчанию, глобал не задан) — цвет без изменений.
#ifndef MAPLAB_GRADE_INCLUDED
#define MAPLAB_GRADE_INCLUDED

// x — сила вида, y — яркость, z — контраст, w — насыщенность.
float4 _MapLabGrade;
// x — доля градиентной карты (тон по светлоте), y — сила подкраски,
// z — сохранить тёплые огни (окна, фонари), w — подъём тени.
float4 _MapLabGradeMap;
float4 _MapLabGradeShadows;
float4 _MapLabGradeMids;
float4 _MapLabGradeLights;
float4 _MapLabGradeTint;

float3 MapLabViewGrade(float3 c)
{
    if (_MapLabGrade.x <= 0.0)
        return c;
    const float3 w = float3(0.299, 0.587, 0.114);
    float3 src = c;
    float3 g = c * _MapLabGrade.y;
    // Контраст вокруг средне-серого (в линейном пространстве ≈ 0,22).
    g = max(0.0, (g - 0.22) * _MapLabGrade.z + 0.22);
    g = g + _MapLabGradeMap.w * (1.0 - saturate(g / 0.35)) * 0.08;
    float l = dot(g, w);
    g = max(0.0, lerp(l.xxx, g, _MapLabGrade.w));
    // Градиентная карта: светлота → тень / середина / свет вида (сепия,
    // ночная гамма). Светлота — в «перцептивной» шкале, чтобы середина
    // палитры пришлась на средние тона рисунка.
    // Смешение цветов вида — в гамма-шкале (≈ sRGB), чтобы тушь и тень
    // оставались тёмными, а не светлели от смешения в линейном пространстве.
    float t = sqrt(saturate(l));
    float3 rs = sqrt(_MapLabGradeShadows.rgb), rm = sqrt(_MapLabGradeMids.rgb), rh = sqrt(_MapLabGradeLights.rgb);
    float3 ramp = t < 0.5 ? lerp(rs, rm, t * 2.0) : lerp(rm, rh, t * 2.0 - 1.0);
    ramp *= ramp;
    g = lerp(g, ramp, _MapLabGradeMap.x);
    g = lerp(g, g * _MapLabGradeTint.rgb, _MapLabGradeMap.y);
    // Тёплые яркие огни (окна, фонари, огонь: ярко-оранжевые, с малой
    // долей зелёного и синего) не гаснут в мрачном виде; солома полей и
    // светлые дороги сюда не попадают.
    float rr = max(src.r, 1e-3);
    float warm = saturate((0.62 - src.g / rr) * 8.0) * saturate((0.25 - src.b / rr) * 8.0) * saturate((src.r - 0.5) * 4.0);
    g = lerp(g, src * 1.1, warm * _MapLabGradeMap.z);
    return lerp(src, g, saturate(_MapLabGrade.x));
}

#endif
