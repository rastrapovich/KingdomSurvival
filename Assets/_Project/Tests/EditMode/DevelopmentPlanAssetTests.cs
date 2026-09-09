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

            plan.MigrateIfNeeded();

            Assert.AreEqual(DevelopmentPlanAsset.CurrentSchemaVersion, plan.schemaVersion);
            Assert.IsNotNull(plan.phases);
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
