using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: контуры рисунка по альфа-каналу — для тени от огня (по
    // желанию — только нижней части рисунка, которой он стоит на земле). Свет
    // проходит там, где рисунок прозрачен: между ногами, под рукой, сквозь
    // кольцо. Внешние контуры обходятся против часовой стрелки, дырки — по
    // часовой (непрозрачное всегда слева). Единицы — спрайта, от его опоры.
    // Альфа читается с GPU (текстура может быть нечитаемой), уменьшенной до
    // MaxGrid; контуры кэшируются по спрайту.
    public static class LocationAlphaContours
    {
        public const int MaxGrid = 1024;
        // Непрозрачно — альфа не меньше этого.
        public const byte AlphaCutoff = 128;
        // Упрощение лесенки пикселей (в клетках сетки) и самые мелкие пятна.
        public const float Tolerance = 1.2f;
        public const float MinArea = 6;

        private sealed class AlphaGrid
        {
            public byte[] Alpha;
            public int Width, Height;
            public float Scale;
        }

        private static readonly Dictionary<Texture2D, AlphaGrid> grids = new Dictionary<Texture2D, AlphaGrid>();
        private static readonly Dictionary<(Sprite, int), List<Vector2[]>> contours = new Dictionary<(Sprite, int), List<Vector2[]>>();

        // Контуры рисунка; band — доля высоты непрозрачного от низа (1 — весь
        // рисунок). Пусто — альфу прочитать не удалось.
        public static List<Vector2[]> Of(Sprite sprite, float band = 1)
        {
            if (sprite == null || sprite.texture == null) return new List<Vector2[]>();
            int key = Mathf.RoundToInt(Mathf.Clamp01(band) * 100);
            if (contours.TryGetValue((sprite, key), out List<Vector2[]> cached)) return cached;
            if (contours.Count > 4096) contours.Clear();
            List<Vector2[]> result = Build(sprite, key / 100f);
            contours[(sprite, key)] = result;
            return result;
        }

        private static List<Vector2[]> Build(Sprite sprite, float band)
        {
            List<Vector2[]> result = new List<Vector2[]>();
            AlphaGrid grid = Grid(sprite.texture);
            if (grid == null) return result;
            Rect rect;
            Vector2 offset;
            try { rect = sprite.textureRect; offset = sprite.textureRectOffset; }
            catch (System.Exception) { rect = sprite.rect; offset = Vector2.zero; }
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.xMin * grid.Scale), 0, grid.Width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.yMin * grid.Scale), 0, grid.Height);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax * grid.Scale), x0, grid.Width);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax * grid.Scale), y0, grid.Height);
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0) return result;
            bool[] inside = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    inside[y * w + x] = grid.Alpha[(y0 + y) * grid.Width + x0 + x] >= AlphaCutoff;
            Band(inside, w, h, band);
            float ppu = Mathf.Max(.001f, sprite.pixelsPerUnit);
            foreach (List<Vector2> loop in Trace(inside, w, h))
            {
                List<Vector2> simple = Simplify(loop, Tolerance);
                if (simple.Count < 3 || Mathf.Abs(Area(simple)) < MinArea) continue;
                Vector2[] points = new Vector2[simple.Count];
                for (int i = 0; i < simple.Count; i++)
                {
                    // Клетка сетки → пиксель текстуры → единицы спрайта от опоры.
                    Vector2 texel = new Vector2((x0 + simple[i].x) / grid.Scale - rect.xMin, (y0 + simple[i].y) / grid.Scale - rect.yMin);
                    points[i] = (offset + texel - sprite.pivot) / ppu;
                }
                result.Add(points);
            }
            return result;
        }

        // Оставить нижнюю долю band непрозрачного (от его нижнего ряда к верхнему).
        public static void Band(bool[] inside, int w, int h, float band)
        {
            if (band >= 1) return;
            int low = -1, high = -1;
            for (int y = 0; y < h && low < 0; y++)
                for (int x = 0; x < w; x++)
                    if (inside[y * w + x]) { low = y; break; }
            for (int y = h - 1; y >= 0 && high < 0; y--)
                for (int x = 0; x < w; x++)
                    if (inside[y * w + x]) { high = y; break; }
            if (low < 0) return;
            int cut = low + Mathf.Max(1, Mathf.CeilToInt((high - low + 1) * Mathf.Clamp01(band)));
            for (int y = cut; y < h; y++)
                for (int x = 0; x < w; x++)
                    inside[y * w + x] = false;
        }

        private static AlphaGrid Grid(Texture2D texture)
        {
            if (grids.TryGetValue(texture, out AlphaGrid cached)) return cached;
            if (grids.Count > 64) grids.Clear();
            AlphaGrid grid = null;
            try
            {
                float scale = Mathf.Min(1, MaxGrid / (float)Mathf.Max(texture.width, texture.height));
                int w = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
                Color32[] pixels;
                if (texture.isReadable && w == texture.width && h == texture.height)
                    pixels = texture.GetPixels32();
                else
                {
                    RenderTexture target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    RenderTexture previous = RenderTexture.active;
                    Graphics.Blit(texture, target);
                    RenderTexture.active = target;
                    Texture2D read = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                    read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                    read.Apply(false);
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    pixels = read.GetPixels32();
                    if (Application.isPlaying) Object.Destroy(read); else Object.DestroyImmediate(read);
                }
                byte[] alpha = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
                grid = new AlphaGrid { Alpha = alpha, Width = w, Height = h, Scale = w / (float)texture.width };
            }
            catch (System.Exception)
            {
                grid = null;
            }
            grids[texture] = grid;
            return grid;
        }

        // Границы непрозрачных клеток — направленные рёбра (непрозрачное слева),
        // собранные в замкнутые обходы. Углы сетки: (w + 1) × (h + 1).
        public static List<List<Vector2>> Trace(bool[] inside, int w, int h)
        {
            bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && inside[y * w + x];
            int stride = w + 1;
            // До двух исходящих рёбер у угла; ребро — угол начала и направление.
            int[] outgoing = new int[stride * (h + 1) * 2];
            for (int i = 0; i < outgoing.Length; i++) outgoing[i] = -1;
            List<int> starts = new List<int>();
            List<int> directions = new List<int>();
            void Add(int x, int y, int direction)
            {
                int corner = y * stride + x;
                int edge = starts.Count;
                starts.Add(corner);
                directions.Add(direction);
                if (outgoing[corner * 2] < 0) outgoing[corner * 2] = edge;
                else outgoing[corner * 2 + 1] = edge;
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!In(x, y)) continue;
                    if (!In(x, y - 1)) Add(x, y, 0);         // низ: вправо
                    if (!In(x + 1, y)) Add(x + 1, y, 1);     // право: вверх
                    if (!In(x, y + 1)) Add(x + 1, y + 1, 2); // верх: влево
                    if (!In(x - 1, y)) Add(x, y + 1, 3);     // лево: вниз
                }

            int[] dx = { 1, 0, -1, 0 }, dy = { 0, 1, 0, -1 };
            bool[] used = new bool[starts.Count];
            List<List<Vector2>> loops = new List<List<Vector2>>();
            for (int first = 0; first < starts.Count; first++)
            {
                if (used[first]) continue;
                List<Vector2> loop = new List<Vector2>();
                int edge = first;
                while (edge >= 0 && !used[edge])
                {
                    used[edge] = true;
                    int corner = starts[edge], direction = directions[edge];
                    loop.Add(new Vector2(corner % stride, corner / stride));
                    int end = (corner / stride + dy[direction]) * stride + corner % stride + dx[direction];
                    int a = outgoing[end * 2], b = outgoing[end * 2 + 1];
                    if (a >= 0 && used[a]) a = -1;
                    if (b >= 0 && used[b]) b = -1;
                    // Касание по диагонали: поворот налево — клетки не сливаются.
                    if (a >= 0 && b >= 0)
                        edge = directions[a] == (direction + 1) % 4 ? a : directions[b] == (direction + 1) % 4 ? b : a;
                    else
                        edge = a >= 0 ? a : b;
                }
                // Только повороты: прямые участки не плодят точек.
                loops.Add(RemoveStraight(loop));
            }
            return loops;
        }

        private static List<Vector2> RemoveStraight(List<Vector2> loop)
        {
            List<Vector2> result = new List<Vector2>();
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 before = loop[(i + n - 1) % n], point = loop[i], after = loop[(i + 1) % n];
                if (Mathf.Abs((point.x - before.x) * (after.y - point.y) - (point.y - before.y) * (after.x - point.x)) > 1e-5f)
                    result.Add(point);
            }
            return result;
        }

        // Площадь со знаком: > 0 — против часовой стрелки.
        public static float Area(IList<Vector2> loop)
        {
            float sum = 0;
            for (int i = 0, n = loop.Count; i < n; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % n];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum / 2;
        }

        // Дуглас — Пекер для замкнутого контура: две дуги от самой дальней пары.
        public static List<Vector2> Simplify(List<Vector2> loop, float tolerance)
        {
            int n = loop.Count;
            if (n < 4) return new List<Vector2>(loop);
            int far = 0;
            float best = -1;
            for (int i = 1; i < n; i++)
            {
                float distance = (loop[i] - loop[0]).sqrMagnitude;
                if (distance > best) { best = distance; far = i; }
            }
            List<Vector2> result = new List<Vector2>();
            Arc(loop, 0, far, tolerance, result);
            Arc(loop, far, n, tolerance, result);
            return result;
        }

        // Добавляет точки дуги [from, to) (to == n — до начала контура).
        private static void Arc(List<Vector2> loop, int from, int to, float tolerance, List<Vector2> result)
        {
            int n = loop.Count;
            Vector2 a = loop[from], b = loop[to % n];
            int keep = -1;
            float best = tolerance;
            Vector2 line = b - a;
            float length = line.magnitude;
            for (int i = from + 1; i < to; i++)
            {
                Vector2 p = loop[i];
                float distance = length < 1e-5f ? (p - a).magnitude : Mathf.Abs(line.x * (p.y - a.y) - line.y * (p.x - a.x)) / length;
                if (distance > best) { best = distance; keep = i; }
            }
            if (keep < 0)
            {
                result.Add(a);
                return;
            }
            Arc(loop, from, keep, tolerance, result);
            Arc(loop, keep, to, tolerance, result);
        }
    }
}
