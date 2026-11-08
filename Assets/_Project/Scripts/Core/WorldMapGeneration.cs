using System;
using System.Collections.Generic;

// Канон v1.58 §9.9: карта «Свободной игры» собирается генератором заново в
// каждой новой партии по её WorldSeed. Здесь — только чистые данные без
// UnityEngine: настройки генератора, стадии и результат (разметка клеток,
// реки, дороги, поселения, приметные места, слоты появления). Рисунок карты
// строит отдельный рисовальщик модуля WorldMapVisual из этого результата;
// разметка и рисунок происходят из одного источника и не расходятся.
//
// Результат сохраняется вместе с партией (GameState.GeneratedMap): загрузка
// не генерирует мир заново и не зависит от того, менялся ли генератор.

// Стадии генерации — порядок сборки и шаги пошагового показа в редакторе.
// Каждая стадия берёт свой поток случайности, поэтому карта «до стадии N»
// всегда совпадает с началом полной карты того же seed.
public enum WorldMapGenerationStage
{
    Relief = 0,
    Water = 1,
    Vegetation = 2,
    Settlements = 3,
    Landmarks = 4,
    Roads = 5,
    Fields = 6,
    Complete = 7
}

public static class WorldMapGenerationStages
{
    public static readonly WorldMapGenerationStage[] All =
    {
        WorldMapGenerationStage.Relief,
        WorldMapGenerationStage.Water,
        WorldMapGenerationStage.Vegetation,
        WorldMapGenerationStage.Settlements,
        WorldMapGenerationStage.Landmarks,
        WorldMapGenerationStage.Roads,
        WorldMapGenerationStage.Fields,
        WorldMapGenerationStage.Complete
    };

    public static string Title(WorldMapGenerationStage stage)
    {
        switch (stage)
        {
            case WorldMapGenerationStage.Relief: return "Рельеф";
            case WorldMapGenerationStage.Water: return "Озёра и реки";
            case WorldMapGenerationStage.Vegetation: return "Лес и болота";
            case WorldMapGenerationStage.Settlements: return "Деревни";
            case WorldMapGenerationStage.Landmarks: return "Приметные места";
            case WorldMapGenerationStage.Roads: return "Дороги и тропы";
            case WorldMapGenerationStage.Fields: return "Поля";
            default: return "Готово";
        }
    }
}

public enum WorldMapLandmarkPlacement
{
    Terrain = 0,
    Crossroads = 1
}

// Правило приметного места (руины, шахта, курган...). Примеры по умолчанию —
// рабочие настройки, не факты мира. LocationId связывает правило с местом
// из Базы карты: слот этого места получает тег "place:<LocationId>", и
// место встаёт именно сюда (WorldMapPopulationService).
[Serializable]
public sealed class WorldMapLandmarkRule
{
    public const string PlaceTagPrefix = "place:";

    public string Id = "landmark";
    public string Name = "Приметное место";
    public bool Enabled = true;
    public int Count = 1;
    // Где можно стоять. Пусто — любая суша.
    public List<WorldMapGameplayTerrainType> Terrains = new List<WorldMapGameplayTerrainType>();
    public bool NearWater;
    public bool NearVillage;
    // Не ближе к деревням (если не NearVillage) и к другим местам.
    public float MinDistanceHexes = 8f;
    public string LocationId = string.Empty;
    // Теги слота появления через запятую, например "Ruins, Hills".
    public string SlotTags = string.Empty;
    // Ставить ли знак на рисунок карты (место может быть и без знака).
    public bool DrawMark = true;

    // Где ставить: по местности или на перекрёстке дорог (тогда место
    // выбирается после дорог, на клетке, где сходятся три дороги и больше).
    public WorldMapLandmarkPlacement Placement = WorldMapLandmarkPlacement.Terrain;

    // Место на карте игры: его находят, к нему приходят, его исследуют
    // (обычный вход в место — описание, исследование, итог). Скрытое место
    // не рисуется на карте, пока его не найдут; видимое — видно сразу.
    public bool Hidden = true;
    public string Description = string.Empty;
    public string Threat = "неизвестна";
    public double ExplorationHours = 2.0;
    public int RewardArmySupply;
    public int RewardArmyGold;

    // Откуда тип места (раздел LORE/канона) — для автора, в игру не идёт.
    public string Source = string.Empty;

    // 12У-E: рисунок места на карте MapLab (ID записи набора арта; пусто —
    // без рисунка). Рисунок не определяет ни ID, ни смысл места: его можно
    // заменить, не трогая сохранённые партии. Скрытое место не рисуется.
    public string Visual = string.Empty;
    // 12У-E: жёсткие условия соседства, словами (расширяемый перечень):
    // river — берег реки; water — берег любой воды; road — у дороги или
    // тропы; forest-edge — опушка; fields — край полей; slope — у склона или
    // уступа; high — на холме; village — у деревни; far — вдали от деревень.
    // Неизвестное слово — предупреждение автору, правило не ставится.
    public List<string> Near = new List<string>();

    public WorldMapLandmarkRule Clone()
    {
        WorldMapLandmarkRule copy = (WorldMapLandmarkRule)MemberwiseClone();
        copy.Terrains = new List<WorldMapGameplayTerrainType>(Terrains ?? new List<WorldMapGameplayTerrainType>());
        copy.Near = new List<string>(Near ?? new List<string>());
        return copy;
    }
}

