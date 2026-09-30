using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase
{
    // Одна ячейка «действие × ракурс»: кадры в порядке показа.
    [Serializable]
    public sealed class CreatureAnimationFrames
    {
        [SerializeField] private CreatureAnimationDirection direction;
        [SerializeField] private List<Sprite> frames = new List<Sprite>();
        // Номера исходных кадров Blender (0001, 0004, …). Пусто, если кадры
        // пришли не из файлов с номерами. Нужны только режиму «исходный темп».
        [SerializeField] private List<int> sourceFrameNumbers = new List<int>();
        // Частная поправка ракурса, доли холста (раздел «Дополнительно»).
        [SerializeField] private Vector2 offset;

        public CreatureAnimationDirection Direction => direction;
        public IReadOnlyList<Sprite> Frames => frames;
        public IReadOnlyList<int> SourceFrameNumbers => sourceFrameNumbers;
        public Vector2 Offset => offset;

        public int FrameCount => frames != null ? frames.Count : 0;

        public CreatureAnimationFrames()
        {
        }

        public CreatureAnimationFrames(CreatureAnimationDirection direction)
        {
            this.direction = direction;
        }

        public bool HasFrames
        {
            get
            {
                if (frames == null)
                    return false;
                for (int i = 0; i < frames.Count; i++)
                {
                    if (frames[i] != null)
                        return true;
                }
                return false;
            }
        }

        // Номера пригодны для исходного темпа: по одному на кадр, строго по возрастанию.
        public bool HasUsableSourceNumbers
        {
            get
            {
                if (sourceFrameNumbers == null || frames == null ||
                    sourceFrameNumbers.Count != frames.Count || frames.Count == 0)
                {
                    return false;
                }
                for (int i = 1; i < sourceFrameNumbers.Count; i++)
                {
                    if (sourceFrameNumbers[i] <= sourceFrameNumbers[i - 1])
                        return false;
                }
                return true;
            }
        }

        public void SetFrames(IEnumerable<Sprite> newFrames, IEnumerable<int> newSourceNumbers)
        {
            frames = new List<Sprite>(newFrames ?? Array.Empty<Sprite>());
            sourceFrameNumbers = new List<int>(newSourceNumbers ?? Array.Empty<int>());
            if (sourceFrameNumbers.Count != frames.Count)
                sourceFrameNumbers.Clear();
        }

        public void AppendFrames(IEnumerable<Sprite> extraFrames, IEnumerable<int> extraSourceNumbers)
        {
            List<Sprite> added = new List<Sprite>(extraFrames ?? Array.Empty<Sprite>());
            List<int> addedNumbers = new List<int>(extraSourceNumbers ?? Array.Empty<int>());
            bool keepNumbers = HasUsableSourceNumbers || frames.Count == 0;
            frames.AddRange(added);
            if (keepNumbers && addedNumbers.Count == added.Count)
                sourceFrameNumbers.AddRange(addedNumbers);
            if (sourceFrameNumbers.Count != frames.Count)
                sourceFrameNumbers.Clear();
        }

        public void SetOffset(Vector2 value)
        {
            offset = value;
        }
    }

    // Действие набора: скорость, воспроизведение, маркер и кадры по ракурсам.
    [Serializable]
    public sealed class CreatureAnimationClipData
    {
        public const float DefaultFramesPerSecond = 12f;
        public const float MinFramesPerSecond = 1f;
        public const float MaxFramesPerSecond = 60f;

        [SerializeField] private CreatureAnimationAction action;
        // Ключ клипа особой атаки: одна способность — один клип.
        [SerializeField] private string clipKey = string.Empty;
        [SerializeField] private float framesPerSecond = DefaultFramesPerSecond;
        [SerializeField] private CreatureAnimationPlayback playback;
        // Момент удара / выпуска снаряда — доля длительности клипа.
        [SerializeField, Range(0f, 1f)] private float impactTime = 0.5f;
        // «Исходный темп»: длительность кадра по разнице номеров Blender.
        [SerializeField] private bool useSourceTiming;
        [SerializeField] private float sourceFramesPerSecond = 24f;
        [SerializeField] private List<CreatureAnimationFrames> directions = new List<CreatureAnimationFrames>();

        public CreatureAnimationAction Action => action;
        public string ClipKey => clipKey ?? string.Empty;
        public float FramesPerSecond => Mathf.Clamp(framesPerSecond, MinFramesPerSecond, MaxFramesPerSecond);
        public float RawFramesPerSecond => framesPerSecond;
        public CreatureAnimationPlayback Playback => playback;
        public float ImpactTime => Mathf.Clamp01(impactTime);
        public float RawImpactTime => impactTime;
        public bool UseSourceTiming => useSourceTiming;
        public float SourceFramesPerSecond => Mathf.Clamp(sourceFramesPerSecond, MinFramesPerSecond, 240f);
        public float RawSourceFramesPerSecond => sourceFramesPerSecond;
        public IReadOnlyList<CreatureAnimationFrames> Directions => directions;

        public CreatureAnimationClipData()
        {
        }

        public CreatureAnimationClipData(CreatureAnimationAction action, string clipKey = null)
        {
            this.action = action;
            this.clipKey = clipKey ?? string.Empty;
            playback = CreatureAnimationLabels.DefaultPlayback(action);
        }

        public bool Matches(CreatureAnimationAction otherAction, string otherKey)
        {
            return action == otherAction &&
                   string.Equals(ClipKey, otherKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public CreatureAnimationFrames FindDirection(CreatureAnimationDirection direction)
        {
            if (directions == null)
                return null;
            for (int i = 0; i < directions.Count; i++)
            {
                if (directions[i] != null && directions[i].Direction == direction)
                    return directions[i];
            }
            return null;
        }

        public CreatureAnimationFrames GetOrAddDirection(CreatureAnimationDirection direction)
        {
            CreatureAnimationFrames cell = FindDirection(direction);
            if (cell != null)
                return cell;
            if (directions == null)
                directions = new List<CreatureAnimationFrames>();
            cell = new CreatureAnimationFrames(direction);
            directions.Add(cell);
            directions.Sort((a, b) => ((int)a.Direction).CompareTo((int)b.Direction));
            return cell;
        }

        public bool RemoveDirection(CreatureAnimationDirection direction)
        {
            return directions != null && directions.RemoveAll(cell => cell == null || cell.Direction == direction) > 0;
        }

        public bool HasAnyFrames
        {
            get
            {
                if (directions == null)
                    return false;
                for (int i = 0; i < directions.Count; i++)
                {
                    if (directions[i] != null && directions[i].HasFrames)
                        return true;
                }
                return false;
            }
        }

        public int DirectionsWithFrames
        {
            get
            {
                int count = 0;
                for (int i = 0; i < CreatureAnimationLabels.DirectionCount; i++)
                {
                    CreatureAnimationFrames cell = FindDirection((CreatureAnimationDirection)i);
                    if (cell != null && cell.HasFrames)
                        count++;
                }
                return count;
            }
        }

        public void SetFramesPerSecond(float value) { framesPerSecond = value; }
        public void SetPlayback(CreatureAnimationPlayback value) { playback = value; }
        public void SetImpactTime(float value) { impactTime = value; }
        public void SetUseSourceTiming(bool value) { useSourceTiming = value; }
        public void SetSourceFramesPerSecond(float value) { sourceFramesPerSecond = value; }
    }

    // Набор анимаций типа существа. Связь с существом — по стабильному Id.
    [Serializable]
    public sealed class CreatureAnimationSetData
    {
        // Точка опоры по умолчанию совпадает с прежним правилом поля:
        // центр гекса на 15% выше нижнего края миниатюры.
        public static readonly Vector2 DefaultPivot = new Vector2(0.5f, 0.15f);

        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        // Точка кадра, которая стоит в центре гекса: доли холста, Y от низа.
        [SerializeField] private Vector2 pivot = DefaultPivot;
        // Масштаб на поле: 1 — высота холста равна прежней рамке миниатюры.
        [SerializeField, Min(0.1f)] private float fieldScale = 1f;
        // Общий холст кадров набора в пикселях; задаётся первым импортом.
        [SerializeField] private Vector2Int canvasSize;
        [SerializeField] private List<CreatureAnimationClipData> clips = new List<CreatureAnimationClipData>();

        public string Id => id ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
        public string RawDisplayName => displayName ?? string.Empty;
        public Vector2 Pivot => pivot;
        public float FieldScale => Mathf.Max(0.1f, fieldScale);
        public Vector2Int CanvasSize => canvasSize;
        public IReadOnlyList<CreatureAnimationClipData> Clips => clips;

        public CreatureAnimationSetData()
        {
        }

        public CreatureAnimationSetData(string id, string displayName)
        {
            this.id = id ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
        }

        public CreatureAnimationClipData FindClip(CreatureAnimationAction action, string clipKey = null)
        {
            if (clips == null)
                return null;
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null && clips[i].Matches(action, clipKey))
                    return clips[i];
            }
            return null;
        }

        public CreatureAnimationClipData GetOrAddClip(CreatureAnimationAction action, string clipKey = null)
        {
            CreatureAnimationClipData clip = FindClip(action, clipKey);
            if (clip != null)
                return clip;
            if (clips == null)
                clips = new List<CreatureAnimationClipData>();
            clip = new CreatureAnimationClipData(action, clipKey);
            clips.Add(clip);
            clips.Sort((a, b) =>
            {
                int byAction = ((int)a.Action).CompareTo((int)b.Action);
                return byAction != 0 ? byAction : string.Compare(a.ClipKey, b.ClipKey, StringComparison.OrdinalIgnoreCase);
            });
            return clip;
        }

        public bool RemoveClip(CreatureAnimationAction action, string clipKey = null)
        {
            return clips != null && clips.RemoveAll(clip => clip == null || clip.Matches(action, clipKey)) > 0;
        }

        public CreatureAnimationFrames FindFrames(CreatureAnimationAction action, CreatureAnimationDirection direction, string clipKey = null)
        {
            CreatureAnimationClipData clip = FindClip(action, clipKey);
            return clip != null ? clip.FindDirection(direction) : null;
        }

        public bool HasAnyFrames
        {
            get
            {
                if (clips == null)
                    return false;
                for (int i = 0; i < clips.Count; i++)
                {
                    if (clips[i] != null && clips[i].HasAnyFrames)
                        return true;
                }
                return false;
            }
        }

        public bool HasCompleteAction(CreatureAnimationAction action)
        {
            CreatureAnimationClipData clip = FindClip(action);
            return clip != null && clip.DirectionsWithFrames == CreatureAnimationLabels.DirectionCount;
        }

        // «Готово»: ожидание, ходьба, смерть и атака или выстрел во всех шести ракурсах.
        public CreatureAnimationSetStatus Status
        {
            get
            {
                if (!HasAnyFrames)
                    return CreatureAnimationSetStatus.Empty;
                bool ready = HasCompleteAction(CreatureAnimationAction.Idle) &&
                             HasCompleteAction(CreatureAnimationAction.Walk) &&
                             HasCompleteAction(CreatureAnimationAction.Death) &&
                             (HasCompleteAction(CreatureAnimationAction.Attack) ||
                              HasCompleteAction(CreatureAnimationAction.Shoot));
                return ready ? CreatureAnimationSetStatus.Ready : CreatureAnimationSetStatus.Partial;
            }
        }

        // Первый имеющийся кадр: ожидание спереди, иначе любой.
        public Sprite FindFirstFrame()
        {
            CreatureAnimationClipData idle = FindClip(CreatureAnimationAction.Idle);
            Sprite sprite = FirstFrameOf(idle);
            if (sprite != null)
                return sprite;
            if (clips == null)
                return null;
            for (int i = 0; i < clips.Count; i++)
            {
                sprite = FirstFrameOf(clips[i]);
                if (sprite != null)
                    return sprite;
            }
            return null;
        }

        private static Sprite FirstFrameOf(CreatureAnimationClipData clip)
        {
            if (clip == null)
                return null;
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
            {
                CreatureAnimationFrames cell = clip.FindDirection(direction);
                if (cell == null)
                    continue;
                for (int i = 0; i < cell.FrameCount; i++)
                {
                    if (cell.Frames[i] != null)
                        return cell.Frames[i];
                }
            }
            return null;
        }

        public void SetId(string value) { id = value ?? string.Empty; }
        public void SetDisplayName(string value) { displayName = value ?? string.Empty; }
        public void SetPivot(Vector2 value) { pivot = value; }
        public void SetFieldScale(float value) { fieldScale = Mathf.Max(0.1f, value); }
        public void SetCanvasSize(Vector2Int value) { canvasSize = value; }
    }

    // Какой ракурс рендера смотрит в какую сторону гекса на экране.
    [Serializable]
    public sealed class CreatureAnimationDirectionMapping
    {
        [SerializeField] private CreatureAnimationDirection direction;
        [SerializeField] private HexFacing facing;

        public CreatureAnimationDirection Direction => direction;
        public HexFacing Facing => facing;

        public CreatureAnimationDirectionMapping()
        {
        }

        public CreatureAnimationDirectionMapping(CreatureAnimationDirection direction, HexFacing facing)
        {
            this.direction = direction;
            this.facing = facing;
        }

        public void SetFacing(HexFacing value) { facing = value; }
    }

    [CreateAssetMenu(
        fileName = "KingdomSurvivalAnimations",
        menuName = "Kingdom Survival/База анимаций")]
    public sealed class CreatureAnimationDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "AnimationDatabase/KingdomSurvivalAnimations";
        public const string AssetPath = "Assets/_Project/AnimationDatabase/Resources/AnimationDatabase/KingdomSurvivalAnimations.asset";
        public const int CurrentSchemaVersion = 1;

        [SerializeField, HideInInspector] private int schemaVersion;
        [SerializeField] private List<CreatureAnimationDirectionMapping> directionMap = new List<CreatureAnimationDirectionMapping>();
        [SerializeField] private List<CreatureAnimationSetData> sets = new List<CreatureAnimationSetData>();

        public int SchemaVersion => schemaVersion;
        public IReadOnlyList<CreatureAnimationSetData> Sets => sets;
        public IReadOnlyList<CreatureAnimationDirectionMapping> DirectionMap => directionMap;

        // Камера KS Sprite Renderer повёрнута на 30°: шесть ракурсов совпадают
        // с шестью соседями гекса. Какая папка какому соседу соответствует,
        // проверяется на первом настоящем существе (12З-4) и правится в окне.
        public static CreatureAnimationDirectionMapping[] DefaultDirectionMap()
        {
            return new[]
            {
                new CreatureAnimationDirectionMapping(CreatureAnimationDirection.Front, HexFacing.SouthEast),
                new CreatureAnimationDirectionMapping(CreatureAnimationDirection.FrontRight, HexFacing.East),
                new CreatureAnimationDirectionMapping(CreatureAnimationDirection.BackRight, HexFacing.NorthEast),
                new CreatureAnimationDirectionMapping(CreatureAnimationDirection.Back, HexFacing.NorthWest),
                new CreatureAnimationDirectionMapping(CreatureAnimationDirection.BackLeft, HexFacing.West),
                new CreatureAnimationDirectionMapping(CreatureAnimationDirection.FrontLeft, HexFacing.SouthWest)
            };
        }

        // Идемпотентно. Схема 1: таблица ракурсов по умолчанию.
        public bool MigrateIfNeeded()
        {
            bool changed = false;
            if (sets == null)
            {
                sets = new List<CreatureAnimationSetData>();
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
            directionMap = new List<CreatureAnimationDirectionMapping>(DefaultDirectionMap());
        }

        // Каждый ракурс ровно один раз, каждое направление ровно один раз.
        public bool IsDirectionMapValid()
        {
            if (directionMap == null || directionMap.Count != CreatureAnimationLabels.DirectionCount)
                return false;
            bool[] seenDirections = new bool[CreatureAnimationLabels.DirectionCount];
            bool[] seenFacings = new bool[CreatureAnimationLabels.DirectionCount];
            foreach (CreatureAnimationDirectionMapping entry in directionMap)
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

        public HexFacing GetFacing(CreatureAnimationDirection direction)
        {
            if (directionMap != null)
            {
                foreach (CreatureAnimationDirectionMapping entry in directionMap)
                {
                    if (entry != null && entry.Direction == direction)
                        return entry.Facing;
                }
            }
            foreach (CreatureAnimationDirectionMapping entry in DefaultDirectionMap())
            {
                if (entry.Direction == direction)
                    return entry.Facing;
            }
            return HexFacing.SouthEast;
        }

        public CreatureAnimationDirection GetDirection(HexFacing facing)
        {
            if (IsDirectionMapValid())
            {
                foreach (CreatureAnimationDirectionMapping entry in directionMap)
                {
                    if (entry.Facing == facing)
                        return entry.Direction;
                }
            }
            foreach (CreatureAnimationDirectionMapping entry in DefaultDirectionMap())
            {
                if (entry.Facing == facing)
                    return entry.Direction;
            }
            return CreatureAnimationDirection.Front;
        }

        // Меняет направление ракурса; прежний владелец направления получает
        // освободившееся, чтобы таблица оставалась взаимно однозначной.
        public void AssignFacing(CreatureAnimationDirection direction, HexFacing facing)
        {
            if (!IsDirectionMapValid())
                ResetDirectionMap();
            CreatureAnimationDirectionMapping target = null;
            CreatureAnimationDirectionMapping owner = null;
            foreach (CreatureAnimationDirectionMapping entry in directionMap)
            {
                if (entry.Direction == direction)
                    target = entry;
                if (entry.Facing == facing)
                    owner = entry;
            }
            if (target == null || owner == null || target == owner)
                return;
            HexFacing previous = target.Facing;
            target.SetFacing(facing);
            owner.SetFacing(previous);
        }

        public CreatureAnimationSetData FindSet(string setId)
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

        public CreatureAnimationSetData AddSet(string setId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(setId))
                throw new ArgumentException("ID набора анимаций не может быть пустым.", nameof(setId));
            if (FindSet(setId) != null)
                throw new InvalidOperationException("Набор анимаций " + setId + " уже есть.");
            if (sets == null)
                sets = new List<CreatureAnimationSetData>();
            CreatureAnimationSetData set = new CreatureAnimationSetData(setId, displayName);
            sets.Add(set);
            return set;
        }

        public bool RemoveSet(string setId)
        {
            return sets != null && sets.RemoveAll(set => set == null || string.Equals(set.Id, setId, StringComparison.Ordinal)) > 0;
        }

        public string MakeUniqueSetId(string seed)
        {
            string baseId = string.IsNullOrWhiteSpace(seed) ? "set" : seed.Trim();
            string candidate = baseId;
            int suffix = 2;
            while (FindSet(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }
    }
}
