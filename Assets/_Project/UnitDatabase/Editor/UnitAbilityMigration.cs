using System;
using System.Collections.Generic;
using KingdomSurvival.BattleSandbox;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Editor
{
    // ПР-16 (бриф пользователя 19.11.2026): адресная, идемпотентная миграция
    // способностей существ. Не пересоздаёт базу и не трогает GUID, рисунки,
    // кадрирование и характеристики. Механика ставится только записи, у
    // которой её ещё нет (Kind = None), — правки автора не перезаписываются.
    // Числа — стартовые настройки брифа (SandboxAbilityDefaults), не канон.
    public static class UnitAbilityMigration
    {
        public const string PackTagId = "combat.pack";

        // Batch: Unity.exe -batchmode -executeMethod KingdomSurvival.UnitDatabase.Editor.UnitAbilityMigration.RunBatch
        public static void RunBatch()
        {
            UnitDatabaseAsset database = AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(UnitCatalogSeed.UnitDatabasePath);
            if (database == null)
            {
                Debug.LogError("Миграция способностей: база не найдена.");
                return;
            }
            List<string> changes = Apply(database);
            if (changes.Count > 0)
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("Миграция способностей ПР-16: изменений " + changes.Count + (changes.Count > 0 ? "\n— " + string.Join("\n— ", changes) : string.Empty));
        }

        public static List<string> Apply(UnitDatabaseAsset database)
        {
            List<string> changes = new List<string>();
            if (database == null)
                return changes;
            database.MigrateIfNeeded();

            if (database.AddTagIfMissing(UnitTagDefinition.Create(PackTagId, "Стая", "Поведение", new Color(0.55f, 0.47f, 0.33f, 1f),
                    "Правило, не кнопка: +1 Атака при контактной атаке, если рядом с той же целью стоит другой живой носитель «Стаи». " +
                    "Не складывается от числа зверей; дальний выстрел бонуса не получает. Число — рабочая настройка.")))
                changes.Add("тег combat.pack «Стая» добавлен");

            // Волк: «Стая» — тег, дублирующая запись способности удаляется.
            UnitDefinitionData wolf = database.FindById("wolf");
            if (wolf != null)
            {
                if (wolf.AddTag(PackTagId))
                    changes.Add("wolf: тег «Стая»");
                if (wolf.RemoveAbility("wolf.pack"))
                    changes.Add("wolf: запись способности wolf.pack удалена (теперь тег)");
            }

            Mechanic(database, changes, "lynx", "lynx.pounce", SandboxAbilityKind.Pounce, "Прыжок");
            Mechanic(database, changes, "boar", "boar.rush", SandboxAbilityKind.Charge, "Короткий разгон");
            Mechanic(database, changes, "bear", "bear.break_guard", SandboxAbilityKind.BreakGuard, "Сбить стойку");
            Mechanic(database, changes, "elk_bull", "elk_bull.toss", SandboxAbilityKind.Shove, "Отбросить");
            Mechanic(database, changes, "pepelny_vepr", "pepelny_vepr.charge", SandboxAbilityKind.Charge, "Разгоняется и сбивает",
                new SandboxAbilityDefinition("pepelny_vepr.charge", "Разгоняется и сбивает", SandboxAbilityKind.Charge,
                    cooldown: 3, pathMin: 2, pathMax: 4, damageMultiplier: 1.25m, push: 1));
            Mechanic(database, changes, "blood_kleshchen", "blood_kleshchen.attach", SandboxAbilityKind.Attach, "Цепляется");
            AddIfMissing(database, changes, "blood_kleshchen", "blood_kleshchen.spit", UnitCombatBrick.StatusMark, SandboxAbilityKind.StunSpit, "Ошеломляющий плевок");
            Mechanic(database, changes, "padalny_mnogonog", "padalny_mnogonog.numb", SandboxAbilityKind.NumbBite, "Оцепеняет");
            Mechanic(database, changes, "padalny_mnogonog", "padalny_mnogonog.feed", SandboxAbilityKind.FeedOnNumbed, "Кормится");
            Mechanic(database, changes, "shipovik", "shipovik.kite", SandboxAbilityKind.KiteAfterShot, "Стреляет и отходит");
            Mechanic(database, changes, "treshchotka", "treshchotka.distance", SandboxAbilityKind.KeepDistance, "Держит дистанцию");
            Mechanic(database, changes, "tinny_polzun", "tinny_polzun.engulf", SandboxAbilityKind.Engulf, "Накрывает");
            Mechanic(database, changes, "zhivuchy_gnilets", "zhivuchy_gnilets.regen", SandboxAbilityKind.RegenOnWater, "Восстанавливается");
            Mechanic(database, changes, "srubnik", "srubnik.hidden", SandboxAbilityKind.HiddenStart, "Скрыт до броска");
            Mechanic(database, changes, "lozovy_khvatun", "lozovy_khvatun.tether", SandboxAbilityKind.VineGrab, "Хватает и подтягивает");
            Mechanic(database, changes, "zvonnik", "zvonnik.mark", SandboxAbilityKind.ChimeMark, "Метит");
            UnitAbilityData toll = database.FindById("zvonnik")?.FindAbility("zvonnik.toll");
            if (toll != null && toll.Status == UnitAbilityStatus.WaitsForMechanic)
            {
                toll.SetStatus(UnitAbilityStatus.Active);
                toll.SetText(null, "Вторая часть «Метит»: в следующую активацию — удар звона ×1,25 по отмеченным, оставшимся в объявленной зоне. Исполняется способностью «Метит».");
                changes.Add("zvonnik.toll: описание второй части «Метит» (своей кнопки нет)");
            }

            Disable(database, changes, "krivolap", "krivolap.flank", "Решение 19.11.2026: способность убрана; существо сохранено, возможно, будет убрано позже.");
            Disable(database, changes, "belorev", "belorev.scatter", "Решение 19.11.2026: фирменные способности (натиск, рёв, устойчивость к контролю) отключены; обычная боевая запись сохранена.");
            Disable(database, changes, "chernolob", "chernolob.breakthrough", "Решение 19.11.2026: «Прорыв» отключён; обычный боец.");
            Disable(database, changes, "chernolob", "chernolob.telegraph", "Решение 19.11.2026: «Показанный рывок» отключён.");
            UnitDefinitionData beregovik = database.FindById("beregovik");
            if (beregovik != null)
            {
                if (!beregovik.ExcludedFromNewEncounters)
                {
                    beregovik.Exclude("Решение 19.11.2026: «убрать, пока не нужен». Не предлагается в новых встречах и полигоне; старые ссылки работают.");
                    changes.Add("beregovik: исключён из активного выбора и новых встреч");
                }
                Disable(database, changes, "beregovik", "beregovik.pull", "Существо выведено из набора 19.11.2026.");
                Disable(database, changes, "beregovik", "beregovik.wet", "Существо выведено из набора 19.11.2026.");
            }

            foreach (UnitDefinitionData creature in SecondTier())
            {
                if (database.AddUnitIfMissing(creature))
                    changes.Add(creature.Id + ": добавлено существо второго эшелона (стартовые характеристики брифа)");
            }
            return changes;
        }

        private static void Mechanic(UnitDatabaseAsset database, List<string> changes, string unitId, string abilityId,
            SandboxAbilityKind kind, string title, SandboxAbilityDefinition values = null)
        {
            UnitAbilityData ability = database.FindById(unitId)?.FindAbility(abilityId);
            if (ability == null || ability.Kind != UnitAbilityKind.None || ability.Status == UnitAbilityStatus.Disabled)
                return;
            SandboxAbilityDefinition source = values ?? SandboxAbilityDefaults.Create(kind, abilityId, title);
            Write(ability, source);
            ability.SetText(title, SandboxAbilityDefaults.Explain(source));
            ability.SetStatus(UnitAbilityStatus.Active);
            changes.Add(unitId + "/" + abilityId + ": механика " + kind + ", активна");
        }

        private static void AddIfMissing(UnitDatabaseAsset database, List<string> changes, string unitId, string abilityId,
            UnitCombatBrick brick, SandboxAbilityKind kind, string title)
        {
            UnitDefinitionData unit = database.FindById(unitId);
            if (unit == null || unit.FindAbility(abilityId) != null)
                return;
            unit.AddAbility(Build(abilityId, title, brick, SandboxAbilityDefaults.Create(kind, abilityId, title)));
            changes.Add(unitId + "/" + abilityId + ": новая способность " + kind);
        }

        private static void Disable(UnitDatabaseAsset database, List<string> changes, string unitId, string abilityId, string note)
        {
            UnitAbilityData ability = database.FindById(unitId)?.FindAbility(abilityId);
            if (ability == null || ability.Status == UnitAbilityStatus.Disabled)
                return;
            ability.SetStatus(UnitAbilityStatus.Disabled);
            ability.SetText(null, ability.Description + (string.IsNullOrEmpty(ability.Description) ? string.Empty : "\n") + note);
            changes.Add(unitId + "/" + abilityId + ": отключена");
        }

        private static void Write(UnitAbilityData ability, SandboxAbilityDefinition source)
        {
            UnitAbilityKind kind = (UnitAbilityKind)Enum.Parse(typeof(UnitAbilityKind), source.Kind.ToString());
            ability.SetMechanic(kind, source.Cooldown, source.RangeMin, source.RangeMax, source.PathMin, source.PathMax,
                (float)source.DamageMultiplier, source.Amount, source.ObjectHitPoints, source.Duration, source.Limit, source.Push, source.SpawnTypeId);
        }

        private static UnitAbilityData Build(string id, string title, UnitCombatBrick brick, SandboxAbilityDefinition source)
        {
            UnitAbilityData ability = UnitAbilityData.Create(id, title, brick, UnitAbilityStatus.Active, SandboxAbilityDefaults.Explain(source));
            Write(ability, source);
            return ability;
        }

        private static UnitAbilityData Ability(string id, string title, UnitCombatBrick brick, SandboxAbilityKind kind)
        {
            return Build(id, title, brick, SandboxAbilityDefaults.Create(kind, id, title));
        }

        // Бриф §14: стартовые параметры существ второго эшелона. Теги
        // «Гроза людей», «Бронированный» не раздаются. Ржавник и Межевой
        // вязень не заводятся (пока убраны / отложены).
        public static List<UnitDefinitionData> SecondTier()
        {
            return new List<UnitDefinitionData>
            {
                UnitDefinitionData.CreateCreature("podkopen", "Подкопень", UnitSize.Large, 16, 3, 2, 4, 3, 4, 1, new string[0],
                    new[] { Ability("podkopen.burrow", "Подкоп", UnitCombatBrick.HiddenBurrow, SandboxAbilityKind.Burrow) }),
                UnitDefinitionData.CreateCreature("golosoed", "Голосоед", UnitSize.Medium, 12, 2, 1, 3, 4, 6, 1, new string[0],
                    new[] { Ability("golosoed.call", "Зов", UnitCombatBrick.ForcedMovement, SandboxAbilityKind.Call) }),
                UnitDefinitionData.CreateCreature("kostyanoi_sadovnik", "Костяной садовник", UnitSize.Large, 18, 3, 2, 4, 3, 3, 1, new string[0],
                    new[] { Ability("kostyanoi_sadovnik.roots", "Пустить корни", UnitCombatBrick.CorpseInteraction, SandboxAbilityKind.ConsumeCorpse) }),
                UnitDefinitionData.CreateCreature("nitnik", "Нитник", UnitSize.Medium, 12, 2, 1, 3, 4, 5, 1, new string[0],
                    new[] { Ability("nitnik.thread", "Сплести нить", UnitCombatBrick.HazardTileState, SandboxAbilityKind.WeaveThread) }),
                UnitDefinitionData.CreateCreature("rechnoy_uzel", "Речной узел", UnitSize.Large, 36, 4, 3, 5, 1, 2, 1, new string[0],
                    new[]
                    {
                        Ability("rechnoy_uzel.tendrils", "Захват жгутом", UnitCombatBrick.GrappleTether, SandboxAbilityKind.TendrilGrab),
                        Ability("rechnoy_uzel.flood", "Разлив", UnitCombatBrick.HazardTileState, SandboxAbilityKind.Flood)
                    }),
                UnitDefinitionData.CreateCreature("khozyain_temnoy_yamy", "Хозяин тёмной ямы", UnitSize.Large, 40, 4, 3, 5, 2, 3, 1, new string[0],
                    new[] { Ability("khozyain_temnoy_yamy.pits", "Провалы", UnitCombatBrick.HiddenBurrow, SandboxAbilityKind.PitCycle) }),
                UnitDefinitionData.CreateCreature("gnezdovaya_matka", "Гнездовая матка", UnitSize.Large, 30, 2, 2, 3, 1, 2, 1, new string[0],
                    new[] { Ability("gnezdovaya_matka.eggs", "Отложить яйцо", UnitCombatBrick.Extension, SandboxAbilityKind.LayEgg) })
            };
        }
    }
}