// Все числа генератора — рабочие настройки Базы карты, не канон.
[Serializable]
public sealed class WorldMapGenerationSettings
{
    // Масштаб: полотно (пропорции и единица координат) и число клеток по
    // ширине — сколько «мира» помещается на карту.
    public float CanvasWidth = WorldMapHexGrid.DefaultCanvasWidth;
    public float CanvasHeight = WorldMapHexGrid.DefaultCanvasHeight;
    public int HexesAcross = WorldMapHexGrid.DefaultHexesAcross;

    // Рельеф.
    public float ReliefScaleHexes = 26f;
    public int ReliefDetail = 4;
    public float ReliefRoughness = 0.5f;
    public float MountainRidges = 0.45f;
    public float HillsShare = 0.16f;
    public float MountainsShare = 0.05f;
    public float CliffsShare = 0.2f;

    // Вода.
    public float LakesShare = 0.025f;
    public int RiverCount = 3;
    public int RiverMinLengthHexes = 14;
    public float RiverWidthHexes = 0.6f;
    public float RiverMeander = 0.5f;

    // Лес и болота.
    public float ForestShare = 0.34f;
    public float ForestScaleHexes = 12f;
    public float ForestOnHills = 0.5f;
    public float SwampShare = 0.04f;
    public float MoistureScaleHexes = 20f;

    // Деревни и поля.
    public int VillageCount = 8;
    public int VillageMinDistanceHexes = 11;
    public float VillageNearWater = 0.7f;
    public float FieldRadiusHexes = 3f;
    public float FieldDensity = 0.5f;

    // Дороги: связать деревни, добавить петли и выходы к краю карты.
    public float ExtraRoadLoops = 0.25f;
    public int RoadsToEdge = 2;
    public bool TrailsToLandmarks = true;
    public float RoadAvoidHills = 2.5f;
    public float RoadAvoidForest = 1.7f;
    public float RoadAvoidSwamp = 4f;
    public float RiverCrossingCost = 7f;
    // Насколько дороги петляют (0 — кратчайший путь по сетке).
    public float RoadWander = 1.2f;

    // Вокруг точки старта — поляна, на которую не заходят вода и горы.
    public int StartClearRadiusHexes = 2;

    public List<WorldMapLandmarkRule> Landmarks = DefaultLandmarks();

    // 12У-D: структура региона (мастер-бриф §5 №1, №8, №9). Тип региона —
    // поправки рельефа, воды, леса и освоенности с разбросом по seed;
    // насыщенность открытиями — бюджет мест относительно площади;
    // связанность — ответвления (хозяйственные тропы) ↔ обходы и петли.
    // Mixed + Normal + 0.5 на стандартной сетке дают прежний состав мест и
    // дорог (плюс хозяйственные тропы, если они включены).
    public WorldMapRegionKind RegionKind = WorldMapRegionKind.BySeed;
    public WorldMapDiscoveryRate Discoveries = WorldMapDiscoveryRate.Normal;
    public float RouteConnectivity = 0.5f;
    // Хозяйственные тропы от деревень к делянке, каменоломне, мосткам, выгону.
    public bool WorkTrails = true;

    // 12У-E: детали мест — видимые хозяйственные и памятные объекты
    // (стан углежогов, пристань, кладбище, навес у дороги...). Это не места
    // игры: у них нет исследования, награды и слота; только рисунок, площадка
    // и описание для будущей подсказки. Каталог открыт для пополнения.
    public List<WorldMapLandmarkRule> Features = DefaultFeatures();

    // 12У-E: минимальный слой фракций (пакет «Фракции на карте»): стороны —
    // данные; ControllingPolities — сколько держав делят деревни карты по
    // связным группам дорог (0 — контроль не назначается); группы мира —
    // постоянные, с маршрутами по дорогам. Рабочие предложения, не канон.
    public List<WorldMapFactionDefinition> Factions = WorldMapFactionCatalog.DefaultFactions();
    public List<WorldMapGroupRule> Groups = WorldMapFactionCatalog.DefaultGroups();
    public int ControllingPolities = 2;

    public static WorldMapGenerationSettings CreateDefault() => new WorldMapGenerationSettings();

