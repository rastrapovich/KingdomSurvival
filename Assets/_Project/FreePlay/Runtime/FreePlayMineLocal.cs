using System;

namespace KingdomSurvival.FreePlay
{
    // ПР-12К (канон v1.54 §28.3, §28.6): Старая шахта как исследуемое место —
    // пилот локальной карты. Содержание и итоги прежние (FreePlayMineStory):
    // железо в отвалах, клеймо старых работ, звери в дальней штольне, отход,
    // топор из железа и угля. Флаги истории остаются единственным источником
    // истины о находках. Расстановка, размеры и минуты на шаг — [РАБОЧЕЕ];
    // кто вырыл шахту — [ОТКРЫТО] (LORE.md §17.1), здесь не решается.
    //
    // Рисунок 1920×1080 (заглушка — рисунка ещё нет). Перемещение — как на
    // глобальной карте, по разметке местности: выработка — овал, вокруг
    // камень, поперёк — стенка с проходом в дальнюю штольню. Кадр боя поля
    // old_mine_01 закрывает весь рисунок, поэтому сетка боя — ровно сетка
    // этого поля из Базы полей боя; стены боя — клетки, чей центр на камне.
    public static class FreePlayMineLocal
    {
        public const string LocalLocationId = "local.old_mine";
        public const string BattlefieldId = "old_mine_01";
        public const string EntranceId = "mine.entrance";
        public const string LairEncounterId = "mine.lair";

        public const string DumpsObjectId = "mine.dumps";
        public const string MarkObjectId = "mine.mark";
        public const string SignsObjectId = "mine.signs";
        public const string FaceObjectId = "mine.dry_face";

        public const string DumpsDialogueId = "freeplay_mine_local_dumps";
        public const string MarkDialogueId = "freeplay_mine_local_mark";
        public const string SignsDialogueId = "freeplay_mine_local_signs";
        public const string LairDialogueId = "freeplay_mine_local_lair";

        public const string BeastUnitTypeId = "forest_beast";

        public const int CanvasWidth = 1920;
        public const int CanvasHeight = 1080;
        public const int HexesAcross = 60;

