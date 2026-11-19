using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

// Смена дня и ночи (бриф 19.11.2026). Круг суток рядом с цифровыми часами
// в нижней панели и свет живой карты по времени суток. Источник — те же
// часы мира (ContinuousSimulationSystem): своих часов нет, пауза и темп
// ×1/×2/×3 действуют сами собой, после загрузки положение берётся из
// восстановленных часов. Элементы круга создаются один раз и рисуются
// программно (Painter2D); каждый кадр меняется только поворот диска.
// Свет — обратимый множитель цвета рисунка живой карты MapLab
// (unityBackgroundImageTintColor): PNG, генератор и рисунок не меняются,
// значки, герой, подписи и интерфейс лежат поверх и не тонируются.
public partial class PrototypeUIController
{
    private VisualElement dayNightDial;
    private VisualElement dayNightDisc;
    private float dayNightShownAngle = float.NaN;
    private string dayNightShownPhase;
    private DayNightSettings dayNightDrawnSettings;
    private VisualElement dayNightTintedElement;
    private Color dayNightShownTint;
    private bool dayNightSettingsApplied;

    // Цвета круга (рисунок карты: спокойные заливки, тёплая тушь).
    private static readonly Color DialDaySky = new Color(0.87f, 0.75f, 0.50f);
    private static readonly Color DialNightSky = new Color(0.17f, 0.21f, 0.33f);
    private static readonly Color DialDawnSky = new Color(0.90f, 0.62f, 0.46f);
    private static readonly Color DialDuskSky = new Color(0.84f, 0.50f, 0.31f);
    private static readonly Color DialInk = new Color(0.15f, 0.10f, 0.07f);
    private static readonly Color DialWarmInk = new Color(0.47f, 0.28f, 0.12f);
    private static readonly Color DialRim = new Color(0.40f, 0.30f, 0.19f);
    private static readonly Color DialSun = new Color(0.98f, 0.83f, 0.44f);
    private static readonly Color DialMoon = new Color(0.93f, 0.90f, 0.77f);
    private static readonly Color DialStar = new Color(0.94f, 0.91f, 0.76f);

    private void RefreshDayNightPresentation(ContinuousClockSnapshot clock)
    {
        if (gameState == null)
            return;

        if (!dayNightSettingsApplied)
        {
            WorldMapDayNightSettingsAsset.ApplyToCore();
            dayNightSettingsApplied = true;
        }

        EnsureDayNightDial();

        if (dayNightDisc != null)
        {
            if (!ReferenceEquals(dayNightDrawnSettings, DayNightCycle.Current))
            {
                dayNightDrawnSettings = DayNightCycle.Current;
                dayNightDisc.MarkDirtyRepaint();
            }

            double absoluteHours = ContinuousSimulationSystem.GetAbsoluteHours(gameState);
            float angle = (float)DayNightCycle.GetDiscRotationWrapped(absoluteHours);
            if (float.IsNaN(dayNightShownAngle) || Mathf.Abs(angle - dayNightShownAngle) > 0.01f)
            {
                dayNightShownAngle = angle;
                dayNightDisc.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            }

            string phase = DayNightCycle.GetPhaseName(clock.HourOfDay);
            if (phase != dayNightShownPhase)
            {
                dayNightShownPhase = phase;
                dayNightDial.tooltip = "Время суток: " + phase.ToLowerInvariant() + ".";
            }
        }

        ApplyWorldMapDayNightLight(clock.HourOfDay);
    }