    // Типы мест взяты из утверждённых разделов источников (поле Source):
    // LORE §8 — роща, курган, старое городище, заброшенное поселение,
    // святилище, перекрёсток; канон §28.3 — пещера, шахта, руины,
    // заброшенный двор, лесное логово; LORE (безопасные места) — охотничья
    // хижина, пасека; LORE §66.4 — лечебный родник; LORE §24 и эпоха —
    // водяная мельница. Описания — нейтральные наблюдения без истории
    // места: кто, когда и зачем — [ОТКРЫТО] и решается в LORE.md.
    public static List<WorldMapLandmarkRule> DefaultLandmarks()
    {
        List<WorldMapGameplayTerrainType> open = Terrains(WorldMapGameplayTerrainType.OpenGround);
        return new List<WorldMapLandmarkRule>
        {
            Rule("ruins", "Руины", 2, 9f, "LORE §8, канон §28.3", "Остатки стен из камня и бревна, заросшие травой.",
                Terrains(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Hills, WorldMapGameplayTerrainType.Forest),
                hidden: true, hours: 2, supply: 2, location: "ruins", tags: "Ruins"),
            Rule("mine", "Старая шахта", 1, 10f, "LORE §17.1, канон §28.6", "Тёмное устье штольни в склоне, рядом — осыпавшийся отвал.",
                Terrains(WorldMapGameplayTerrainType.Hills, WorldMapGameplayTerrainType.Mountains),
                hidden: true, hours: 3, supply: 2, location: "mine", tags: "Mine, Hills"),
            Rule("deep-forest", "Глухой лес", 1, 8f, "канон §24.2, LORE §23", "Лес стоит стеной, тропы теряются в буреломе.",
                Terrains(WorldMapGameplayTerrainType.Forest), hidden: true, hours: 2, location: "forest", tags: "Forest", mark: false),
            Rule("cave", "Пещера", 2, 8f, "канон §28.3, LORE: убежище", "Тёмный вход в склоне. Изнутри тянет холодом и сыростью.",
                Terrains(WorldMapGameplayTerrainType.Hills, WorldMapGameplayTerrainType.Mountains),
                hidden: true, hours: 3, supply: 2, tags: "Cave, Hills"),
            Rule("grove", "Роща", 2, 7f, "LORE §8, §17", "Старые деревья стоят тесным кругом, подлеска почти нет.",
                Terrains(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Forest),
                hidden: false, hours: 1, tags: "Grove"),
            Rule("barrow", "Курган", 2, 7f, "LORE §8, §17.1, §28", "Высокий насыпной холм, поросший травой.",
                Terrains(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Hills),
                hidden: false, hours: 1, tags: "Barrow"),
            Rule("hillfort", "Старое городище", 1, 9f, "LORE §8, §17", "Оплывшие валы на холме, по гребню — следы частокола.",
                Terrains(WorldMapGameplayTerrainType.Hills), hidden: false, hours: 3, gold: 10, tags: "Hillfort, Hills"),
            Rule("abandoned-village", "Заброшенное поселение", 1, 9f, "LORE §8, §16", "Пустые срубы с провалившимися крышами, дворы заросли бурьяном.",
                Terrains(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Forest),
                hidden: false, hours: 3, supply: 3, tags: "AbandonedVillage"),
            Rule("abandoned-yard", "Заброшенный двор", 1, 7f, "канон §28.3", "Одинокий двор: покосившийся плетень, изба без двери.",
                Terrains(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Forest),
                hidden: true, hours: 2, supply: 2, tags: "AbandonedYard"),
            Rule("sanctuary", "Старое святилище", 1, 9f, "LORE §8, §16", "Круг из врытых камней и потемневших столбов.",
                Terrains(WorldMapGameplayTerrainType.Forest, WorldMapGameplayTerrainType.Hills, WorldMapGameplayTerrainType.OpenGround),
                hidden: true, hours: 2, tags: "Sanctuary"),
            Rule("crossroads", "Перекрёсток", 2, 6f, "LORE §8, §29", "Здесь сходятся дороги. У обочины — старый столб.",
                open, hidden: false, hours: 0, tags: "Crossroads, Road", placement: WorldMapLandmarkPlacement.Crossroads),
            Rule("lair", "Лесное логово", 2, 9f, "канон §28.3, BESTIARY §10", "Под вывороченными корнями — нора, вокруг обглоданные кости.",
                Terrains(WorldMapGameplayTerrainType.Forest), hidden: true, hours: 1, threat: "высокая", tags: "Lair, Forest"),
            Rule("spring", "Родник", 1, 6f, "LORE §66.4", "Из-под камня бьёт холодная вода, вокруг выложены камни.",
                Terrains(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Forest, WorldMapGameplayTerrainType.Hills),
                hidden: true, hours: 1, tags: "Spring"),
            Rule("hunters-hut", "Охотничья хижина", 1, 7f, "LORE: безопасное место как знание региона", "Низкая избушка под елями, у стены — колода для разделки.",
                Terrains(WorldMapGameplayTerrainType.Forest), hidden: true, hours: 1, supply: 1, tags: "Hut, Forest"),
            Rule("apiary", "Пасека", 1, 4f, "LORE: безопасное место как знание региона", "Колоды-ульи на поляне у леса, гудят пчёлы.",
                Terrains(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Forest),
                hidden: true, hours: 1, supply: 1, tags: "Apiary"),
            new WorldMapLandmarkRule
            {
                Id = "watermill", Name = "Водяная мельница", Count = 1, MinDistanceHexes = 3f, SlotTags = "Mill, Shore",
                NearWater = true, NearVillage = true, Hidden = false, ExplorationHours = 0,
                Description = "Колесо на протоке, сруб под навесом, скрипит вал.", Source = "LORE §24, канон §2.2.1",
                Visual = "Watermill_A_FrontLeft", Near = new List<string> { "river" }
            }
        };
    }

    private static List<WorldMapGameplayTerrainType> Terrains(params WorldMapGameplayTerrainType[] types) =>
        new List<WorldMapGameplayTerrainType>(types);

