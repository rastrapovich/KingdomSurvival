using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Локальный детерминированный генератор: SplitMix64. Не трогает
    // глобальный UnityEngine.Random и не зависит от string.GetHashCode —
    // поток задаётся seed + стабильным FNV-1a-хешем имени потока + версией
    // алгоритма, поэтому изменение числа кустов не сдвигает рельеф.
    public struct MapLabRandom
    {
        private ulong state;

        public MapLabRandom(ulong seed)
        {
            state = seed ^ 0x9E3779B97F4A7C15UL;
            NextULong();
        }

        public static MapLabRandom ForStream(int seed, string stream, int algorithmVersion)
        {
            return new MapLabRandom(StreamSeed(seed, stream, algorithmVersion));
        }

        public static ulong StreamSeed(int seed, string stream, int algorithmVersion)
        {
            ulong hash = Fnv1a(stream);
            hash ^= (ulong)(uint)seed * 0xBF58476D1CE4E5B9UL;
            hash ^= (ulong)(uint)algorithmVersion * 0x94D049BB133111EBUL;
            return Mix(hash);
        }

        public static ulong Fnv1a(string text)
        {
            ulong hash = 14695981039346656037UL;
            if (text == null)
                return hash;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                hash ^= (byte)(c & 0xFF);
                hash *= 1099511628211UL;
                hash ^= (byte)(c >> 8);
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // Хеш целых координат — для шума и «случайности по месту»
        // (одна и та же точка всегда даёт одно значение).
        public static uint Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = seed;
                h ^= (uint)x * 0x27D4EB2Du;
                h = (h ^ (h >> 15)) * 0x85EBCA6Bu;
                h ^= (uint)y * 0x165667B1u;
                h = (h ^ (h >> 13)) * 0xC2B2AE35u;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int x, int y, uint seed)
        {
            return (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;
        }

        public ulong NextULong()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                return Mix(state);
            }
        }

        public uint NextUInt()
        {
            return (uint)(NextULong() >> 32);
        }

        public float Value()
        {
            return (NextULong() >> 40) / 16777216f;
        }

        public float Range(float min, float max)
        {
            return min + (max - min) * Value();
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(float probability)
        {
            return Value() < probability;
        }

        public float Signed()
        {
            return Value() * 2f - 1f;
        }

        public Vector2 InsideUnitCircle()
        {
            float angle = Value() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Value());
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }

    // Градиентный шум по хешу координат (без таблицы перестановок),
    // fbm и искажение области. Все значения — около [-1, 1].
    public sealed class MapLabNoise
    {
        private const int GradientCount = 256;
        private static readonly float[] GradX = new float[GradientCount];
        private static readonly float[] GradY = new float[GradientCount];
        private readonly uint seed;

        static MapLabNoise()
        {
            for (int i = 0; i < GradientCount; i++)
            {
                float angle = i / (float)GradientCount * Mathf.PI * 2f;
                GradX[i] = Mathf.Cos(angle);
                GradY[i] = Mathf.Sin(angle);
            }
        }

        public MapLabNoise(ulong streamSeed)
        {
            seed = (uint)(streamSeed ^ (streamSeed >> 32));
        }

        public float Gradient(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float a = Dot(x0, y0, fx, fy);
            float b = Dot(x0 + 1, y0, fx - 1f, fy);
            float c = Dot(x0, y0 + 1, fx, fy - 1f);
            float d = Dot(x0 + 1, y0 + 1, fx - 1f, fy - 1f);
            float ab = a + (b - a) * u;
            float cd = c + (d - c) * u;
            return (ab + (cd - ab) * v) * 1.414f;
        }

        public float Fbm(float x, float y, int octaves, float persistence = 0.5f, float lacunarity = 2f)
        {
            float sum = 0f;
            float amplitude = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Gradient(x + i * 17.31f, y - i * 9.17f) * amplitude;
                norm += amplitude;
                amplitude *= persistence;
                x *= lacunarity;
                y *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        // fbm со сдвигом координат другим fbm — органичные лопасти опушек и берегов.
        public float Warped(float x, float y, int octaves, float warp)
        {
            float wx = Fbm(x + 5.2f, y + 1.3f, 2);
            float wy = Fbm(x - 3.7f, y + 8.1f, 2);
            return Fbm(x + wx * warp, y + wy * warp, octaves);
        }

        private float Dot(int ix, int iy, float dx, float dy)
        {
            int g = (int)(MapLabRandom.Hash(ix, iy, seed) & (GradientCount - 1));
            return GradX[g] * dx + GradY[g] * dy;
        }
    }

    // Тайлящийся шум для бесшовных текстур фактуры (период в ячейках решётки).
    public static class MapLabTileNoise
    {
        public static float Value(float x, float y, int period, uint seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            float u = fx * fx * (3f - 2f * fx);
            float v = fy * fy * (3f - 2f * fy);
            float a = Corner(x0, y0, period, seed);
            float b = Corner(x0 + 1, y0, period, seed);
            float c = Corner(x0, y0 + 1, period, seed);
            float d = Corner(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        public static float Fbm(float x, float y, int period, int octaves, uint seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x, y, period, seed + (uint)i * 7919u) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x *= 2f;
                y *= 2f;
                period *= 2;
            }
            return sum / norm;
        }

        private static float Corner(int x, int y, int period, uint seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            return MapLabRandom.Hash01(x, y, seed);
        }
    }
}
