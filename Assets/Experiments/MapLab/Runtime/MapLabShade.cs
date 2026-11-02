using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Запечённые падающие тени и затенение. Свет по нормалям остаётся в шейдере
    // (меняется ползунком без пересчёта), а здесь — то, что зависит от формы:
    // тень уступа на долину, тень лесной массы на луг, затенение впадин и опушек.
    public static class MapLabShade
    {
        public const int DefaultResolution = 512;

        public static void Compute(MapLabData data, MapLabSettings settings)
        {
            int r = DefaultResolution;
            data.ShadeResolution = r;
            float cell = data.Size / (r - 1);
            float[] ground = new float[r * r];
            float[] occluder = new float[r * r];
            float[] canopy = new float[r * r];
            for (int z = 0; z < r; z++)
            for (int x = 0; x < r; x++)
            {
                float h = data.SampleHeight(x * cell, z * cell);
                ground[z * r + x] = h;
                occluder[z * r + x] = h;
            }
            // Этап 03: тени крон и домов рисует MapLabObjectShadows (собранные
            // пятна), здесь — только рельеф: две тени от одного объекта не складываются.
            if (!(settings.IsStage03 && settings.Stage03.ObjectShadows))
                RasterizeOccluders(data, occluder, canopy, r, cell);

            Vector3 light = settings.LightDirection;
            Vector2 dir = new Vector2(light.x, light.z);
            float horizontal = dir.magnitude;
            dir = horizontal > 1e-4f ? dir / horizontal : Vector2.up;
            float rise = light.y / Mathf.Max(0.05f, horizontal);
            float step = Mathf.Max(cell, 0.6f);
            int steps = Mathf.CeilToInt(36f / step);
            data.ShadeLight = new float[r * r];
            float[] lit = data.ShadeLight;
            System.Threading.Tasks.Parallel.For(0, r, z =>
            {
                for (int x = 0; x < r; x++)
                {
                    int i = z * r + x;
                    float h0 = ground[i] + 0.12f;
                    // Над водой приёмник — поверхность воды, а не дно русла.
                    float level = data.SampleWaterLevel(x * cell, z * cell);
                    if (!float.IsNaN(level) && level > h0)
                        h0 = level + 0.05f;
                    float maxRatio = -1f;
                    float px = x * cell, pz = z * cell;
                    for (int k = 1; k <= steps; k++)
                    {
                        float dist = k * step;
                        float qx = px + dir.x * dist;
                        float qz = pz + dir.y * dist;
                        if (qx < 0f || qz < 0f || qx > data.Size || qz > data.Size)
                            break;
                        float o = Sample(occluder, r, cell, qx, qz);
                        float ratio = (o - h0) / dist - rise;
                        if (ratio > maxRatio)
                            maxRatio = ratio;
                    }
                    // Мягкий полутон: насколько луч «заглублён» под препятствие.
                    lit[i] = 1f - MapLabGeometry.SmoothStep(-0.04f, 0.18f, maxRatio);
                }
            });

            // Затенение: вогнутость относительно размытой высоты + близость крон.
            float[] blurred = Blur(ground, r, Mathf.Max(2, Mathf.RoundToInt(5f / cell)));
            float[] canopyNear = Blur(canopy, r, Mathf.Max(1, Mathf.RoundToInt(2.2f / cell)));
            data.ShadeOcclusion = new float[r * r];
            for (int i = 0; i < r * r; i++)
            {
                float concave = Mathf.Max(0f, blurred[i] - ground[i]);
                float ao = 1f - Mathf.Clamp01(concave * 0.12f) - Mathf.Clamp01(canopyNear[i] * 1.2f) * 0.5f * (1f - canopy[i] * 0.4f);
                data.ShadeOcclusion[i] = Mathf.Clamp(ao, 0.25f, 1f);
            }
        }

        // Кроны и дома как препятствия для падающей тени.
        private static void RasterizeOccluders(MapLabData data, float[] occluder, float[] canopy, int r, float cell)
        {
            for (int k = 0; k < data.Objects.Count; k++)
            {
                MapLabPlacement o = data.Objects[k];
                float radius, height;
                switch (o.Kind)
                {
                    case MapLabObjectKind.Deciduous: radius = o.Scale * 0.42f; height = o.Scale * 0.9f; break;
                    case MapLabObjectKind.DeciduousGroup: radius = o.Scale * 0.45f; height = o.Scale * 0.6f; break;
                    case MapLabObjectKind.Conifer: radius = o.Scale * 0.3f; height = o.Scale * 1.15f; break;
                    case MapLabObjectKind.Bush: radius = o.Scale * 0.4f; height = o.Scale * 0.55f; break;
                    case MapLabObjectKind.House: radius = Mathf.Max(o.Size.x, o.Size.y) * 0.5f; height = 3.2f; break;
                    case MapLabObjectKind.Barn: radius = Mathf.Max(o.Size.x, o.Size.y) * 0.5f; height = 2.4f; break;
                    default: continue;
                }
                float top = o.Position.y + height;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((o.Position.x - radius) / cell));
                int x1 = Mathf.Min(r - 1, Mathf.CeilToInt((o.Position.x + radius) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((o.Position.z - radius) / cell));
                int z1 = Mathf.Min(r - 1, Mathf.CeilToInt((o.Position.z + radius) / cell));
                bool tree = o.Kind != MapLabObjectKind.House && o.Kind != MapLabObjectKind.Barn;
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - o.Position.x;
                    float dz = z * cell - o.Position.z;
                    float q = (dx * dx + dz * dz) / (radius * radius);
                    if (q > 1f)
                        continue;
                    int i = z * r + x;
                    float shape = tree ? Mathf.Sqrt(1f - q) : 1f;
                    occluder[i] = Mathf.Max(occluder[i], o.Position.y + (top - o.Position.y) * (0.45f + 0.55f * shape));
                    if (tree)
                        canopy[i] = Mathf.Max(canopy[i], 1f);
                }
            }
        }

        public static float Sample(float[] values, int r, float cell, float x, float z)
        {
            float gx = Mathf.Clamp(x / cell, 0f, r - 1.001f);
            float gz = Mathf.Clamp(z / cell, 0f, r - 1.001f);
            int x0 = (int)gx;
            int z0 = (int)gz;
            float fx = gx - x0;
            float fz = gz - z0;
            int i = z0 * r + x0;
            float a = values[i];
            float b = values[i + 1];
            float c = values[i + r];
            float d = values[i + r + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        // Раздельное box-размытие (два прохода по осям).
        public static float[] Blur(float[] source, int r, int radius)
        {
            float[] temp = new float[source.Length];
            float[] result = new float[source.Length];
            for (int z = 0; z < r; z++)
            {
                float sum = 0f;
                for (int x = -radius; x <= radius; x++)
                    sum += source[z * r + Mathf.Clamp(x, 0, r - 1)];
                for (int x = 0; x < r; x++)
                {
                    temp[z * r + x] = sum / (radius * 2 + 1);
                    sum += source[z * r + Mathf.Min(r - 1, x + radius + 1)] - source[z * r + Mathf.Max(0, x - radius)];
                }
            }
            for (int x = 0; x < r; x++)
            {
                float sum = 0f;
                for (int z = -radius; z <= radius; z++)
                    sum += temp[Mathf.Clamp(z, 0, r - 1) * r + x];
                for (int z = 0; z < r; z++)
                {
                    result[z * r + x] = sum / (radius * 2 + 1);
                    sum += temp[Mathf.Min(r - 1, z + radius + 1) * r + x] - temp[Mathf.Max(0, z - radius) * r + x];
                }
            }
            return result;
        }
    }
}