    private static WorldMapLandmarkRule Rule(
        string id, string name, int count, float minDistance, string source, string description,
        List<WorldMapGameplayTerrainType> terrains, bool hidden, double hours, int supply = 0, int gold = 0,
        string threat = "неизвестна", string location = "", string tags = "", bool mark = true,
        WorldMapLandmarkPlacement placement = WorldMapLandmarkPlacement.Terrain)
    {
        return new WorldMapLandmarkRule
        {
            Id = id, Name = name, Count = count, MinDistanceHexes = minDistance, Source = source, Description = description,
            Terrains = terrains, Hidden = hidden, ExplorationHours = hours, RewardArmySupply = supply, RewardArmyGold = gold,
            Threat = threat, LocationId = location, SlotTags = tags, DrawMark = mark, Placement = placement
        };
    }

    public WorldMapGenerationSettings Clone()
    {
        WorldMapGenerationSettings copy = (WorldMapGenerationSettings)MemberwiseClone();
        copy.Landmarks = new List<WorldMapLandmarkRule>();
        if (Landmarks != null)
        {
            foreach (WorldMapLandmarkRule rule in Landmarks)
            {
                if (rule != null)
                    copy.Landmarks.Add(rule.Clone());
            }
        }
        copy.Factions = new List<WorldMapFactionDefinition>();
        if (Factions != null)
            foreach (WorldMapFactionDefinition f in Factions)
                if (f != null)
                    copy.Factions.Add(f.Clone());
        copy.Groups = new List<WorldMapGroupRule>();
        if (Groups != null)
            foreach (WorldMapGroupRule g in Groups)
                if (g != null)
                    copy.Groups.Add(g.Clone());
        copy.Features = new List<WorldMapLandmarkRule>();
        if (Features != null)
        {
            foreach (WorldMapLandmarkRule rule in Features)
            {
                if (rule != null)
                    copy.Features.Add(rule.Clone());
            }
        }
        return copy;
    }

