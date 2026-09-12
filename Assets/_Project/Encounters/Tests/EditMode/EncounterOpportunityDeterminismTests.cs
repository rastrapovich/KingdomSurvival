using System.Collections.Generic;
using KingdomSurvival.Encounters;
using NUnit.Framework;

// E01-T05/T06: детерминированный random не должен зависеть от
// UnityEngine.Random.state и обязан давать одинаковый результат для
// одинаковых входных параметров (§54, §55).
public sealed class EncounterOpportunityDeterminismTests
{
    [Test]
    public void RollPercent_Is_Repeatable_For_Same_Inputs()
    {
        bool first = EncounterDeterministicRandom.RollPercent(42, "OP_1", "ENC_A", "discovery", 40);
        bool second = EncounterDeterministicRandom.RollPercent(42, "OP_1", "ENC_A", "discovery", 40);
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void BuildSeed_Differs_By_RollPurpose()
    {
        int poolSeed = EncounterDeterministicRandom.BuildSeed(1, "OP_1", "ENC_A", "pool_trigger");
        int discoverySeed = EncounterDeterministicRandom.BuildSeed(1, "OP_1", "ENC_A", "discovery");
        Assert.That(poolSeed, Is.Not.EqualTo(discoverySeed));
    }

    [Test]
    public void RollPercent_Zero_Always_Fails_Hundred_Always_Passes()
    {
        Assert.That(EncounterDeterministicRandom.RollPercent(1, "OP", "ENC", "p", 0), Is.False);
        Assert.That(EncounterDeterministicRandom.RollPercent(1, "OP", "ENC", "p", 100), Is.True);
    }

    [Test]
    public void Different_OpportunityId_Can_Produce_Different_Rolls()
    {
        // Мягкая проверка: не требуем строгого распределения, только что
        // не ВСЕ результаты совпадают (иначе seed фактически игнорирует
        // OpportunityId).
        bool sawTrue = false;
        bool sawFalse = false;
        for (int i = 0; i < 200 && !(sawTrue && sawFalse); i++)
        {
            bool roll = EncounterDeterministicRandom.RollPercent(7, "OP_" + i, "ENC_A", "discovery", 50);
            if (roll) sawTrue = true; else sawFalse = true;
        }

        Assert.That(sawTrue && sawFalse, Is.True);
    }

    [Test]
    public void WeightedPick_Single_Item_Always_Returned()
    {
        List<string> items = new List<string> { "only" };
        string picked = EncounterDeterministicRandom.WeightedPick(1, "OP", "purpose", items, _ => 5);
        Assert.That(picked, Is.EqualTo("only"));
    }

    [Test]
    public void WeightedPick_Is_Repeatable_For_Same_Inputs()
    {
        List<string> items = new List<string> { "a", "b", "c" };
        string first = EncounterDeterministicRandom.WeightedPick(1, "OP_1", "purpose", items, s => s == "a" ? 10 : 1);
        string second = EncounterDeterministicRandom.WeightedPick(1, "OP_1", "purpose", items, s => s == "a" ? 10 : 1);
        Assert.That(second, Is.EqualTo(first));
    }
}
