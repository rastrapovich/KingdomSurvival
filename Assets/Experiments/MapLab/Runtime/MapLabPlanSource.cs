using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // 12У-D: план региона из генератора игры для окна «Генератор карты».
    // MapLab не зависит от ядра игры: мост (KingdomSurvival.MapLabBridge)
    // регистрирует построитель, окно вызывает его перед генерацией рисунка.
    // Построитель кладёт композицию в settings.RegionPlan и возвращает отчёт.
    public sealed class MapLabPlanReport
    {
        public readonly List<(bool Error, string Text)> Issues = new List<(bool, string)>();
        public readonly List<string> Passport = new List<string>();
        public string Key = string.Empty;
        public Texture2D Diagram;
    }

    public static class MapLabPlanSource
    {
        public static Func<MapLabSettings, MapLabPlanReport> Build;

        public static bool Available => Build != null;

        public static readonly string[] RegionNames = { "По seed", "Речная долина", "Лесное озеро", "Холм и уступ", "Обжитая земля" };
    }
}