    // Выбор автора 05.11.2026 (пакет «Выбранные объекты для MapLab», id
    // ks-poi-*): рабочие детали эпохи, не факты мира. Описание — нейтральное
    // наблюдение; кто здесь работает и что случилось — не утверждено.
    public static List<WorldMapLandmarkRule> DefaultFeatures()
    {
        WorldMapLandmarkRule F(string id, string name, string visual, string source, string description, int count, params string[] near) =>
            new WorldMapLandmarkRule
            {
                Id = id, Name = name, Visual = visual, Source = source, Description = description, Count = count,
                Near = new List<string>(near), MinDistanceHexes = 3f, Hidden = false, DrawMark = false, ExplorationHours = 0
            };
        return new List<WorldMapLandmarkRule>
        {
            F("charcoal-camp", "Стан углежогов", "Charcoal_Mound_A", "выбор автора, ks-poi-008", "Дымится куча, обложенная дёрном; рядом шалаш и поленница.", 1, "forest-edge"),
            F("tar-kiln", "Смолокурня", "Tar_Kiln_A", "выбор автора, ks-poi-009", "Яма с глиняным сводом, по жёлобу натекла чёрная смола.", 1, "forest-edge", "road"),
            F("fishing-dock", "Рыбацкая пристань", "Fishing_Dock_A", "выбор автора, ks-poi-010", "Мостки на сваях, на жердях сохнут сети.", 1, "water", "village"),
            F("fishing-nets", "Сети у реки", "Fishing_Nets_A", "выбор автора, сети возле реки", "На кольях вдоль берега растянуты сети.", 2, "river", "village"),
            F("road-shelter", "Дорожный навес", "Road_Shelter_A", "выбор автора, ks-poi-014", "Навес на столбах у обочины, под ним — лавка и кострище.", 1, "road", "far"),
            F("broken-cart", "Сломанная телега", "Broken_Cart_A", "выбор автора, ks-poi-025", "На обочине телега без колеса, оглобли в траве.", 1, "road"),
            // Составные места: «ID*N» — N отдельных рисунков, «+» — ещё часть
            // того же места (одно место, один ID; части — только рисунок).
            F("cemetery", "Кладбище", "Grave_Marker_A*7", "выбор автора, кладбище без ограды", "За полем — несколько могил с деревянными голбцами.", 1, "fields", "village"),
            F("lone-grave", "Одинокая могила", "Grave_Marker_A", "выбор автора, одинокая могила", "У тропы — одна могила под потемневшим голбцом.", 1, "road", "far"),
            F("offering-tree", "Дерево с дарами", "Offering_Tree_A", "выбор автора, ks-poi-031", "На ветвях старого дерева висят лоскуты и мелкие подношения.", 1, "forest-edge"),
            F("marked-stone", "Камень с отметинами", "Marked_Stone_A", "выбор автора, ks-poi-032", "У дороги валун, на нём выбиты знаки, затёртые временем.", 1, "road"),
            F("watch-post", "Покинутый дозорный пост", "Watch_Tower_A", "выбор автора, ks-poi-132", "Пустая вышка над дорогой, лестница без двух ступеней.", 1, "high", "road"),
            F("wild-hives", "Бортная делянка", "Log_Beehive_A", "выбор автора, ks-poi-092", "На опушке колоды-борти, над ними гудят пчёлы.", 1, "forest-edge", "village"),
            F("pottery-kiln", "Гончарный обжиг", "Pottery_Kiln_A", "выбор автора, ks-poi-082", "Глиняная печь на краю деревни, рядом черепки.", 1, "village"),
            F("hide-rack", "Кожевенный двор", "Hide_Drying_Rack_A", "выбор автора, ks-poi-081", "У воды на рамах сохнут шкуры, пахнет дубильной корой.", 1, "water", "village"),
            F("archery-range", "Стрельбище", "Archery_Target_A", "выбор автора, ks-poi-115", "За деревней соломенная мишень, утыканная следами стрел.", 1, "village"),
            F("flax-retting", "Льняная мочка", "Flax_Bundles_A", "выбор автора, ks-poi-080", "В тихой заводи мокнут снопы льна, придавленные жердями.", 1, "water", "village"),
            F("fish-drying", "Сушильня рыбы", "Fish_Drying_Rack_A", "выбор автора, ks-poi-086", "На вешалах вялится рыба, вокруг вьются осы.", 1, "water", "village"),
            F("old-felling", "Старая вырубка", "Tree_Stump_A", "выбор автора, ks-poi-175", "Пни затянуло мхом, между ними поднялся молодняк.", 1, "forest-edge"),
            // Пещера ks-poi-048 — скрытое место «cave» (рисунок — после обнаружения);
            // видимая деталь — сухой грот под скалой у тропы.
            F("dry-grotto", "Сухой грот у тропы", "Cave_Entrance_A+Map05_Rock_Angular_B", "выбор автора, ks-poi-094", "Под скалой у тропы — сухой неглубокий грот, у входа натоптано.", 1, "cliff", "road"),
            F("landslide", "Оползень у дороги", "Fallen_Tree_A+Map05_Rock_Round_A*2+Map05_Rock_Angular_B+Map05_Rock_Pebbles_C", "выбор автора, ks-poi-066", "Со склона сошла земля: камни и поваленное дерево у обочины.", 1, "cliff", "road"),
            F("beaver-dam", "Бобровая запруда", "Beaver_Dam_Module_A", "выбор автора, ks-poi-186", "Поперёк ручья — плотина из обглоданных жердей, выше неё вода стоит заводью.", 1, "on-river"),
            // 12У-H: остальные рисунки пакета, которые встают деталью места.
            // Описание — что видно со стороны; кто здесь и что случилось — не
            // утверждено (содержание — CONTENT_TODO пакета).
            F("market-glade", "Торговая поляна", "Market_Stall_A+Camp_Tent_A", "выбор автора, ks-poi-016", "У развилки утоптана поляна: прилавок под навесом, рядом шатёр.", 1, "road", "village"),
            F("salt-works", "Солеварный двор", "Salt_Pan_Kiln_A", "выбор автора, ks-poi-079", "Над печью с плоской сковородой поднимается пар, у стены — мешки.", 1, "water", "village"),
            F("weaving-shed", "Ткацкий навес", "Road_Shelter_A+Weaving_Loom_A", "выбор автора, ks-poi-089", "Под навесом стоит стан с недотканым полотном.", 1, "village"),
            F("carpenter-yard", "Плотницкая площадка", "Carpenter_Bench_A+Tree_Stump_A", "выбор автора, ks-poi-084", "Верстак, щепа по колено, тёсаные брёвна сложены в клеть.", 1, "village"),
            F("broken-boat", "Разбитая лодка", "Broken_Boat_A", "выбор автора, ks-poi-029", "На отмели лежит лодка с проломленным бортом.", 1, "river"),
            F("forked-boulder", "Раздвоенный валун", "Forked_Boulder_A", "выбор автора, ks-poi-199", "У дороги — валун, расколотый надвое; в трещине растёт берёзка.", 1, "road", "far"),
            F("toy-boats", "Берег с корабликами", "Wooden_Toy_Boat_A", "выбор автора, ks-poi-212", "У воды на камнях сохнут деревянные кораблики.", 1, "water", "village"),
            F("boundary-post", "Пограничный знак", "Boundary_Post_A", "выбор автора, ks-poi-021", "У дороги вкопан столб с зарубками и выжженным знаком.", 1, "road", "far"),
            F("collapsed-cellar", "Обвалившийся погреб", "Cellar_Entrance_A", "выбор автора, ks-poi-154", "Из травы торчит провалившийся вход в погреб.", 1, "far"),
            F("berry-glade", "Ягодная поляна", "Berry_Bush_A*4", "выбор автора, ks-poi-169", "На опушке густо разрослись ягодные кусты.", 1, "forest-edge"),
            F("mushroom-patch", "Грибной участок", "Mushroom_Clump_A*3", "выбор автора, ks-poi-170", "Под деревьями — россыпь грибов.", 1, "forest-edge"),
            F("herb-garden", "Огород трав", "Herb_Clump_A*4", "выбор автора, ks-poi-090", "Грядки с пахучими травами, обнесённые низким плетнём.", 1, "village"),
            F("wild-orchard", "Дикий сад", "Fruit_Tree_A*3", "выбор автора, ks-poi-172", "Одичавшие плодовые деревья стоят рядами, которых уже не видно.", 1, "far"),
            F("windfall", "Бурелом", "Fallen_Tree_A*3", "выбор автора, ks-poi-176", "Деревья повалены в одну сторону, корни вывернуты с землёй.", 1, "forest-edge"),
            F("burnt-farm", "Сгоревший хутор", "Burnt_House_A_FrontRight+Tree_Stump_A", "выбор автора, ks-poi-155", "Чёрный остов избы, двор зарос иван-чаем.", 1, "far"),
            F("bathhouse", "Баня у воды", "Bathhouse_A_FrontRight", "выбор автора, ks-poi-015", "Низкая баня у самой воды, у двери — поленница.", 1, "water", "village"),
            F("granary", "Общий амбар", "Granary_A_FrontRight", "выбор автора, ks-poi-012", "Амбар на столбах у полей, дверь подпёрта колом.", 1, "fields", "village"),
            F("shepherd", "Пастух в поле", "Worker_Shepherd_Front", "выбор автора, ks-poi-237", "В поле пасёт скотину пастух с длинной палкой.", 1, "fields"),
            F("fisher", "Рыбак у воды", "Worker_Fisher_Front", "выбор автора, место для ловли рыбы", "У воды с удой стоит рыбак.", 1, "river"),
            F("woodcutters", "Стоянка лесорубов", "Camp_Tent_A+Worker_Lumberjack_Front", "выбор автора, ks-poi-099", "На опушке шалаш и сложенные брёвна, стучит топор.", 1, "forest-edge", "far"),
            F("gatherers", "Сборщики", "Worker_Gatherer_Front", "выбор автора, ks-poi-231", "Из леса идут сборщики с полными корзинами.", 1, "forest-edge"),
            F("abandoned-camp", "Покинутая стоянка", "Cold_Campfire_A+Camp_Tent_A", "выбор автора, ks-poi-026", "У дороги — остывшее кострище и брошенный шалаш.", 1, "road", "far"),
            F("shepherd-hut", "Пастуший шалаш", "Camp_Tent_A", "выбор автора, ks-poi-095", "У края пастбища — шалаш из жердей и лапника.", 1, "fields"),
            F("boat-shelter", "Навес лодочников", "Road_Shelter_A+Fishing_Dock_A", "выбор автора, ks-poi-102", "На берегу навес, под ним перевёрнутые лодки; от берега — мостки.", 1, "river", "village"),
            F("forge-ruins", "Руины придорожной кузни", "Ruined_Wall_A+Carpenter_Bench_A", "выбор автора, ks-poi-158", "У дороги — обвалившаяся кладка горна, вокруг окалина.", 1, "road", "far"),
            F("sealed-adit", "Засыпанная выработка", "Mine_Entrance_A+Map05_Rock_Round_A", "выбор автора, ks-poi-160", "В склоне — вход в выработку, заваленный камнем.", 1, "cliff"),
            F("miners-camp", "Старый стан рудокопов", "Camp_Tent_A+Mine_Entrance_A+Cold_Campfire_A", "выбор автора, ks-poi-167", "У склона — брошенные шалаши и устье старой выработки.", 1, "slope", "far"),
            F("rare-herb", "Место редкого растения", "Herb_Clump_A", "выбор автора, ks-poi-181", "В тени у опушки — несколько кустиков незнакомой травы.", 1, "forest-edge", "far"),
            F("overgrown-garden", "Заросший сад", "Fruit_Tree_A*2+Ruined_Wall_A", "выбор автора, ks-poi-162", "Среди бурьяна — старые яблони и остатки ограды.", 1, "far"),
            F("burnt-forest", "Лесная гарь", "Tree_Stump_A*3+Fallen_Tree_A", "выбор автора, ks-poi-174", "Обугленные пни и упавшие стволы, между ними поднимается иван-чай.", 1, "forest-edge"),
            F("scree", "Осыпь", "Map05_Rock_Round_A*2+Map05_Rock_Pebbles_C*2", "выбор автора, ks-poi-177", "Под склоном — свежая осыпь камня.", 1, "cliff"),
            F("lone-hunter", "Охотник", "Worker_Hunter_Front", "выбор автора, ks-poi-232", "На опушке стоит охотник с луком.", 1, "forest-edge", "far")
        };
    }

