using System;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    public enum WorldMapStrokeEnd
    {
        Pointed,
        Blunt,
        Blob
    }

    // Как выглядит один вид линий туши (берег, кромка леса, обводка дорог…).
    // Окно «Редактор туши». Рабочие настройки облика, не канон.
    [Serializable]
    public sealed class WorldMapStrokeStyle
    {
        public bool Enabled = true;
        [Range(0f, 1f)] public float Opacity = 0.9f;
        [Tooltip("Свой цвет вместо общего цвета туши.")]
        public bool OwnColor;
        public Color Color = new Color(0.07f, 0.06f, 0.05f, 1f);
        [Tooltip("Толщина — множитель общей толщины туши.")]
        [Range(0f, 4f)] public float Width = 1f;

        [Header("Нажим")]
        [Tooltip("Разнотолщинность: 0 — ровная линия, 1 — от самого тонкого до самого толстого.")]
        [Range(0f, 1f)] public float Pressure = 0.6f;
        [Range(0.05f, 1f)] public float Thinnest = 0.25f;
        [Range(1f, 3f)] public float Thickest = 1.75f;
        [Tooltip("Длина волны нажима вдоль линии, в клетках.")]
        [Range(0.1f, 8f)] public float PressureWaveHexes = 0.9f;
        [Tooltip("Мелкая дрожь нажима поверх длинной волны.")]
        [Range(0f, 1f)] public float PressureJitter = 0.3f;

        [Header("Перо")]
        [Tooltip("Широкое перо: поперёк пера линия толще, вдоль — тоньше.")]
        [Range(0f, 1f)] public float Nib = 0.5f;
        [Range(0f, 180f)] public float NibAngle = 53f;

        [Header("Концы")]
        public WorldMapStrokeEnd Ends = WorldMapStrokeEnd.Pointed;
        [Tooltip("Длина сужения или капли на конце, в толщинах линии.")]
        [Range(0f, 8f)] public float TaperLength = 3f;

        [Header("Обрывы")]
        [Tooltip("Доля линии, где кисть отрывается от бумаги.")]
        [Range(0f, 0.8f)] public float Gaps = 0.12f;
        [Tooltip("Размер обрывов и кусков между ними, в клетках.")]
        [Range(0.2f, 10f)] public float GapScaleHexes = 2.2f;

        [Header("Штрихи внахлёст")]
        [Tooltip("Длина одного штриха, в клетках: 0 — сплошная линия.")]
        [Range(0f, 6f)] public float StrokeLengthHexes;
        [Tooltip("Разброс длины штрихов.")]
        [Range(0f, 1f)] public float StrokeLengthJitter = 0.4f;
        [Tooltip("Нахлёст соседних штрихов, доля длины.")]
        [Range(0f, 0.9f)] public float StrokeOverlap = 0.25f;
        [Tooltip("Сдвиг штрихов в сторону от линии, в толщинах.")]
        [Range(0f, 2f)] public float StrokeOffset = 0.3f;

        [Header("Рука")]
        [Tooltip("Дрожь руки — линия гуляет в стороны, в клетках. У берега и кромки леса гуляет и сам край заливки.")]
        [Range(0f, 0.4f)] public float WobbleHexes = 0.03f;
        [Tooltip("Длина волны дрожи, в клетках.")]
        [Range(0.1f, 8f)] public float WobbleWaveHexes = 1.2f;

        [Header("Сухая кисть")]
        [Tooltip("Волосков кисти: 1 — сплошной мазок, больше — параллельные волоски.")]
        [Range(1, 7)] public int Bristles = 1;
        [Tooltip("Ширина веера волосков, в толщинах.")]
        [Range(0f, 2f)] public float BristleSpread = 0.8f;
        [Tooltip("Сухость: волоски местами не оставляют следа.")]
        [Range(0f, 1f)] public float Dryness;

        public WorldMapStrokeStyle Clone() => (WorldMapStrokeStyle)MemberwiseClone();

        public void CopyFrom(WorldMapStrokeStyle other)
        {
            if (other == null)
                return;
            bool enabled = Enabled;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
            Enabled = enabled;
        }
    }

    public enum WorldMapInkLine
    {
        Shore,
        ShoreEcho,
        ForestEdge,
        RoadEdge,
        Trail,
        Bridge
    }

    // Все линии туши карты. Общие цвет и толщина — в WorldMapArtStyle
    // (InkColor, InkWidth), здесь — облик каждого вида линий.
    [Serializable]
    public sealed class WorldMapInkStyle
    {
        public const int CurrentVersion = 1;
        [Tooltip("0 — настройки ещё не перенесены из старых полей стиля.")]
        public int Version;

        [Tooltip("Там, где есть тушь (берег, кромка леса), заливка и узор обрываются ровно по линии туши.")]
        public bool FillsFollowInk = true;

        public WorldMapStrokeStyle Shore = DefaultShore();
        public WorldMapStrokeStyle ShoreEcho = DefaultShoreEcho();
        [Tooltip("Линий воды вдоль берега, как на старых картах.")]
        [Range(0, 5)] public int ShoreEchoCount;
        [Tooltip("Расстояние между линиями воды, в клетках.")]
        [Range(0.03f, 1f)] public float ShoreEchoSpacingHexes = 0.12f;
        public WorldMapStrokeStyle ForestEdge = DefaultForestEdge();
        public WorldMapStrokeStyle RoadEdge = DefaultRoadEdge();
        public WorldMapStrokeStyle Trail = DefaultTrail();
        public WorldMapStrokeStyle Bridge = DefaultBridge();

        public WorldMapStrokeStyle Get(WorldMapInkLine line)
        {
            switch (line)
            {
                case WorldMapInkLine.Shore: return Shore ??= DefaultShore();
                case WorldMapInkLine.ShoreEcho: return ShoreEcho ??= DefaultShoreEcho();
                case WorldMapInkLine.ForestEdge: return ForestEdge ??= DefaultForestEdge();
                case WorldMapInkLine.RoadEdge: return RoadEdge ??= DefaultRoadEdge();
                case WorldMapInkLine.Trail: return Trail ??= DefaultTrail();
                default: return Bridge ??= DefaultBridge();
            }
        }

        public void Set(WorldMapInkLine line, WorldMapStrokeStyle value)
        {
            switch (line)
            {
                case WorldMapInkLine.Shore: Shore = value; break;
                case WorldMapInkLine.ShoreEcho: ShoreEcho = value; break;
                case WorldMapInkLine.ForestEdge: ForestEdge = value; break;
                case WorldMapInkLine.RoadEdge: RoadEdge = value; break;
                case WorldMapInkLine.Trail: Trail = value; break;
                default: Bridge = value; break;
            }
        }

        public static string Name(WorldMapInkLine line)
        {
            switch (line)
            {
                case WorldMapInkLine.Shore: return "Берег";
                case WorldMapInkLine.ShoreEcho: return "Линии воды у берега";
                case WorldMapInkLine.ForestEdge: return "Кромка леса";
                case WorldMapInkLine.RoadEdge: return "Обводка дорог";
                case WorldMapInkLine.Trail: return "Тропы";
                default: return "Перила мостов";
            }
        }

        public static WorldMapStrokeStyle Default(WorldMapInkLine line)
        {
            switch (line)
            {
                case WorldMapInkLine.Shore: return DefaultShore();
                case WorldMapInkLine.ShoreEcho: return DefaultShoreEcho();
                case WorldMapInkLine.ForestEdge: return DefaultForestEdge();
                case WorldMapInkLine.RoadEdge: return DefaultRoadEdge();
                case WorldMapInkLine.Trail: return DefaultTrail();
                default: return DefaultBridge();
            }
        }

        public static WorldMapStrokeStyle DefaultShore() => new WorldMapStrokeStyle { Opacity = 0.92f, Width = 1.15f };

        public static WorldMapStrokeStyle DefaultShoreEcho() => new WorldMapStrokeStyle
        {
            Opacity = 0.55f, Width = 0.45f, Pressure = 0.7f, Gaps = 0.35f, GapScaleHexes = 1.2f, Nib = 0.3f, WobbleHexes = 0.02f
        };

        public static WorldMapStrokeStyle DefaultForestEdge() => new WorldMapStrokeStyle
        {
            Opacity = 0.55f, Width = 0.85f, Gaps = 0.18f, WobbleHexes = 0.04f
        };

        public static WorldMapStrokeStyle DefaultRoadEdge() => new WorldMapStrokeStyle
        {
            Opacity = 0.75f, Width = 1.2f, Pressure = 0.45f, Gaps = 0.1f, Nib = 0.35f, WobbleHexes = 0.02f
        };

        // У троп толщина — множитель ширины тропы, цвет — цвет тропы.
        public static WorldMapStrokeStyle DefaultTrail() => new WorldMapStrokeStyle
        {
            Opacity = 0.95f, Width = 1f, Pressure = 0.3f, Gaps = 0f, Nib = 0.25f, TaperLength = 2f, WobbleHexes = 0.02f
        };

        public static WorldMapStrokeStyle DefaultBridge() => new WorldMapStrokeStyle
        {
            Opacity = 1f, Width = 0.7f, Pressure = 0.3f, Gaps = 0f, Nib = 0.3f, WobbleHexes = 0f
        };
    }
}
