namespace KingdomSurvival.FreePlay
{
    // ПР-12К (канон v1.53 §28.3, §28.6): Старая шахта как исследуемое место —
    // пилот локальной карты. Содержание и итоги прежние (FreePlayMineStory):
    // железо в отвалах, клеймо старых работ, звери в дальней штольне, отход,
    // топор из железа и угля. Флаги истории остаются единственным источником
    // истины о находках. Расстановка, клетки и минуты на шаг — [РАБОЧЕЕ];
    // кто вырыл шахту — [ОТКРЫТО] (LORE.md §17.1), здесь не решается.
    //
    // Поле old_mine_01 (База полей боя): камень сверху и снизу, стенка в
    // столбце 5 с проходом в дальнюю штольню; рисунка ещё нет — заглушка.
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
                HoursPerCell = 0.05,
                HoursPerInteraction = 0.25
            };

            mine.Entrances.Add(new LocalEntranceDefinition
            {
                Id = EntranceId,
                Label = "Выход из шахты",
                Cell = new LocalCellData(0, 3)
            });

            // Отвалы у входа: прежняя проверка Наблюдательности, один раз.
            mine.Objects.Add(new LocalObjectDefinition
            {
                Id = DumpsObjectId,
                Label = "Отвалы",
                ActionLabel = "Перебрать отвалы",
                Kind = LocalObjectKind.Dialogue,
                Cell = new LocalCellData(2, 0),
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
                Cell = new LocalCellData(4, 6),
                DialogueId = MarkDialogueId,
                Text = "Кованые скобы в старой крепи.",
                OnceOnly = true,
                HiddenWhenFlag = FreePlayMineStory.Flags.Mark
            });

            // Признаки опасности перед глубиной.
            mine.Objects.Add(new LocalObjectDefinition
            {
                Id = SignsObjectId,
                Label = "Проход в дальнюю штольню",
                ActionLabel = "Прислушаться",
                Kind = LocalObjectKind.Dialogue,
                Cell = new LocalCellData(5, 2),
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
                Cell = new LocalCellData(8, 0),
                Text = "Сухой забой и железо, сложенное аккуратно, будто его оставили до завтра.",
                OnceOnly = false,
                RequiresFlag = FreePlayMineStory.Flags.LairCleared
            });

            mine.Enemies.Add(new LocalEnemyDefinition
            {
                InstanceId = "mine.beast.1",
                UnitTypeId = BeastUnitTypeId,
                Level = 1,
                Cell = new LocalCellData(8, 2),
                EncounterId = LairEncounterId
            });
            mine.Enemies.Add(new LocalEnemyDefinition
            {
                InstanceId = "mine.beast.2",
                UnitTypeId = BeastUnitTypeId,
                Level = 1,
                Cell = new LocalCellData(8, 4),
                EncounterId = LairEncounterId
            });

            // Логово: шаг за проход в дальнюю штольню поднимает зверей.
            LocalEncounterDefinition lair = new LocalEncounterDefinition
            {
                Id = LairEncounterId,
                BattleIdPrefix = FreePlayMineStory.LairBattlePrefix,
                IntroDialogueId = LairDialogueId,
                AllowRetreat = true,
                RetreatCell = new LocalCellData(1, 3),
                PreparedStartCompanionId = CampRest.AgnessaId,
                ResolvedFlag = FreePlayMineStory.Flags.LairCleared
            };
            for (int r = 1; r <= 5; r++)
                lair.TriggerCells.Add(new LocalCellData(6, r));
            mine.Encounters.Add(lair);

            // Осыпи [РАБОЧЕЕ].
            mine.DifficultCells.Add(new LocalCellData(2, 4));
            mine.DifficultCells.Add(new LocalCellData(7, 1));
            return mine;
        }
    }
}