    // Безопасные пределы: генератор не падает и не зависает на любых
    // значениях из инспектора.
    public void Sanitize()
    {
        CanvasWidth = CanvasWidth > 64f && !float.IsNaN(CanvasWidth) ? CanvasWidth : WorldMapHexGrid.DefaultCanvasWidth;
        CanvasHeight = CanvasHeight > 64f && !float.IsNaN(CanvasHeight) ? CanvasHeight : WorldMapHexGrid.DefaultCanvasHeight;
        HexesAcross = Math.Max(20, Math.Min(300, HexesAcross));
        ReliefScaleHexes = Clamp(ReliefScaleHexes, 2f, 200f);
        ReliefDetail = Math.Max(1, Math.Min(7, ReliefDetail));
        ReliefRoughness = Clamp(ReliefRoughness, 0.1f, 0.9f);
        MountainRidges = Clamp(MountainRidges, 0f, 1f);
        HillsShare = Clamp(HillsShare, 0f, 0.8f);
        MountainsShare = Clamp(MountainsShare, 0f, 0.5f);
        CliffsShare = Clamp(CliffsShare, 0f, 1f);
        LakesShare = Clamp(LakesShare, 0f, 0.3f);
        RiverCount = Math.Max(0, Math.Min(24, RiverCount));
        RiverMinLengthHexes = Math.Max(2, Math.Min(400, RiverMinLengthHexes));
        RiverWidthHexes = Clamp(RiverWidthHexes, 0.05f, 3f);
        RiverMeander = Clamp(RiverMeander, 0f, 1f);
        ForestShare = Clamp(ForestShare, 0f, 0.9f);
        ForestScaleHexes = Clamp(ForestScaleHexes, 2f, 200f);
        ForestOnHills = Clamp(ForestOnHills, 0f, 1f);
        SwampShare = Clamp(SwampShare, 0f, 0.5f);
        MoistureScaleHexes = Clamp(MoistureScaleHexes, 2f, 200f);
        VillageCount = Math.Max(1, Math.Min(80, VillageCount));
        VillageMinDistanceHexes = Math.Max(2, Math.Min(100, VillageMinDistanceHexes));
        VillageNearWater = Clamp(VillageNearWater, 0f, 2f);
        FieldRadiusHexes = Clamp(FieldRadiusHexes, 0f, 10f);
        FieldDensity = Clamp(FieldDensity, 0f, 1f);
        ExtraRoadLoops = Clamp(ExtraRoadLoops, 0f, 2f);
        RoadsToEdge = Math.Max(0, Math.Min(12, RoadsToEdge));
        RoadAvoidHills = Clamp(RoadAvoidHills, 1f, 20f);
        RoadAvoidForest = Clamp(RoadAvoidForest, 1f, 20f);
        RoadAvoidSwamp = Clamp(RoadAvoidSwamp, 1f, 40f);
        RiverCrossingCost = Clamp(RiverCrossingCost, 1f, 100f);
        RoadWander = Clamp(RoadWander, 0f, 4f);
        RouteConnectivity = Clamp(RouteConnectivity, 0f, 1f);
        StartClearRadiusHexes = Math.Max(0, Math.Min(6, StartClearRadiusHexes));
        if (Landmarks == null)
            Landmarks = new List<WorldMapLandmarkRule>();
        if (Features == null)
            Features = new List<WorldMapLandmarkRule>();
        Factions ??= new List<WorldMapFactionDefinition>();
        Groups ??= new List<WorldMapGroupRule>();
        ControllingPolities = Math.Max(0, Math.Min(8, ControllingPolities));
        foreach (WorldMapGroupRule g in Groups)
            if (g != null)
                g.Count = Math.Max(0, Math.Min(12, g.Count));
        foreach (WorldMapLandmarkRule rule in Features)
        {
            if (rule == null)
                continue;
            rule.Count = Math.Max(0, Math.Min(20, rule.Count));
            rule.MinDistanceHexes = Clamp(rule.MinDistanceHexes, 0f, 100f);
            rule.Terrains ??= new List<WorldMapGameplayTerrainType>();
            rule.Near ??= new List<string>();
        }
        foreach (WorldMapLandmarkRule rule in Landmarks)
        {
            rule?.Near?.RemoveAll(string.IsNullOrWhiteSpace);
            if (rule == null)
                continue;
            rule.Count = Math.Max(0, Math.Min(40, rule.Count));
            rule.MinDistanceHexes = Clamp(rule.MinDistanceHexes, 0f, 100f);
            if (rule.Terrains == null)
                rule.Terrains = new List<WorldMapGameplayTerrainType>();
        }
    }

