using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    public enum MapLabArtCategory
    {
        Vegetation = 0,
        Building = 1,
        Rock = 2,
        Cliff = 3,
        Texture = 4,
        Figure = 5,
        FigureCalibration = 6,
        // Этап 05 (дописаны в конец).
        Prop = 7,
        Decal = 8,
        FenceModule = 9,
        BridgeModule = 10,
    }

    // Как рисунок получает свет: художественное затенение уже в рисунке (не
    // накладываем второй Lambert), только общий тон тени рельефа под опорой.
    public enum MapLabArtLight
    {
        BakedInImage = 0,
        Unlit = 1,
    }

    // Рисунок нарисован под одним ракурсом: его нельзя крутить или отражать.
    public enum MapLabArtOrientation
    {
        FixedView = 0,
    }

    // Один нарисованный элемент: исходник, опора, масштаб и правила посадки.
    // Поля «источника» обновляет импорт; поля калибровки — художник, повторный
    // импорт их сохраняет.
    [Serializable]
    public sealed class MapLabArtEntry
    {
        [Tooltip("Стабильный ID из manifest; по нему повторный импорт обновляет запись.")]
        public string Id;
        public MapLabArtCategory Category;
        [Tooltip("Тип объекта MapLab, который заменяет рисунок.")]
        public MapLabObjectKind Kind;
        [Tooltip("Исходное поле kind из manifest (для описания).")]
        public string ManifestKind;
        public Texture2D Texture;

        [Header("Калибровка (сохраняется при повторном импорте)")]
        [Tooltip("Вероятность выбора среди рисунков того же типа.")]
        [Min(0f)] public float Weight = 1f;
        [Tooltip("Высота видимого силуэта (alpha ≥ порога) в мире, единиц.")]
        [Min(0.1f)] public float WorldHeight = 4f;
        [Tooltip("Точка опоры на холсте: 0..1 от левого нижнего угла.")]
        public Vector2 Pivot = new Vector2(0.5f, 0.1f);
        [Tooltip("Пятно основания в мире (ширина × глубина), для посадки и дворов.")]
        public Vector2 Footprint = new Vector2(1.5f, 1.5f);
        [Tooltip("Наибольший уклон под опорой, градусов; круче — объект уменьшается или не ставится.")]
        [Range(0f, 60f)] public float MaxSlope = 18f;
        public MapLabArtOrientation Orientation = MapLabArtOrientation.FixedView;
        [Tooltip("Небольшой диапазон масштаба.")]
        public Vector2 ScaleRange = new Vector2(0.9f, 1.1f);
        public MapLabArtLight Light = MapLabArtLight.BakedInImage;
        [Tooltip("Порог AlphaClip.")]
        [Range(0.05f, 0.95f)] public float Cutoff = 0.5f;
        [Tooltip("Поправка роста кадра (регистрация направлений фигурки).")]
        [Range(0.8f, 1.25f)] public float HeightCorrection = 1f;
        [Tooltip("Пользователь правил опору в редакторе калибровки.")]
        public bool PivotEdited;

        [Header("Источник (обновляет импорт)")]
        public string SourcePath;
        public string Sha256;
        public int Width;
        public int Height;
        [Tooltip("Силуэт по alpha ≥ 160 в долях холста (левый нижний угол).")]
        public Rect SolidRect = new Rect(0f, 0f, 1f, 1f);
        [Tooltip("Подсказка опоры из manifest (не калибровка).")]
        public Vector2 PivotHint = new Vector2(0.5f, 0.1f);
        [Tooltip("Центр нижней полосы силуэта по X (доля холста) — опора между ступнями/под стволом.")]
        public float FootX = 0.5f;
        public string Direction;
        public float PitchDownDegrees = 50f;
        [Tooltip("Этап 03: роль из manifest (single_tree, bush, house, barn…).")]
        public string Role;
        [Tooltip("Этап 03: группа ракурсов одного типа постройки (кадры P50/P35 — один дом, не два типа).")]
        public string VariantGroup;
        [Tooltip("Происхождение файла (new_stage03_imagegen, unchanged_stage02).")]
        public string Provenance;
        [Tooltip("Средний цвет (для фактур — нейтральная точка примеси).")]
        public Color MeanColor = Color.gray;
        [Tooltip("Средняя разница противоположных краёв (RGB, 0..255) — проверка повторения фактуры.")]
        public float TileEdgeDifference;
        public string Notes;

        [Header("Этап 05 (паспорт и регистрация)")]
        [Tooltip("Плоскость рисования модуля: vertical_front — вертикальная поверхность, horizontal_top — вид сверху; пусто — спрайт.")]
        public string AuthoringPlane;
        [Tooltip("Ракурс постройки: FrontLeft, FrontRight, BackLeft, BackRight.")]
        public string ViewTag;
        [Tooltip("Где вход (front_gable — на переднем фронтоне).")]
        public string EntranceLocation;
        [Tooltip("Видимая рамка модуля (connection_hint.visual_bbox) в долях холста, левый нижний угол.")]
        public Rect ModuleRect = new Rect(0f, 0f, 1f, 1f);
        [Tooltip("Диапазон непрозрачности декали (opacity_start_range).")]
        public Vector2 OpacityRange = new Vector2(0.04f, 0.12f);
        [Tooltip("Регистрация постройки по углам основания: угол конька в мире (от +X к +Z), градусов.")]
        public float DrawnYaw;
        [Tooltip("Направление входа в мире (нормировано): по ракурсу и фронтону с дверью.")]
        public Vector2 EntranceDirection;
        [Tooltip("Основание найдено по силуэту (иначе — запасная опора).")]
        public bool BaseRegistered;
        [Tooltip("Запись взята из набора другого этапа (тот же SHA-256) — файл не дублируется.")]
        public string ReusedFrom;

        public bool HasTexture => Texture != null;

        public float Aspect => Height > 0 ? Width / (float)Height : 1f;

        // Высота всей карточки (холста) в мире.
        public float CardHeight => WorldHeight * HeightCorrection / Mathf.Max(0.05f, SolidRect.height);
    }

    // Подключённый художественный набор (этап 02 или 03). В git — этот ассет и
    // .meta картинок; сами PNG — локально (их восстанавливает повторный импорт).
    [CreateAssetMenu(menuName = "Kingdom Survival/MapLab/Набор арта", fileName = "MapLabArtSet")]
    public sealed class MapLabArtSet : ScriptableObject
    {
        public int SchemaVersion;
        [Tooltip("package_revision из manifest (2 — этап 02, 3 — этап 03).")]
        public int PackageRevision;
        public string PackageId;
        [Tooltip("Номер подключения: растёт при каждом импорте.")]
        public int ImportVersion;
        public string ImportedAt;
        [Tooltip("SHA-256 manifest пакета.")]
        public string ManifestSha256;
        public List<MapLabArtEntry> Entries = new List<MapLabArtEntry>();

        public MapLabArtEntry Find(string id)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i] != null && Entries[i].Id == id)
                    return Entries[i];
            return null;
        }

        // Кадры одной группы ракурсов (P50/P35 одного дома).
        public MapLabArtEntry FindView(string variantGroup, float pitch)
        {
            MapLabArtEntry best = null;
            for (int i = 0; i < Entries.Count; i++)
            {
                MapLabArtEntry e = Entries[i];
                if (e == null || !e.HasTexture || e.VariantGroup != variantGroup)
                    continue;
                if (best == null || Mathf.Abs(e.PitchDownDegrees - pitch) < Mathf.Abs(best.PitchDownDegrees - pitch))
                    best = e;
            }
            return best;
        }

        // Рисунки, заменяющие данный тип объекта (только с картинкой).
        public void Collect(MapLabObjectKind kind, MapLabArtCategory category, List<MapLabArtEntry> result)
        {
            result.Clear();
            for (int i = 0; i < Entries.Count; i++)
            {
                MapLabArtEntry e = Entries[i];
                if (e != null && e.HasTexture && e.Category == category && e.Kind == kind && e.Weight > 0f)
                    result.Add(e);
            }
        }

        public int MissingTextures
        {
            get
            {
                int missing = 0;
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i] != null && !Entries[i].HasTexture)
                        missing++;
                return missing;
            }
        }

        // Короткая «версия» набора для JSON экспорта: пакет, номер подключения и
        // хеш, зависящий от исходных PNG и калибровки.
        public string Fingerprint
        {
            get
            {
                ulong h = 1469598103934665603UL;
                for (int i = 0; i < Entries.Count; i++)
                {
                    MapLabArtEntry e = Entries[i];
                    if (e == null)
                        continue;
                    h = Mix(h, e.Id + e.Sha256 + e.Pivot + e.WorldHeight + e.Cutoff + e.HeightCorrection + e.Weight + (e.HasTexture ? "1" : "0"));
                }
                return $"{PackageId}#{ImportVersion}:{h:x16}";
            }
        }

        private static ulong Mix(ulong h, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                h ^= text[i];
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
