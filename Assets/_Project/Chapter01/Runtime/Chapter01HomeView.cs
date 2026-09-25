using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    // ПР-12А: HomeObjectView / HomeCareView — общие модели Core (CampaignContent.cs).

    // ПР-07А-2: модель экрана Дома — переводит существующие флаги главы,
    // Chapter01HomeState и функции Дома в текст. Ничего не меняет.
    public static class Chapter01HomeView
    {
        public const string WaterId = "home.object.water";
        public const string MillId = "home.object.mill";
        public const string DamId = "home.object.dam";
        public const string WalkwayId = "home.object.mill_walkway";
        public const string YardDeckId = "home.object.yard_deck";
        public const string LivestockId = "home.object.livestock";

        // Порядок забот (§7.1): сюжетное решение → нехватка/остановленная
        // необходимая функция → доступные работы → идущие работы.
        public const int PriorityStory = HomeCares.PriorityStory;
        public const int PriorityNeed = HomeCares.PriorityNeed;
        public const int PriorityOptional = HomeCares.PriorityOptional;
        public const int PriorityRunning = HomeCares.PriorityRunning;

        public static IReadOnlyList<HomeObjectView> DescribeObjects(GameState gameState)
        {
            List<HomeObjectView> objects = new List<HomeObjectView>();
            if (gameState == null || !Chapter01Crisis.IsActive(gameState))
                return objects;

            NarrativeStateData state = gameState.Narrative ?? new NarrativeStateData();
            Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);
            bool atHome = !HomePeopleService.HasDeparted(gameState);

            objects.Add(Water(snapshot.Water));
            objects.Add(Mill(snapshot.Mill));
            objects.Add(Dam(state, atHome));
            objects.Add(Walkway(snapshot.Walkway));
            HomeObjectView yard = YardDeck(gameState);
            if (yard != null)
                objects.Add(yard);
            objects.Add(Livestock(snapshot.Livestock, snapshot.Sound));
            foreach (HomeObjectView view in objects)
                view.Short = ShortOf(view.Id, view.StateKey);
            return objects;
        }

        private static HomeObjectView Water(Chapter01HomeWaterState water)
        {
            HomeObjectView view = new HomeObjectView { Id = WaterId, Title = "Вода", StateKey = water.ToString() };
            switch (water)
            {
                case Chapter01HomeWaterState.FloodDisturbed:
                    view.State = "Паводок сбил русло — вода идёт мутно и не там, где раньше.";
                    view.Source = "Видели сами в ночь паводка.";
                    break;
                case Chapter01HomeWaterState.Wrong:
                    view.State = "Вода ведёт себя неправильно: идёт, когда не должна, и стоит, когда должна идти.";
                    view.Source = "Замечено после ночного удара на мельнице. Ремонт этого не объясняет.";
                    break;
                case Chapter01HomeWaterState.RestoredOldPattern:
                    view.State = "Вода снова идёт по-старому, как её вели раньше.";
                    view.Source = "Видно после старого ремонта плотины.";
                    break;
                case Chapter01HomeWaterState.AlteredByNewRepair:
                    view.State = "Вода идёт иначе, чем прежде: новый ремонт повёл её по-своему.";
                    view.Source = "Видно после нового ремонта плотины.";
                    break;
                default:
                    view.State = "Вода идёт как обычно.";
                    view.Source = "Так было всегда, сколько помнят.";
                    break;
            }
            return view;
        }

        private static HomeObjectView Mill(Chapter01HomeMillState mill)
        {
            HomeObjectView view = new HomeObjectView { Id = MillId, Title = "Мельница", StateKey = mill.ToString() };
            switch (mill)
            {
                case Chapter01HomeMillState.DamagedOrStopped:
                    view.State = "Мельница стоит: паводок разбил настил у колеса.";
                    view.Source = "Видели сами после паводка.";
                    break;
                case Chapter01HomeMillState.RestoredOldWay:
                    view.State = "Мельница снова работает — по-старому, со следами ремонта.";
                    view.Source = "Видно после старого ремонта.";
                    break;
                case Chapter01HomeMillState.RestoredNewWay:
                    view.State = "Мельница работает по-новому: колесо звучит иначе.";
                    view.Source = "Видно после нового ремонта.";
                    break;
                default:
                    view.State = "Мельница работает.";
                    view.Source = "Колесо слышно с утра.";
                    break;
            }
            return view;
        }

        // Плотина — из существующих флагов паводка, выбора ремонта и его
        // завершения. Выбранный способ ещё не означает выполненную работу.
        private static HomeObjectView Dam(NarrativeStateData state, bool atHome)
        {
            HomeObjectView view = new HomeObjectView { Id = DamId, Title = "Плотина" };
            Chapter01RepairChoice choice = Chapter01StoryDirector.GetRepairChoice(state);
            bool completed = state.HasFlag(Chapter01Ids.Flags.RepairCompleted);

            if (!state.HasFlag(Chapter01Ids.Flags.FloodHappened))
            {
                view.StateKey = "Intact";
                view.State = "Плотина держит воду.";
                view.Source = "Так было всегда.";
            }
            else if (choice == Chapter01RepairChoice.None)
            {
                view.StateKey = "Damaged";
                view.State = "Плотина повреждена паводком.";
                view.Source = "Видели сами в ночь паводка.";
                if (atHome)
                {
                    view.ActionLabel = "Осмотреть";
                    view.ActionDialogueId = Chapter01Ids.Dialogues.D05;
                }
            }
            else if (!completed)
            {
                view.StateKey = choice == Chapter01RepairChoice.Old ? "ChosenOld" : "ChosenNew";
                view.State = choice == Chapter01RepairChoice.Old
                    ? "Способ ремонта выбран — по-старому. Работа ещё не завершена."
                    : "Способ ремонта выбран — по-новому. Работа ещё не завершена.";
                view.Source = "Решено на осмотре плотины.";
            }
            else
            {
                view.StateKey = choice == Chapter01RepairChoice.Old ? "RepairedOld" : "RepairedNew";
                view.State = choice == Chapter01RepairChoice.Old
                    ? "Плотина отремонтирована по-старому."
                    : "Плотина отремонтирована по-новому.";
                view.Source = "Работа завершена.";
            }
            return view;
        }

        private static HomeObjectView Walkway(Chapter01HomeWalkwayState walkway)
        {
            HomeObjectView view = new HomeObjectView { Id = WalkwayId, Title = "Настил у мельницы", StateKey = walkway.ToString() };
            switch (walkway)
            {
                case Chapter01HomeWalkwayState.Destroyed:
                    view.State = "Настил у мельницы разбит паводком.";
                    view.Source = "Видели сами после паводка.";
                    break;
                case Chapter01HomeWalkwayState.RestoredOldWay:
                    view.State = "Настил у мельницы восстановлен по-старому.";
                    view.Source = "Видно после ремонта.";
                    break;
                case Chapter01HomeWalkwayState.RebuiltNewWay:
                    view.State = "Настил у мельницы собран заново, по-новому.";
                    view.Source = "Видно после ремонта.";
                    break;
                default:
                    view.State = "Настил у мельницы цел.";
                    view.Source = "По нему ходят каждый день.";
                    break;
            }
            return view;
        }

        // Хозяйственный настил во дворе — отдельная работа ПР-06, не
        // сюжетный настил у мельницы.
        private static HomeObjectView YardDeck(GameState gameState)
        {
            HomeWorkState deck = HomeLife.FindWork(gameState, HomeLife.YardDeckWorkId);
            if (deck == null && !Chapter01HomeActivities.IsYardDeckOffered(gameState))
                return null;

            HomeObjectView view = new HomeObjectView { Id = YardDeckId, Title = "Хозяйственный настил во дворе" };
            if (deck == null)
            {
                view.StateKey = "Broken";
                view.State = "Разбит паводком. По двору обходят лужи.";
            }
            else if (deck.Completed)
            {
                view.StateKey = "Restored";
                view.State = "Восстановлен: по двору снова ходят напрямую.";
            }
            else
            {
                view.StateKey = "Repairing";
                view.State = "Ремонт идёт.";
            }
            view.Source = "Видно во дворе.";
            return view;
        }

        private static HomeObjectView Livestock(Chapter01HomeLivestockState livestock, Chapter01HomeSoundState sound)
        {
            HomeObjectView view = new HomeObjectView { Id = LivestockId, Title = "Скот и звук Дома", StateKey = livestock.ToString() };
            switch (livestock)
            {
                case Chapter01HomeLivestockState.AvoidingWater:
                    view.State = "Скот не идёт к старому водопою.";
                    break;
                case Chapter01HomeLivestockState.Lost:
                    view.State = "Часть скота унёс паводок.";
                    break;
                default:
                    view.State = "Скот спокоен.";
                    break;
            }

            switch (sound)
            {
                case Chapter01HomeSoundState.MillSilent:
                    view.Source = "Утром тихо: колеса не слышно.";
                    break;
                case Chapter01HomeSoundState.UnevenWaterAndMill:
                    view.Source = "Вода и колесо звучат неровно.";
                    break;
                case Chapter01HomeSoundState.RestoredButChanged:
                    view.Source = "Звук вернулся, но стал другим.";
                    break;
                default:
                    view.Source = "Обычное утро: слышно колесо и воду.";
                    break;
            }
            return view;
        }

        private static string ShortOf(string objectId, string stateKey)
        {
            bool mill = objectId == MillId;
            switch (stateKey)
            {
                case "Normal": return "спокойна";
                case "FloodDisturbed": return "мутная после паводка";
                case "Wrong": return "ведёт себя неправильно";
                case "RestoredOldPattern": return "по-старому";
                case "AlteredByNewRepair": return "по-новому";
                case "RunningNormally": return "работает";
                case "DamagedOrStopped": return "стоит";
                case "RestoredOldWay": return mill ? "работает по-старому" : "восстановлен по-старому";
                case "RestoredNewWay": return "работает по-новому";
                case "RebuiltNewWay": return "собран заново";
                case "OldIntact": return "цел";
                case "Destroyed": return "разбит";
                case "Intact": return "цела";
                case "Damaged": return "повреждена";
                case "ChosenOld": return "выбран ремонт по-старому";
                case "ChosenNew": return "выбран ремонт по-новому";
                case "RepairedOld": return "отремонтирована по-старому";
                case "RepairedNew": return "отремонтирована по-новому";
                case "Broken": return "разбит";
                case "Repairing": return "ремонт идёт";
                case "Restored": return "восстановлен";
                case "Calm": return "спокоен";
                case "AvoidingWater": return "сторонится воды";
                case "Lost": return "часть унесло";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // Заботы
        // ------------------------------------------------------------------

        public static IReadOnlyList<HomeCareView> DescribeCares(GameState gameState)
        {
            if (gameState == null)
                return new List<HomeCareView>();

            // ПР-12А: еда и раненые — общие заботы любого Дома (Core, HomeCares).
            List<HomeCareView> cares = HomeCares.DescribeCommon(gameState);
            bool atHome = !HomePeopleService.HasDeparted(gameState);

            AddMaintenance(gameState, cares, atHome);
            AddFishing(gameState, cares);

            cares.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            return cares;
        }

        private static void AddMaintenance(GameState gameState, List<HomeCareView> cares, bool atHome)
        {
            HomeWorkState deck = HomeLife.FindWork(gameState, HomeLife.YardDeckWorkId);
            HomeFunctionReport maintenance = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.MaintenanceId);

            if (deck == null)
            {
                if (!Chapter01HomeActivities.IsYardDeckOffered(gameState))
                    return;

                double hours = maintenance.Rate > 0.0 ? HomeLife.YardDeckRequiredWork / maintenance.Rate : 0.0;
                cares.Add(new HomeCareView
                {
                    Id = "home.care.yard_deck",
                    Title = "Хозяйственный настил во дворе",
                    Cause = "Разбит паводком: по двору обходят лужи.",
                    Status = maintenance.Rate > 0.0
                        ? "Сделает " + maintenance.ExecutorName + " — около " + (int)Math.Ceiling(hours) + " ч." +
                          (maintenance.Status == HomeFunctionStatus.Limited ? " (медленнее, без Лады)" : "")
                        : "Сейчас некому: " + maintenance.Reason + ".",
                    Detail = "Цена: " + HomeLife.YardDeckGoldCost + " золота один раз. Плотину, мельницу и воду этот ремонт не касается.",
                    Priority = PriorityOptional,
                    ActionLabel = "Восстановить · " + HomeLife.YardDeckGoldCost + " золота",
                    Action = HomeCareAction.StartYardDeck,
                    ActionEnabled = atHome && gameState.Gold >= HomeLife.YardDeckGoldCost
                });
                return;
            }

            if (deck.Completed)
                return;

            double remaining = HomeLife.RemainingWork(deck);
            cares.Add(new HomeCareView
            {
                Id = "home.care.yard_deck",
                Title = "Хозяйственный настил во дворе",
                Cause = "Ремонт начат.",
                Status = maintenance.Rate > 0.0
                    ? "Работает " + maintenance.ExecutorName + " — осталось около " +
                      (int)Math.Ceiling(remaining / maintenance.Rate) + " ч."
                    : "Приостановлен: " + maintenance.Reason + ". Сделанное не пропадёт.",
                Priority = maintenance.Rate > 0.0 ? PriorityRunning : PriorityNeed,
                Urgent = maintenance.Rate <= 0.0
            });
        }

        // ПР-07Б: ловля в заботах — только когда остановлена.
        private static void AddFishing(GameState gameState, List<HomeCareView> cares)
        {
            if (!HomeFunctionResolver.IsFishingOpen(gameState))
                return;

            HomeFunctionReport fishing = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.FishingId);
            if (fishing.Status == HomeFunctionStatus.Working)
                return;

            cares.Add(new HomeCareView
            {
                Id = "home.care.fishing",
                Title = "Рыбная ловля остановлена",
                Cause = Capitalize(fishing.Reason) + ".",
                Status = "Новых поступлений от ловли нет.",
                Detail = EarnedLine(gameState),
                Priority = PriorityRunning
            });
        }

        // Строка функции ловли для «Заботы» (null — ловля ещё не открыта).
        public static string FishingLine(GameState gameState)
        {
            if (gameState == null || !HomeFunctionResolver.IsFishingOpen(gameState))
                return null;

            HomeFunctionReport fishing = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.FishingId);
            string earned = EarnedLine(gameState);
            if (fishing.Status == HomeFunctionStatus.Working)
            {
                return "Рыбная ловля — Тихон дома. До " + HomeLife.FishingFoodPerFullDay +
                       " пищи за полные сутки работы." + (earned != null ? " " + earned : string.Empty);
            }

            return "Рыбная ловля остановлена: " + fishing.Reason + " — новых поступлений нет." +
                   (earned != null ? " " + earned : string.Empty);
        }

        private static string EarnedLine(GameState gameState)
        {
            double earned = gameState.People != null ? gameState.People.FishingEarned : 0.0;
            if (earned < 0.05)
                return null;
            return "Заработано за сегодня: " + earned.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') +
                   "; поступит в ближайшую полночь.";
        }

        private static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        public static string StatusLine(HomeFunctionReport report)
        {
            return HomeCares.StatusLine(report);
        }
    }
}
