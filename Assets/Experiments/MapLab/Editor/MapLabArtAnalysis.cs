using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Измерения исходного PNG (читается из файла, импортированная текстура не
    // обязана быть readable): силуэт по порогу альфы, центр нижней полосы
    // силуэта, средний цвет. Bbox по любому alpha > 0 не используется: в
    // рисунках есть ореол и единичные пиксели далеко от силуэта.
    public static class MapLabArtAnalysis
    {
        public struct Result
        {
            public Rect Solid;     // доли холста, левый нижний угол
            public float FootX;    // доля холста
            public Color Mean;
            public int Width;
            public int Height;
        }

        public static Result Analyze(byte[] png, int alphaThreshold)
        {
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!image.LoadImage(png, false))
                    return new Result { Solid = new Rect(0, 0, 1, 1), FootX = 0.5f, Mean = Color.gray };
                return Analyze(image.GetPixels32(), image.width, image.height, alphaThreshold);
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        // Этап 05: углы основания постройки по нижнему контуру силуэта. F — самая
        // низкая точка (передний угол сруба), L и R — дальние нижние углы стен:
        // от F контур идёт ровно вверх-влево и вверх-вправо, у угла стены — скачок
        // вверх (дальше нависает свес крыши). Центр основания параллелограмма —
        // (L + R) / 2: по нему ставится опора широкого дома, а не по нижнему пикселю.
        public struct BaseCorners
        {
            public bool Found;
            public Vector2 Front, Left, Right; // доли холста, левый нижний угол
            public Vector2 Center => (Left + Right) * 0.5f;
        }

        public static BaseCorners FindBase(byte[] png, int alphaThreshold)
        {
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!image.LoadImage(png, false))
                    return default;
                return FindBase(image.GetPixels32(), image.width, image.height, alphaThreshold);
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        public static BaseCorners FindBase(Color32[] pixels, int width, int height, int alphaThreshold)
        {
            // Нижний контур: по столбцу — нижний непрозрачный пиксель (подряд
            // несколько, чтобы пылинки не считались).
            int[] bottom = new int[width];
            int minX = width, maxX = -1;
            for (int x = 0; x < width; x++)
            {
                bottom[x] = -1;
                int run = 0;
                for (int y = 0; y < height; y++)
                {
                    if (pixels[y * width + x].a >= alphaThreshold)
                    {
                        if (++run >= 3)
                        {
                            bottom[x] = y - 2;
                            break;
                        }
                    }
                    else
                        run = 0;
                }
                if (bottom[x] >= 0)
                {
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                }
            }
            BaseCorners result = default;
            if (maxX - minX < 16)
                return result;
            int front = minX;
            for (int x = minX; x <= maxX; x++)
                if (bottom[x] >= 0 && (bottom[front] < 0 || bottom[x] < bottom[front]))
                    front = x;
            // Плоское дно (несколько столбцов на одной высоте) — середина участка.
            int f0 = front, f1 = front;
            while (f0 - 1 >= minX && bottom[f0 - 1] >= 0 && bottom[f0 - 1] <= bottom[front] + 1) f0--;
            while (f1 + 1 <= maxX && bottom[f1 + 1] >= 0 && bottom[f1 + 1] <= bottom[front] + 1) f1++;
            front = (f0 + f1) / 2;
            int jump = Mathf.Max(6, height / 60);
            int Walk(int dir)
            {
                int x = front;
                int last = front;
                while (true)
                {
                    int next = x + dir;
                    if (next < minX || next > maxX || bottom[next] < 0)
                        return last;
                    // Скачок вверх за 1–3 столбца — угол стены, дальше свес крыши.
                    int ahead = Mathf.Clamp(next + dir * 2, minX, maxX);
                    if (bottom[next] - bottom[x] > jump || (bottom[ahead] >= 0 && bottom[ahead] - bottom[x] > jump * 2))
                        return last;
                    // Контур пошёл вниз заметно — это уже не стена основания.
                    if (bottom[next] < bottom[x] - jump)
                        return last;
                    x = next;
                    last = x;
                }
            }
            int left = Walk(-1), right = Walk(1);
            if (front - left < 8 || right - front < 8)
                return result;
            Vector2 P(int x) => new Vector2((x + 0.5f) / width, (bottom[x] + 0.5f) / height);
            result.Front = P(front);
            result.Left = P(left);
            result.Right = P(right);
            result.Found = true;
            return result;
        }

        public static Result Analyze(Color32[] pixels, int width, int height, int alphaThreshold)
        {
            // Строки и столбцы, где силуэт занимает хотя бы несколько пикселей —
            // одиночные «пылинки» не расширяют рамку.
            int[] rowCount = new int[height];
            int[] colCount = new int[width];
            double r = 0, g = 0, b = 0, weight = 0;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 c = pixels[row + x];
                    if (c.a >= alphaThreshold)
                    {
                        rowCount[y]++;
                        colCount[x]++;
                    }
                    if (c.a > 0)
                    {
                        double w = c.a / 255.0;
                        r += c.r * w; g += c.g * w; b += c.b * w;
                        weight += w;
                    }
                }
            }
            int minRun = Mathf.Max(2, Mathf.Min(width, height) / 300);
            int y0 = 0, y1 = height - 1, x0 = 0, x1 = width - 1;
            while (y0 < height && rowCount[y0] < minRun) y0++;
            while (y1 > y0 && rowCount[y1] < minRun) y1--;
            while (x0 < width && colCount[x0] < minRun) x0++;
            while (x1 > x0 && colCount[x1] < minRun) x1--;
            Result result = new Result { Width = width, Height = height };
            if (y0 >= height || x0 >= width)
            {
                result.Solid = new Rect(0, 0, 1, 1);
                result.FootX = 0.5f;
            }
            else
            {
                result.Solid = new Rect(x0 / (float)width, y0 / (float)height, (x1 - x0 + 1) / (float)width, (y1 - y0 + 1) / (float)height);
                // Нижние 5 % силуэта: средний X непрозрачных пикселей.
                int band = Mathf.Max(2, (y1 - y0) / 20);
                double sum = 0;
                int count = 0;
                for (int y = y0; y <= Mathf.Min(y1, y0 + band); y++)
                {
                    int row = y * width;
                    for (int x = x0; x <= x1; x++)
                    {
                        if (pixels[row + x].a >= alphaThreshold)
                        {
                            sum += x;
                            count++;
                        }
                    }
                }
                result.FootX = count > 0 ? (float)(sum / count + 0.5) / width : result.Solid.center.x;
            }
            result.Mean = weight > 0 ? new Color((float)(r / weight / 255.0), (float)(g / weight / 255.0), (float)(b / weight / 255.0), 1f) : Color.gray;
            return result;
        }
    }
}
