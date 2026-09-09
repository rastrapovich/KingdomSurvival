using System.Collections.Generic;
using KingdomSurvival.DevelopmentTracker.Editor;
using NUnit.Framework;
using UnityEngine;

public sealed class DevelopmentTrackerProgressTests
{
    private static DevelopmentTaskData MakeTask(string id, DevelopmentTaskStatus status, bool required)
    {
        return new DevelopmentTaskData { id = id, title = id, status = status, required = required };
    }

    [Test]
    public void ComputeForTasks_CountsOnlyRequiredTasksTowardRatio()
    {
        List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
        {
            MakeTask("T1", DevelopmentTaskStatus.Completed, true),
            MakeTask("T2", DevelopmentTaskStatus.NotStarted, true),
            MakeTask("T3", DevelopmentTaskStatus.Completed, false)
        };

        DevelopmentPlanProgress.Summary summary = DevelopmentPlanProgress.ComputeForTasks(tasks);

        Assert.AreEqual(2, summary.totalRequired);
        Assert.AreEqual(1, summary.completedRequired);
        Assert.AreEqual(1, summary.optionalTotal);
        Assert.AreEqual(1, summary.optionalCompleted);
        Assert.AreEqual(0.5f, summary.Ratio, 0.0001f);
    }

    [Test]
    public void ComputeForTasks_ExcludesDeferredEntirely()
    {
        List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
        {
            MakeTask("T1", DevelopmentTaskStatus.Completed, true),
            MakeTask("T2", DevelopmentTaskStatus.Deferred, true)
        };

        DevelopmentPlanProgress.Summary summary = DevelopmentPlanProgress.ComputeForTasks(tasks);

        Assert.AreEqual(1, summary.totalRequired);
        Assert.AreEqual(1, summary.completedRequired);
        Assert.AreEqual(1f, summary.Ratio, 0.0001f);
    }

    [Test]
    public void ComputeForTasks_TracksBlockedAndNeedsUnityCheckCounts()
    {
        List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
        {
            MakeTask("T1", DevelopmentTaskStatus.Blocked, true),
            MakeTask("T2", DevelopmentTaskStatus.NeedsUnityCheck, true),
            MakeTask("T3", DevelopmentTaskStatus.NotStarted, true)
        };

        DevelopmentPlanProgress.Summary summary = DevelopmentPlanProgress.ComputeForTasks(tasks);

        Assert.AreEqual(1, summary.blockedCount);
        Assert.AreEqual(1, summary.needsUnityCheckCount);
    }

    [Test]
    public void ComputeOverall_AggregatesAcrossPhases()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            DevelopmentPhaseData p1 = new DevelopmentPhaseData { id = "P1" };
            p1.tasks.Add(MakeTask("T1", DevelopmentTaskStatus.Completed, true));
            DevelopmentPhaseData p2 = new DevelopmentPhaseData { id = "P2" };
            p2.tasks.Add(MakeTask("T2", DevelopmentTaskStatus.NotStarted, true));

            plan.phases.Add(p1);
            plan.phases.Add(p2);

            DevelopmentPlanProgress.Summary summary = DevelopmentPlanProgress.ComputeOverall(plan);

            Assert.AreEqual(2, summary.totalRequired);
            Assert.AreEqual(1, summary.completedRequired);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void FindNextRecommendedTask_SkipsCompletedBlockedAndUnsatisfiedDependencies()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };

            DevelopmentTaskData done = MakeTask("T1", DevelopmentTaskStatus.Completed, true);
            done.order = 1;

            DevelopmentTaskData blocked = MakeTask("T2", DevelopmentTaskStatus.Blocked, true);
            blocked.order = 2;
            blocked.blockerNote = "Ждём решения.";

            DevelopmentTaskData waitingOnDependency = MakeTask("T3", DevelopmentTaskStatus.NotStarted, true);
            waitingOnDependency.order = 3;
            waitingOnDependency.dependencies.Add("T2");

            DevelopmentTaskData ready = MakeTask("T4", DevelopmentTaskStatus.NotStarted, true);
            ready.order = 4;

            phase.tasks.Add(done);
            phase.tasks.Add(blocked);
            phase.tasks.Add(waitingOnDependency);
            phase.tasks.Add(ready);
            plan.phases.Add(phase);

            DevelopmentTaskData next = DevelopmentPlanProgress.FindNextRecommendedTask(plan, "P1");

            Assert.IsNotNull(next);
            Assert.AreEqual("T4", next.id);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void FindNextRecommendedTask_ReturnsNullWhenNothingAvailable()
    {
        DevelopmentPlanAsset plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            DevelopmentTaskData done = MakeTask("T1", DevelopmentTaskStatus.Completed, true);
            phase.tasks.Add(done);
            plan.phases.Add(phase);

            DevelopmentTaskData next = DevelopmentPlanProgress.FindNextRecommendedTask(plan, "P1");

            Assert.IsNull(next);
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }
}