    private void EnsureDayNightDial()
    {
        if (dayLabel == null || dayLabel.parent == null)
            return;

        if (dayNightDial == null)
        {
            dayNightDial = new VisualElement { name = "day-night-dial", pickingMode = PickingMode.Position };
            dayNightDial.AddToClassList("day-night-dial");

            dayNightDisc = new VisualElement { name = "day-night-disc", pickingMode = PickingMode.Ignore };
            dayNightDisc.AddToClassList("day-night-disc");
            dayNightDisc.generateVisualContent += DrawDayNightDisc;
            dayNightDial.Add(dayNightDisc);

            VisualElement frame = new VisualElement { name = "day-night-frame", pickingMode = PickingMode.Ignore };
            frame.AddToClassList("day-night-frame");
            frame.generateVisualContent += DrawDayNightFrame;
            dayNightDial.Add(frame);
        }

        // Сразу справа от «День N · HH:MM» (темп карты — дальше).
        VisualElement host = dayLabel.parent;
        int want = host.IndexOf(dayLabel) + 1;
        if (dayNightDial.parent != host || host.IndexOf(dayNightDial) != want)
        {
            dayNightDial.RemoveFromHierarchy();
            host.Insert(host.IndexOf(dayLabel) + 1, dayNightDial);
        }
    }

    private void ApplyWorldMapDayNightLight(double hourOfDay)
    {
        DayNightLight light = DayNightCycle.GetLight(hourOfDay);
        Color tint = new Color(light.R, light.G, light.B, 1f);
        // Облака тумана лежат над картой — тот же свет суток, иначе они
        // светятся днём посреди ночной карты.
        SetWorldMapFogCloudsLight(tint);

        if (worldMapLiveImage == null)
            return;

        if (ReferenceEquals(dayNightTintedElement, worldMapLiveImage) &&
            Mathf.Abs(tint.r - dayNightShownTint.r) < 0.002f &&
            Mathf.Abs(tint.g - dayNightShownTint.g) < 0.002f &&
            Mathf.Abs(tint.b - dayNightShownTint.b) < 0.002f)
        {
            return;
        }

        dayNightTintedElement = worldMapLiveImage;
        dayNightShownTint = tint;
        worldMapLiveImage.style.unityBackgroundImageTintColor = tint;
    }

    // ---------- Рисунок круга ----------

    // Цвет неба в данный час: день → рассвет/закат → ночь, как и свет карты.
    private static Color DialSkyAt(double hour)
    {
        float night = (float)DayNightCycle.GetNightWeight(hour);
        float edge = (float)DayNightCycle.GetEdgeWeight(hour);
        Color sky = Color.Lerp(DialDaySky, DialNightSky, night);
        Color edgeColor = DayNightCycle.IsMorningSide(hour) ? DialDawnSky : DialDuskSky;
        return Color.Lerp(sky, edgeColor, edge * 0.85f);
    }

    // Точка диска: угол по часовой стрелке от верха, доля радиуса.
    private static Vector2 DialPoint(Vector2 center, float radius, float angleDegrees, float fraction)
    {
        float a = angleDegrees * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius * fraction;
    }

    private static Angle PainterAngle(float clockwiseFromTop) =>
        new Angle(clockwiseFromTop - 90f, AngleUnit.Degree);

