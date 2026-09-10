using System.Collections.Generic;
using KingdomSurvival.DevelopmentTracker.Editor;
using NUnit.Framework;
using UnityEngine;

public sealed class DevelopmentPlanAssetTests
{
    private static DevelopmentTaskData MakeTask(string id, DevelopmentTaskStatus status, bool required)
    {
        return new DevelopmentTaskData { id = id, title = id, status = status, required = required };
    }

    [Test]
    public void ComputeStatus_AllRequiredCompleted_ReturnsCompleted()
    {
        DevelopmentPhaseData phase = new DevelopmentPhaseData
        {
            id = "P_TEST",
            tasks = new List<DevelopmentTaskData>
            {
                MakeTask("T1", DevelopmentTaskStatus.Completed, true),
                MakeTask("T2", DevelopmentTaskStatus.Completed, true),
                MakeTask("T3", DevelopmentTaskStatus.Completed, false)
            }
        };

        Assert.AreEqual(DevelopmentTaskStatus.Completed, phase.ComputeStatus());
    }

    [Test]
    public void ComputeStatus_AnyRequiredBlocked_ReturnsBlocked()
    {
        DevelopmentPhaseData phase = new DevelopmentPhaseData
        {
            id = "P_TEST",
            tasks = new List<DevelopmentTaskData>
            {
                MakeTask("T1", DevelopmentTaskStatus.Completed, true),
                MakeTask("T2", DevelopmentTaskStatus.Blocked, true)
            }
        };

        Assert.AreEqual(DevelopmentTaskStatus.Blocked, phase.ComputeStatus());
    }

    [Test]
    public void ComputeStatus_AnyRequiredNeedsUnityCheck_ReturnsNeedsUnityCheck()
    {
        DevelopmentPhaseData phase = new DevelopmentPhaseData
        {
            id = "P_TEST",
            tasks = new List<DevelopmentTaskData>
            {
                MakeTask("T1", DevelopmentTaskStatus.Completed, true),
                MakeTask("T2", DevelopmentTaskStatus.NeedsUnityCheck, true)
            }
        };

        Assert.AreEqual(DevelopmentTaskStatus.NeedsUnityCheck, phase.ComputeStatus());
    }

    [Test]
    public void ComputeStatus_OnlyDeferredRequiredTasks_ReturnsDeferred()
    {
        DevelopmentPhaseData phase = new DevelopmentPhaseData
        {
            id = "P_TEST",
            tasks = new List<DevelopmentTaskData>
            {
                MakeTask("T1", DevelopmentTaskStatus.Deferred, false)
            }
        };

        Assert.AreEqual(DevelopmentTaskStatus.Deferred, phase.ComputeStatus());
    }

    [Test]
    public void ComputeStatus_NoTasks_ReturnsNotStarted()
    {
        DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P_TEST" };
        Assert.AreEqual(DevelopmentTaskStatus.NotStarted, phase.ComputeStatus());
    }

    [Test]
    public void FindPhase_And_FindTask_LocateExistingEntities()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData
            {
                id = "P01",
                tasks = new List<DevelopmentTaskData> { MakeTask("P01-T01", DevelopmentTaskStatus.NotStarted, true) }
            };
            plan.phases.Add(phase);

            Assert.AreSame(phase, plan.FindPhase("P01"));
            Assert.IsNull(plan.FindPhase("MISSING"));

            DevelopmentTaskData found = plan.FindTask("P01-T01", out DevelopmentPhaseData owner);
            Assert.IsNotNull(found);
            Assert.AreSame(phase, owner);
            Assert.IsNull(plan.FindTask("MISSING", out _));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void MigrateIfNeeded_FixesNullListsAndBumpsSchemaVersion()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            plan.schemaVersion = 0;
            plan.phases = null;

            bool changed = plan.MigrateIfNeeded();

            Assert.IsTrue(changed);
            Assert.AreEqual(DevelopmentPlanAsset.CurrentSchemaVersion, plan.schemaVersion);
            Assert.IsNotNull(plan.phases);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void MigrateIfNeeded_V2_AddsNewTrackerPhases_AndPreservesCompletedP08()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            plan.schemaVersion = 1;
            DevelopmentPhaseData p08 = new DevelopmentPhaseData
            {
                id = "P08_DEPARTURE",
                title = "Старый след и решение идти дальше",
                order = 8,
                required = true,
                tasks = new List<DevelopmentTaskData>
                {
                    new DevelopmentTaskData { id = "P08-T01", title = "old 1", status = DevelopmentTaskStatus.Completed, required = true },
                    new DevelopmentTaskData { id = "P08-T02", title = "old 2", status = DevelopmentTaskStatus.Completed, required = true },
                    new DevelopmentTaskData { id = "P08-T03", title = "old 3", status = DevelopmentTaskStatus.Completed, required = true },
                    new DevelopmentTaskData { id = "P08-T04", title = "old 4", status = DevelopmentTaskStatus.Completed, required = true }
                }
            };
            plan.phases.Add(p08);

