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

    // Набор кадров деревянной фишки отряда: шесть направлений при условных 50°
    // и два передних калибровочных кадра для 40° и 60°. Кадры — записи ArtSet
    // по стабильным ID; поправки регистрации хранятся в самих записях.
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
