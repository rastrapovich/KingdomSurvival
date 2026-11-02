using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Участки полей: ячейки Вороного вокруг точек-участков, внутри — полосы-наделы
    // поперёк своей оси борозд. Одна и та же функция работает и на сетке масок,
    // и на пикселях покраски, поэтому границы совпадают.
    public sealed class MapLabFields
    {
        public struct Sample
        {
            public int Block;
            public int Strip;
            public float Along;        // ед. вдоль борозд
            public float Across;       // ед. поперёк, внутри полосы 0..StripWidth
            public float StripWidth;
            public float BorderDistance; // до границы участка, ед.
        }

        private const float CellSize = 40f;
        private readonly List<MapLabFieldBlock> blocks;
        private readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        private readonly float stripWidth;
        private readonly uint seed;

        public MapLabFields(List<MapLabFieldBlock> blocks, float stripWidth, int seed)
        {
            this.blocks = blocks;
            this.stripWidth = Mathf.Max(2f, stripWidth);
            this.seed = (uint)seed * 2654435761u;
            for (int i = 0; i < blocks.Count; i++)
            {
                long key = Key(Mathf.FloorToInt(blocks[i].Site.x / CellSize), Mathf.FloorToInt(blocks[i].Site.y / CellSize));
                if (!grid.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }
                list.Add(i);
            }
        }

        public int Count => blocks.Count;

        public bool Evaluate(Vector2 p, out Sample sample)
        {
            sample = default;
            if (blocks.Count == 0)
                return false;
            int cx = Mathf.FloorToInt(p.x / CellSize);
            int cz = Mathf.FloorToInt(p.y / CellSize);
            int best = -1;
            float bestD = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!grid.TryGetValue(Key(cx + dx, cz + dz), out List<int> list))
                    continue;
                for (int k = 0; k < list.Count; k++)
                {
                    float d = (blocks[list[k]].Site - p).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = list[k];
                    }
                }
            }
            if (best < 0)
                return false;
            Vector2 site = blocks[best].Site;
            float border = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!grid.TryGetValue(Key(cx + dx, cz + dz), out List<int> list))
                    continue;
                for (int k = 0; k < list.Count; k++)
                {
                    if (list[k] == best)
                        continue;
                    Vector2 other = blocks[list[k]].Site;
                    float ab = Vector2.Distance(site, other);
                    if (ab < 1e-4f)
                        continue;
                    // Расстояние до серединного перпендикуляра двух участков.
                    float d = ((other - p).sqrMagnitude - (site - p).sqrMagnitude) / (2f * ab);
                    border = Mathf.Min(border, d);
                }
            }
            MapLabFieldBlock block = blocks[best];
            float width = stripWidth * (0.7f + 0.7f * MapLabRandom.Hash01(block.ToneSeed, 3, seed));
            Vector2 axis = new Vector2(Mathf.Cos(block.Angle), Mathf.Sin(block.Angle));
            Vector2 perp = new Vector2(-axis.y, axis.x);
            Vector2 rel = p - site;
            float across = Vector2.Dot(rel, perp) / width + MapLabRandom.Hash01(block.ToneSeed, 5, seed);
            int strip = Mathf.FloorToInt(across);
            sample.Block = best;
            sample.Strip = strip;
            sample.Along = Vector2.Dot(rel, axis);
            sample.Across = (across - strip) * width;
            sample.StripWidth = width;
            sample.BorderDistance = border;
            return true;
        }

        public MapLabFieldBlock Block(int index)
        {
            return blocks[index];
        }

        // Тон полосы: стабильный хеш участка и номера полосы.
        public float StripTone(int block, int strip)
        {
            return MapLabRandom.Hash01(blocks[block].ToneSeed, strip, seed ^ 0x51ED27u);
        }

        private static long Key(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }
    }
}
