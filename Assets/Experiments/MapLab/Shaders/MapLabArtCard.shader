// Карточки нарисованных объектов этапа 02 (обычный RGBA-цвет, не packed
// atlas): RGB рисунка сохраняется, alpha управляет вырезанием. Свет и объём уже
// нарисованы — второго Lambert нет; общий тон тени рельефа под опорой и мягкий
// оттенок палитры регулируются отдельно (0 — рисунок без изменений).
//
// Глубина: карточка стоит в опоре и разворачивается к фиксированной камере
// (ствол вертикален). Часть рисунка ниже опоры (видимое основание дома,
// плинт фигурки) сдвигается к камере вдоль луча зрения ровно настолько, чтобы
// не уйти под землю. В ортографии такой сдвиг не меняет положения на экране.
Shader "Kingdom Survival/MapLab/Art Card"
{
    Properties
    {
        _MainTex ("Рисунок (RGBA)", 2D) = "white" {}
        _ShadeTex ("Shade (R свет, G затенение)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0.05, 0.95)) = 0.5
        _CardLean ("Card lean", Range(0, 1)) = 0.35
        _TintColor ("Тон палитры", Color) = (0.5, 0.5, 0.4, 1)
        _TintStrength ("Сила оттенка палитры", Range(0, 1)) = 0
        _ShadowTint ("Shadow tint", Color) = (0.4, 0.42, 0.56, 1)
        _GroundShade ("Тень рельефа под опорой", Range(0, 1)) = 0.6
        _MapSize ("Map size", Float) = 256
        _DepthMargin ("Запас глубины к камере", Float) = 0.6
        _Saturation ("Saturation", Float) = 1
        _AlphaView ("Диагностика альфы: 0 нет, 1 исходная, 2 после clip", Float) = 0
        _Ghost ("Призрачный контур", Float) = 0
        _GhostColor ("Цвет контура", Color) = (0.95, 0.85, 0.6, 0.45)
        _AccentInk ("Этап 03: перо акцента (sRGB)", Color) = (0.33, 0.25, 0.16, 1)
        _AccentOpacity ("Непрозрачность акцента", Range(0, 1)) = 0.85
        _AccentLight ("Подсветка массы", Color) = (0.62, 0.64, 0.42, 1)
        _AccentLightStrength ("Сила подсветки", Range(0, 1)) = 0.35
        _AccentHighlight ("Ширина подсветки, толщин линии", Float) = 2.2
        _AccentOn ("Акценты включены", Float) = 1
        _DiagMode ("Диагностика: 12 группы, 13 акценты", Float) = 0
        _DetailFade ("Этап 05: исчезновение мелких спрайтов, пикс./ед. (скрыт, виден)", Vector) = (0, 0, 0, 0)
        [Enum(Off, 0, On, 1)] _AlphaToMask ("Этап 05: AlphaToMask", Float) = 0
        _BlendAlpha ("R03: вывод мягкой альфы (смешивание тонких штрихов)", Float) = 0
        _SpriteStrokes ("Мазки на спрайтах: x сила, y режим (1 крона, 3 камень, 4 дерево/постройка), z кисть (BrushScale), ед.", Vector) = (0, 0, 1.6, 0)
        _StrokeDry ("Мазки: сухой оттенок", Color) = (0.77, 0.68, 0.38, 1)
        _StrokeLush ("Мазки: сочный оттенок", Color) = (0.45, 0.48, 0.25, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "MapLabCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 offset : TEXCOORD1;   // x — вправо по кадру, y — вверх от опоры (мир)
                float4 color : COLOR;        // r — яркость/1.25, g — сила оттенка, b — доля тени рельефа, a — низ кроны (v)
                float4 extra : TEXCOORD2;    // этап 03: x — акцент, y — метка группы, z/w — толщина пера (v/u)
                float2 rhythm : TEXCOORD3;   // этап 04: x — тепло/холод массы, y — светлый гребень
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float shade : TEXCOORD2;
                float4 extra : TEXCOORD3;
                float2 rhythm : TEXCOORD4;
                float fade : TEXCOORD5;
                float3 cardPos : TEXCOORD6;   // мир в плоскости карточки (x вправо, y вверх), z — seed экземпляра
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                float _CardLean;
                float4 _TintColor;
                float _TintStrength;
                float4 _ShadowTint;
                float _GroundShade;
                float _MapSize;
                float _DepthMargin;
                float _Saturation;
                float _AlphaView;
                float _Ghost;
                float4 _GhostColor;
                float4 _AccentInk;
                float _AccentOpacity;
                float4 _AccentLight;
                float _AccentLightStrength;
                float _AccentHighlight;
                float _AccentOn;
                float _DiagMode;
                float4 _DetailFade;
                float _AlphaToMask;
                float _BlendAlpha;
                float4 _SpriteStrokes;
                float4 _StrokeDry;
                float4 _StrokeLush;
            CBUFFER_END

            float StrokeHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float StrokeNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(StrokeHash(i), StrokeHash(i + float2(1, 0)), f.x), lerp(StrokeHash(i + float2(0, 1)), StrokeHash(i + float2(1, 1)), f.x), f.y);
            }

            // Те же мазки, что кладёт покраска на луга и поля (MapLabTiledPainter):
            // след кисти со скруглённой головой и сужением к хвосту, плотной
            // серединой, узким чётким краем и продольными «щетинками» (треугольная
            // волна поперёк следа — три-четыре линии). Длина — та же кисть
            // (BrushScale), раскладка — дрожащая сетка в мире плоскости карточки,
            // группы с пустотами. Цвет мазка — рисунок в его центре со сдвигом
            // оттенка (сухой/сочный для растений) и разбросом тона. Направление
            // задаётся один раз на мазок и подчиняется форме: крона — дугами по
            // массе и вдоль силуэта, ствол — вдоль, камень и дерево — по плоскостям.
            float StrokeWave(float x)
            {
                float t = frac(x * 0.15915494);
                return abs(t * 4.0 - 2.0) - 1.0;
            }

            float2 StrokeDirection(float2 uvc, int mode, float crownMinV)
            {
                const float3 w = float3(0.299, 0.587, 0.114);
                float2 d = float2(0.035, 0.035);
                float aR = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc + float2(d.x, 0), 4).a;
                float aL = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc - float2(d.x, 0), 4).a;
                float aU = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc + float2(0, d.y), 4).a;
                float aD = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc - float2(0, d.y), 4).a;
                float2 g = float2(aR - aL, aU - aD);
                float2 contour = normalize(float2(-g.y, g.x) + 1e-4);
                if (mode == 1 && uvc.y < crownMinV)
                    return float2(0, 1);
                if (mode == 1)
                {
                    float2 radial = uvc - float2(0.5, max(crownMinV, 0.35) + 0.25);
                    float2 swirl = normalize(float2(-radial.y, radial.x) + 1e-4);
                    return normalize(lerp(swirl, contour, saturate(length(g) * 5.0)) + 1e-4);
                }
                float lR = dot(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc + float2(d.x, 0), 3).rgb, w);
                float lL = dot(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc - float2(d.x, 0), 3).rgb, w);
                float lU = dot(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc + float2(0, d.y), 3).rgb, w);
                float lD = dot(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc - float2(0, d.y), 3).rgb, w);
                float2 lg = float2(lR - lL, lU - lD);
                float2 planes = normalize(float2(-lg.y, lg.x) + 1e-4);
                float2 fallback = mode == 4 ? float2(1, 0) : contour;
                return normalize(lerp(fallback, planes, saturate(length(lg) * 8.0)) + 1e-4);
            }

            float3 SpriteStrokes(float3 c, float2 uv, float3 cardPos, float crownMinV, float2 uvWidth)
            {
                int mode = (int)round(_SpriteStrokes.y);
                if (_SpriteStrokes.x <= 0.001 || mode == 0)
                    return c;
                float interior = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 3).a;
                float keep = smoothstep(0.5, 0.8, interior);
                if (keep <= 0.001)
                    return c;
                // Связь мира карточки с UV (ортография: постоянна по карточке).
                float2 p = cardPos.xy;
                float2 px = ddx(p), py = ddy(p), ux = ddx(uv), uy = ddy(uv);
                float det = px.x * py.y - px.y * py.x;
                if (abs(det) < 1e-10)
                    return c;
                float2x2 invP = float2x2(py.y, -py.x, -px.y, px.x) / det;
                float2 pxw2 = float2(length(px), length(py));
                float pixel = max(max(pxw2.x, pxw2.y), 1e-4);
                float brush = max(0.2, _SpriteStrokes.z);
                float cellSize = brush * 0.62;
                float2 cell0 = floor(p / cellSize);
                float strength = saturate(_SpriteStrokes.x);
                float split = 0.55;
                float edge = max(pixel * 0.9, cellSize * 0.012);
                float3 c0 = c;
                for (int j = -1; j <= 1; j++)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 cell = cell0 + float2(i, j);
                        float2 hseed = cell + cardPos.z * 13.7;
                        float roll = StrokeHash(hseed);
                        // Группы и тихие места (как у луга этапа 05).
                        float2 center = (cell + 0.5 + (float2(StrokeHash(hseed + 3.1), StrokeHash(hseed + 7.7)) - 0.5) * 0.5) * cellSize;
                        float group = lerp(0.75, 1.0, smoothstep(0.3, 0.62, StrokeNoise(center * 0.42 / brush + cardPos.z * 5.0)));
                        if (roll > 0.92 * group)
                            continue;
                        // UV центра мазка — для направления и цвета, взятого из рисунка.
                        float2 dp = center - p;
                        float2 coeff = mul(invP, dp);
                        float2 uvc = uv + ux * coeff.x + uy * coeff.y;
                        if (SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uvc, 3).a < 0.5)
                            continue;
                        float2 dir = StrokeDirection(uvc, mode, crownMinV);
                        float angleJ = (StrokeHash(hseed + 11.3) - 0.5) * 0.4;
                        float ca = cos(angleJ), sa = sin(angleJ);
                        dir = float2(dir.x * ca - dir.y * sa, dir.x * sa + dir.y * ca);
                        // 70/25/5 по весу и разброс длины, как у следов луга.
                        float sizeRoll = StrokeHash(hseed + 17.9);
                        float lengthJ = lerp(0.7, 1.3, StrokeHash(hseed + 5.3)) * (sizeRoll < 0.7 ? 1.0 : (sizeRoll < 0.95 ? 1.2 : 1.4));
                        float half = brush * 0.5 * lengthJ;
                        float halfW = half * 0.3;
                        float2 rel = p - center;
                        float along = dot(rel, dir);
                        float across = dot(rel, float2(-dir.y, dir.x));
                        float t = clamp(along / max(half, 1e-3), -1.0, 1.0);
                        float wv = halfW * (1.0 - 0.45 * smoothstep(-0.2, 1.0, t));
                        if (t < -0.7)
                            wv *= sqrt(max(0.0, 1.0 - pow((t + 0.7) / 0.3, 2.0)));
                        float cover = saturate((wv - abs(across)) / edge + 0.5);
                        float ends = saturate((half - abs(along)) / edge + 0.5);
                        float alphaJ = lerp(0.6, 1.0, StrokeHash(hseed + 23.1));
                        float a = alphaJ * lerp(0.35, 0.9, strength) * cover * ends * keep;
                        if (a <= 0.002)
                            continue;
                        // Основа — сам пиксель рисунка (с тонировкой карточки): мазок сдвигает
                        // оттенок и тон, детали и тушь рисунка остаются под ним.
                        float3 baseColor = c0;
                        float hueJ = StrokeHash(hseed + 29.3) * 2.0 - 1.0;
                        float valueJ = StrokeHash(hseed + 31.7) * 2.0 - 1.0;
                        float3 shift;
                        if (mode == 1)
                        {
                            const float3 wl = float3(0.299, 0.587, 0.114);
                            float3 tone = hueJ > 0 ? _StrokeDry.rgb : _StrokeLush.rgb;
                            shift = tone * (dot(baseColor, wl) / max(1e-3, dot(tone, wl)));
                        }
                        else
                            shift = baseColor * (hueJ > 0 ? float3(1.14, 1.06, 0.9) : float3(0.88, 0.94, 1.02));
                        float3 col = lerp(baseColor, shift, abs(hueJ) * 0.75) * (1.0 + valueJ * 0.3 * strength);
                        // Щетинки: продольные полосы поперёк следа.
                        float bristle = 1.0 + 0.14 * StrokeWave(across / max(halfW, 1e-3) * 7.9 + StrokeHash(hseed + 37.1) * 6.283);
                        // На общем виде мазок меньше пары пикселей — плавно гаснет без мерцания.
                        a *= saturate(halfW / pixel - 0.6);
                        c = lerp(c, col * bristle, a);
                    }
                }
                return c;
            }

            float _MapLabPixelScale;

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 basePoint = TransformObjectToWorld(input.positionOS);
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 camUp = normalize(UNITY_MATRIX_V[1].xyz);
                float3 toCamera = normalize(UNITY_MATRIX_V[2].xyz);
                float3 forward = -toCamera;
                camRight = normalize(float3(camRight.x, 0, camRight.z) + 1e-5);
                float3 up = normalize(lerp(float3(0, 1, 0), camUp, _CardLean));
                // Экранная высота карточки не зависит от наклона камеры.
                float screenScale = 1.0 / max(0.25, dot(up, camUp));
                float y = input.offset.y;
                float sinPitch = max(0.05, -forward.y);
                float cosPitch = length(forward.xz);
                // Насколько земля перед опорой ближе к камере, чем точка
                // карточки на той же экранной высоте ниже опоры.
                float need = max(0.0, cosPitch / sinPitch - screenScale * dot(up, forward));
                float push = max(0.0, -y) * need + _DepthMargin;
                float3 world = basePoint + camRight * input.offset.x + up * y * screenScale + toCamera * push;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.color = input.color;
                o.extra = input.extra;
                o.rhythm = input.rhythm;
                o.cardPos = float3(input.offset.x, input.offset.y, frac(dot(basePoint.xz, float2(0.0137, 0.0193)) * 97.0));
                float4 s = SAMPLE_TEXTURE2D_LOD(_ShadeTex, sampler_ShadeTex, basePoint.xz / max(1.0, _MapSize), 0);
                o.shade = saturate((1.0 - s.r) * 0.9 + (1.0 - s.g) * 0.35);
                // Этап 05: плотность пикселей итогового кадра на единицу мира (орто).
                o.fade = 1.0;
                if (_DetailFade.y > _DetailFade.x)
                {
                    float scale = _MapLabPixelScale > 0.0 ? _MapLabPixelScale : 1.0;
                    float ppu = _ScreenParams.y / max(1e-3, 2.0 * unity_OrthoParams.y) / scale;
                    o.fade = saturate((ppu - _DetailFade.x) / (_DetailFade.y - _DetailFade.x));
                }
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                if (_AlphaView > 0.5 && _AlphaView < 1.5)
                    return float4(tex.aaa, 1);
                if (_AlphaView > 1.5)
                {
                    // Отсечённое — красной штриховкой, оставшееся — исходным цветом.
                    float hatch = frac((i.positionCS.x + i.positionCS.y) / 8.0) < 0.5 ? 1 : 0.6;
                    if (tex.a < _Cutoff)
                    {
                        clip(tex.a - 0.02);
                        return float4(0.85 * hatch, 0.1, 0.1, 1);
                    }
                    return float4(tex.rgb, 1);
                }
                // Мелкий спрайт вдали плавно истаивает (силуэт уменьшается к нулю).
                tex.a *= i.fade;
                float outAlpha = 1;
                if (_BlendAlpha > 0.5)
                {
                    // Тонкий штрих: мягкая альфа рисунка, без порога-ступеньки.
                    clip(tex.a - _Cutoff);
                    outAlpha = saturate(tex.a);
                }
                else if (_AlphaToMask > 0.5)
                {
                    outAlpha = saturate((tex.a - _Cutoff) / max(fwidth(tex.a), 1e-4) + 0.5);
                    clip(outAlpha - 0.002);
                }
                else
                    clip(tex.a - _Cutoff);
                if (_Ghost > 0.5)
                    return _GhostColor;
                float3 c = tex.rgb;
                const float3 w = float3(0.299, 0.587, 0.114);
                float tint = saturate(_TintStrength * i.color.g * 2.0);
                float3 toned = _TintColor.rgb * (dot(c, w) / max(1e-3, dot(_TintColor.rgb, w)));
                c = lerp(c, toned, tint * 0.5);
                c *= i.color.r * 1.25;
                // Этап 04: тёплая или холодная масса, светлее выбранный гребень.
                c *= lerp(float3(1, 1, 1), float3(1.05, 1.0, 0.86), saturate(i.rhythm.x)) * lerp(float3(1, 1, 1), float3(0.93, 0.99, 1.07), saturate(-i.rhythm.x));
                c *= 1.0 + i.rhythm.y * 0.08;
                c = SpriteStrokes(c, i.uv, i.cardPos, i.color.a, abs(ddx(i.uv)) + abs(ddy(i.uv)));
                c = MapLabShadow(c, i.shade * i.color.b * _GroundShade, _ShadowTint.rgb);

                // Этап 03: акцент массы крон — линия по верхней/освещённой кромке
                // силуэта и узкая приглушённая подсветка под ней. Только на
                // непрозрачных пикселях этой же карточки (её глубина и её alpha):
                // не ложится на землю, не просвечивает сквозь ближнее дерево.
                float edgeMask = 0, bandMask = 0;
                float2 uvDx = ddx(i.uv), uvDy = ddy(i.uv);
                float2 uvWidth = abs(uvDx) + abs(uvDy);
                if (_AccentOn > 0.5 && i.extra.x > 0.01 && i.uv.y > i.color.a)
                {
                    float2 t = max(i.extra.wz, uvWidth * 0.9);
                    float up1 = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, i.uv + float2(0, t.y), uvDx, uvDy).a;
                    float ul1 = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, i.uv + float2(-t.x * 0.7, t.y * 0.7), uvDx, uvDy).a;
                    float k = 1.0 + _AccentHighlight;
                    float up2 = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, i.uv + float2(0, t.y * k), uvDx, uvDy).a;
                    float ul2 = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, i.uv + float2(-t.x * k * 0.8, t.y * k * 0.6), uvDx, uvDy).a;
                    edgeMask = smoothstep(_Cutoff + 0.18, _Cutoff - 0.18, min(up1, ul1));
                    bandMask = smoothstep(_Cutoff + 0.18, _Cutoff - 0.18, min(up2, ul2)) * (1.0 - edgeMask);
                    float fade = saturate((i.uv.y - i.color.a) * 12.0);
                    edgeMask *= fade * i.extra.x;
                    bandMask *= fade * i.extra.x;
                    float3 light = lerp(c, _AccentLight.rgb * (dot(c, w) / max(1e-3, dot(_AccentLight.rgb, w))) * 1.18, 0.7);
                    c = lerp(c, light, bandMask * _AccentLightStrength);
                    c = lerp(c, _AccentInk.rgb, edgeMask * _AccentOpacity);
                }
                int diag = (int)round(_DiagMode);
                if (diag == 12)
                    return float4(lerp(Hue(i.extra.y) * 0.8 + 0.1, float3(0.1, 0.1, 0.1), edgeMask), 1);
                if (diag == 13)
                    return float4(lerp(lerp(float3(0.82, 0.82, 0.78), float3(0.95, 0.85, 0.2), bandMask), float3(0.75, 0.1, 0.05), edgeMask), 1);
                c = MapLabSaturate(c, _Saturation);
                return float4(c, outAlpha);
            }
            ENDHLSL
        }
    }
}
