using System;

// Смена дня и ночи на глобальной карте (бриф 19.11.2026): чистая логика без
// Unity. Источник времени — существующие часы ContinuousSimulationSystem;
// своих часов и своей симуляции суток здесь нет. Все границы и силы света —
// рабочие настройки (WorldMapDayNightSettingsAsset), сезонов пока нет.
public enum DayPhase
{
    Dawn,
    Day,
    Dusk,
    Night
}

public struct DayNightLight
{
    // Множитель цвета рисунка местности (1,1,1 — без изменений).
    public float R;
    public float G;
    public float B;
}

public sealed class DayNightSettings
{
    public bool Enabled = true;

    public double DawnStartHour = 5.0;
    public double DayStartHour = 7.0;
    public double DuskStartHour = 17.0;
    public double NightStartHour = 19.0;

    // Ширина плавного перехода света вокруг середины рассвета/заката (часы).
    public double TransitionHours = 3.0;

    public DayNightLight DawnTint = new DayNightLight { R = 1.00f, G = 0.93f, B = 0.82f };
    public DayNightLight DuskTint = new DayNightLight { R = 0.97f, G = 0.84f, B = 0.66f };
    // Ночь не чёрная: холодный сине-серый, умеренно темнее.
    public DayNightLight NightTint = new DayNightLight { R = 0.62f, G = 0.68f, B = 0.84f };

    // Общая сила тонирования: 0 — карта всегда дневная, 1 — полные цвета выше.
    public double Strength = 1.0;

    public static DayNightSettings Default => new DayNightSettings();
}

public static class DayNightCycle
{
    // Текущие настройки (их задаёт ассет настроек при запуске карты).
    public static DayNightSettings Current = DayNightSettings.Default;

    public static double NormalizeHour(double hour)
    {
        double h = hour % 24.0;
        return h < 0 ? h + 24.0 : h;
    }

    public static DayPhase GetPhase(double hourOfDay, DayNightSettings settings = null)
    {
        DayNightSettings s = settings ?? Current;
        double h = NormalizeHour(hourOfDay);
        if (h >= s.DawnStartHour && h < s.DayStartHour) return DayPhase.Dawn;
        if (h >= s.DayStartHour && h < s.DuskStartHour) return DayPhase.Day;
        if (h >= s.DuskStartHour && h < s.NightStartHour) return DayPhase.Dusk;
        return DayPhase.Night;
    }

    // Название времени суток для подсказки: 07–11 — утро.
    public static string GetPhaseName(double hourOfDay, DayNightSettings settings = null)
    {
        double h = NormalizeHour(hourOfDay);
        switch (GetPhase(h, settings))
        {
            case DayPhase.Dawn: return "Рассвет";
            case DayPhase.Dusk: return "Закат";
            case DayPhase.Night: return "Ночь";
            default: return h < 11.0 ? "Утро" : "День";
        }
    }

    public static double GetAbsoluteHours(int day, double hourOfDay)
    {
        return Math.Max(0, day - 1) * 24.0 + hourOfDay;
    }

    // Поворот внутреннего диска (градусы, по часовой стрелке) при неподвижном
    // указателе на 12 часов. Солнце нарисовано сверху диска (0°), луна снизу:
    // в 12:00 под указателем солнце, в 00:00 — луна. Абсолютные часы партии
    // дают непрерывный угол без скачка на смене суток (один оборот — 24 ч).
    public static double GetDiscRotationDegrees(double absoluteHours)
    {
        return -(absoluteHours - 12.0) * 15.0;
    }

    // Тот же угол, приведённый к (-360; 0]: для показа без потери точности
    // на долгой партии. Полный оборот визуально неотличим, рывка нет.
    public static double GetDiscRotationWrapped(double absoluteHours)
    {
        double a = GetDiscRotationDegrees(absoluteHours) % 360.0;
        return a > 0 ? a - 360.0 : a;
    }

    // Час суток, который нарисован на диске под углом angle (градусы по
    // часовой стрелке от верха диска).
    public static double HourAtDiscAngle(double angleDegrees)
    {
        return NormalizeHour(12.0 + angleDegrees / 15.0);
    }

    // Доля ночи: 1 ночью, 0 днём; плавный переход шириной TransitionHours
    // вокруг середины рассвета и заката.
    public static double GetNightWeight(double hourOfDay, DayNightSettings settings = null)
    {
        DayNightSettings s = settings ?? Current;
        double h = NormalizeHour(hourOfDay);
        double half = Math.Max(0.01, s.TransitionHours * 0.5);
        double dawnMid = (s.DawnStartHour + s.DayStartHour) * 0.5;
        double duskMid = (s.DuskStartHour + s.NightStartHour) * 0.5;
        if (IsMorningSide(h, s))
            return 1.0 - Smooth((h - (dawnMid - half)) / (2 * half));
        return Smooth((h - (duskMid - half)) / (2 * half));
    }

    // Сила цвета рассвета/заката: наибольшая посреди перехода.
    public static double GetEdgeWeight(double hourOfDay, DayNightSettings settings = null)
    {
        double night = GetNightWeight(hourOfDay, settings);
        return 1.0 - Math.Abs(night * 2.0 - 1.0);
    }

    // Рассвет — до полудня, закат — после.
    public static bool IsMorningSide(double hourOfDay, DayNightSettings settings = null)
    {
        DayNightSettings s = settings ?? Current;
        double noon = (s.DawnStartHour + s.DayStartHour + s.DuskStartHour + s.NightStartHour) * 0.25;
        return NormalizeHour(hourOfDay) < noon;
    }

    // Свет местности: плавное смешение день → рассвет/закат → ночь.
    public static DayNightLight GetLight(double hourOfDay, DayNightSettings settings = null)
    {
        DayNightSettings s = settings ?? Current;
        DayNightLight day = new DayNightLight { R = 1f, G = 1f, B = 1f };
        if (!s.Enabled)
            return day;
        double night = GetNightWeight(hourOfDay, s);
        double edgeWeight = GetEdgeWeight(hourOfDay, s);
        DayNightLight edge = IsMorningSide(hourOfDay, s) ? s.DawnTint : s.DuskTint;
        DayNightLight baseLight = Lerp(day, s.NightTint, night);
        DayNightLight light = Lerp(baseLight, Multiply(baseLight, edge), edgeWeight);
        double strength = s.Strength < 0 ? 0 : s.Strength > 1 ? 1 : s.Strength;
        return Lerp(day, light, strength);
    }

    private static double Smooth(double t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return t * t * (3 - 2 * t);
    }

    private static DayNightLight Lerp(DayNightLight a, DayNightLight b, double t)
    {
        return new DayNightLight
        {
            R = (float)(a.R + (b.R - a.R) * t),
            G = (float)(a.G + (b.G - a.G) * t),
            B = (float)(a.B + (b.B - a.B) * t)
        };
    }

    private static DayNightLight Multiply(DayNightLight a, DayNightLight b)
    {
        return new DayNightLight { R = a.R * b.R, G = a.G * b.G, B = a.B * b.B };
    }
}
