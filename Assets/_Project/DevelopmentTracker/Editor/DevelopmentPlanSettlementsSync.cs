using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (29.10.2026): в ПР-12 добавляется поставка
    // 12Т «Поселения, торговля, корчма и доска» (задание v1.0 и дополнение
    // «Доска объявлений»). Отмечено только то, что проверено тестами при
    // реализации; ручные проверки вёрстки и редакторов остаются открытыми.
    [InitializeOnLoad]
    public static class DevelopmentPlanSettlementsSync
    {
        private const string Marker = "[PR12T_SETTLEMENTS_V1_SYNCED]";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string SpecDoc = "ProjectDocs/SETTLEMENTS_TRADE_TAVERN_SPEC.md";
        private const string GuideDoc = "ProjectDocs/SETTLEMENTS_AUTHOR_GUIDE.md";

        static DevelopmentPlanSettlementsSync()
        {
            EditorApplication.delayCall += Sync;
        }

        public static void Sync()
        {
            DevelopmentPlanAsset plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(DevelopmentPlanBootstrap.AssetPath);
            if (plan == null || (plan.projectNotes ?? string.Empty).Contains(Marker))
                return;
            if (!Apply(plan))
                return;
            plan.projectNotes = string.IsNullOrWhiteSpace(plan.projectNotes) ? Marker : plan.projectNotes + "\n" + Marker;
            EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();
        }

        // Чистая часть — для EditMode-тестов. False, если ПР-12 ещё нет.
        public static bool Apply(DevelopmentPlanAsset plan)
        {
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            if (freePlay == null)
                return false;
            int order = 0;
            foreach (DevelopmentTaskData existing in freePlay.tasks)
                order = System.Math.Max(order, existing.order + 1);
            foreach (DevelopmentTaskData task in BuildTasks())
            {
                if (freePlay.tasks.Exists(existing => existing != null && existing.id == task.id))
                    continue;
                task.order = order++;
                freePlay.tasks.Add(task);
            }
            return true;
        }

        public static List<DevelopmentTaskData> BuildTasks()
        {
            return new List<DevelopmentTaskData>
            {
                Task("PR12T-T01", "12Т-1 · База предметов и эффекты из данных", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck,
                    "Каталог вещей — «База предметов» (Kingdom Survival → База предметов); прежние ID, владельцы, заряды и выдачи сохранены; эффекты «пока надето» и «при использовании» (HP, изнеможение, припасы, знание) без switch по ID; рабочие товары.",
                    new[] { ("Старые вещи и их поведение перенесены (EditMode)", true), ("Новый расходник из данных работает и не тратит заряд без пользы (EditMode)", true),
                            ("Ожидающий в лагере не даёт вещей сцене (EditMode)", true), ("Редактор: создать, дублировать, картинка, эффект, Undo — проверено руками", false) }),
                Task("PR12T-T02", "12Т-2 · База поселений и экран поселения", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck,
                    "Профили, варианты, люди, действия, привязка к местам и деревням генератора; экран поселения с сохраняемым присутствием (с лагерем и без); «Прогуляться» — только явным действием.",
                    new[] { ("Профили деревень детерминированы и в сохранении (EditMode)", true), ("Вход → экран → выход к лагерю в сцене (PlayMode)", true),
                            ("Вёрстка 1920×1080 и 1280×720 просмотрена на снимках", true), ("Редактор: предпросмотр кадра, выбор диалога, проверка ссылок — руками", false) }),
                Task("PR12T-T03", "12Т-3 · Торговля корзиной", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.Completed,
                    "Корзина, конечный запас и деньги торговца, выкуп того же экземпляра, припасы, обновление по игровым часам, подтверждение «всё или ничего», защита от повтора.",
                    new[] { ("Смешанная сделка, нехватка денег, слоты, некорректные корзины, повтор (EditMode)", true), ("Обновление только игровым временем, уникальное не возвращается (EditMode)", true),
                            ("Сделка, повторный клик и отмена корзины в сцене (PlayMode)", true) }),
                Task("PR12T-T04", "12Т-4 · Корчма, поручения, ночлег", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck,
                    "Хозяин, посетители, платные сведения (оплата и знание атомарны), поручения с общим состоянием, срок выполнения, ночлег общим временем с лечением только вошедших.",
                    new[] { ("Сведения не продаются дважды; поручение общее для доски и корчмы; срок и провал (EditMode)", true), ("Ночлег платится раз, лечит только вошедших (EditMode)", true),
                            ("Сцена рассказа и возвращение в корчму без хода времени (PlayMode)", true), ("Ночлег до утра в игре — руками", false) }),
                Task("PR12T-T05", "12Т-5 · Доска объявлений", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.Completed,
                    "Типы и носители объявлений, сроки и повтор, условия появления и исчезновения, только подключённые действия, чтение ничего не принимает.",
                    new[] { ("Последствия меняют доску, сроки и повтор, знание один раз (EditMode)", true), ("Чтение в сцене не принимает задание (PlayMode)", true) }),
                Task("PR12T-T06", "12Т-6 · Пилот «Тайник у старого русла»", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck,
                    "Сведения у возчика или берестой, область поиска на карте, находка в достижимой точке, нож один раз, возвращение и закрытие поручения; три рабочих профиля и сцены.",
                    new[] { ("Тайник достижим, область — не точка, нож один раз, сохранение (EditMode)", true), ("Сцены примера валидны (EditMode)", true),
                            ("Сквозной проход пилота в свободной игре без Debug — руками", false) }),
                Task("PR12T-T07", "12Т-7 · Приёмка поселений", DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NotStarted,
                    "Ручной проход связного пилота: группа, торговля, новый предмет, слух и просьба, сведения, тайник, возвращение, ночлег, сохранение и загрузка; редакторы глазами автора.",
                    new[] { ("Пилот пройден руками", false), ("Автор без C# настроил изображение, товар и продал его", false) })
            };
        }

        private static DevelopmentTaskData Task(string id, string title, DevelopmentTaskCategory category, DevelopmentTaskStatus status,
            string details, (string Text, bool Done)[] criteria)
        {
            DevelopmentTaskData task = new DevelopmentTaskData
            {
                id = id,
                title = title,
                category = category,
                status = status,
                details = details,
                priority = DevelopmentTaskPriority.Now,
                verificationType = category == DevelopmentTaskCategory.Code ? DevelopmentVerificationType.EditMode : DevelopmentVerificationType.Manual
            };
            if (status == DevelopmentTaskStatus.Completed)
                task.completedAt = "2026-10-29";
            task.fileReferences.Add(PlanDoc);
            task.fileReferences.Add(SpecDoc);
            task.fileReferences.Add(GuideDoc);
            foreach ((string text, bool done) in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = text, done = done });
            task.manualChecks.Add("Проверить в игре");
            return task;
        }
    }
}