    private static void DrawDayNightDisc(MeshGenerationContext context)
    {
        Rect rect = context.visualElement.contentRect;
        if (rect.width < 4f || rect.height < 4f)
            return;

        Painter2D p = context.painter2D;
        Vector2 c = rect.center;
        float r = Mathf.Min(rect.width, rect.height) * 0.5f;

        // Небо: 24 часа по кругу узкими секторами с лёгким перекрытием.
        const int wedges = 72;
        float step = 360f / wedges;
        for (int i = 0; i < wedges; i++)
        {
            float a0 = i * step;
            double hour = DayNightCycle.HourAtDiscAngle(a0 + step * 0.5f);
            p.fillColor = DialSkyAt(hour);
            p.BeginPath();
            p.MoveTo(c);
            p.Arc(c, r, PainterAngle(a0), PainterAngle(a0 + step + 0.8f));
            p.ClosePath();
            p.Fill();
        }

        // Живописный мазок: светлая дуга по дневной части, тёмная — по ночной.
        p.lineCap = LineCap.Round;
        p.lineWidth = r * 0.16f;
        p.strokeColor = new Color(1f, 0.95f, 0.80f, 0.16f);
        p.BeginPath();
        p.Arc(c, r * 0.74f, PainterAngle(-62f), PainterAngle(48f));
        p.Stroke();
        p.strokeColor = new Color(0.05f, 0.07f, 0.14f, 0.20f);
        p.BeginPath();
        p.Arc(c, r * 0.80f, PainterAngle(122f), PainterAngle(214f));
        p.Stroke();

        DrawDialSun(p, DialPoint(c, r, 0f, 0.50f), r * 0.20f, r);
        DrawDialMoon(p, DialPoint(c, r, 180f, 0.50f), r * 0.19f, DialSkyAt(0.0));

        // Звёзды: редкие, разного размера, без ровного ритма.
        DrawDialStar(p, DialPoint(c, r, 138f, 0.80f), 1.5f, true);
        DrawDialStar(p, DialPoint(c, r, 226f, 0.78f), 1.1f, false);
        DrawDialStar(p, DialPoint(c, r, 116f, 0.48f), 1.0f, false);
        DrawDialStar(p, DialPoint(c, r, 252f, 0.54f), 1.7f, true);
        DrawDialStar(p, DialPoint(c, r, 203f, 0.27f), 0.9f, false);
        DrawDialStar(p, DialPoint(c, r, 160f, 0.90f), 0.8f, false);
    }

