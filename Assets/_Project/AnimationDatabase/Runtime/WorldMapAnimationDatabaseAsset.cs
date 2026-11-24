using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase
{
    // Курс фигуры на глобальной карте — шесть секторов экрана по 60°.
    // Порядок совпадает с WorldMapActorMotion.DirectionIndex и ракурсами
    // фишки MapLab: 0 — к зрителю (вниз), дальше против часовой стрелки.
    public enum WorldMapFacing
    {
        Down,
        DownRight,
        UpRight,
        Up,
        UpLeft,
        DownLeft
    }

    public static class WorldMapFacingLabels
    {
        public static string Title(WorldMapFacing facing)
        {
            switch (facing)
            {
                case WorldMapFacing.Down: return "вниз";
                case WorldMapFacing.DownRight: return "вправо-вниз";
                case WorldMapFacing.UpRight: return "вправо-вверх";
                case WorldMapFacing.Up: return "вверх";
                case WorldMapFacing.UpLeft: return "влево-вверх";
                case WorldMapFacing.DownLeft: return "влево-вниз";
                default: return facing.ToString();
            }
        }

        public static string Arrow(WorldMapFacing facing)
        {
            switch (facing)
            {
                case WorldMapFacing.Down: return "↓";
                case WorldMapFacing.DownRight: return "↘";
                case WorldMapFacing.UpRight: return "↗";
                case WorldMapFacing.Up: return "↑";
                case WorldMapFacing.UpLeft: return "↖";
                case WorldMapFacing.DownLeft: return "↙";
                default: return "?";
            }
        }

        // Сектор по направлению движения на экране (x вправо, y вниз) —
        // то же правило, что WorldMapActorMotion.DirectionIndex.
        public static WorldMapFacing FromScreenDirection(float dx, float dy)
        {
            double heading = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            int index = (int)Math.Round((180.0 - heading) / 60.0);
            return (WorldMapFacing)(((index % 6) + 6) % 6);
        }
    }

    // Какой ракурс кадров смотрит в какой сектор карты.
    [Serializable]
    public sealed class WorldMapDirectionMapping
    {
        [SerializeField] private CreatureAnimationDirection direction;
        [SerializeField] private WorldMapFacing facing;

        public CreatureAnimationDirection Direction => direction;
        public WorldMapFacing Facing => facing;

        public WorldMapDirectionMapping()
        {
        }

        public WorldMapDirectionMapping(CreatureAnimationDirection direction, WorldMapFacing facing)
        {
            this.direction = direction;
            this.facing = facing;
        }

        public void SetFacing(WorldMapFacing value) { facing = value; }
    }

    // Набор анимаций фигуры глобальной карты: кадры, опора, холст и клипы —
    // общий формат Базы анимаций (CreatureAnimationSetData); поля боя
    // fieldScale и walkHexesPerCycle здесь не используются. Размер и шаг —
    // в клетках карты.
    [Serializable]
    public sealed class WorldMapAnimationSetData
    {
        // Высота холста кадра в ширинах клетки карты (клетка = 9 единиц мира
        // MapLab). Фигура в пакете героя занимает ~77% холста: 0,4 ≈ рост
        // прежней фишки (2,8 единицы ≈ 0,31 клетки).
        public const float DefaultHeightInHexes = 0.4f;
        public const float MinHeightInHexes = 0.1f;
        public const float MaxHeightInHexes = 3f;
        // Сколько клеток карты проходит один цикл «Ходьбы» (ноги не скользят).
        public const float DefaultStrideHexesPerCycle = 0.5f;
        public const float MinStrideHexesPerCycle = 0.05f;
        public const float MaxStrideHexesPerCycle = 10f;
        // Опора пакета героя по умолчанию: ноги на 12% от низа холста.
        public static readonly Vector2 DefaultPivot = new Vector2(0.5f, 0.12109375f);

        [SerializeField] private CreatureAnimationSetData animation = new CreatureAnimationSetData();
        [SerializeField] private float heightInHexes = DefaultHeightInHexes;
        [SerializeField] private float strideHexesPerCycle = DefaultStrideHexesPerCycle;
        // Подгонять темп «Ходьбы» под фактическую скорость фигуры (по «Шагу
        // ходьбы»). Выключено — кадры идут ровно с «Кадров в секунду» набора.
        [SerializeField] private bool syncCadenceToSpeed;

        public CreatureAnimationSetData Animation => animation ?? (animation = new CreatureAnimationSetData());
        public string Id => Animation.Id;
        public string DisplayName => Animation.DisplayName;
        public float HeightInHexes => Mathf.Clamp(heightInHexes > 0f ? heightInHexes : DefaultHeightInHexes, MinHeightInHexes, MaxHeightInHexes);
        public float StrideHexesPerCycle => Mathf.Clamp(strideHexesPerCycle > 0f ? strideHexesPerCycle : DefaultStrideHexesPerCycle, MinStrideHexesPerCycle, MaxStrideHexesPerCycle);

        public WorldMapAnimationSetData()
        {
        }

        public WorldMapAnimationSetData(string id, string displayName)
        {
            animation = new CreatureAnimationSetData(id, displayName);
            animation.SetPivot(DefaultPivot);
        }

        public void SetHeightInHexes(float value) { heightInHexes = Mathf.Clamp(value, MinHeightInHexes, MaxHeightInHexes); }
        public void SetStrideHexesPerCycle(float value) { strideHexesPerCycle = Mathf.Clamp(value, MinStrideHexesPerCycle, MaxStrideHexesPerCycle); }
        public bool SyncCadenceToSpeed => syncCadenceToSpeed;
        public void SetSyncCadenceToSpeed(bool value) { syncCadenceToSpeed = value; }

        // Есть кадры передвижения хотя бы в одном ракурсе.
        public bool HasWalk
        {
            get
            {
                CreatureAnimationClipData walk = Animation.FindClip(CreatureAnimationAction.Walk);
                return walk != null && walk.HasAnyFrames;
            }
        }

        // «Готово» для карты: передвижение во всех шести ракурсах.
        public bool HasCompleteWalk => Animation.HasCompleteAction(CreatureAnimationAction.Walk);

        // Темп клипа ходьбы при скорости hexesPerSecond (клетки карты в
        // секунду показа): шаг набора ложится на путь — ноги не скользят.
        public float GetWalkRate(float clipDuration, float hexesPerSecond)
        {
            if (clipDuration <= 0.0001f || hexesPerSecond <= 0f)
                return 1f;
            return Mathf.Clamp(hexesPerSecond * clipDuration / StrideHexesPerCycle, 0.25f, 4f);
        }
    }

    // Раздел «Глобальная карта» Базы анимаций — отдельный ассет: фигуры,
    // которые ходят по основной карте (сначала герой, затем группы мира).
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapAnimations",
        menuName = "Kingdom Survival/Анимации глобальной карты")]
    public sealed class WorldMapAnimationDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "AnimationDatabase/KingdomSurvivalWorldMapAnimations";
        public const string AssetPath = "Assets/_Project/AnimationDatabase/Resources/AnimationDatabase/KingdomSurvivalWorldMapAnimations.asset";
        public const string DefaultHeroSetId = "hero";
        public const int CurrentSchemaVersion = 1;

        [SerializeField, HideInInspector] private int schemaVersion;
        [SerializeField] private List<WorldMapDirectionMapping> directionMap = new List<WorldMapDirectionMapping>();
        [SerializeField] private List<WorldMapAnimationSetData> sets = new List<WorldMapAnimationSetData>();
        // Набор фигуры героя на карте.
        [SerializeField] private string heroSetId = DefaultHeroSetId;

        public int SchemaVersion => schemaVersion;
        public IReadOnlyList<WorldMapAnimationSetData> Sets => sets;
        public IReadOnlyList<WorldMapDirectionMapping> DirectionMap => directionMap;
        public string HeroSetId => heroSetId ?? string.Empty;
        public void SetHeroSetId(string value) { heroSetId = value ?? string.Empty; }

        // Набор героя, если в нём есть кадры передвижения; иначе null.
        public WorldMapAnimationSetData HeroSet
        {
            get
            {
                WorldMapAnimationSetData set = FindSet(heroSetId);
                return set != null && set.HasWalk ? set : null;
            }
        }

        private static WorldMapAnimationDatabaseAsset cached;
        private static bool cachedLoaded;

        // База из Resources (кэш). Null — раздела ещё нет. В игре отсутствие
        // проверяется один раз за запуск, в редакторе — при каждом обращении.
        public static WorldMapAnimationDatabaseAsset Current
        {
            get
            {
                if (!cachedLoaded || (cached == null && !Application.isPlaying))
                {
                    cached = Resources.Load<WorldMapAnimationDatabaseAsset>(ResourcesPath);
                    cachedLoaded = true;
                }
                return cached;
            }
        }

        // ------------------------------------------------------------------
        // Ракурсы анимаций карты — как в бою (24.11.2026). Клетки карты, как
        // и гексы боя, «остриём вверх»: шаг к соседу идёт вправо, вправо-вверх,
        // влево-вверх, влево, влево-вниз, вправо-вниз (порядок HexFacing).
        // Сектор выбирается по этим шести векторам, ракурс кадров — по
        // таблице ракурсов боевой Базы анимаций: один и тот же пакет кадров
        // (Front — вправо, Front_Right — вправо-вверх, …) смотрит одинаково
        // и в бою, и на карте. Прежняя таблица карты (directionMap,
        // WorldMapFacing) остаётся только для статичной фишки MapLab.

        // Экранные векторы к соседним клеткам (x вправо, y вниз).
        public static readonly Vector2[] HexNeighborScreenVectors =
        {
            new Vector2(1f, 0f),
            new Vector2(0.5f, -0.8660254f),
            new Vector2(-0.5f, -0.8660254f),
            new Vector2(-1f, 0f),
            new Vector2(-0.5f, 0.8660254f),
            new Vector2(0.5f, 0.8660254f)
        };

        // Запас, на который новый сектор должен быть ближе прежнего, — фигура
        // не дёргается между ракурсами на границе секторов.
        public const float FacingHysteresis = 0.05f;

        // Сектор боя по смещению на плоском полотне карты (до наклона камеры).
        // Нулевое смещение — прежний сектор.
        public static HexFacing FacingFromScreen(Vector2 screen, HexFacing current)
        {
            if (screen.sqrMagnitude < 1e-10f)
                return current;
            HexFacing nearest = HexFacingMath.Nearest(screen, HexNeighborScreenVectors, current);
            if (nearest == current)
                return current;
            Vector2 normalized = screen.normalized;
            float currentDot = Vector2.Dot(normalized, HexNeighborScreenVectors[(int)current]);
            float nearestDot = Vector2.Dot(normalized, HexNeighborScreenVectors[(int)nearest]);
            return nearestDot - currentDot > FacingHysteresis ? nearest : current;
        }

        private static CreatureAnimationDatabaseAsset battleTable;
        private static bool battleTableLoaded;

        // Боевая База анимаций — источник таблицы ракурсов (кэш, как Current).
        private static CreatureAnimationDatabaseAsset BattleTable
        {
            get
            {
                if (!battleTableLoaded || (battleTable == null && !Application.isPlaying))
                {
                    battleTable = Resources.Load<CreatureAnimationDatabaseAsset>(CreatureAnimationDatabaseAsset.ResourcesPath);
                    battleTableLoaded = true;
                }
                return battleTable;
            }
        }

        // Ракурс кадров, который смотрит в сектор facing, — по таблице боя.
        public static CreatureAnimationDirection BattleDirection(HexFacing facing)
        {
            CreatureAnimationDatabaseAsset table = BattleTable;
            if (table != null)
                return table.GetDirection(facing);
            foreach (CreatureAnimationDirectionMapping entry in CreatureAnimationDatabaseAsset.DefaultDirectionMap())
            {
                if (entry.Facing == facing)
                    return entry.Direction;
            }
            return CreatureAnimationDirection.Front;
        }

        // Куда на карте смотрит ракурс кадров — по таблице боя.
        public static HexFacing BattleFacing(CreatureAnimationDirection direction)
        {
            CreatureAnimationDatabaseAsset table = BattleTable;
            if (table != null)
                return table.GetFacing(direction);
            foreach (CreatureAnimationDirectionMapping entry in CreatureAnimationDatabaseAsset.DefaultDirectionMap())
            {
                if (entry.Direction == direction)
                    return entry.Facing;
            }
            return HexFacing.East;
        }

        // Единичный экранный вектор сектора боя (y вниз).
        public static Vector2 FacingScreenVector(HexFacing facing)
        {
            int index = (int)facing;
            return index >= 0 && index < HexNeighborScreenVectors.Length ? HexNeighborScreenVectors[index] : Vector2.right;
        }

        // Ракурсы фишки MapLab названы по исходным картинкам (MapLabMarkerSet):
        // Front — к зрителю, Front_Right — вправо-вниз, и т. д. по кругу.
        public static WorldMapDirectionMapping[] DefaultDirectionMap()
        {
            return new[]
            {
                new WorldMapDirectionMapping(CreatureAnimationDirection.Front, WorldMapFacing.Down),
                new WorldMapDirectionMapping(CreatureAnimationDirection.FrontRight, WorldMapFacing.DownRight),
                new WorldMapDirectionMapping(CreatureAnimationDirection.BackRight, WorldMapFacing.UpRight),
                new WorldMapDirectionMapping(CreatureAnimationDirection.Back, WorldMapFacing.Up),
                new WorldMapDirectionMapping(CreatureAnimationDirection.BackLeft, WorldMapFacing.UpLeft),
                new WorldMapDirectionMapping(CreatureAnimationDirection.FrontLeft, WorldMapFacing.DownLeft)
            };
        }

        // Идемпотентно. Схема 1: таблица ракурсов карты по умолчанию.
        public bool MigrateIfNeeded()
        {
            bool changed = false;
            if (sets == null)
            {
                sets = new List<WorldMapAnimationSetData>();
                changed = true;
            }
            if (!IsDirectionMapValid())
            {
                ResetDirectionMap();
                changed = true;
            }
            if (schemaVersion < CurrentSchemaVersion)
            {
                schemaVersion = CurrentSchemaVersion;
                changed = true;
            }
            return changed;
        }

        public void ResetDirectionMap()
        {
            directionMap = new List<WorldMapDirectionMapping>(DefaultDirectionMap());
        }

        public bool IsDirectionMapValid()
        {
            if (directionMap == null || directionMap.Count != CreatureAnimationLabels.DirectionCount)
                return false;
            bool[] seenDirections = new bool[CreatureAnimationLabels.DirectionCount];
            bool[] seenFacings = new bool[CreatureAnimationLabels.DirectionCount];
            foreach (WorldMapDirectionMapping entry in directionMap)
            {
                if (entry == null)
                    return false;
                int direction = (int)entry.Direction;
                int facing = (int)entry.Facing;
                if (direction < 0 || direction >= seenDirections.Length || facing < 0 || facing >= seenFacings.Length ||
                    seenDirections[direction] || seenFacings[facing])
                {
                    return false;
                }
                seenDirections[direction] = true;
                seenFacings[facing] = true;
            }
            return true;
        }

        public WorldMapFacing GetFacing(CreatureAnimationDirection direction)
        {
            IEnumerable<WorldMapDirectionMapping> map = IsDirectionMapValid() ? directionMap : (IEnumerable<WorldMapDirectionMapping>)DefaultDirectionMap();
            foreach (WorldMapDirectionMapping entry in map)
            {
                if (entry.Direction == direction)
                    return entry.Facing;
            }
            return WorldMapFacing.Down;
        }

        public CreatureAnimationDirection GetDirection(WorldMapFacing facing)
        {
            IEnumerable<WorldMapDirectionMapping> map = IsDirectionMapValid() ? directionMap : (IEnumerable<WorldMapDirectionMapping>)DefaultDirectionMap();
            foreach (WorldMapDirectionMapping entry in map)
            {
                if (entry.Facing == facing)
                    return entry.Direction;
            }
            return CreatureAnimationDirection.Front;
        }

        // Меняет сектор ракурса; прежний владелец сектора получает
        // освободившийся — таблица остаётся взаимно однозначной.
        public void AssignFacing(CreatureAnimationDirection direction, WorldMapFacing facing)
        {
            if (!IsDirectionMapValid())
                ResetDirectionMap();
            WorldMapDirectionMapping target = null;
            WorldMapDirectionMapping owner = null;
            foreach (WorldMapDirectionMapping entry in directionMap)
            {
                if (entry.Direction == direction)
                    target = entry;
                if (entry.Facing == facing)
                    owner = entry;
            }
            if (target == null || owner == null || target == owner)
                return;
            WorldMapFacing previous = target.Facing;
            target.SetFacing(facing);
            owner.SetFacing(previous);
        }

        public WorldMapAnimationSetData FindSet(string setId)
        {
            if (string.IsNullOrWhiteSpace(setId) || sets == null)
                return null;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != null && string.Equals(sets[i].Id, setId, StringComparison.Ordinal))
                    return sets[i];
            }
            return null;
        }

        public WorldMapAnimationSetData AddSet(string setId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(setId))
                throw new ArgumentException("ID набора анимаций карты не может быть пустым.", nameof(setId));
            if (FindSet(setId) != null)
                throw new InvalidOperationException("Набор анимаций карты " + setId + " уже есть.");
            if (sets == null)
                sets = new List<WorldMapAnimationSetData>();
            WorldMapAnimationSetData set = new WorldMapAnimationSetData(setId, displayName);
            sets.Add(set);
            return set;
        }

        public bool RemoveSet(string setId)
        {
            return sets != null && sets.RemoveAll(set => set == null || string.Equals(set.Id, setId, StringComparison.Ordinal)) > 0;
        }

        public string MakeUniqueSetId(string seed)
        {
            string baseId = string.IsNullOrWhiteSpace(seed) ? "map_set" : seed.Trim();
            string candidate = baseId;
            int suffix = 2;
            while (FindSet(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }
    }
}
