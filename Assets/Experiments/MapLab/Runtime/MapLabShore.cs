using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Этап 05: одна общая сглаженная граница берега. «Высота над водой»
    // above = h − level считается на мелкой сетке по непрерывному уровню:
    // уровень воды продолжен на ближнюю сушу (расширение по соседям), поэтому
    // above не обрывается на краю клеток, где уровень определён (источник
    // «гармошки» этапа 04: береговая полоса кончалась ступеньками по сетке).
    // Несколько проходов мягкого сглаживания только у нуля: узкие протоки не
    // закрываются, острова не срезаются. Из этого поля строятся береговая
    // полоса покраски, перо берега и край воды в шейдере.
    public sealed class MapLabShore
    {
        public const float Step = 0.25f;
        public const float Far = 100f;

        public readonly int N;
        public readonly float[] Above;
        public float Milliseconds;

        public MapLabShore(MapLabData data, int smoothing)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            float[] level = FillLevels(data, 10);
            N = Mathf.CeilToInt(data.Size / Step) + 1;
            Above = new float[N * N];
            for (int z = 0; z < N; z++)
            {
                for (int x = 0; x < N; x++)
                {
                    float wx = Mathf.Min(x * Step, data.Size), wz = Mathf.Min(z * Step, data.Size);
                    float l = SampleLevel(data, level, wx, wz);
                    float above = float.IsNaN(l) ? Far : Mathf.Min(Far, data.SampleHeight(wx, wz) - l);
                    // Продолженный уровень не создаёт воду в сухих ложбинах вдали от неё.
                    if (data.WaterDistance != null)
                        above = Mathf.Max(above, (data.Bilinear(data.WaterDistance, wx, wz) - 1.6f) * 0.6f);
                    Above[z * N + x] = above;
                }
            }
            // Сглаживание только в полосе у кромки (|above| < 1.2): форма берега
            // становится плавной, дальние значения не трогаются.
            float[] tmp = new float[Above.Length];
            float[] orig = (float[])Above.Clone();
            for (int pass = 0; pass < Mathf.Clamp(smoothing, 0, 8); pass++)
            {
                System.Array.Copy(Above, tmp, Above.Length);
                for (int z = 1; z < N - 1; z++)
                {
                    for (int x = 1; x < N - 1; x++)
                    {
                        int i = z * N + x;
                        float v = tmp[i];
                        if (v > 1.2f || v < -1.2f)
                            continue;
                        float sum = v * 4f + (tmp[i - 1] + tmp[i + 1] + tmp[i - N] + tmp[i + N]) * 2f
                                    + tmp[i - N - 1] + tmp[i - N + 1] + tmp[i + N - 1] + tmp[i + N + 1];
                        float smooth = sum / 16f;
                        // Не дальше 0.35 от исходного значения: протоки и острова целы.
                        Above[i] = Mathf.Clamp(smooth, orig[i] - 0.35f, orig[i] + 0.35f);
                    }
                }
            }
            Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
        }

        public float Sample(float x, float z)
        {
            float gx = Mathf.Clamp(x / Step, 0f, N - 1.001f), gz = Mathf.Clamp(z / Step, 0f, N - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            int i = z0 * N + x0;
            return Mathf.Lerp(Mathf.Lerp(Above[i], Above[i + 1], fx), Mathf.Lerp(Above[i + N], Above[i + N + 1], fx), fz);
        }

        public float Sample(Vector2 p) => Sample(p.x, p.y);

        // Направление к суше (рост above), нормированное.
        public Vector2 Gradient(Vector2 p)
        {
            float e = Step;
            Vector2 g = new Vector2(Sample(p.x + e, p.y) - Sample(p.x - e, p.y), Sample(p.x, p.y + e) - Sample(p.x, p.y - e));
            return g.sqrMagnitude > 1e-10f ? g.normalized : Vector2.zero;
        }

        // Уровень воды, продолженный на ближнюю сушу: NaN-клетки получают среднее
        // определённых соседей, проход за проходом (≈ iterations клеток от воды).
        public static float[] FillLevels(MapLabData data, int iterations)
        {
            int n = data.Resolution;
            float[] level = (float[])data.WaterLevel.Clone();
            float[] next = new float[level.Length];
            for (int it = 0; it < iterations; it++)
            {
                System.Array.Copy(level, next, level.Length);
                bool changed = false;
                for (int z = 0; z < n; z++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        int i = z * n + x;
                        if (!float.IsNaN(level[i]))
                            continue;
                        float sum = 0f;
                        int count = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, zz = z + dz;
                            if ((dx == 0 && dz == 0) || xx < 0 || zz < 0 || xx >= n || zz >= n)
                                continue;
                            float v = level[zz * n + xx];
                            if (!float.IsNaN(v))
                            {
                                sum += v;
                                count++;
                            }
                        }
                        if (count > 0)
                        {
                            next[i] = sum / count;
                            changed = true;
                        }
                    }
                }
                float[] swap = level;
                level = next;
                next = swap;
                if (!changed)
                    break;
            }
            return level;
        }

        private static float SampleLevel(MapLabData data, float[] level, float x, float z)
        {
            data.GridCoords(x, z, out int x0, out int z0, out float fx, out float fz);
            float a = level[data.Index(x0, z0)], b = level[data.Index(x0 + 1, z0)];
            float c = level[data.Index(x0, z0 + 1)], d = level[data.Index(x0 + 1, z0 + 1)];
            float sum = 0f, weight = 0f;
            void Add(float v, float w)
            {
                if (!float.IsNaN(v) && w > 0f)
                {
                    sum += v * w;
                    weight += w;
                }
            }
            Add(a, (1f - fx) * (1f - fz));
            Add(b, fx * (1f - fz));
            Add(c, (1f - fx) * fz);
            Add(d, fx * fz);
            // На краю продолженной зоны вес < 1 — дальше 10 клеток от воды берега нет.
            return weight > 0.999f ? sum / weight : (weight > 1e-4f ? sum / weight : float.NaN);
        }

        // Проверка: нет NaN/бесконечностей.
        public bool HasInvalid()
        {
            for (int i = 0; i < Above.Length; i++)
                if (float.IsNaN(Above[i]) || float.IsInfinity(Above[i]))
                    return true;
            return false;
        }
    }
}