            plan.MigrateIfNeeded();

            Assert.AreSame(p08, plan.FindPhase("P08_DEPARTURE"), "Уже существующий P08 не должен пересобираться.");
            Assert.AreEqual(DevelopmentTaskStatus.Completed, p08.ComputeStatus());
            Assert.IsNotNull(plan.FindPhase("P08J_JOURNAL"));
            Assert.IsNotNull(plan.FindPhase("P08M_MAP_TIME"));
            Assert.IsNotNull(plan.FindPhase("P09_ROAD"));
            Assert.AreEqual("Первая дальняя дорога и лагерь", plan.FindPhase("P09_ROAD").title);
            Assert.AreEqual(6, plan.FindPhase("P09_ROAD").tasks.Count);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void MigrateIfNeeded_V2_DoesNotCarryCompletedStatus_WhenTaskIdWasRepurposed()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            plan.schemaVersion = 1;
            plan.phases.Add(new DevelopmentPhaseData
            {
                id = "P09_ROAD",
                title = "Сбор отряда и первая дальняя дорога",
                order = 10,
                required = true,
                tasks = new List<DevelopmentTaskData>
                {
                    new DevelopmentTaskData
                    {
                        id = "P09-T04",
                        title = "Пассивная проверка пути",
                        status = DevelopmentTaskStatus.Completed,
                        required = true
                    }
                }
            });

            plan.MigrateIfNeeded();

            DevelopmentTaskData migrated = plan.FindTask("P09-T04", out DevelopmentPhaseData owner);
            Assert.IsNotNull(migrated);
            Assert.AreEqual("P09_ROAD", owner.id);
            Assert.AreEqual("Camp Screen v1", migrated.title);
            Assert.AreNotEqual(DevelopmentTaskStatus.Completed, migrated.status,
                "Старый P09-T04 означал RoadReading, новый P09-T04 означает Camp Screen — прогресс переносить нельзя.");
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void MigrateIfNeeded_V2_PreservesProgress_WhenTaskIdAndTitleMatch()
    {
        DevelopmentPlanAsset seed = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            DevelopmentPlanSeedData.Populate(seed);
            DevelopmentPhaseData seededJournal = seed.FindPhase("P08J_JOURNAL");
            DevelopmentTaskData seededTask = seededJournal.tasks[0];

            plan.schemaVersion = 1;
            plan.phases.Add(new DevelopmentPhaseData
            {
                id = seededJournal.id,
                title = seededJournal.title,
                tasks = new List<DevelopmentTaskData>
                {
                    new DevelopmentTaskData
                    {
                        id = seededTask.id,
                        title = seededTask.title,
                        status = DevelopmentTaskStatus.Completed,
                        required = true,
                        completedAt = "2026-09-11",
                        blockerNote = "user note"
                    }
                }
            });

            plan.MigrateIfNeeded();

            DevelopmentTaskData migrated = plan.FindTask(seededTask.id, out _);
            Assert.AreEqual(DevelopmentTaskStatus.Completed, migrated.status);
            Assert.AreEqual("2026-09-11", migrated.completedAt);
            Assert.AreEqual("user note", migrated.blockerNote);
        }
        finally
        {
            Object.DestroyImmediate(seed);
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void MigrateIfNeeded_CurrentVersion_IsIdempotent()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            DevelopmentPlanSeedData.Populate(plan);
            Assert.AreEqual(DevelopmentPlanAsset.CurrentSchemaVersion, plan.schemaVersion);

            bool changed = plan.MigrateIfNeeded();

            Assert.IsFalse(changed);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Task_IsCompleted_ReflectsStatus()
    {
        DevelopmentTaskData task = MakeTask("T1", DevelopmentTaskStatus.Completed, true);
        Assert.IsTrue(task.IsCompleted);

        task.status = DevelopmentTaskStatus.InProgress;
        Assert.IsFalse(task.IsCompleted);
    }
}
