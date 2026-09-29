using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (29.09.2026): в ПР-12 добавляется поставка
    // 12Ж «Боевой каталог существ», после ПР-13 — новый этап ПР-16 «Боевые
    // кирпичи существ» (PROTOTYPE_DEVELOPMENT_PLAN.md). Существующие задачи
    // не трогаются.
    [InitializeOnLoad]
    public static class DevelopmentPlanCreatureCatalogSync
    {
        private const string Marker = "[PR12J_CREATURE_CATALOG_V1_SYNCED]";
        public const string MechanicsPhaseId = "PR16_CREATURE_MECHANICS";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string CatalogDoc = "ProjectDocs/BESTIARY_COMBAT_PASSPORTS.md";

        static DevelopmentPlanCreatureCatalogSync()
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

            plan.projectNotes = string.IsNullOrWhiteSpace(plan.projectNotes)
                ? Marker
                : plan.projectNotes + "\n" + Marker;
            EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();
        }

        // Чистая часть — для EditMode-тестов. False, если ПР-12 или ПР-13 ещё нет.
        public static bool Apply(DevelopmentPlanAsset plan)
        {
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            if (freePlay == null || plan.FindPhase(DevelopmentPlanPr12FreePlaySync.ReleasePhaseId) == null)
                return false;

            int order = 0;
            foreach (DevelopmentTaskData existing in freePlay.tasks)
                order = System.Math.Max(order, existing.order + 1);
            foreach (DevelopmentTaskData task in BuildCatalogTasks())
            {
                if (freePlay.tasks.Exists(existing => existing != null && existing.id == task.id))
                    continue;
                task.order = order++;
                freePlay.tasks.Add(task);
            }

            if (plan.FindPhase(MechanicsPhaseId) == null)
            {
                DevelopmentPhaseData mechanics = new DevelopmentPhaseData
                {
                    id = MechanicsPhaseId,
                    title = "ПР-16. Боевые кирпичи существ",
                    purpose = "Общие механики COMBAT-B3 включают отложенные способности существ каталога без уникального кода под каждое существо.",
                    order = 116,
                    acceptanceSummary = "Существа каталога требуют разных решений на поле; реакция на каждое не совпадает с реакцией на обычного зверя."
                };
                mechanics.dependencies.Add(DevelopmentPlanPr12FreePlaySync.ReleasePhaseId);
                mechanics.tasks = BuildMechanicsTasks();
                plan.phases.Add(mechanics);
            }
            return true;
        }

        public static List<DevelopmentTaskData> BuildCatalogTasks()
        {
            return new List<DevelopmentTaskData>
            {
                Task("PR12J-T01", "12Ж-1 · Канон и документы каталога существ", DevelopmentTaskCategory.Documentation,
                    "Поправки COMBAT-B5/B6 и шлюза бестиария; «Прототипный каталог 24»; составы; второй эшелон; заказ художнику.",
                    new[] { "Каталог и поправки записаны в BESTIARY.md и BESTIARY_COMBAT_PASSPORTS.md" }),
                Task("PR12J-T02", "12Ж-2 · Модель и засев 24 существ", DevelopmentTaskCategory.Data,
                    "Размер, способности со статусом «ждёт механики», составы боя; отсутствие рисунка — «ждёт рисунка»; повторяемый засев.",
                    new[] { "24 существа с числами каталога", "Проверка базы без ошибок", "Повторный засев ничего не меняет" }),
                Task("PR12J-T03", "12Ж-3 · Бой на нынешнем фундаменте", DevelopmentTaskCategory.Code,
                    "До 8 противников; «Защитник» даёт +25% в стойке; жетон вместо отсутствующего рисунка.",
                    new[] { "Бой с 8 противниками создаётся", "Защитник в стойке получает 75%" }),
                Task("PR12J-T04", "12Ж-4 · Сборка состава в тестовом бою", DevelopmentTaskCategory.UI,
                    "Засада по умолчанию, готовый состав или свой состав до 8 существ.",
                    new[] { "Любой из 8 составов запускается и доигрывается" }),
                Task("PR12J-T05", "12Ж-5 · Цена противника в опыте", DevelopmentTaskCategory.Code,
                    "Ход, Инициатива, Дальность и боевые теги входят в цену; веса в Базе развития.",
                    new[] { "Стрелок и бронированный стоят дороже прежнего", "Окно базы и ядро считают одинаково" })
            };
        }

        public static List<DevelopmentTaskData> BuildMechanicsTasks()
        {
            List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
            {
                Task("PR16-T00", "16-0 · Каркас способностей и приоритеты ИИ", DevelopmentTaskCategory.Code,
                    "Исполнитель способностей по данным; приоритет цели как данные; стрелки держат дистанцию. Криволап, Падальный многоног.",
                    new[] { "Способность включается статусом в Базе существ" }),
                Task("PR16-T01", "16-1 · Status / Mark", DevelopmentTaskCategory.Code,
                    "Оцепенение, Ослабление, Метка. Падальный многоног, Звонник.", new[] { "Состояние видно на поле и в карточке" }),
                Task("PR16-T02", "16-2 · Forced Movement (Push / Pull)", DevelopmentTaskCategory.Code,
                    "Береговик, Лозовый хватун, Лось-секач.", new[] { "Толчок упирается в препятствие и край поля" }),
                Task("PR16-T03", "16-3 · Charge, Multi-Push, сбитая стойка", DevelopmentTaskCategory.Code,
                    "Пепельный вепрь, Кабан, Медведь, Белорев, Чернолоб.", new[] { "Разгон виден до удара", "Сорванный рывок открывает фланг" }),
                Task("PR16-T04", "16-4 · Tile State / Hazard", DevelopmentTaskCategory.Code,
                    "Мокрый, затопленный, рыхлый гекс. Живучий гнилец, Береговик.", new[] { "Состояние гекса видно на поле" }),
                Task("PR16-T05", "16-5 · Telegraph", DevelopmentTaskCategory.Code,
                    "Подсвеченные гексы будущего удара. Звонник, Чернолоб.", new[] { "Удар приходит туда, куда показан" }),
                Task("PR16-T06", "16-6 · Grapple / Tether / Escape", DevelopmentTaskCategory.Code,
                    "Тинный ползун, Лозовый хватун.", new[] { "Освободиться можно действием или перерубанием" }),
                Task("PR16-T07", "16-7 · Attach / Persistent Effect", DevelopmentTaskCategory.Code,
                    "Кровяной клещень.", new[] { "Снять можно самому или действием союзника" }),
                Task("PR16-T08", "16-8 · Hidden / Burrow и знание до боя", DevelopmentTaskCategory.Code,
                    "Срубник, затем Подкопень.", new[] { "Разведка меняет стартовое состояние боя" }),
                Task("PR16-T09", "16-9 · Corpse Interaction", DevelopmentTaskCategory.Code,
                    "Костяной садовник.", new[] { "Тело на поле — объект боя" }),
                Task("PR16-T10", "16-10 · Второй эшелон существ", DevelopmentTaskCategory.Content,
                    "Подкопень, Ржавник, Голосоед, Межевой вязень, Костяной садовник, Нитник, Речной узел, Хозяин тёмной ямы, Гнездовая матка — по готовности кирпичей.",
                    new[] { "Каждое существо заводится только со своей механикой" })
            };
            for (int i = 0; i < tasks.Count; i++)
                tasks[i].order = i;
            return tasks;
        }

        private static DevelopmentTaskData Task(string id, string title, DevelopmentTaskCategory category, string details, string[] criteria)
        {
            DevelopmentTaskData task = new DevelopmentTaskData
            {
                id = id,
                title = title,
                category = category,
                details = details,
                priority = id.StartsWith("PR12J") ? DevelopmentTaskPriority.Now : DevelopmentTaskPriority.Later,
                verificationType = category == DevelopmentTaskCategory.Code || category == DevelopmentTaskCategory.Data
                    ? DevelopmentVerificationType.EditMode
                    : DevelopmentVerificationType.Manual
            };

            task.fileReferences.Add(PlanDoc);
            task.fileReferences.Add(CatalogDoc);
            foreach (string criterion in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criterion });
            if (category != DevelopmentTaskCategory.Documentation)
                task.manualChecks.Add("Проверить в игре");
            return task;
        }
    }
}
