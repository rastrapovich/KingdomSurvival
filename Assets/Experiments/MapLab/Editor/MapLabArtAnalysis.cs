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
