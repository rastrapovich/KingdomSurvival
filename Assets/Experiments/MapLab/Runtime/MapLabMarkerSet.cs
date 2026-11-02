using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Направления фишки относительно камеры: шесть секторов по 60°.
    // «Right» — правая сторона самой фигурки (меч), не отражение картинки.
    public enum MapLabMarkerDirection
    {
        Front = 0,
        FrontRight = 1,
        BackRight = 2,
        Back = 3,
        BackLeft = 4,
        FrontLeft = 5,
    }

    // Этап 04: полный набор шести направлений для одного номинального наклона.
    [Serializable]
    public sealed class MapLabMarkerPitchSet
    {
        public float Pitch = 50f;
        [Tooltip("ID кадров в порядке Front → Front_Right → Back_Right → Back → Back_Left → Front_Left.")]
        public string[] ViewIds = new string[6];
    }

    // Набор кадров фишки отряда: шесть направлений при условных 50° и два
    // передних калибровочных кадра для 40° и 60° (этап 02) либо несколько
    // полных наборов по наклонам (этап 04, PitchSets: кадр адресуется парой
    // «наклон + направление», наборы не перезаписывают друг друга). Кадры —
    // записи ArtSet по стабильным ID; поправки регистрации хранятся в записях.
    [CreateAssetMenu(menuName = "Kingdom Survival/MapLab/Набор фишки", fileName = "MapLabMarkerSet")]
    public sealed class MapLabMarkerSet : ScriptableObject
    {
        public static readonly string[] DirectionNames = { "Front", "Front_Right", "Back_Right", "Back", "Back_Left", "Front_Left" };

        public MapLabArtSet ArtSet;
        [Tooltip("ID кадров в порядке Front → Front_Right → Back_Right → Back → Back_Left → Front_Left.")]
        public string[] ViewIds = new string[6];
        public string Front40Id;
        public string Front60Id;
        [Tooltip("Наклон камеры, для которого нарисован полный набор.")]
        public float FullSetPitch = 50f;
        [Tooltip("Допуск по наклону, в котором калибровочный кадр считается подходящим.")]
        public float CalibrationTolerance = 4f;
        [Tooltip("Этап 04: полные наборы направлений по наклонам (50°, 35°). Пусто — прежний набор ViewIds.")]
        public List<MapLabMarkerPitchSet> PitchSets = new List<MapLabMarkerPitchSet>();

        public bool HasPitchSets => PitchSets != null && PitchSets.Count > 0;

        // Набор для наклона: ближайший, но у порога между соседними наборами —
        // с гистерезисом (switchPitch ± hysteresis/2), чтобы кадр не мигал.
        // current — прежний выбор (−1 — нет).
        public int ChooseSet(float pitch, int current, float switchPitch, float hysteresis)
        {
            if (!HasPitchSets)
                return -1;
            int nearest = 0;
            for (int i = 1; i < PitchSets.Count; i++)
                if (Mathf.Abs(PitchSets[i].Pitch - pitch) < Mathf.Abs(PitchSets[nearest].Pitch - pitch))
                    nearest = i;
            if (current < 0 || current >= PitchSets.Count || current == nearest)
                return nearest;
            float a = PitchSets[current].Pitch, b = PitchSets[nearest].Pitch;
            float boundary = (Mathf.Min(a, b) < switchPitch && switchPitch < Mathf.Max(a, b)) ? switchPitch : (a + b) * 0.5f;
            float past = b > a ? pitch - boundary : boundary - pitch;
            return past > hysteresis * 0.5f ? nearest : current;
        }

        // Кадр пары (набор, направление). Нет кадра — то же направление другого
        // набора (приближённо), затем прежний ViewIds.
        public MapLabArtEntry ResolveSet(MapLabMarkerDirection direction, float pitch, int set, out bool approximate)
        {
            approximate = true;
            if (ArtSet == null || !HasPitchSets)
                return Resolve(direction, pitch, out approximate);
            set = Mathf.Clamp(set, 0, PitchSets.Count - 1);
            MapLabArtEntry entry = Find(PitchSets[set], direction);
            if (entry != null)
            {
                approximate = Mathf.Abs(pitch - PitchSets[set].Pitch) > CalibrationTolerance + 1f;
                return entry;
            }
            for (int i = 0; i < PitchSets.Count; i++)
            {
                entry = Find(PitchSets[i], direction);
                if (entry != null)
                    return entry;
            }
            entry = View(direction);
            return entry != null && entry.HasTexture ? entry : null;
        }

        private MapLabArtEntry Find(MapLabMarkerPitchSet set, MapLabMarkerDirection direction)
        {
            if (set == null || set.ViewIds == null || set.ViewIds.Length < 6)
                return null;
            MapLabArtEntry e = ArtSet.Find(set.ViewIds[(int)direction]);
            return e != null && e.HasTexture ? e : null;
        }

        // Сектор по направлению взгляда фигурки и азимуту камеры (градусы, 0 — +Z).
        // Лицом к камере — Front; повернулась вправо по кадру (видна её правая
        // сторона) — Front_Right; спиной — Back.
        public static MapLabMarkerDirection Sector(float facingDegrees, float cameraAzimuthDegrees)
        {
            float relative = Mathf.DeltaAngle(cameraAzimuthDegrees, facingDegrees);
            int index = Mathf.RoundToInt((180f - relative) / 60f);
            index = ((index % 6) + 6) % 6;
            return (MapLabMarkerDirection)index;
        }

        public MapLabArtEntry View(MapLabMarkerDirection direction)
        {
            if (ArtSet == null || ViewIds == null || ViewIds.Length < 6)
                return null;
            return ArtSet.Find(ViewIds[(int)direction]);
        }

        // Кадр для камеры: калибровочный 40°/60° — только для Front при
        // соответствующем наклоне; иначе основной набор 50° (возможно — «приближённый ракурс»).
        public MapLabArtEntry Resolve(MapLabMarkerDirection direction, float pitch, out bool approximate)
        {
            MapLabArtEntry entry = View(direction);
            approximate = Mathf.Abs(pitch - FullSetPitch) > CalibrationTolerance;
            if (direction == MapLabMarkerDirection.Front && ArtSet != null)
            {
                MapLabArtEntry calibration = null;
                if (Mathf.Abs(pitch - 40f) <= CalibrationTolerance)
                    calibration = ArtSet.Find(Front40Id);
                else if (Mathf.Abs(pitch - 60f) <= CalibrationTolerance)
                    calibration = ArtSet.Find(Front60Id);
                if (calibration != null && calibration.HasTexture)
                {
                    approximate = false;
                    return calibration;
                }
            }
            return entry;
        }

        public bool Complete
        {
            get
            {
                if (HasPitchSets)
                {
                    foreach (MapLabMarkerPitchSet set in PitchSets)
                        for (int i = 0; i < 6; i++)
                            if (Find(set, (MapLabMarkerDirection)i) == null)
                                return false;
                    return true;
                }
                for (int i = 0; i < 6; i++)
                {
                    MapLabArtEntry e = View((MapLabMarkerDirection)i);
                    if (e == null || !e.HasTexture)
                        return false;
                }
                return true;
            }
        }
    }
}