        public static LocalLocationDefinition Create()
        {
            LocalLocationDefinition mine = new LocalLocationDefinition
            {
                Id = LocalLocationId,
                ModeId = FreePlayContent.ModeId,
                WorldLocationId = FreePlayMineStory.LocationId,
                DisplayName = "Старая шахта",
                BattlefieldId = BattlefieldId,
                PlaceholderArt = true,
                CanvasWidth = CanvasWidth,
                CanvasHeight = CanvasHeight,
                HexesAcross = HexesAcross,
                BattleFrameWidth = CanvasWidth,
                HoursPerInteraction = 0.25
            };
            mine.TerrainCells = CreateTerrain(mine);

            mine.Entrances.Add(new LocalEntranceDefinition
            {
                Id = EntranceId,
                Label = "Выход из шахты",
                Point = new LocalPointData(368, 551)
            });

            // Отвалы у входа: прежняя проверка Наблюдательности, один раз.
            mine.Objects.Add(new LocalObjectDefinition
            {
                Id = DumpsObjectId,
                Label = "Отвалы",
                ActionLabel = "Перебрать отвалы",
                Kind = LocalObjectKind.Dialogue,
                Point = new LocalPointData(565, 330),
                DialogueId = DumpsDialogueId,
                Text = "Заросшие иван-чаем отвалы, из них торчит старое железо.",
                OnceOnly = true,
                HiddenWhenFlag = FreePlayMineStory.Flags.Entered
            });

            // Старая крепь со скобами: клеймо читает Остафий или Расследование.
            mine.Objects.Add(new LocalObjectDefinition
            {
                Id = MarkObjectId,
                Label = "Старая крепь",
                ActionLabel = "Осмотреть скобы",
                Kind = LocalObjectKind.Dialogue,
                Point = new LocalPointData(828, 790),
                DialogueId = MarkDialogueId,
                Text = "Кованые скобы в старой крепи.",
                // Пока клеймо не прочитано, к крепи можно вернуться — с
                // Остафием; своя попытка Расследования одна (условие сцены).
                OnceOnly = false,
                HiddenWhenFlag = FreePlayMineStory.Flags.Mark
            });

            // Признаки опасности перед глубиной.
            mine.Objects.Add(new LocalObjectDefinition
            {
                Id = SignsObjectId,
                Label = "Проход в дальнюю штольню",
                ActionLabel = "Прислушаться",
                Kind = LocalObjectKind.Dialogue,
                Point = new LocalPointData(930, 470),
                DialogueId = SignsDialogueId,
                Text = "Из дальней штольни тянет теплом и зверем.",
                OnceOnly = true,
                HiddenWhenFlag = FreePlayMineStory.Flags.LairCleared
            });

            // После очищения штольни — сухой забой (итог прежней истории).
            mine.Objects.Add(new LocalObjectDefinition
            {
                Id = FaceObjectId,
                Label = "Сухой забой",
                ActionLabel = "Осмотреть забой",
                Kind = LocalObjectKind.Inspect,
                Point = new LocalPointData(1355, 330),
                Text = "Сухой забой и железо, сложенное аккуратно, будто его оставили до завтра.",
                OnceOnly = false,
                RequiresFlag = FreePlayMineStory.Flags.LairCleared
            });

            mine.Enemies.Add(new LocalEnemyDefinition
            {
                InstanceId = "mine.beast.1",
                UnitTypeId = BeastUnitTypeId,
                Level = 1,
                Point = new LocalPointData(1355, 465),
                EncounterId = LairEncounterId
            });
            mine.Enemies.Add(new LocalEnemyDefinition
            {
                InstanceId = "mine.beast.2",
                UnitTypeId = BeastUnitTypeId,
                Level = 1,
                Point = new LocalPointData(1355, 636),
                EncounterId = LairEncounterId
            });

            // Логово: шаг за проход в дальнюю штольню поднимает зверей. Кадр
            // боя — весь рисунок (его центр).
            mine.Encounters.Add(new LocalEncounterDefinition
            {
                Id = LairEncounterId,
                BattleIdPrefix = FreePlayMineStory.LairBattlePrefix,
                TriggerArea = new LocalAreaData(1100, 320, 110, 460),
                HasArenaCenter = true,
                ArenaCenter = new LocalPointData(CanvasWidth / 2f, CanvasHeight / 2f),
                IntroDialogueId = LairDialogueId,
                AllowRetreat = true,
                RetreatPoint = new LocalPointData(499, 551),
                PreparedStartCompanionId = CampRest.AgnessaId,
                ResolvedFlag = FreePlayMineStory.Flags.LairCleared
            });
            return mine;
        }

        // Разметка: овал выработки, стенка с проходом, осыпи [РАБОЧЕЕ].
        public static string CreateTerrain(LocalLocationDefinition mine)
        {
            WorldMapHexGrid grid = mine.CreateGrid();
            WorldMapTerrainLayer layer = new WorldMapTerrainLayer(grid);
            for (int index = 0; index < grid.CellCount; index++)
            {
                WorldMapHexCell cell = grid.CellAt(index);
                grid.CellCenter(cell, out double x, out double y);
                layer.Set(cell, TerrainAt(x, y));
            }
            return layer.Encode();
        }

        public static WorldMapGameplayTerrainType TerrainAt(double x, double y)
        {
            double dx = (x - 960) / 720;
            double dy = (y - 550) / 240;
            bool inside = dx * dx + dy * dy <= 1 && y > 320 && y < 780;
            bool wall = x >= 930 && x <= 1065 && (y < 510 || y > 680);
            if (!inside || wall)
                return WorldMapGameplayTerrainType.Cliffs;
            if (Near(x, y, 565, 636, 45) || Near(x, y, 1289, 390, 45))
                return WorldMapGameplayTerrainType.Hills;
            return WorldMapGameplayTerrainType.OpenGround;
        }

        private static bool Near(double x, double y, double cx, double cy, double radius) =>
            Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) <= radius;
    }
}
