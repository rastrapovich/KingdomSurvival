using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Editor
{
    // ПР-12Ж: прототипный боевой каталог из 24 существ и 8 составов
    // тестового боя (BESTIARY_COMBAT_PASSPORTS.md, «Прототипный каталог 24»).
    // [РАБОЧЕЕ — БОЕВОЙ СЛОЙ]. Засев только ДОБАВЛЯЕТ недостающие записи и
    // никогда не перезаписывает существующие: после засева числа правятся в
    // окне Базы существ, а не здесь.
    public static class UnitCatalogSeed
    {
        public const string UnitDatabasePath = "Assets/_Project/UnitDatabase/Resources/UnitDatabase/KingdomSurvivalUnits.asset";

        private const string Beast = "species.beast";
        private const string Ranged = "combat.ranged";
        private const string Defender = "role.defender";
        private const string Armored = "trait.armored";
        private const string HumanSlayer = "trait.human_slayer";

        [MenuItem("Kingdom Survival/База существ/Добавить недостающих существ каталога")]
        public static void Seed()
        {
            UnitDatabaseAsset database = AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(UnitDatabasePath);
            if (database == null)
            {
                Debug.LogError("Каталог существ: база не найдена: " + UnitDatabasePath);
                return;
            }

            bool migrated = database.MigrateIfNeeded();
            List<string> added = AddMissing(database);
            if (migrated || added.Count > 0)
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("Каталог существ: добавлено " + added.Count +
                      (added.Count > 0 ? " — " + string.Join(", ", added) : string.Empty));
        }

        public static List<string> AddMissing(UnitDatabaseAsset database)
        {
            List<string> added = new List<string>();
            foreach (UnitDefinitionData creature in Creatures())
            {
                if (database.AddUnitIfMissing(creature))
                    added.Add(creature.Id);
            }
            foreach (UnitEncounterPreset preset in Presets())
            {
                if (database.AddPresetIfMissing(preset))
                    added.Add(preset.Id);
            }
            return added;
        }

        public static List<UnitDefinitionData> Creatures()
        {
            return new List<UnitDefinitionData>
            {
                C("wolf", "Волк", UnitSize.Medium, 10, 3, 1, 3, 5, 6, 1, new[] { Beast },
                    A("wolf.pack", "Стая", UnitCombatBrick.AiPriority, "Волки окружают одну цель, а не расходятся по всем.")),
                C("lynx", "Рысь", UnitSize.Medium, 9, 4, 1, 4, 5, 8, 1, new[] { Beast },
                    A("lynx.pounce", "Прыжок на отставшего", UnitCombatBrick.AiPriority, "Выбирает бойца, до которого не дотянутся свои.")),
                C("boar", "Кабан", UnitSize.Medium, 16, 3, 2, 4, 4, 4, 1, new[] { Beast },
                    A("boar.rush", "Короткий разгон", UnitCombatBrick.Charge, "После разбега по прямой бьёт сильнее.")),
                C("bear", "Медведь", UnitSize.Large, 22, 4, 3, 5, 3, 2, 1, new[] { Beast },
                    A("bear.break_guard", "Сбить стойку", UnitCombatBrick.Charge, "Тяжёлый удар выбивает бойца из защитной стойки.")),
                C("elk_bull", "Лось-секач", UnitSize.Large, 18, 3, 2, 4, 5, 5, 1, new[] { Beast },
                    A("elk_bull.toss", "Отбросить", UnitCombatBrick.ForcedMovement, "Рогами отбрасывает цель на гекс назад.")),
                C("sheshka", "Шешка", UnitSize.Small, 8, 2, 0, 2, 5, 8, 1, new string[0]),
                C("prakh", "Прах", UnitSize.Small, 10, 2, 1, 3, 4, 6, 1, new string[0]),
                C("blood_kleshchen", "Кровяной клещень", UnitSize.Small, 8, 3, 0, 2, 5, 7, 1, new[] { Beast },
                    A("blood_kleshchen.attach", "Цепляется", UnitCombatBrick.AttachPersistent, "Садится на бойца и пьёт кровь каждый ход, пока его не снимут.")),
                C("krivolap", "Криволап", UnitSize.Medium, 12, 3, 2, 3, 5, 7, 1, new[] { Beast },
                    A("krivolap.flank", "Обход и окружение", UnitCombatBrick.AiPriority, "Заходит с фланга, а не в лоб.")),
                C("padalny_mnogonog", "Падальный многоног", UnitSize.Medium, 13, 3, 2, 3, 4, 5, 1, new[] { Beast },
                    A("padalny_mnogonog.numb", "Оцепеняет", UnitCombatBrick.StatusMark, "Укус оцепеняет бойца."),
                    A("padalny_mnogonog.feed", "Кормится", UnitCombatBrick.AiPriority, "Идёт к оцепеневшему прежде всех.")),
                C("shipovik", "Шиповик", UnitSize.Medium, 10, 3, 1, 3, 3, 6, 4, new[] { Ranged },
                    A("shipovik.kite", "Стреляет и отходит", UnitCombatBrick.AiPriority, "После выстрела отступает, держа дистанцию.")),
                C("kosteplyuy", "Костеплюй", UnitSize.Medium, 12, 3, 1, 4, 3, 4, 3, new[] { Ranged }),
                C("treshchotka", "Трещотка", UnitSize.Small, 8, 3, 0, 3, 4, 8, 5, new[] { Ranged },
                    A("treshchotka.distance", "Держит дистанцию", UnitCombatBrick.AiPriority, "Не подходит ближе, чем нужно для выстрела.")),
                C("tinny_polzun", "Тинный ползун", UnitSize.Large, 20, 2, 4, 3, 2, 2, 1, new[] { Defender },
                    A("tinny_polzun.engulf", "Накрывает", UnitCombatBrick.GrappleTether, "Захватывает соседнего бойца; вырваться — действием.")),
                C("kamnespin", "Камнеспин", UnitSize.Large, 16, 2, 3, 3, 2, 2, 1, new[] { Armored, Defender }),
                C("zhivuchy_gnilets", "Живучий гнилец", UnitSize.Large, 22, 3, 3, 4, 2, 2, 1, new string[0],
                    A("zhivuchy_gnilets.regen", "Восстанавливается", UnitCombatBrick.HazardTileState, "Лечится, пока стоит на мокром гексе.")),
                C("srubnik", "Срубник", UnitSize.Medium, 12, 4, 1, 4, 3, 7, 1, new[] { HumanSlayer },
                    A("srubnik.hidden", "Скрыт до броска", UnitCombatBrick.HiddenBurrow, "Начинает бой скрытым, если разведка его не заметила.")),
                C("pepelny_vepr", "Пепельный вепрь", UnitSize.Large, 18, 4, 2, 4, 4, 5, 1, new[] { Beast },
                    A("pepelny_vepr.charge", "Разгоняется и сбивает", UnitCombatBrick.Charge, "Разгон по прямой, удар отбрасывает цель.")),
                C("bivnesty_gromila", "Бивнестый громила", UnitSize.Large, 20, 4, 2, 5, 3, 3, 1, new[] { HumanSlayer }),
                C("beregovik", "Береговик", UnitSize.Medium, 16, 3, 3, 4, 3, 4, 1, new string[0],
                    A("beregovik.pull", "Тянет к воде", UnitCombatBrick.ForcedMovement, "Притягивает бойца к воде."),
                    A("beregovik.wet", "Сильнее у воды", UnitCombatBrick.HazardTileState, "На мокром гексе бьёт сильнее.")),
                C("lozovy_khvatun", "Лозовый хватун", UnitSize.Large, 15, 3, 2, 3, 1, 3, 3, new[] { Ranged },
                    A("lozovy_khvatun.tether", "Хватает и подтягивает", UnitCombatBrick.GrappleTether, "Лоза связывает бойца и тянет его; лозу можно перерубить.")),
                C("zvonnik", "Звонник", UnitSize.Large, 18, 3, 3, 4, 3, 5, 2, new string[0],
                    A("zvonnik.mark", "Метит", UnitCombatBrick.StatusMark, "Отмечает бойца звоном."),
                    A("zvonnik.toll", "Бьёт отмеченных", UnitCombatBrick.Telegraph, "Через ход ударяет по всем отмеченным; удар показан заранее.")),
                C("belorev", "Белорев", UnitSize.Large, 26, 4, 3, 5, 3, 3, 1, new[] { Beast },
                    A("belorev.scatter", "Разгоняет строй", UnitCombatBrick.Charge, "Натиском разбрасывает нескольких бойцов и сбивает стойки.")),
                C("chernolob", "Чернолоб", UnitSize.Large, 28, 5, 4, 5, 3, 2, 1, new[] { Beast, Armored },
                    A("chernolob.breakthrough", "Прорыв", UnitCombatBrick.Charge, "Прорывается через строй; после сорванного рывка открыт с фланга."),
                    A("chernolob.telegraph", "Показанный рывок", UnitCombatBrick.Telegraph, "Линия рывка видна за ход."))
            };
        }

        public static List<UnitEncounterPreset> Presets()
        {
            return new List<UnitEncounterPreset>
            {
                UnitEncounterPreset.Create("preset.swarm", "Много мелочи", "Окружение, ценность стойки и ответного удара.",
                    S("sheshka", 6)),
                UnitEncounterPreset.Create("preset.fast", "Несколько быстрых", "Тающий строй, кто успевает первым.",
                    S("wolf", 2), S("krivolap", 2)),
                UnitEncounterPreset.Create("preset.covered_archer", "Дальник под прикрытием", "Дойти до стрелка мимо брони.",
                    S("shipovik", 2), S("kamnespin", 1)),
                UnitEncounterPreset.Create("preset.blocker_archer", "Толстый блокер и стрелок", "Обойти или продавить проход.",
                    S("tinny_polzun", 1), S("shipovik", 1)),
                UnitEncounterPreset.Create("preset.one_strong", "Один сильный против группы", "Концентрация ударов, раненые.",
                    S("bear", 1)),
                UnitEncounterPreset.Create("preset.glass_killers", "Хрупкие убийцы", "Кто ходит первым, защита слабых.",
                    S("lynx", 2), S("srubnik", 1)),
                UnitEncounterPreset.Create("preset.slow_heavy", "Медленный тяжёлый зверь", "Темп и отход.",
                    S("boar", 1), S("bivnesty_gromila", 1)),
                UnitEncounterPreset.Create("preset.boss_retinue", "Мини-босс со свитой", "Снять свиту или бить вожака.",
                    S("belorev", 1), S("wolf", 2))
            };
        }

        private static UnitDefinitionData C(string id, string label, UnitSize size,
            int hp, int attack, int defense, int damage, int movement, int initiative, int range,
            string[] tags, params UnitAbilityData[] abilities)
        {
            return UnitDefinitionData.CreateCreature(id, label, size, hp, attack, defense, damage, movement, initiative, range, tags, abilities);
        }

        private static UnitAbilityData A(string id, string title, UnitCombatBrick brick, string description)
        {
            return UnitAbilityData.WaitingFor(id, title, brick, description);
        }

        private static UnitEncounterSlot S(string unitId, int count)
        {
            return new UnitEncounterSlot(unitId, count);
        }
    }
}
