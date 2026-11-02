using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    public enum MapLabRoadKind
    {
        Road = 0,
        Path = 1,
    }

    public enum MapLabWaterKind : byte
    {
        Dry = 0,
        Lake = 1,
        River = 2,
    }

    public enum MapLabCliffModule
    {
        Start = 0,
        End = 1,
        ShortWall = 2,
        LongWall = 3,
        OuterCorner = 4,
        InnerCorner = 5,
        GrassTransition = 6,
    }

    public enum MapLabObjectKind
    {
        Deciduous = 0,
        DeciduousGroup = 1,
        Conifer = 2,
        Bush = 3,
        Reed = 4,
        Boulder = 5,
        House = 6,
        Barn = 7,
        Haystack = 8,
    }

    // Ломаная в плоскости XZ (x — вправо по кадру, y — вглубь кадра = мировая Z).
    public sealed class MapLabPolyline
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public float Width;
        public MapLabRoadKind Kind;

        public float Length
        {
            get
            {
                float length = 0f;
                for (int i = 1; i < Points.Count; i++)
                    length += Vector2.Distance(Points[i - 1], Points[i]);
                return length;
            }
        }
    }

    public sealed class MapLabRiver
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> Widths = new List<float>();
        // Уровень воды в каждой точке — монотонно не растёт вниз по течению.
        public readonly List<float> Levels = new List<float>();
    }

    public sealed class MapLabLake
    {
        public Vector2 Center;
        public float Level;
        public readonly List<Vector4> Lobes = new List<Vector4>(); // x, z, радиус X, радиус Z
    }

    public sealed class MapLabCliffLine
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> Taper = new List<float>();
        public float Height;
        // Сторона верхней площадки: +1 — слева по ходу линии.
        public float UpperSide = 1f;
        public readonly List<MapLabCliffColumn> Columns = new List<MapLabCliffColumn>();
    }

    public struct MapLabCliffColumn
    {
        public Vector2 Start;
        public Vector2 End;
        public Vector2 Outward; // к нижней стороне
        public float Bottom;
        public float Top;
        public MapLabCliffModule Module;
    }

    public struct MapLabBridge
    {
        public Vector2 EndA;
        public Vector2 EndB;
        public float DeckHeight;
        public float Width;
        public float WaterLevel;
    }

    public struct MapLabPier
    {
        public Vector2 Shore;
        public Vector2 Tip;
        public float DeckHeight;
        public float Width;
    }

    public struct MapLabPlacement
    {
        public MapLabObjectKind Kind;
        public Vector3 Position;
        public float Yaw;      // градусы, только для 3D-объектов
        public float Scale;
        public int Variant;
        public float Tone;     // 0..1, смесь двух тонов палитры
        public float Brightness;
        public Vector2 Size;   // ширина × глубина для построек
    }

    public struct MapLabFence
    {
        public Vector2 A;
        public Vector2 B;
    }

    public sealed class MapLabFieldBlock
    {
        public Vector2 Site;
        public float Angle;      // направление борозд, радианы
        public bool Cultivated;  // false — луг/выгон
        public int ToneSeed;
    }

    public struct MapLabStats
    {
        public float GenerateMs;
        public float TerrainMs;
        public float PaintMs;
        public float ObjectsMs;
        public int Vertices;
        public int Triangles;
        public int Renderers;
        public int Materials;
    }

    // Результат генерации. Координаты карты — XZ, высота — Y; массивы сетки —
    // [z * Resolution + x], отсчёт (0,0) — угол карты (0, 0).
    public sealed class MapLabData
    {
        public const int AlgorithmVersion = 1;

        public int Seed;
        public MapLabPresetKind Preset;
        public int Resolution;
        public float Size;
        public float Cell;
        public float MinHeight;
        public float MaxHeight;
        public float CliffSharpness = 1.6f;

        public float[] Heights;
        public Vector3[] Normals;
        public float[] SlopeDegrees;
        public Vector2[] Downhill;     // направление вниз по склону (нормированное, 0 на ровном)
        public float[] WaterLevel;     // уровень воды над точкой или float.NaN
        public byte[] Water;           // MapLabWaterKind
        public float[] Shore;          // 1 у кромки, 0 вдали
        public float[] Forest;         // плотность леса 0..1
        public float[] Field;          // 1 — пашня/надел
        public float[] Road;           // 1 — полотно дороги
        public float[] Cliff;          // 1 — стенка уступа
        public float[] Rock;           // вес камня в покраске
        public float[] Settlement;     // 1 — площадка поселения
        // Свет и затенение — на своей сетке ShadeResolution² (точнее сетки высот).
        public int ShadeResolution;
        public float[] ShadeOcclusion;  // 1 — открыто, меньше — впадина/опушка
        public float[] ShadeLight;      // 1 — освещено, 0 — в падающей тени

        public float[] LakeField;      // > 0 — внутри котловины озера
        public float[] RoadDistance;   // расстояние от края полотна дороги, ед.
        public float[] CliffSigned;    // знаковое расстояние до линии уступа (+ — верх)
        public float[] WaterDistance;  // расстояние до воды, ед.

        public MapLabComposition Composition;
        public MapLabFields Fields;

        public readonly List<MapLabPolyline> Roads = new List<MapLabPolyline>();
        public readonly List<MapLabRiver> Rivers = new List<MapLabRiver>();
        public readonly List<MapLabLake> Lakes = new List<MapLabLake>();
        public readonly List<MapLabCliffLine> CliffLines = new List<MapLabCliffLine>();
        public readonly List<MapLabBridge> Bridges = new List<MapLabBridge>();
        public readonly List<MapLabPier> Piers = new List<MapLabPier>();
        public readonly List<Vector3> SettlementSites = new List<Vector3>(); // x, z, радиус
        public readonly List<MapLabFieldBlock> FieldBlocks = new List<MapLabFieldBlock>();
        public readonly List<MapLabPlacement> Objects = new List<MapLabPlacement>();
        public readonly List<MapLabFence> Fences = new List<MapLabFence>();
        public readonly List<Vector4> Yards = new List<Vector4>(); // центр x,z; полуразмер; угол

        public MapLabStats Stats;

        public MapLabData(int resolution, float size)
        {
            Resolution = Mathf.Max(3, resolution);
            Size = Mathf.Max(1f, size);
            Cell = Size / (Resolution - 1);
            int count = Resolution * Resolution;
            Heights = new float[count];
            Normals = new Vector3[count];
            SlopeDegrees = new float[count];
            Downhill = new Vector2[count];
            WaterLevel = new float[count];
            Water = new byte[count];
            Shore = new float[count];
            Forest = new float[count];
            Field = new float[count];
            Road = new float[count];
            Cliff = new float[count];
            Rock = new float[count];
            Settlement = new float[count];
            for (int i = 0; i < count; i++)
            {
                WaterLevel[i] = float.NaN;
            }
        }

        public int Index(int x, int z)
        {
            return z * Resolution + x;
        }

        public bool Contains(float x, float z)
        {
            return x >= 0f && z >= 0f && x <= Size && z <= Size;
        }

        public Vector2 GridToWorld(int x, int z)
        {
            return new Vector2(x * Cell, z * Cell);
        }

        public float SampleHeight(float x, float z)
        {
            return Bilinear(Heights, x, z);
        }

        public Vector3 SampleNormal(float x, float z)
        {
            GridCoords(x, z, out int x0, out int z0, out float fx, out float fz);
            Vector3 a = Normals[Index(x0, z0)];
            Vector3 b = Normals[Index(x0 + 1, z0)];
            Vector3 c = Normals[Index(x0, z0 + 1)];
            Vector3 d = Normals[Index(x0 + 1, z0 + 1)];
            Vector3 n = Vector3.Lerp(Vector3.Lerp(a, b, fx), Vector3.Lerp(c, d, fx), fz);
            return n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up;
        }

        public float SampleSlope(float x, float z)
        {
            return Bilinear(SlopeDegrees, x, z);
        }

        public float SampleMask(float[] mask, float x, float z)
        {
            return Bilinear(mask, x, z);
        }

        // Уровень воды в точке: ближайший определённый отсчёт (NaN — суша).
        public float SampleWaterLevel(float x, float z)
        {
            GridCoords(x, z, out int x0, out int z0, out float fx, out float fz);
            float sum = 0f;
            float weight = 0f;
            Accumulate(Index(x0, z0), (1f - fx) * (1f - fz), ref sum, ref weight);
            Accumulate(Index(x0 + 1, z0), fx * (1f - fz), ref sum, ref weight);
            Accumulate(Index(x0, z0 + 1), (1f - fx) * fz, ref sum, ref weight);
            Accumulate(Index(x0 + 1, z0 + 1), fx * fz, ref sum, ref weight);
            return weight > 1e-5f ? sum / weight : float.NaN;
        }

        // Точка под водой: определён уровень и он выше земли.
        public bool IsUnderWater(float x, float z, float margin = 0f)
        {
            float level = SampleWaterLevel(x, z);
            return !float.IsNaN(level) && SampleHeight(x, z) < level + margin;
        }

        public float Bilinear(float[] values, float x, float z)
        {
            GridCoords(x, z, out int x0, out int z0, out float fx, out float fz);
            float a = values[Index(x0, z0)];
            float b = values[Index(x0 + 1, z0)];
            float c = values[Index(x0, z0 + 1)];
            float d = values[Index(x0 + 1, z0 + 1)];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        public void GridCoords(float x, float z, out int x0, out int z0, out float fx, out float fz)
        {
            float gx = Mathf.Clamp(x / Cell, 0f, Resolution - 1.0001f);
            float gz = Mathf.Clamp(z / Cell, 0f, Resolution - 1.0001f);
            if (float.IsNaN(gx)) gx = 0f;
            if (float.IsNaN(gz)) gz = 0f;
            x0 = (int)gx;
            z0 = (int)gz;
            fx = gx - x0;
            fz = gz - z0;
        }

        private void Accumulate(int index, float w, ref float sum, ref float weight)
        {
            float level = WaterLevel[index];
            if (float.IsNaN(level) || w <= 0f)
                return;
            sum += level * w;
            weight += w;
        }

        public int CountObjects(MapLabObjectKind kind)
        {
            int count = 0;
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].Kind == kind)
                    count++;
            return count;
        }
    }
}
