using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // 20.11.2026, просьба пользователя: «Генератор арен» вместо собранных
    // вручную полей боя Базы локаций. Один ассет — все общие настройки
    // генерации обычной арены (дорожный бой, полигон), их запоминает игра:
    //   Tactics — клетки боя (доли, объём холмов/растительности/основания/
    //             преград, река) — ядро боя, SandboxArenaGenerator;
    //   Look    — рисунок арены MapLab (деревья, трава, пни, бурьян, камни,
    //             рельеф, масштаб, мазки травы MapLab) — MapLabArena;
    //   Frame   — обработка кадра арены: общая и своя у каждого вида карты.
    // Значения по умолчанию дают прежние арены (те же seed — те же клетки и
    // тот же декор). Правится в окне «Kingdom Survival → Генератор арен».
    public sealed class ArenaGeneratorAsset : ScriptableObject
    {
        public const string ResourcesPath = "ArenaGenerator/KingdomSurvivalArenaGenerator";
        public const string AssetPath = "Assets/_Project/MapLabBridge/Resources/ArenaGenerator/KingdomSurvivalArenaGenerator.asset";

        public SandboxArenaGenerationSettings Tactics = new SandboxArenaGenerationSettings();
        public MapLabArenaLook Look = new MapLabArenaLook();
        public ArenaFrameSettings Frame = new ArenaFrameSettings();

        // «Стандартная тема» — Tactics и Look выше: общие настройки, которыми
        // пользуются все темы без своих. У темы могут быть свои (клетки боя и
        // рисунок арены); обработка кадра общая.
        public List<ArenaThemeProfile> ThemeProfiles = new List<ArenaThemeProfile>();

        // Только окно генератора: показ «Стандартной» темы — настройки по темам
        // временно не применяются (предпросмотр общих). Не сохраняется.
        public static bool PreviewStandard;

        private static ArenaGeneratorAsset cached;

        // Номер правки настроек (окно генератора): идущий бой перерисовывает
        // кадр, если сменилась обработка кадра.
        public static int Version { get; private set; }

        public static void Touch() => Version++;

        // Ассет из Resources; нет его — настройки по умолчанию (в памяти).
        public static ArenaGeneratorAsset Load()
        {
            if (cached != null)
                return cached;
            cached = Resources.Load<ArenaGeneratorAsset>(ResourcesPath);
            if (cached == null)
            {
                cached = CreateInstance<ArenaGeneratorAsset>();
                cached.name = "Генератор арен (по умолчанию)";
                cached.hideFlags = HideFlags.HideAndDontSave;
            }
            cached.Activate();
            return cached;
        }

        public static MapLabArenaLook CurrentLook => Load().Look ?? new MapLabArenaLook();

        // Рисунок арены для темы: свой профиль темы, иначе стандартный.
        public static MapLabArenaLook LookFor(SandboxArenaTheme theme) =>
            Load().Profile(SandboxArenaThemes.Id(theme))?.Look ?? CurrentLook;

        public ArenaThemeProfile Profile(string themeId)
        {
            if (PreviewStandard || ThemeProfiles == null) return null;
            foreach (ArenaThemeProfile profile in ThemeProfiles)
                if (profile != null && profile.ThemeId == themeId) return profile;
            return null;
        }

        public int ProfileIndex(string themeId)
        {
            if (ThemeProfiles == null) return -1;
            for (int i = 0; i < ThemeProfiles.Count; i++)
                if (ThemeProfiles[i] != null && ThemeProfiles[i].ThemeId == themeId) return i;
            return -1;
        }

        // Общая обработка кадра арены (null — выключена).
        public static LocationPostEffects CurrentFrame()
        {
            LocationPostEffects post = Load().Frame?.Common;
            return post != null && post.Enabled ? post : null;
        }

        private void OnEnable()
        {
            Repair();
            Activate();
        }

        // Тактика клеток — общая для ядра боя (оно ассета не видит).
        public void Activate()
        {
            Repair();
            SandboxArenaGenerationSettings.Current = Tactics;
            SandboxArenaGenerationSettings.Resolver = theme => Profile(SandboxArenaThemes.Id(theme))?.Tactics;
        }

        public void Repair()
        {
            Tactics ??= new SandboxArenaGenerationSettings();
            Look ??= new MapLabArenaLook();
            Frame ??= new ArenaFrameSettings();
            Frame.Repair();
            ThemeProfiles ??= new List<ArenaThemeProfile>();
            ThemeProfiles.RemoveAll(profile => profile == null);
            foreach (ArenaThemeProfile profile in ThemeProfiles)
            {
                profile.Tactics ??= new SandboxArenaGenerationSettings();
                profile.Look ??= new MapLabArenaLook();
            }
        }
    }

    // Свои настройки одной темы: клетки боя и рисунок арены.
    [Serializable]
    public sealed class ArenaThemeProfile
    {
        public string ThemeId;
        public SandboxArenaGenerationSettings Tactics = new SandboxArenaGenerationSettings();
        public MapLabArenaLook Look = new MapLabArenaLook();
    }

    // Рисунок арены MapLab: всё, что не меняет клеток и правил боя (свой поток
    // случайности декора). Умолчания — прежние числа MapLabArena.Decor.
    [Serializable]
    public sealed class MapLabArenaLook
    {
        // Общая плотность декора (множитель числа групп, пятен травы, камней).
        public float DecorDensity = 1f;

        // Деревья: группы по краям и углам кадра.
        public float TreeGroupsMin = 9f;
        public float TreeGroupsMax = 13f;
        public int TreesPerGroupMin = 3;
        public int TreesPerGroupMax = 8;
        // Размах группы (1 — 2,6 × 1,8 гекса).
        public float TreeGroupSpread = 1f;
        // Расстояние между центрами групп, в гексах.
        public float TreeGroupGap = 3.2f;
        // Отступ деревьев от клеток поля, в гексах.
        public float TreeClearance = 1.3f;
        // Доля главной породы в группе (остальное — вторая порода).
        public float TreeMainSpecies = 0.65f;
        // Доля молодых деревьев на краю группы.
        public float TreeYoungAtEdge = 0.5f;
        // Размер деревьев (1 — по Базе ассетов).
        public float TreeScale = 1f;

        // Пни и бурьян у края групп, со стороны поля (вероятность на группу).
        public float WeedChance = 0.65f;
        public float StumpChance = 0.65f;
        // Бурьяна у края группы — до стольких растений (каждое — с вероятностью WeedChance).
        public int WeedsPerGroup = 3;
        // Куртины бурьяна (1–3 растения) по окраине кадра, мимо клеток поля, штук на арену.
        public float WeedClumps = 6f;
        public float WeedScale = 1f;

        // Низкая трава — рисунками Базы ассетов, пятнами с паузами.
        public float GrassPatchesMin = 8f;
        public float GrassPatchesMax = 13f;
        // Размер пятна в гексах.
        public float GrassPatchSizeMin = 0.6f;
        public float GrassPatchSizeMax = 1.6f;
        public int GrassPerPatchMin = 2;
        public int GrassPerPatchMax = 7;
        // Теснота: меньше — травинки ближе друг к другу (гуще).
        public float GrassSpacing = 0.7f;
        // Отступ от стартовых клеток бойцов, в гексах (0 — трава и под ними).
        public float GrassClearOfStart = 0.8f;
        // Пятна и за краем поля (на окраинах кадра).
        public float GrassOutside = 0f;
        // Густота травы на поле клеток и за ним (множители всех видов травы:
        // пятна, ковёр, россыпь). 1 и 1 — ровно, как прежде. Поляна — на поле
        // больше; «за клетками гуще» — за полем больше.
        public float GrassInField = 1f;
        public float GrassOutsideField = 1f;
        // Ширина перехода от поля к окраине, в гексах.
        public float GrassFieldEdge = 1.5f;
        public float GrassScale = 1f;
        // Доля высокой травы (пакет «Grass_*», от 0,75 м) среди травы пятен и
        // россыпи; 0 — только низкая. Нет высокой травы в Базе — не действует.
        // Высокая не ставится на клетки поля и перед ними, как бурьян.
        public float TallGrassShare = 0.15f;

        // Травяной ковёр: пучки травы карты мира (набор этапа 05, роли
        // grass_tuft и grass_ink_tuft) равномерно по полю, пучков на гекс
        // (0 — нет). Свой поток случайности: прочий декор не меняется.
        public float GrassCarpet;
        // Доля контурных (тушевых) пучков в ковре.
        public float GrassCarpetInk = 0.3f;
        public float GrassCarpetScale = 1f;
        // Неровность ковра: 0 — ровно, 1 — плешины и густые места.
        public float GrassCarpetPatchiness = 0.5f;
        // Россыпь одиночных травинок Базы ассетов по всему полю, на гекс.
        public float GrassSingles;
        // Камыш MapLab у воды (река арены), множитель; 0 — без камыша.
        public float ReedDensity;

        // Камни.
        public float SmallRocksMin = 2f;
        public float SmallRocksMax = 5f;
        public int RubbleStonesMin = 2;
        public int RubbleStonesMax = 3;
        // Непроходимая клетка: доля валунов (остальное — крупный пень).
        public float BlockBoulderShare = 0.7f;

        // Рельеф: подъём холма в радиусах гекса.
        public float HillLiftHexes = 0.5f;
        // Единиц мира MapLab на единицу мира мест (0 — из MapLabGameAssets).
        public float AssetScale;
        // Плотность покраски земли (текселей на единицу): выше — чётче, дольше сборка.
        public float PaintTexelsPerUnit = 24f;

        public MapLabArenaLook Clone() => (MapLabArenaLook)MemberwiseClone();
    }

    // Обработка кадра арены: одна общая для всех арен.
    [Serializable]
    public sealed class ArenaFrameSettings
    {
        public LocationPostEffects Common = new LocationPostEffects();

        public void Repair()
        {
            Common ??= new LocationPostEffects();
            Common.Repair();
        }
    }
}
