using NUnit.Framework;

public sealed class HeroCombatStatsBuilderTests
{
    [TestCase(1, -2)]
    [TestCase(2, -2)]
    [TestCase(3, -1)]
    [TestCase(4, -1)]
    [TestCase(5, 0)]
    [TestCase(6, 0)]
    [TestCase(7, 1)]
    [TestCase(8, 1)]
    [TestCase(9, 2)]
    [TestCase(10, 2)]
    public void CombatModifier_Matches_Quality_Table(int quality, int expectedModifier)
    {
        Assert.AreEqual(expectedModifier, HeroCombatStatsBuilder.GetCombatModifier(quality));
    }

    [Test]
    public void DefaultDexterity_Leaves_Initiative_Unchanged()
    {
        UnitCombatStats baseStats = new UnitCombatStats { Initiative = 4, Attack = 3, Defense = 2, Damage = 5, MaxHitPoints = 10, Movement = 3, AttackRange = 1 };
        HeroProfileData hero = new HeroProfileData();

        UnitCombatStats result = HeroCombatStatsBuilder.BuildCommanderCombatStats(baseStats, hero);

        Assert.AreEqual(4, result.Initiative);
    }

    [Test]
    public void HighDexterity_Increases_Initiative_Only()
    {
        UnitCombatStats baseStats = new UnitCombatStats { Initiative = 4, Attack = 3, Defense = 2, Damage = 5, MaxHitPoints = 10, Movement = 3, AttackRange = 1 };
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Dexterity, 9);

        UnitCombatStats result = HeroCombatStatsBuilder.BuildCommanderCombatStats(baseStats, hero);

        Assert.AreEqual(6, result.Initiative);
        Assert.AreEqual(baseStats.Attack, result.Attack);
        Assert.AreEqual(baseStats.Defense, result.Defense);
        Assert.AreEqual(baseStats.Damage, result.Damage);
        Assert.AreEqual(baseStats.MaxHitPoints, result.MaxHitPoints);
        Assert.AreEqual(baseStats.Movement, result.Movement);
        Assert.AreEqual(baseStats.AttackRange, result.AttackRange);
    }

    [Test]
    public void LowDexterity_Does_Not_Push_Initiative_Below_Zero()
    {
        UnitCombatStats baseStats = new UnitCombatStats { Initiative = 1 };
        HeroProfileData hero = new HeroProfileData();
        hero.SetQuality(HeroQuality.Dexterity, 1);

        UnitCombatStats result = HeroCombatStatsBuilder.BuildCommanderCombatStats(baseStats, hero);

        Assert.AreEqual(0, result.Initiative);
    }

    [Test]
    public void Null_Hero_Returns_Base_Stats_Unchanged()
    {
        UnitCombatStats baseStats = new UnitCombatStats { Initiative = 4 };

        UnitCombatStats result = HeroCombatStatsBuilder.BuildCommanderCombatStats(baseStats, null);

        Assert.AreEqual(4, result.Initiative);
    }
}
