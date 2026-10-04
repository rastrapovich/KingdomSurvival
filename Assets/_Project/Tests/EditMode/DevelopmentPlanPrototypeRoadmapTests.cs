using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.DevelopmentTracker.Editor;
using NUnit.Framework;
using UnityEngine;

// Карта разработки ПР-00…ПР-13 (ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md)
// добавляется в существующий план без ошибок валидатора и без повторов.
public sealed class DevelopmentPlanPrototypeRoadmapTests
{
    private static DevelopmentPlanAsset NewSeededPlan()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        DevelopmentPlanSeedData.Populate(plan);
        return plan;
    }

    [Test]
    public void Apply_AddsFourteenRoadmapPhasesWithoutValidatorErrors()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            int before = plan.phases.Count;
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);

            Assert.AreEqual(before + 14, plan.phases.Count);
            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Apply_IsIdempotentAndSetsFirstMilestone()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            int afterFirst = plan.phases.Count;
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);

            Assert.AreEqual(afterFirst, plan.phases.Count);
            Assert.AreEqual(DevelopmentPlanPrototypeRoadmapSync.FirstMilestoneId, plan.currentMilestoneId);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Pr06Split_ReplacesTasksWith06AAnd06B_WithoutValidatorErrors()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            Assert.IsTrue(DevelopmentPlanPr06SplitSync.Apply(plan));

            DevelopmentPhaseData phase = plan.FindPhase("PR06_PEOPLE");
            Assert.AreEqual(7, phase.tasks.Count(t => t.id.StartsWith("PR06A-")));
            Assert.AreEqual(3, phase.tasks.Count(t => t.id.StartsWith("PR06B-")));

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Pr07Split_ReplacesTasksWithThreeDeliveries_WithoutValidatorErrors()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            Assert.IsTrue(DevelopmentPlanPr07SplitSync.Apply(plan));

            DevelopmentPhaseData phase = plan.FindPhase("PR07_HOME");
            Assert.AreEqual(6, phase.tasks.Count(t => t.id.StartsWith("PR07A1-")));
            Assert.AreEqual(5, phase.tasks.Count(t => t.id.StartsWith("PR07A2-")));
            Assert.AreEqual(4, phase.tasks.Count(t => t.id.StartsWith("PR07B-")));

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    // 27.09.2026: поставка 12Е (особенности по каталогу) добавляется в ПР-12
    // один раз, без ошибок проверки плана.
    [Test]
    public void Pr12Features_AddsTenTasksToFreePlay_Once()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            Assert.IsFalse(DevelopmentPlanPr12FeaturesSync.Apply(plan), "Без ПР-12 синхронизация не срабатывает.");
            DevelopmentPlanPr12FreePlaySync.Apply(plan);
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            int before = freePlay.tasks.Count;

            Assert.IsTrue(DevelopmentPlanPr12FeaturesSync.Apply(plan));
            Assert.IsTrue(DevelopmentPlanPr12FeaturesSync.Apply(plan));
            Assert.AreEqual(before + 10, freePlay.tasks.Count, "Повторный запуск не дублирует задачи.");
            Assert.AreEqual(10, freePlay.tasks.Count(t => t.id.StartsWith("PR12E-")));
            Assert.AreEqual(freePlay.tasks.Count, freePlay.tasks.Select(t => t.order).Distinct().Count());

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    // 29.09.2026: поставка 12Ж (каталог существ) и этап ПР-16 добавляются
    // один раз, без ошибок проверки плана.
    [Test]
    public void CreatureCatalog_AddsCatalogTasksAndMechanicsPhase_Once()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            Assert.IsFalse(DevelopmentPlanCreatureCatalogSync.Apply(plan), "Без ПР-12 синхронизация не срабатывает.");
            DevelopmentPlanPr12FreePlaySync.Apply(plan);
            DevelopmentPlanPr12FeaturesSync.Apply(plan);
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            int before = freePlay.tasks.Count;

            Assert.IsTrue(DevelopmentPlanCreatureCatalogSync.Apply(plan));
            Assert.IsTrue(DevelopmentPlanCreatureCatalogSync.Apply(plan));
            Assert.AreEqual(before + 5, freePlay.tasks.Count, "Повторный запуск не дублирует задачи.");
            Assert.AreEqual(freePlay.tasks.Count, freePlay.tasks.Select(t => t.order).Distinct().Count());
            DevelopmentPhaseData mechanics = plan.FindPhase(DevelopmentPlanCreatureCatalogSync.MechanicsPhaseId);
            Assert.IsNotNull(mechanics);
            Assert.AreEqual(1, plan.phases.Count(p => p.id == DevelopmentPlanCreatureCatalogSync.MechanicsPhaseId));
            CollectionAssert.Contains(mechanics.dependencies, DevelopmentPlanPr12FreePlaySync.ReleasePhaseId);
            Assert.AreEqual(11, mechanics.tasks.Count);

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    // 01.10.2026: поставка 12И (прямое управление героем на карте) добавляется в ПР-12 один раз.
    [Test]
    public void MapMovement_AddsTasksToFreePlay_Once()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            Assert.IsFalse(DevelopmentPlanMapMovementSync.Apply(plan), "Без ПР-12 синхронизация не срабатывает.");
            DevelopmentPlanPr12FreePlaySync.Apply(plan);
            DevelopmentPlanPr12FeaturesSync.Apply(plan);
            DevelopmentPlanCreatureCatalogSync.Apply(plan);
            DevelopmentPlanCreatureAnimationSync.Apply(plan);
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            int before = freePlay.tasks.Count;

            Assert.IsTrue(DevelopmentPlanMapMovementSync.Apply(plan));
            Assert.IsTrue(DevelopmentPlanMapMovementSync.Apply(plan));
            Assert.AreEqual(before + 5, freePlay.tasks.Count, "Повторный запуск не дублирует задачи.");
            Assert.AreEqual(freePlay.tasks.Count, freePlay.tasks.Select(t => t.order).Distinct().Count());

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    // 04.10.2026: поставка 12К (исследуемые места) добавляется в ПР-12 один раз.
    [Test]
    public void LocalExploration_AddsTasksToFreePlay_Once()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            Assert.IsFalse(DevelopmentPlanLocalExplorationSync.Apply(plan), "Без ПР-12 синхронизация не срабатывает.");
            DevelopmentPlanPr12FreePlaySync.Apply(plan);
            DevelopmentPlanMapMovementSync.Apply(plan);
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            int before = freePlay.tasks.Count;

            Assert.IsTrue(DevelopmentPlanLocalExplorationSync.Apply(plan));
            Assert.IsTrue(DevelopmentPlanLocalExplorationSync.Apply(plan));
            Assert.AreEqual(before + 5, freePlay.tasks.Count, "Повторный запуск не дублирует задачи.");
            Assert.AreEqual(freePlay.tasks.Count, freePlay.tasks.Select(t => t.order).Distinct().Count());

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    // 01.10.2026: поставка 12З (анимации существ) добавляется в ПР-12 один раз.
    [Test]
    public void CreatureAnimation_AddsTasksToFreePlay_Once()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            Assert.IsFalse(DevelopmentPlanCreatureAnimationSync.Apply(plan), "Без ПР-12 синхронизация не срабатывает.");
            DevelopmentPlanPr12FreePlaySync.Apply(plan);
            DevelopmentPlanPr12FeaturesSync.Apply(plan);
            DevelopmentPlanCreatureCatalogSync.Apply(plan);
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            int before = freePlay.tasks.Count;

            Assert.IsTrue(DevelopmentPlanCreatureAnimationSync.Apply(plan));
            Assert.IsTrue(DevelopmentPlanCreatureAnimationSync.Apply(plan));
            Assert.AreEqual(before + 4, freePlay.tasks.Count, "Повторный запуск не дублирует задачи.");
            Assert.AreEqual(freePlay.tasks.Count, freePlay.tasks.Select(t => t.order).Distinct().Count());

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    // Канон v1.45: ПР-12 — свободная игра; прежние ПР-12/ПР-13 сдвигаются
    // в ПР-14/ПР-13, главовая часть — в ПР-15. Задачи не теряются.
    [Test]
    public void Pr12FreePlay_InsertsPhase_ShiftsOldOnes_KeepsTasks_WithoutValidatorErrors()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);
            int replayTasks = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.ReplayPhaseId).tasks.Count;
            int releaseTasks = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.ReleasePhaseId).tasks.Count;

            Assert.IsTrue(DevelopmentPlanPr12FreePlaySync.Apply(plan));
            int phases = plan.phases.Count;
            Assert.IsTrue(DevelopmentPlanPr12FreePlaySync.Apply(plan));
            Assert.AreEqual(phases, plan.phases.Count, "Повторный запуск не добавляет этапы.");

            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            Assert.AreEqual(112, freePlay.order);
            Assert.AreEqual(3, freePlay.tasks.Count(t => t.id.StartsWith("PR12A-")));
            Assert.AreEqual(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId, plan.currentMilestoneId);
            Assert.AreEqual(114, plan.FindPhase(DevelopmentPlanPr12FreePlaySync.ReplayPhaseId).order);
            Assert.AreEqual(replayTasks, plan.FindPhase(DevelopmentPlanPr12FreePlaySync.ReplayPhaseId).tasks.Count);
            Assert.AreEqual(releaseTasks, plan.FindPhase(DevelopmentPlanPr12FreePlaySync.ReleasePhaseId).tasks.Count);
            Assert.IsNotNull(plan.FindPhase(DevelopmentPlanPr12FreePlaySync.HomeWaterPhaseId));

            List<ValidationIssue> errors = DevelopmentPlanValidator.Validate(plan)
                .Where(i => i.Severity == ValidationSeverity.Error)
                .ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.EntityId + ": " + e.Message)));
            Assert.IsFalse(DevelopmentPlanValidator.Validate(plan).Any(i => i.Message.Contains("одинаковый порядок")));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Roadmap_PhaseOrdersDoNotCollideWithChapterPhases()
    {
        DevelopmentPlanAsset plan = NewSeededPlan();
        try
        {
            DevelopmentPlanPrototypeRoadmapSync.Apply(plan);

            bool duplicateOrders = DevelopmentPlanValidator.Validate(plan)
                .Any(i => i.Message.Contains("одинаковый порядок"));
            Assert.IsFalse(duplicateOrders);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }
}
