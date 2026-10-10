using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Как записана карта нормалей. Correct — векторы единичные (как надо);
    // Gamma — записана с гамма-коррекцией sRGB (Blender не в Raw): единичными
    // векторы становятся после пересчёта sRGB → линейные, это исправимо;
    // Other — не единичные ни так, ни так: другой проход (нормали в мировых
    // координатах), тон-маппинг AgX / Filmic и т. п. — пересчёт не поможет.
    public enum ArtAssetNormalEncoding { Unknown, Correct, Gamma, Other }

    public readonly struct ArtAssetNormalStats
    {
        public readonly int Samples;
        // Средняя длина вектора как записано.
        public readonly float Length;
        // Доля векторов длиной 1 ± 5 %: как записано и после пересчёта sRGB → линейные.
        public readonly float UnitShare, LinearUnitShare;
        // Доля векторов, повёрнутых от зрителя (синий заметно меньше 128).
        public readonly float AwayShare;

        public ArtAssetNormalStats(int samples, float length, float unitShare, float linearUnitShare, float awayShare)
        {
            Samples = samples;
            Length = length;
            UnitShare = unitShare;
            LinearUnitShare = linearUnitShare;
            AwayShare = awayShare;
        }

        public ArtAssetNormalEncoding Encoding =>
            Samples < 50 ? ArtAssetNormalEncoding.Unknown
            : LinearUnitShare >= .85f && LinearUnitShare >= UnitShare + .3f ? ArtAssetNormalEncoding.Gamma
            : UnitShare >= .5f ? ArtAssetNormalEncoding.Correct
            : ArtAssetNormalEncoding.Other;
    }

    // Проверка и исправление карт нормалей по самому PNG (импортированная
    // текстура сжата и для этого не годится). Пороги выверены на картах
    // проекта: правильные — 60–100 % единичных векторов как записано, с гаммой
    // sRGB — 96–100 % после пересчёта, мировые нормали и тон-маппинг —
    // меньше половины и так, и так.
    public static class ArtAssetNormalCheck
    {
        private const float UnitTolerance = .05f;
        private const int MaxSamples = 40000;

        private static readonly Dictionary<string, (DateTime stamp, ArtAssetNormalStats stats)> cache =
            new Dictionary<string, (DateTime, ArtAssetNormalStats)>();

        public static ArtAssetNormalStats Measure(Texture2D normal) =>
            normal == null ? default : Measure(AssetDatabase.GetAssetPath(normal));

        // path — PNG в проекте или вне его.
        public static ArtAssetNormalStats Measure(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return default;
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (cache.TryGetValue(path, out (DateTime stamp, ArtAssetNormalStats stats) cached) && cached.stamp == stamp) return cached.stats;
            ArtAssetNormalStats stats = default;
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (image.LoadImage(File.ReadAllBytes(path), false)) stats = Measure(image.GetPixels32());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
            cache[path] = (stamp, stats);
            return stats;
        }

        public static ArtAssetNormalStats Measure(Color32[] pixels)
        {
            float[] linear = LinearTable();
            int step = Mathf.Max(1, pixels.Length / MaxSamples), count = 0, unit = 0, linearUnit = 0, away = 0;
            double sum = 0;
            for (int i = 0; i < pixels.Length; i += step)
            {
                Color32 c = pixels[i];
                if (c.a < 200) continue;
                float x = c.r / 127.5f - 1, y = c.g / 127.5f - 1, z = c.b / 127.5f - 1;
                float length = Mathf.Sqrt(x * x + y * y + z * z);
                sum += length;
                if (Mathf.Abs(length - 1) < UnitTolerance) unit++;
                float lx = linear[c.r] * 2 - 1, ly = linear[c.g] * 2 - 1, lz = linear[c.b] * 2 - 1;
                if (Mathf.Abs(Mathf.Sqrt(lx * lx + ly * ly + lz * lz) - 1) < UnitTolerance) linearUnit++;
                if (z < -.05f) away++;
                count++;
            }
            if (count == 0) return default;
            return new ArtAssetNormalStats(count, (float)(sum / count), unit / (float)count, linearUnit / (float)count, away / (float)count);
        }

        // Снять гамма-коррекцию sRGB с PNG на месте (альфа не меняется).
        // Файл переписывается — вызывать только для копии в проекте.
        public static bool Linearize(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path), false)) return false;
                float[] linear = LinearTable();
                Color32[] pixels = image.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 c = pixels[i];
                    pixels[i] = new Color32(Byte(linear[c.r]), Byte(linear[c.g]), Byte(linear[c.b]), c.a);
                }
                image.SetPixels32(pixels);
                image.Apply(false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
            cache.Remove(path);
            return true;
        }

        private static float[] linearTable;

        private static float[] LinearTable()
        {
            if (linearTable != null) return linearTable;
            linearTable = new float[256];
            for (int i = 0; i < 256; i++) linearTable[i] = Mathf.GammaToLinearSpace(i / 255f);
            return linearTable;
        }

        private static byte Byte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        // Готовый текст замечания проверки; null — замечаний нет.
        public static string Problem(ArtAssetNormalStats stats)
        {
            switch (stats.Encoding)
            {
                case ArtAssetNormalEncoding.Gamma:
                    return "нормаль записана с гамма-коррекцией sRGB (длина векторов " + stats.Length.ToString("0.00") +
                           " вместо 1,00): свет ложится криво. В Blender — View Transform = Raw; для готового файла — «Исправить гамму нормалей» " +
                           "(при загрузке папкой исправляется само).";
                case ArtAssetNormalEncoding.Other:
                    return "это не похоже на карту нормалей для 2D-света: единичных векторов " + Percent(stats.UnitShare) +
                           (stats.AwayShare > .05f ? ", от зрителя повёрнуто " + Percent(stats.AwayShare) : "") +
                           ". Скорее всего, другой проход Blender (нормали в мировых координатах) или тон-маппинг AgX / Filmic. " +
                           "Перерендерите: View Transform = Raw, нормали в пространстве камеры. «Исправить гамму» тут не поможет.";
                default:
                    return null;
            }
        }

        private static string Percent(float share) => Mathf.RoundToInt(share * 100) + " %";
    }
}
