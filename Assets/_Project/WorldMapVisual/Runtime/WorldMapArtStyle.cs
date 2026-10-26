using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Канон v1.58 §9.9: как выглядит сгенерированная карта. Только облик —
    // разметка, проходимость и расстановка мест от этих настроек не зависят.
    // Каждый слой рисуется рисунком художника, если он назначен (подложка,
    // узор местности, деревья, домики, знаки мест), иначе простым рисунком
    // кодом с чёрным контуром — стиль канона v1.56; так карта собирается и
    // без готового арта. Все значения — рабочие настройки, не канон.
    [System.Serializable]
    public sealed class WorldMapArtStyle
    {
        [Header("Качество")]
        [Tooltip("Пикселей рисунка на пиксель полотна карты. 0,5 — половинное разрешение: быстрее и легче.")]
        [Range(0.1f, 1f)] public float Resolution = 0.5f;

        // Выключатели слоёв: любой слой и любой пул рисунков можно убрать
        // с карты, не теряя его настроек. Выключенная подложка — ровный
        // цвет основы; выключенный пул рисунков — знаки кодом.
        [Header("Слои")]
        public bool DrawBase = true;
        public bool UseBasePattern = true;
        public bool UseTintVariation = true;
        public bool UseDetailPattern = true;
        public bool DrawTerrain = true;
        public bool UseTerrainPatterns = true;
        public bool DrawInk = true;
        public bool DrawForestOutline = true;
        public bool DrawWater = true;
        public bool DrawLakes = true;
        public bool DrawRivers = true;
        public bool DrawShoreBand = true;
        public bool DrawShoreInk = true;
        public bool UseWaterPattern = true;
        public bool DrawSparseTrees = true;
        public bool DrawReeds = true;
        public bool DrawCliffs = true;
        public bool DrawRoads = true;
        public bool DrawTrails = true;
        public bool DrawBridges = true;
        public bool DrawVillages = true;
        public bool DrawLandmarks = true;
        public bool DrawPost = true;
        public bool UseColorAdjust = true;
        public bool UsePaper = true;
        public bool UseGrain = true;
        public bool UseVignette = true;

        // Свои рисунки: общий выключатель и по пулу. Выключено — пул не
        // берётся (рисует код), но рисунки в нём остаются.
        public bool UseCustomArt = true;
        [Tooltip("Высота знака считается по нарисованной части, пустые поля вокруг рисунка не уменьшают знак.")]
        public bool TrimSpritePadding = true;
        public bool UseConiferSprites = true;
        public bool UseLeafSprites = true;
        public bool UseTreeSprites = true;
        public bool UseReedSprites = true;
        public bool UseHillSprites = true;
        public bool UseMountainSprites = true;
        public bool UseCliffSprites = true;
        public bool UseBridgeSprites = true;
        public bool UseVillageSprites = true;
        public bool UseVillageIcons = true;
        public bool UseStartSprites = true;

        // Пул с учётом выключателей: null — рисунков не брать.
        public List<Sprite> Pool(List<Sprite> pool, bool use) => UseCustomArt && use ? pool : null;

        [Header("Подложка")]
        public Color BaseColor = new Color(0.60f, 0.58f, 0.46f, 1f);
        [Tooltip("Бесшовный узор, который размножается на всю карту.")]
        public Texture2D BasePattern;
        [Tooltip("Размер одной плитки узора в клетках карты.")]
        [Range(0.5f, 60f)] public float BasePatternTileHexes = 8f;
        [Range(0f, 1f)] public float BasePatternStrength = 1f;
        public Color BasePatternTint = Color.white;
        [Tooltip("Крупные пятна светлее/темнее — прячут повтор узора.")]
        [Range(0f, 0.6f)] public float TintVariation = 0.12f;
        [Range(2f, 120f)] public float TintVariationScaleHexes = 16f;
        [Tooltip("Второй, мелкий узор поверх (трава, зерно) — необязателен.")]
        public Texture2D DetailPattern;
        [Range(0.2f, 30f)] public float DetailPatternTileHexes = 2f;
        [Range(0f, 1f)] public float DetailPatternStrength = 0.35f;

        [Header("Местность")]
        public List<WorldMapTerrainArt> Terrain = new List<WorldMapTerrainArt>();
        [Tooltip("Неровность границ местности, в клетках: 0 — видны шестиугольники.")]
        [Range(0f, 1.5f)] public float EdgeRoughness = 0.55f;
        [Tooltip("Мягкость переходов между местностями, в клетках.")]
        [Range(0f, 1f)] public float EdgeSoftness = 0.18f;

        [Header("Тушь")]
        public Color InkColor = new Color(0.07f, 0.06f, 0.05f, 1f);
        [Tooltip("Толщина контура в пикселях полотна (до учёта качества).")]
        [Range(0f, 12f)] public float InkWidth = 3f;
        [Range(0f, 1f)] public float ForestOutline = 0.55f;

        [Header("Вода")]
        public Color WaterShallowColor = new Color(0.36f, 0.47f, 0.47f, 1f);
        public Color WaterDeepColor = new Color(0.20f, 0.30f, 0.33f, 1f);
        [Tooltip("Узор воды (рябь) — необязателен.")]
        public Texture2D WaterPattern;
        [Range(0.5f, 30f)] public float WaterPatternTileHexes = 4f;
        [Range(0f, 1f)] public float WaterPatternStrength = 0.35f;
        [Tooltip("Множитель ширины рек на рисунке.")]
        [Range(0.2f, 4f)] public float RiverWidthScale = 1f;
        [Tooltip("Светлая отмель вдоль берега, в клетках.")]
        [Range(0f, 1f)] public float ShoreBand = 0.25f;

        [Header("Лес")]
        public bool DrawTrees = true;
        [Tooltip("Общие рисунки деревьев — если для елей или лиственных своих нет.")]
        public List<Sprite> TreeSprites = new List<Sprite>();
        [Tooltip("Ели и сосны.")]
        public List<Sprite> ConiferSprites = new List<Sprite>();
        [Tooltip("Лиственные деревья.")]
        public List<Sprite> LeafSprites = new List<Sprite>();
        [Tooltip("Камыш и кочки на болоте.")]
        public List<Sprite> ReedSprites = new List<Sprite>();
        [Range(0f, 12f)] public float TreesPerHex = 4f;
        [Range(0.1f, 3f)] public float TreeSizeHexes = 0.55f;
        [Range(0f, 1f)] public float TreeSizeJitter = 0.3f;
        [Tooltip("Доля елей среди деревьев, нарисованных кодом.")]
        [Range(0f, 1f)] public float ConiferShare = 0.6f;
        public Color ConiferColor = new Color(0.17f, 0.26f, 0.18f, 1f);
        public Color LeafColor = new Color(0.27f, 0.35f, 0.20f, 1f);
        [Tooltip("Деревца на болоте и редкие деревья в поле.")]
        [Range(0f, 2f)] public float SparseTreesPerHex = 0.35f;
        [Tooltip("Камыш и кочки на болоте: сколько на клетку.")]
        [Range(0f, 6f)] public float ReedsPerHex = 1.4f;
        [Tooltip("Высота камыша в клетках (рисунком; кодом — 0,7 от неё).")]
        [Range(0.1f, 2f)] public float ReedSizeHexes = 0.4f;

        [Header("Холмы и горы")]
        public bool DrawHills = true;
        public List<Sprite> HillSprites = new List<Sprite>();
        [Range(0f, 4f)] public float HillsPerHex = 0.7f;
        [Range(0.2f, 4f)] public float HillSizeHexes = 0.9f;
        public Color HillColor = new Color(0.55f, 0.50f, 0.36f, 1f);
        public bool DrawMountains = true;
        public List<Sprite> MountainSprites = new List<Sprite>();
        [Tooltip("Скалы; пусто — рисунки гор.")]
        public List<Sprite> CliffSprites = new List<Sprite>();
        [Range(0f, 4f)] public float MountainsPerHex = 0.9f;
        [Range(0.2f, 5f)] public float MountainSizeHexes = 1.3f;
        public Color MountainLightColor = new Color(0.66f, 0.63f, 0.58f, 1f);
        public Color MountainShadowColor = new Color(0.36f, 0.34f, 0.33f, 1f);

        [Header("Дороги")]
        public Color RoadColor = new Color(0.72f, 0.62f, 0.44f, 1f);
        [Range(0.02f, 1f)] public float RoadWidthHexes = 0.14f;
        [Range(0f, 1f)] public float RoadInk = 0.6f;
        public bool RoadDashed;
        public Color TrailColor = new Color(0.45f, 0.38f, 0.27f, 1f);
        [Range(0.02f, 1f)] public float TrailWidthHexes = 0.07f;
        public bool TrailDashed = true;
        public Color BridgeColor = new Color(0.42f, 0.30f, 0.20f, 1f);
        [Tooltip("Рисунки мостов (до 20); ставятся как есть, без поворота вдоль дороги.")]
        public List<Sprite> BridgeSprites = new List<Sprite>();
        [HideInInspector] public Sprite BridgeSprite;

        [Header("Деревни")]
        public List<Sprite> VillageSprites = new List<Sprite>();
        [Range(0.2f, 4f)] public float VillageSizeHexes = 1.8f;
        public Color WallColor = new Color(0.55f, 0.45f, 0.33f, 1f);
        public Color RoofColor = new Color(0.36f, 0.27f, 0.19f, 1f);
        [Tooltip("Рисовать точку старта отряда отдельным знаком.")]
        public bool MarkStart = true;
        [Tooltip("Рисунки стоянки отряда вместо частокола (до 20).")]
        public List<Sprite> StartSprites = new List<Sprite>();
        [Tooltip("Иконки метки деревни на карте игры (до 20).")]
        public List<Sprite> VillageIcons = new List<Sprite>();
        [HideInInspector] public Sprite StartSprite;
        [HideInInspector] public Sprite VillageIcon;

        [Header("Приметные места")]
        public List<WorldMapLandmarkArt> Landmarks = new List<WorldMapLandmarkArt>();
        [Range(0.2f, 4f)] public float LandmarkSizeHexes = 1.4f;
        public Color LandmarkColor = new Color(0.50f, 0.47f, 0.42f, 1f);

        [Header("Обработка")]
        [Range(-0.5f, 0.5f)] public float Brightness;
        [Range(0.3f, 2f)] public float Contrast = 1.05f;
        [Range(0f, 2f)] public float Saturation = 0.85f;
        [Tooltip("Теплее (+) или холоднее (−).")]
        [Range(-1f, 1f)] public float Warmth = 0.15f;
        [Range(0f, 1f)] public float Vignette = 0.35f;
        [Range(0f, 0.3f)] public float Grain = 0.04f;
        [Tooltip("Бумага или холст поверх всей карты (умножение).")]
        public Texture2D PaperTexture;
        [Range(1f, 120f)] public float PaperTileHexes = 20f;
        [Range(0f, 1f)] public float PaperStrength = 0.5f;

        public WorldMapTerrainArt FindTerrain(WorldMapGameplayTerrainType type)
        {
            if (Terrain == null)
                return null;
            foreach (WorldMapTerrainArt entry in Terrain)
            {
                if (entry != null && entry.Terrain == type)
                    return entry;
            }
            return null;
        }

        public WorldMapLandmarkArt FindLandmark(string ruleId)
        {
            if (Landmarks == null || string.IsNullOrEmpty(ruleId))
                return null;
            foreach (WorldMapLandmarkArt entry in Landmarks)
            {
                if (entry != null && entry.RuleId == ruleId)
                    return entry;
            }
            return null;
        }

        // Идемпотентно: у каждого типа местности есть строка облика.
        public bool EnsureTerrainEntries()
        {
            if (Terrain == null)
                Terrain = new List<WorldMapTerrainArt>();
            bool changed = false;
            foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
            {
                if (FindTerrain(type) != null)
                    continue;
                Terrain.Add(WorldMapTerrainArt.CreateDefault(type));
                changed = true;
            }
            Terrain.Sort((a, b) => System.Array.IndexOf(WorldMapTerrainLabels.All, a.Terrain)
                .CompareTo(System.Array.IndexOf(WorldMapTerrainLabels.All, b.Terrain)));
            return changed;
        }

        // У каждого объекта карты — пул до 20 рисунков; из пула каждому
        // дереву, горе, мосту, деревне или месту достаётся свой рисунок.
        public const int MaxPoolSize = 20;

        // Прежние одиночные рисунки (мост, стоянка, иконка деревни, знак и
        // иконка места) переходят в пулы; пулы обрезаются до 20.
        public bool NormalizePools()
        {
            bool changed = false;
            changed |= Absorb(ref BridgeSprite, ref BridgeSprites);
            changed |= Absorb(ref StartSprite, ref StartSprites);
            changed |= Absorb(ref VillageIcon, ref VillageIcons);
            foreach (List<Sprite> pool in new[]
                     {
                         TreeSprites, ConiferSprites, LeafSprites, ReedSprites, HillSprites, MountainSprites,
                         CliffSprites, VillageSprites, BridgeSprites, StartSprites, VillageIcons
                     })
            {
                changed |= Trim(pool);
            }
            if (Landmarks != null)
            {
                foreach (WorldMapLandmarkArt landmark in Landmarks)
                    changed |= landmark != null && landmark.NormalizePools();
            }
            return changed;
        }

        internal static bool Absorb(ref Sprite single, ref List<Sprite> pool)
        {
            pool ??= new List<Sprite>();
            if (single == null)
                return false;
            if (!pool.Contains(single))
                pool.Insert(0, single);
            single = null;
            return true;
        }

        internal static bool Trim(List<Sprite> pool)
        {
            if (pool == null || pool.Count <= MaxPoolSize)
                return false;
            pool.RemoveRange(MaxPoolSize, pool.Count - MaxPoolSize);
            return true;
        }

        // Устойчивый случайный выбор по ключу (ID места): одно и то же
        // место после загрузки получает ту же иконку. Пустые строки пула
        // не считаются.
        public static Sprite PickStable(List<Sprite> pool, string key)
        {
            if (pool == null)
                return null;
            List<Sprite> valid = pool.Where(sprite => sprite != null).ToList();
            return valid.Count == 0 ? null : valid[(int)(StableHash(key) % (uint)valid.Count)];
        }

        public static uint StableHash(string key)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in key ?? string.Empty)
                    hash = (hash ^ c) * 16777619;
                return hash;
            }
        }

        public WorldMapArtStyle Clone()
        {
            WorldMapArtStyle copy = (WorldMapArtStyle)MemberwiseClone();
            copy.Terrain = new List<WorldMapTerrainArt>();
            if (Terrain != null)
            {
                foreach (WorldMapTerrainArt entry in Terrain)
                {
                    if (entry != null)
                        copy.Terrain.Add(entry.Clone());
                }
            }
            copy.Landmarks = new List<WorldMapLandmarkArt>();
            if (Landmarks != null)
            {
                foreach (WorldMapLandmarkArt entry in Landmarks)
                {
                    if (entry != null)
                        copy.Landmarks.Add(entry.Clone());
                }
            }
            copy.TreeSprites = new List<Sprite>(TreeSprites ?? new List<Sprite>());
            copy.HillSprites = new List<Sprite>(HillSprites ?? new List<Sprite>());
            copy.MountainSprites = new List<Sprite>(MountainSprites ?? new List<Sprite>());
            copy.VillageSprites = new List<Sprite>(VillageSprites ?? new List<Sprite>());
            copy.ConiferSprites = new List<Sprite>(ConiferSprites ?? new List<Sprite>());
            copy.LeafSprites = new List<Sprite>(LeafSprites ?? new List<Sprite>());
            copy.ReedSprites = new List<Sprite>(ReedSprites ?? new List<Sprite>());
            copy.CliffSprites = new List<Sprite>(CliffSprites ?? new List<Sprite>());
            copy.BridgeSprites = new List<Sprite>(BridgeSprites ?? new List<Sprite>());
            copy.StartSprites = new List<Sprite>(StartSprites ?? new List<Sprite>());
            copy.VillageIcons = new List<Sprite>(VillageIcons ?? new List<Sprite>());
            return copy;
        }

        public static WorldMapArtStyle CreateDefault()
        {
            WorldMapArtStyle style = new WorldMapArtStyle();
            style.EnsureTerrainEntries();
            return style;
        }
    }

    // Облик одного типа местности: подкраска поверх подложки и свой узор.
    [System.Serializable]
    public sealed class WorldMapTerrainArt
    {
        public WorldMapGameplayTerrainType Terrain;
        public bool Enabled = true;
        public Color Color = Color.white;
        [Range(0f, 1f)] public float Opacity = 0.6f;
        public Texture2D Pattern;
        [Range(0.2f, 60f)] public float PatternTileHexes = 3f;
        [Range(0f, 1f)] public float PatternStrength = 0.6f;

        public WorldMapTerrainArt Clone() => (WorldMapTerrainArt)MemberwiseClone();

        public static WorldMapTerrainArt CreateDefault(WorldMapGameplayTerrainType type)
        {
            WorldMapTerrainArt art = new WorldMapTerrainArt { Terrain = type };
            switch (type)
            {
                case WorldMapGameplayTerrainType.OpenGround:
                    art.Color = new Color(0.62f, 0.62f, 0.46f, 1f); art.Opacity = 0.25f; break;
                case WorldMapGameplayTerrainType.Road:
                case WorldMapGameplayTerrainType.Trail:
                    art.Color = new Color(0.62f, 0.62f, 0.46f, 1f); art.Opacity = 0.25f; break;
                case WorldMapGameplayTerrainType.Field:
                    art.Color = new Color(0.78f, 0.70f, 0.43f, 1f); art.Opacity = 0.7f; break;
                case WorldMapGameplayTerrainType.Forest:
                    art.Color = new Color(0.27f, 0.34f, 0.22f, 1f); art.Opacity = 0.85f; break;
                case WorldMapGameplayTerrainType.Swamp:
                    art.Color = new Color(0.35f, 0.40f, 0.31f, 1f); art.Opacity = 0.85f; break;
                case WorldMapGameplayTerrainType.Hills:
                    art.Color = new Color(0.60f, 0.54f, 0.39f, 1f); art.Opacity = 0.55f; break;
                case WorldMapGameplayTerrainType.Mountains:
                    art.Color = new Color(0.52f, 0.50f, 0.47f, 1f); art.Opacity = 0.8f; break;
                case WorldMapGameplayTerrainType.Water:
                    art.Color = new Color(0.30f, 0.40f, 0.42f, 1f); art.Opacity = 1f; break;
                case WorldMapGameplayTerrainType.Cliffs:
                    art.Color = new Color(0.33f, 0.31f, 0.31f, 1f); art.Opacity = 0.9f; break;
            }
            return art;
        }
    }

    // Знак приметного места на карте: рисунок художника или знак кодом.
    [System.Serializable]
    public sealed class WorldMapLandmarkArt
    {
        public string RuleId = string.Empty;
        [Tooltip("Рисовать знак этого места на карте.")]
        public bool Enabled = true;
        [Tooltip("Брать знаки из пула; выключено — знак кодом.")]
        public bool UseSprites = true;
        [Tooltip("Брать иконки метки из пула; выключено — метка без иконки.")]
        public bool UseIcons = true;
        [Tooltip("Знаки на рисунке карты (до 20) — у каждого места выбирается свой.")]
        public List<Sprite> Variants = new List<Sprite>();
        [HideInInspector] public Sprite Sprite;
        [Tooltip("Иконки метки места на карте игры (до 20) — кнопка, по которой идут к месту.")]
        public List<Sprite> Icons = new List<Sprite>();
        [HideInInspector] public Sprite Icon;
        [Range(0.2f, 4f)] public float SizeHexes = 1f;
        public Color Tint = Color.white;

        // Знак для места с устойчивым случайным числом variant.
        public Sprite PickSprite(int variant)
        {
            int extra = Variants != null ? Variants.Count(sprite => sprite != null) : 0;
            int total = (Sprite != null ? 1 : 0) + extra;
            if (total == 0)
                return null;
            int index = Math.Abs(variant) % total;
            if (Sprite != null)
            {
                if (index == 0)
                    return Sprite;
                index--;
            }
            return Variants.Where(sprite => sprite != null).ElementAt(index);
        }

        public Sprite PickIcon(string key) => WorldMapArtStyle.PickStable(Icons, key);

        public bool NormalizePools()
        {
            bool changed = WorldMapArtStyle.Absorb(ref Sprite, ref Variants);
            changed |= WorldMapArtStyle.Absorb(ref Icon, ref Icons);
            changed |= WorldMapArtStyle.Trim(Variants);
            changed |= WorldMapArtStyle.Trim(Icons);
            return changed;
        }

        public WorldMapLandmarkArt Clone()
        {
            WorldMapLandmarkArt copy = (WorldMapLandmarkArt)MemberwiseClone();
            copy.Variants = new List<Sprite>(Variants ?? new List<Sprite>());
            copy.Icons = new List<Sprite>(Icons ?? new List<Sprite>());
            return copy;
        }
    }
}
