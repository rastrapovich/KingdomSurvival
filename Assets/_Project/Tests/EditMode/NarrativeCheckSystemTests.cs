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
}