    private static void DrawDialSun(Painter2D p, Vector2 sun, float radius, float discRadius)
    {
        // Редкие лучи неровной длины.
        float[] rays = { 8f, 52f, 101f, 147f, 206f, 251f, 304f };
        float[] lengths = { 0.55f, 0.40f, 0.62f, 0.38f, 0.50f, 0.60f, 0.42f };
        p.lineCap = LineCap.Round;
        p.lineWidth = Mathf.Max(1f, discRadius * 0.045f);
        p.strokeColor = new Color(DialWarmInk.r, DialWarmInk.g, DialWarmInk.b, 0.85f);
        for (int i = 0; i < rays.Length; i++)
        {
            p.BeginPath();
            p.MoveTo(DialPoint(sun, radius, rays[i], 1.30f));
            p.LineTo(DialPoint(sun, radius, rays[i], 1.30f + lengths[i]));
            p.Stroke();
        }

        p.fillColor = DialSun;
        p.BeginPath();
        p.Arc(sun, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
        p.ClosePath();
        p.Fill();

        // Цветная тень снизу и выборочная тушь — не сплошной обвод.
        p.fillColor = new Color(0.85f, 0.56f, 0.24f, 0.45f);
        p.BeginPath();
        p.Arc(sun, radius * 0.98f, PainterAngle(100f), PainterAngle(250f));
        p.ClosePath();
        p.Fill();
        p.lineWidth = Mathf.Max(1f, discRadius * 0.04f);
        p.strokeColor = DialWarmInk;
        p.BeginPath();
        p.Arc(sun, radius, PainterAngle(110f), PainterAngle(285f));
        p.Stroke();
    }

    private static void DrawDialMoon(Painter2D p, Vector2 moon, float radius, Color night)
    {
        p.fillColor = DialMoon;
        p.BeginPath();
        p.Arc(moon, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
        p.ClosePath();
        p.Fill();

        // Серп: тёмный круг со сдвигом закрывает часть луны.
        p.fillColor = night;
        p.BeginPath();
        p.Arc(moon + new Vector2(radius * 0.48f, -radius * 0.22f), radius * 0.86f,
            new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
        p.ClosePath();
        p.Fill();

        p.lineCap = LineCap.Round;
        p.lineWidth = 1f;
        p.strokeColor = new Color(DialInk.r, DialInk.g, DialInk.b, 0.7f);
        p.BeginPath();
        p.Arc(moon, radius, PainterAngle(200f), PainterAngle(318f));
        p.Stroke();
    }

    private static void DrawDialStar(Painter2D p, Vector2 at, float size, bool cross)
    {
        p.fillColor = DialStar;
        p.BeginPath();
        p.Arc(at, size * 0.75f, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
        p.ClosePath();
        p.Fill();
        if (!cross)
            return;
        p.lineCap = LineCap.Round;
        p.lineWidth = 0.8f;
        p.strokeColor = new Color(DialStar.r, DialStar.g, DialStar.b, 0.75f);
        p.BeginPath();
        p.MoveTo(at + new Vector2(-size * 1.9f, 0f));
        p.LineTo(at + new Vector2(size * 1.9f, 0f));
        p.MoveTo(at + new Vector2(0f, -size * 1.9f));
        p.LineTo(at + new Vector2(0f, size * 1.9f));
        p.Stroke();
    }

    // Неподвижная рамка и указатель на 12 часов.
    private static void DrawDayNightFrame(MeshGenerationContext context)
    {
        Rect rect = context.visualElement.contentRect;
        if (rect.width < 8f || rect.height < 8f)
            return;

        Painter2D p = context.painter2D;
        Vector2 c = rect.center;
        float R = Mathf.Min(rect.width, rect.height) * 0.5f;
        Angle full0 = new Angle(0f, AngleUnit.Degree), full1 = new Angle(360f, AngleUnit.Degree);

        // Обод — спокойная заливка с тёплым светом сверху.
        p.lineWidth = 5f;
        p.strokeColor = DialRim;
        p.BeginPath();
        p.Arc(c, R - 2.6f, full0, full1);
        p.Stroke();
        p.lineCap = LineCap.Round;
        p.lineWidth = 1.6f;
        p.strokeColor = new Color(0.72f, 0.58f, 0.36f, 0.55f);
        p.BeginPath();
        p.Arc(c, R - 3.2f, PainterAngle(-70f), PainterAngle(55f));
        p.Stroke();

        // Тонкий край диска и выборочная тушь снаружи — с просветами и
        // живыми окончаниями (MAP_STYLE_GUIDE §2.5).
        p.lineWidth = 0.9f;
        p.strokeColor = new Color(DialInk.r, DialInk.g, DialInk.b, 0.55f);
        p.BeginPath();
        p.Arc(c, R - 5.1f, full0, full1);
        p.Stroke();

        p.strokeColor = DialInk;
        p.lineWidth = 1.3f;
        p.BeginPath();
        p.Arc(c, R - 0.8f, PainterAngle(14f), PainterAngle(152f));
        p.Stroke();
        p.lineWidth = 1.0f;
        p.BeginPath();
        p.Arc(c, R - 0.8f, PainterAngle(166f), PainterAngle(282f));
        p.Stroke();
        p.lineWidth = 0.8f;
        p.BeginPath();
        p.Arc(c, R - 0.8f, PainterAngle(291f), PainterAngle(300f));
        p.Stroke();
        p.fillColor = DialInk;
        foreach (float dot in new[] { 158f, 306f })
        {
            p.BeginPath();
            p.Arc(DialPoint(c, R - 0.8f, dot, 1f), 0.55f, full0, full1);
            p.ClosePath();
            p.Fill();
        }

        // Указатель: небольшой наконечник сверху, остриём к диску.
        float top = rect.yMin + 0.5f;
        float half = 5.2f;
        p.fillColor = new Color(0.90f, 0.70f, 0.33f);
        p.strokeColor = DialInk;
        p.lineWidth = 1.2f;
        p.lineJoin = LineJoin.Round;
        p.BeginPath();
        p.MoveTo(new Vector2(c.x - half, top));
        p.LineTo(new Vector2(c.x + half, top));
        p.LineTo(new Vector2(c.x, top + 11f));
        p.ClosePath();
        p.Fill();
        p.Stroke();
    }
}
