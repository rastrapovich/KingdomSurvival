using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (04.11.2026): в ПР-12 добавляется поставка
    // 12У «Переход основной карты на MapLab» (мастер-бриф v1.3). Отмечено
    // только проверенное; остальные этапы открыты до их реализации.
    [InitializeOnLoad]
    public static class DevelopmentPlanMapLabMigrationSync
    {
        private const string Marker = "[PR12U_MAPLAB_MIGRATION_V1_SYNCED]";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string AuditDoc = "ProjectDocs/MAPLAB_MIGRATION_AUDIT.md";

        static DevelopmentPlanMapLabMigrationSync()
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
                Task("PR12U-T01", "12У-A · Аудит и база сравнения", DevelopmentTaskCategory.Documentation, DevelopmentTaskStatus.Completed,
                    "Карта контрактов основной карты и MapLab, ограничения масштаба, противоречия и решения, контрольные кадры — ProjectDocs/MAPLAB_MIGRATION_AUDIT.md.",
                    new[] { ("Аудит записан, план дополнен поставкой 12У", true) }),
                Task("PR12U-T02", "12У-B · Окно «Генератор карты» и каталог ассетов", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck,
                    "Три вкладки: Карта / Ассеты / Проверка; десять основных настроек, «Внешний вид», точная настройка; каталог всех используемых рисунков: размер, яркость, тон, насыщенность, частота, групповая правка; профиль и Undo.",
                    new[] { ("Поля действуют на активную реализацию (EditMode)", true), ("Коррекция ассета нейтральна по умолчанию, рендер без изменений (EditMode + кадр)", true), ("Окно проверено руками", false) }),
                Task("PR12U-T03", "12У-C · Подгрузка, обзор, окраины", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted,
                    "Участки данных и покраски, очередь с бюджетами, освобождение, облегчённый обзор, реестр дальних замен, широкие природные окраины, камера по полному кадру.",
                    new[] { ("Швы участков и детерминизм (EditMode)", false), ("Замеры 256² → 2048²", false), ("Стороны и углы без пустоты", false) }),
                Task("PR12U-T04", "12У-D · Структура региона и исследование", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted,
                    "Различимые схемы районов и связей, переправы, обходы, места с причинами; один наполненный пилот.",
                    new[] { ("Разные seed дают разные схемы (EditMode)", false), ("Пилот пройден", false) }),
                Task("PR12U-T05", "12У-E · Деревни и поля", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted,
                    "Три семейства планировки, три размера, уличный каркас, группы полей, шесть отдельных зданий как ассеты.",
                    new[] { ("27 планировок воспроизводимы, входы достижимы (EditMode)", false), ("Новый декоративный тип через каталог", false) }),
                Task("PR12U-T06", "12У-F · MapLab в основной игре", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted,
                    "Runtime-часть MapLab в сборке, карта новой партии из MapLab, разметка для навигации, тумана и мест; старые партии читаются.",
                    new[] { ("Новая партия на карте MapLab (PlayMode)", false), ("Старое сохранение загружается", false), ("Сборка Player", false) }),
                Task("PR12U-T07", "12У-G · Услуги у отдельных зданий", DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted,
                    "Клик по зданию → подход → окно его услуги; общий экран не перехватывает карту; лагерь, ночлег, бой, возвраты.",
                    new[] { ("Таблица переноса услуг без потерь", false), ("Проход по зданиям (PlayMode)", false) }),
                Task("PR12U-T08", "12У-H · Замеры и поставка", DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NotStarted,
                    "Замеры производительности, контрольные кадры, инструкция автора, реестр замен, документация.",
                    new[] { ("Отчёт о производительности", false), ("Инструкция автора", false) })
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
                task.completedAt = "2026-11-04";
            task.fileReferences.Add(PlanDoc);
            task.fileReferences.Add(AuditDoc);
            foreach ((string text, bool done) in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = text, done = done });
            task.manualChecks.Add("Проверить в игре");
            return task;
        }
    }
}