    private static float Clamp(float value, float min, float max) =>
        float.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));
}

// Река, дорога или тропа: клетки разметки и сглаженная линия для рисунка.
// Ширина задаётся в клетках пути и растёт от истока к устью.
[Serializable]
public sealed class GeneratedMapLine
{
    public const string RiverKind = "river";
    public const string RoadKind = "road";
    public const string TrailKind = "trail";

    public string Kind = RoadKind;
    public float StartWidthHexes;
    public float EndWidthHexes;
    public List<MapPointData> Points = new List<MapPointData>();
    public List<int> Cells = new List<int>();
}

// Деревня или приметное место. Variant — устойчивое случайное число для
// выбора рисунка (какой домик, какой знак), не меняется между загрузками.
[Serializable]
public sealed class GeneratedMapSite
{
    public const string VillageKind = "village";

    public string Kind = VillageKind;
    public string Name = string.Empty;
    public int Cell;
    public float XPercent;
    public float YPercent;
    public int Size = 1;
    public int Variant;
    public bool DrawMark = true;
    public bool IsStart;
    // 12У-E: рисунок на карте MapLab (из правила; скрытое не рисуется).
    public string Visual = string.Empty;
    // 12У-E: держава, контролирующая деревню (пусто — не назначено).
    public string ControllerFactionId = string.Empty;

    // Место карты игры из правила (GeneratedMapPlaces): связанное место
    // Базы карты, скрытость до находки, описание, угроза, исследование.
    public string LocationId = string.Empty;
    public bool Hidden;
    public string Description = string.Empty;
    public string Threat = string.Empty;
    public double ExplorationHours;
    public int RewardArmySupply;
    public int RewardArmyGold;

    // 12У-D: структура. Деревня — причина положения и планировочное
    // семейство; место — район и основания его обнаружить (сигналы).
    public string Reason = string.Empty;
    public string ReasonText = string.Empty;
    public string Family = string.Empty;
    public string DistrictId = string.Empty;
    public List<string> Signals = new List<string>();

    public const string FamilyAlongRoad = "along-road";
    public const string FamilyCrossing = "crossing";
    public const string FamilyCraft = "craft";
    public const string FamilyScattered = "scattered";

    public static string FamilyTitle(string family)
    {
        switch (family)
        {
            case FamilyAlongRoad: return "Деревня вдоль дороги";
            case FamilyCrossing: return "Поселение у переправы";
            case FamilyCraft: return "Промысловая слобода";
            case FamilyScattered: return "Рассеянные дворы";
            default: return "—";
        }
    }

