using System.Collections.Generic;
using NUnit.Framework;

public sealed class NarrativeCheckSystemTests
{
    private static NarrativeEvaluationContext NewContext(HeroProfileData hero = null, NarrativeStateData state = null)
    {
        return new NarrativeEvaluationContext(hero ?? new HeroProfileData(), state ?? new NarrativeStateData());
    }

    [Test]
    public void HeroProfile_Quality_Is_Clamped_To_1_10()
    {
        HeroProfileData hero = new HeroProfileData();

        hero.SetQuality(HeroQuality.Strength, 0);
        Assert.AreEqual(1, hero.GetQuality(HeroQuality.Strength));

        hero.SetQuality(HeroQuality.Strength, 99);
        Assert.AreEqual(10, hero.GetQuality(HeroQuality.Strength));

        hero.SetQuality(HeroQuality.Strength, 7);
        Assert.AreEqual(7, hero.GetQuality(HeroQuality.Strength));
    }

    [Test]
    public void HeroProfile_Competency_Is_Clamped_To_0_5()
    {
        HeroProfileData hero = new HeroProfileData();

        hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, -3);
        Assert.AreEqual(0, hero.GetCompetency(NarrativeCompetencyIds.Fieldcraft));

        hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, 99);
        Assert.AreEqual(5, hero.GetCompetency(NarrativeCompetencyIds.Fieldcraft));

        hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, 3);
        Assert.AreEqual(3, hero.GetCompetency(NarrativeCompetencyIds.Fieldcraft));
    }

    [Test]
    public void ContextModifier_Is_Clamped_To_Minus3_Plus3()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "ctx_clamp",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            Difficulty = NarrativeDifficulty.Ordinary
        };
        spec.ModifierRules.Add(new NarrativeContextModifierRule { SourceId = "a", Label = "A", Value = 3 });
        spec.ModifierRules.Add(new NarrativeContextModifierRule { SourceId = "b", Label = "B", Value = 3 });
        spec.ModifierRules.Add(new NarrativeContextModifierRule { SourceId = "c", Label = "C", Value = 3 });

        NarrativeCheckMathBreakdown breakdown = NarrativeCheckResolver.ComputeBreakdown(spec, NewContext(hero));

        Assert.AreEqual(9, breakdown.RawContextModifier);
        Assert.AreEqual(NarrativeCheckMath.MaxContextModifier, breakdown.AppliedContextModifier);
    }

    [Test]
    public void PassiveCheck_Succeeds_On_Exact_Equality()
    {
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Instinct, 5);
        hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, 2);

        // 6 (база) + 5 (качество) + 2 (компетенция) = 13 == сложность.
        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "passive_exact",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Fieldcraft,
            Difficulty = 13
        };

        NarrativeCheckResult result = NarrativeCheckResolver.ResolvePassive(spec, NewContext(hero));

        Assert.AreEqual(13, result.Total);
        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.HasDice);
    }

    [TestCase(2, 1.0)]
    [TestCase(7, 21.0 / 36.0)]
    [TestCase(12, 1.0 / 36.0)]
    [TestCase(13, 0.0)]
    public void ActiveProbability_Matches_2d6_Distribution(int requiredRoll, double expected)
    {
        Assert.That(NarrativeProbabilityTable.GetSuccessProbability(requiredRoll), Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void DeterministicRandom_Same_Seed_CheckId_Attempt_Gives_Same_Dice()
    {
        NarrativeDeterministicRandom.RollTwoDice(42, "check_a", 1, out int firstA, out int firstB);
        NarrativeDeterministicRandom.RollTwoDice(42, "check_a", 1, out int secondA, out int secondB);

        Assert.AreEqual(firstA, secondA);
        Assert.AreEqual(firstB, secondB);
    }

    [Test]
    public void DeterministicRandom_New_Attempt_Gets_New_Roll_Key()
    {
        ulong seedAttempt1 = NarrativeDeterministicRandom.ComputeSeed(42, "check_a", 1);
        ulong seedAttempt2 = NarrativeDeterministicRandom.ComputeSeed(42, "check_a", 2);

        Assert.AreNotEqual(seedAttempt1, seedAttempt2);
    }

    private static NarrativeCheckSpec MakeGuaranteedFailSpec(string checkId, NarrativeCheckKind kind)
    {
        // Максимально возможный бонус (10 + 5 + 3 = 18) плюс максимальный
        // бросок 12 всё равно меньше сложности 23 — провал гарантирован
        // при любых допустимых значениях героя.
        return new NarrativeCheckSpec
        {
            CheckId = checkId,
            Kind = kind,
            Quality = HeroQuality.Dexterity,
            Difficulty = NarrativeDifficulty.Legendary + 8
        };
    }

    [Test]
    public void ActiveReturnable_Locks_After_Failure()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeEvaluationContext context = NewContext(hero, state);
        NarrativeCheckSpec spec = MakeGuaranteedFailSpec("returnable_lock", NarrativeCheckKind.ActiveReturnable);

        NarrativeCheckAttempt first = NarrativeCheckResolver.TryResolveActive(spec, context, worldSeed: 1);
        Assert.AreEqual(NarrativeCheckAttemptOutcome.Resolved, first.Outcome);
        Assert.IsFalse(first.Result.Success);
        Assert.IsTrue(state.IsCheckLocked("returnable_lock"));

        NarrativeCheckAttempt second = NarrativeCheckResolver.TryResolveActive(spec, context, worldSeed: 1);
        Assert.AreEqual(NarrativeCheckAttemptOutcome.Blocked, second.Outcome);
    }

    [Test]
    public void UnlockCheck_Allows_Next_Attempt()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeEvaluationContext context = NewContext(hero, state);
        NarrativeCheckSpec spec = MakeGuaranteedFailSpec("returnable_unlock", NarrativeCheckKind.ActiveReturnable);

        NarrativeCheckResolver.TryResolveActive(spec, context, worldSeed: 1);
        Assert.IsTrue(state.IsCheckLocked("returnable_unlock"));

        state.UnlockCheck("returnable_unlock");
        Assert.IsFalse(state.IsCheckLocked("returnable_unlock"));

        NarrativeCheckAttempt third = NarrativeCheckResolver.TryResolveActive(spec, context, worldSeed: 1);
        Assert.AreEqual(NarrativeCheckAttemptOutcome.Resolved, third.Outcome);
        Assert.AreEqual(2, third.Result.AttemptNumber);
    }

    [Test]
    public void ActiveDecisive_Does_Not_Repeat()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeEvaluationContext context = NewContext(hero, state);
        NarrativeCheckSpec spec = MakeGuaranteedFailSpec("decisive_once", NarrativeCheckKind.ActiveDecisive);

        NarrativeCheckAttempt first = NarrativeCheckResolver.TryResolveActive(spec, context, worldSeed: 7);
        Assert.AreEqual(NarrativeCheckAttemptOutcome.Resolved, first.Outcome);

        state.UnlockCheck("decisive_once");
        NarrativeCheckAttempt second = NarrativeCheckResolver.TryResolveActive(spec, context, worldSeed: 999);
        Assert.AreEqual(NarrativeCheckAttemptOutcome.AlreadyDecided, second.Outcome);
        Assert.AreSame(first.Result, second.Result);
    }

    [Test]
    public void Effect_Is_Applied_Only_Once()
    {
        NarrativeStateData state = new NarrativeStateData();
        NarrativeEvaluationContext context = NewContext(state: state);
        NarrativeEffect effect = new NarrativeEffect
        {
            EffectExecutionId = "grant_relation_once",
            Type = NarrativeEffectType.ChangeRelation,
            StringParam = "miller",
            IntParam = 5
        };

        effect.Apply(context);
        effect.Apply(context);

        Assert.AreEqual(5, state.GetRelation("miller"));
    }

    // §20 инструкции по визуализации проверок, пункты 5-6: в snapshot
    // попадают только реально сработавшие модификаторы.
    [Test]
    public void AppliedModifiers_Snapshot_Contains_Only_Rules_Whose_Condition_Passed()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag("hunter_present");
        NarrativeEvaluationContext context = NewContext(hero, state);

        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "chk_snapshot_filter",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            Difficulty = NarrativeDifficulty.Ordinary
        };
        spec.ModifierRules.Add(new NarrativeContextModifierRule
        {
            SourceId = "companion_hunter",
            Label = "Охотник рядом",
            Value = 1,
            Condition = new NarrativeConditionGroup
            {
                Conditions = new List<NarrativeCondition>
                {
                    new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = "hunter_present" }
                }
            }
        });
        spec.ModifierRules.Add(new NarrativeContextModifierRule
        {
            SourceId = "heavy_rain",
            Label = "Сильный дождь",
            Value = -1,
            Condition = new NarrativeConditionGroup
            {
                Conditions = new List<NarrativeCondition>
                {
                    new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = "rain_active" }
                }
            }
        });

        NarrativeCheckResult result = NarrativeCheckResolver.ResolvePassive(spec, context);

        Assert.AreEqual(1, result.AppliedModifiers.Count);
        Assert.AreEqual("companion_hunter", result.AppliedModifiers[0].SourceId);
        Assert.AreEqual("Охотник рядом", result.AppliedModifiers[0].Label);
        Assert.AreEqual(1, result.AppliedModifiers[0].Value);
    }

    // §20, пункт 7: сырая сумма и применённое (ограниченное ±3) значение
    // должны отображаться по-разному, а не совпадать.
    [Test]
    public void RawContextModifier_And_AppliedContextModifier_Differ_When_Clamped()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeEvaluationContext context = NewContext(hero, state);

        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "chk_snapshot_clamp",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            Difficulty = NarrativeDifficulty.Ordinary
        };
        spec.ModifierRules.Add(new NarrativeContextModifierRule { SourceId = "a", Label = "A", Value = 2 });
        spec.ModifierRules.Add(new NarrativeContextModifierRule { SourceId = "b", Label = "B", Value = 2 });
        spec.ModifierRules.Add(new NarrativeContextModifierRule { SourceId = "c", Label = "C", Value = 1 });

        NarrativeCheckResult result = NarrativeCheckResolver.ResolvePassive(spec, context);
        NarrativeCheckPresentationData presentation = NarrativeCheckPresentationBuilder.Build(spec, result);

        Assert.AreEqual(5, presentation.RawContextModifier);
        Assert.AreEqual(3, presentation.AppliedContextModifier);
        Assert.AreNotEqual(presentation.RawContextModifier, presentation.AppliedContextModifier);
    }

    // §20, пункт 4: качество и компетенция должны попасть в расшифровку
    // с правильными подписями и значениями.
    [Test]
    public void Presentation_Exposes_Quality_And_Competency_With_Correct_Values()
    {
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Instinct, 7);
        hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, 3);
        NarrativeStateData state = new NarrativeStateData();
        NarrativeEvaluationContext context = NewContext(hero, state);

        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "chk_presentation_values",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Fieldcraft,
            Difficulty = NarrativeDifficulty.Ordinary
        };

        NarrativeCheckResult result = NarrativeCheckResolver.ResolvePassive(spec, context);
        NarrativeCheckPresentationData presentation = NarrativeCheckPresentationBuilder.Build(spec, result);

        Assert.AreEqual("Чутьё", presentation.QualityLabel);
        Assert.AreEqual(7, presentation.QualityValue);
        Assert.IsTrue(presentation.HasCompetency);
        Assert.AreEqual("Следопытство", presentation.CompetencyLabel);
        Assert.AreEqual(3, presentation.CompetencyValue);
    }

    // §20, пункт 8: tooltip обязан показывать снимок фактического расчёта,
    // а не пересчитывать проверку заново по изменившемуся состоянию мира.
    [Test]
    public void Presentation_Snapshot_Is_Unaffected_By_Later_World_State_Change()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();

        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "chk_snapshot_frozen",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            Difficulty = NarrativeDifficulty.Ordinary
        };
        spec.ModifierRules.Add(new NarrativeContextModifierRule
        {
            SourceId = "companion_hunter",
            Label = "Охотник рядом",
            Value = 2,
            Condition = new NarrativeConditionGroup
            {
                Conditions = new List<NarrativeCondition>
                {
                    new NarrativeCondition { Type = NarrativeConditionType.CompanionPresent, StringParam = "hunter" }
                }
            }
        });

        NarrativeEvaluationContext contextWithHunter = new NarrativeEvaluationContext(
            hero, state, presentCompanionIds: new[] { "hunter" });
        NarrativeCheckResult result = NarrativeCheckResolver.ResolvePassive(spec, contextWithHunter);
        NarrativeCheckPresentationData presentation = NarrativeCheckPresentationBuilder.Build(spec, result);

        Assert.AreEqual(1, presentation.AppliedModifiers.Count);
        Assert.AreEqual(2, presentation.AppliedModifiers[0].Value);

        // Охотник ушёл из отряда — мир изменился уже после расчёта.
        NarrativeEvaluationContext contextWithoutHunter = NewContext(hero, state);
        NarrativeCheckMathBreakdown recomputed = NarrativeCheckResolver.ComputeBreakdown(spec, contextWithoutHunter);
        Assert.AreEqual(0, recomputed.AppliedModifiers.Count);

        // Старый снимок не пересчитался вслед за миром.
        Assert.AreEqual(1, presentation.AppliedModifiers.Count);
        Assert.AreEqual(2, presentation.AppliedModifiers[0].Value);
        Assert.AreEqual(result.Total, presentation.Total);
    }

    // §20, пункт 13: внутренние ID (SourceId, CompetencyId) никогда не
    // должны попадать в текст, который видит игрок.
    [Test]
    public void PlayerFacing_Breakdown_Text_Never_Contains_Internal_Ids()
    {
        HeroProfileData hero = new HeroProfileData();
        hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, 2);
        NarrativeStateData state = new NarrativeStateData();

        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "check_dam_waterflow_01",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Fieldcraft,
            Difficulty = NarrativeDifficulty.Ordinary
        };
        spec.ModifierRules.Add(new NarrativeContextModifierRule
        {
            SourceId = "companion_hunter",
            Label = "Охотник рядом",
            Value = 1,
            Condition = new NarrativeConditionGroup()
        });

        NarrativeEvaluationContext context = NewContext(hero, state);
        NarrativeCheckResult result = NarrativeCheckResolver.ResolvePassive(spec, context);
        NarrativeCheckPresentationData presentation = NarrativeCheckPresentationBuilder.Build(spec, result);

        string sourceLabel = NarrativeCheckPresentationBuilder.BuildSourceLabel(presentation);
        string breakdown = NarrativeCheckPresentationText.BuildFullBreakdown(presentation);

        Assert.IsFalse(sourceLabel.Contains(NarrativeCompetencyIds.Fieldcraft));
        Assert.IsFalse(breakdown.Contains("check_dam_waterflow_01"));
        Assert.IsFalse(breakdown.Contains("companion_hunter"));
        Assert.IsFalse(breakdown.Contains(NarrativeCompetencyIds.Fieldcraft));
        StringAssert.Contains("Охотник рядом", breakdown);
        StringAssert.Contains("Следопытство", breakdown);
    }

    // §20, пункт 15: hover/повторная сборка presentation читает уже готовый
    // результат и не запускает проверку заново — Build() не принимает
    // NarrativeEvaluationContext и не создаёт новую попытку в истории.
    [Test]
    public void PresentationBuilder_Build_Does_Not_Reresolve_The_Check()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "chk_no_reresolve",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            Difficulty = NarrativeDifficulty.Ordinary
        };

        NarrativeCheckResult result = NarrativeCheckResolver.ResolvePassive(spec, NewContext(hero, state));
        Assert.AreEqual(1, state.FindHistory("chk_no_reresolve").Attempts.Count);

        NarrativeCheckPresentationData first = NarrativeCheckPresentationBuilder.Build(spec, result);
        NarrativeCheckPresentationData second = NarrativeCheckPresentationBuilder.Build(spec, result);

        Assert.AreEqual(1, state.FindHistory("chk_no_reresolve").Attempts.Count);
        Assert.AreEqual(first.Total, second.Total);
        Assert.AreEqual(first.Success, second.Success);
    }

    // Инструкция "новое отображение пассивных наблюдений и проверок", §2/§8:
    // "СУЖДЕНИЕ" для проверки только качеством, "ЧУТЬЁ + СЛЕДОПЫТСТВО" —
    // качество + компетенция. UI добавляет двоеточие и УСПЕХ/ПРОВАЛ поверх
    // этой строки — сама BuildSourceLabel не должна меняться под это.
    [Test]
    public void BuildSourceLabel_QualityOnly_Vs_QualityPlusCompetency()
    {
        HeroProfileData hero = new HeroProfileData();
        NarrativeStateData state = new NarrativeStateData();

        NarrativeCheckSpec qualityOnly = new NarrativeCheckSpec
        {
            CheckId = "chk_quality_only",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Judgment,
            Difficulty = NarrativeDifficulty.Ordinary
        };
        NarrativeCheckResult qualityOnlyResult = NarrativeCheckResolver.ResolvePassive(qualityOnly, NewContext(hero, state));
        string qualityOnlyLabel = NarrativeCheckPresentationBuilder.BuildSourceLabel(
            NarrativeCheckPresentationBuilder.Build(qualityOnly, qualityOnlyResult));
        Assert.AreEqual("Суждение", qualityOnlyLabel);

        NarrativeCheckSpec qualityPlusCompetency = new NarrativeCheckSpec
        {
            CheckId = "chk_quality_plus_competency",
            Kind = NarrativeCheckKind.Passive,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Fieldcraft,
            Difficulty = NarrativeDifficulty.Ordinary
        };
        NarrativeCheckResult combinedResult = NarrativeCheckResolver.ResolvePassive(qualityPlusCompetency, NewContext(hero, state));
        string combinedLabel = NarrativeCheckPresentationBuilder.BuildSourceLabel(
            NarrativeCheckPresentationBuilder.Build(qualityPlusCompetency, combinedResult));
        Assert.AreEqual("Чутьё + Следопытство", combinedLabel);
    }
}
