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
        // Диагностические слои (проходимость, время пути, заметность) и
        // привязка историй к этому плану.
        public readonly List<(string Title, string Legend, Texture2D Texture)> Layers = new List<(string, string, Texture2D)>();
        public readonly List<string> Stories = new List<string>();
    }

    public static class MapLabPlanSource
    {
        public static Func<MapLabSettings, MapLabPlanReport> Build;
        // Настройки окна — в новые партии игры (возвращает сообщение для автора).
        public static Func<MapLabSettings, string> ApplyToGame;

        public static bool Available => Build != null;

        // Общие настройки игры для окна генератора (время и движение по карте):
        // рисует мост к игре, у самого MapLab таких данных нет.
        public static Action DrawGameSettings;

        public static readonly string[] RegionNames = { "По seed", "Речная долина", "Лесное озеро", "Холм и уступ", "Обжитая земля" };
    }
}
