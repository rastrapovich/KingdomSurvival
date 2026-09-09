using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.DevelopmentTracker.Editor;
using NUnit.Framework;
using UnityEngine;

public sealed class DevelopmentPlanValidatorTests
{
    private static DevelopmentPlanAsset NewPlan()
    {
        return ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
    }

    private static DevelopmentTaskData MakeTask(string id, bool required = true, string acceptance = "ok")
    {
        DevelopmentTaskData task = new DevelopmentTaskData { id = id, title = id, required = required };
        if (!string.IsNullOrEmpty(acceptance))
            task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = acceptance });
        return task;
    }

    [Test]
    public void Validate_DetectsDuplicateTaskIds()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            phase.tasks.Add(MakeTask("DUP"));
            phase.tasks.Add(MakeTask("DUP"));
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.EntityId == "DUP"));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_DetectsMissingDependency()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            DevelopmentTaskData task = MakeTask("T1");
            task.dependencies.Add("MISSING-DEP");
            phase.tasks.Add(task);
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error
                && i.EntityId == "T1" && i.Message.Contains("MISSING-DEP")));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_DetectsSelfDependency()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            DevelopmentTaskData task = MakeTask("T1");
            task.dependencies.Add("T1");
            phase.tasks.Add(task);
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.EntityId == "T1"));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_DetectsIndirectCycle()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            DevelopmentTaskData a = MakeTask("A");
            DevelopmentTaskData b = MakeTask("B");
            DevelopmentTaskData c = MakeTask("C");
            a.dependencies.Add("B");
            b.dependencies.Add("C");
            c.dependencies.Add("A");
            phase.tasks.Add(a);
            phase.tasks.Add(b);
            phase.tasks.Add(c);
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("Циклическая")));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_RequiredTaskWithoutAcceptanceCriteria_IsError()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            phase.tasks.Add(MakeTask("T1", required: true, acceptance: null));
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.EntityId == "T1"));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_BlockedWithoutBlockerNote_IsError()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            DevelopmentTaskData task = MakeTask("T1");
            task.status = DevelopmentTaskStatus.Blocked;
            task.blockerNote = string.Empty;
            phase.tasks.Add(task);
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error
                && i.EntityId == "T1" && i.Message.Contains("blockerNote")));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_BlockedWithBlockerNote_NoBlockerError()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            DevelopmentTaskData task = MakeTask("T1");
            task.status = DevelopmentTaskStatus.Blocked;
            task.blockerNote = "Ожидает решения.";
            phase.tasks.Add(task);
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsFalse(issues.Any(i => i.Severity == ValidationSeverity.Error
                && i.EntityId == "T1" && i.Message.Contains("blockerNote")));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_CompletedWithUnmetRequiredDependency_IsError()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1" };
            DevelopmentTaskData dependency = MakeTask("DEP");
            dependency.status = DevelopmentTaskStatus.NotStarted;

            DevelopmentTaskData completed = MakeTask("DONE");
            completed.status = DevelopmentTaskStatus.Completed;
            completed.dependencies.Add("DEP");

            phase.tasks.Add(dependency);
            phase.tasks.Add(completed);
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.EntityId == "DONE"));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }

    [Test]
    public void Validate_ValidPlan_HasNoErrors()
    {
        DevelopmentPlanAsset plan = NewPlan();
        try
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData { id = "P1", order = 0 };
            DevelopmentTaskData a = MakeTask("A");
            DevelopmentTaskData b = MakeTask("B");
            b.dependencies.Add("A");
            phase.tasks.Add(a);
            phase.tasks.Add(b);
            plan.phases.Add(phase);

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);

            Assert.IsFalse(issues.Any(i => i.Severity == ValidationSeverity.Error));
        }
        finally
        {
            Object.DestroyImmediate(plan);
        }
    }
}
