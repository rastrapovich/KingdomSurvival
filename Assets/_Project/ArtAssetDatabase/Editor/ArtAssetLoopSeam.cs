using System.IO;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Стык петли: рендер «LOOP» часто кончается кадром, который повторяет
    // первый (Blender: кадры 1…61 при петле в 60 — 61-й и есть 1-й). В игре
    // петля тогда показывает один рисунок два кадра подряд — анимация
    // «зависает» на стыке. Такой последний кадр распознаётся по картинке:
    // он заметно ближе к первому, чем соседние кадры друг к другу.
    public static class ArtAssetLoopSeam
    {
        // Последний ближе к первому, чем эта доля обычной разницы соседей.
        public const float SeamShare = .5f;
        // Выборка — не больше стольких точек по стороне (рисунки до 4096).
        private const int Samples = 256;

        // Пути PNG по порядку кадров (первый — первый кадр). true — последний
        // кадр повторяет первый и его стоит убрать.
        public static bool LastRepeatsFirst(System.Collections.Generic.IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count < 4) return false;
            Texture2D first = Load(paths[0]), second = Load(paths[1]), beforeLast = Load(paths[paths.Count - 2]), last = Load(paths[paths.Count - 1]);
            try
            {
                if (first == null || second == null || beforeLast == null || last == null) return false;
                float seam = Difference(first, last);
                float neighbours = Mathf.Min(Difference(first, second), Difference(beforeLast, last));
                if (float.IsNaN(seam) || float.IsNaN(neighbours)) return false;
                return seam <= .001f || (neighbours > .001f && seam < neighbours * SeamShare);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(beforeLast);
                Object.DestroyImmediate(last);
            }
        }

        private static Texture2D Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(path), false)) return texture;
            Object.DestroyImmediate(texture);
            return null;
        }

        // Средняя разница цвета и альфы по выборке; разный размер — NaN.
        public static float Difference(Texture2D a, Texture2D b)
        {
            if (a.width != b.width || a.height != b.height) return float.NaN;
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            int step = Mathf.Max(1, Mathf.Max(a.width, a.height) / Samples);
            double sum = 0;
            int count = 0;
            for (int y = 0; y < a.height; y += step)
                for (int x = 0; x < a.width; x += step)
                {
                    Color32 p = pa[y * a.width + x], q = pb[y * a.width + x];
                    sum += Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b) + Mathf.Abs(p.a - q.a);
                    count++;
                }
            return count == 0 ? float.NaN : (float)(sum / count);
        }
    }
}