    public static string SignalTitle(string signal)
    {
        switch (signal)
        {
            case "trail": return "тропа";
            case "view": return "вид с дороги";
            case "rumor": return "слух в деревне";
            case "water": return "по воде";
            case "neighbor": return "соседнее место";
            default: return signal;
        }
    }
}

[Serializable]
public sealed class GeneratedSpawnSlot
{
    public string Id;
    public List<string> Tags = new List<string>();
    public float MinXPercent;
    public float MaxXPercent;
    public float MinYPercent;
    public float MaxYPercent;
}

[Serializable]
public sealed class GeneratedWorldMap
{
    public const int CurrentVersion = 1;
    public const string WorldDefinitionId = "generated";

    // Поддельный объект на месте null (так JsonUtility пишет пустые ссылки)
    // имеет нулевое полотно и честно не проходит IsValid.
    public int Version = CurrentVersion;
    public int Seed;
    public float CanvasWidth;
    public float CanvasHeight;
    public int HexesAcross;
    public string TerrainCells = string.Empty;
    public WorldMapGenerationStage CompletedStage = WorldMapGenerationStage.Complete;

    public List<GeneratedMapLine> Rivers = new List<GeneratedMapLine>();
    public List<GeneratedMapLine> Roads = new List<GeneratedMapLine>();
    public List<int> LakeCells = new List<int>();
    public List<int> RiverCells = new List<int>();
    public List<GeneratedMapSite> Villages = new List<GeneratedMapSite>();
    public List<GeneratedMapSite> Landmarks = new List<GeneratedMapSite>();
    public List<MapPointData> Bridges = new List<MapPointData>();
    // Бриф правок п. 13: пролёты мостов — парами точек «берег → берег»:
    // последняя клетка суши перед водой и первая за ней на пути дороги.
    // Пусто у карт, созданных раньше, — рисунок тогда по-старому.
    public List<MapPointData> BridgeSpans = new List<MapPointData>();
    public List<GeneratedSpawnSlot> SpawnSlots = new List<GeneratedSpawnSlot>();

    // 12У-D: структура региона. Пусто у карт, созданных раньше.
    public GeneratedRegionPassport Passport = new GeneratedRegionPassport();
    public List<GeneratedMapDistrict> Districts = new List<GeneratedMapDistrict>();
    // Номер района (индекс в Districts + 1; 0 — нет) по клеткам, base64.
    public string DistrictCells = string.Empty;
    // Хозяйственные площадки — концы хозяйственных троп (не места игры).
    public List<GeneratedMapSite> WorkSites = new List<GeneratedMapSite>();
    // 12У-E: детали мест (не места игры): рисунок, площадка, описание.
    public List<GeneratedMapSite> Features = new List<GeneratedMapSite>();
    // 12У-E: группы мира с назначенными маршрутами (PassiveRoutes).
    public List<GeneratedMapActor> Actors = new List<GeneratedMapActor>();
    public GeneratedRegionReport Report = new GeneratedRegionReport();
    public float StartXPercent = WorldMapNavigation.CapitalXPercent;
    public float StartYPercent = WorldMapNavigation.CapitalYPercent;

    // Только для показа в редакторе (карта высот и влажности); в сохранение
    // не попадают — игре они не нужны.
    [NonSerialized] public float[] Elevation;
    [NonSerialized] public float[] Moisture;

    public bool IsValid => Version > 0 && CanvasWidth > 1f && CanvasHeight > 1f && HexesAcross > 0;

    public WorldMapHexGrid CreateGrid() => new WorldMapHexGrid(CanvasWidth, CanvasHeight, HexesAcross);

    public WorldMapTerrainLayer CreateTerrainLayer() => WorldMapTerrainLayer.Decode(CreateGrid(), TerrainCells);

    // Карта как обычный мир для навигации и расстановки мест: та же
    // разметка клеток, полотно и слоты, что у авторского World Definition.
    public WorldMapDefinitionData ToDefinitionData()
    {
        WorldMapDefinitionData data = new WorldMapDefinitionData
        {
            WorldDefinitionId = WorldDefinitionId,
            GeographyVersion = Version,
            HomeLocationId = "home",
            HomeXPercent = StartXPercent,
            HomeYPercent = StartYPercent,
            CanvasWidth = CanvasWidth,
            CanvasHeight = CanvasHeight,
            HexesAcross = HexesAcross,
            TerrainCells = TerrainCells ?? string.Empty
        };
        if (Roads != null)
            foreach (GeneratedMapLine line in Roads)
                if (line?.Points != null && line.Points.Count >= 2)
                    data.RoadLines.Add(new List<MapPointData>(line.Points));

        if (SpawnSlots != null)
        {
            foreach (GeneratedSpawnSlot slot in SpawnSlots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.Id))
                    continue;
                data.SpawnSlots.Add(new WorldMapSpawnSlotDefinition
                {
                    Id = slot.Id,
                    Tags = new List<string>(slot.Tags ?? new List<string>()),
                    MinXPercent = slot.MinXPercent,
                    MaxXPercent = slot.MaxXPercent,
                    MinYPercent = slot.MinYPercent,
                    MaxYPercent = slot.MaxYPercent
                });
            }
        }

        return data;
    }
}
