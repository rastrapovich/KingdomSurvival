using NUnit.Framework;

// E01-T03: EncounterRuntimeStateData/EncounterRuntimeEntry + встраивание
// в GameState. Класс лежит в Scripts/Core (глобальный namespace), но
// тестово это часть Encounters — поэтому файл здесь, а не в общей сборке
// Core.EditModeTests.
public sealed class EncounterRuntimeStateTests
{
    [Test]
    public void CreateNewGame_Initializes_Encounters_State()
    {
        GameState state = new GameState();
        state.CreateNewGame();

        Assert.That(state.Encounters, Is.Not.Null);
        Assert.That(state.Encounters.Entries, Is.Not.Null.And.Empty);
        Assert.That(state.Encounters.ProcessedOpportunities, Is.Not.Null.And.Empty);
    }

    [Test]
    public void FindOrCreateEntry_Creates_Once_Then_Reuses()
    {
        EncounterRuntimeStateData data = new EncounterRuntimeStateData();

        EncounterRuntimeEntry first = data.FindOrCreateEntry("A");
        EncounterRuntimeEntry second = data.FindOrCreateEntry("A");

        Assert.That(first, Is.SameAs(second));
        Assert.That(data.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void FindEntry_Returns_Null_When_Never_Seen()
    {
        EncounterRuntimeStateData data = new EncounterRuntimeStateData();
        Assert.That(data.FindEntry("NEVER_SEEN"), Is.Null);
    }

    [Test]
    public void ProcessedOpportunities_Roundtrip()
    {
        EncounterRuntimeStateData data = new EncounterRuntimeStateData();

        Assert.That(data.WasOpportunityProcessed("OP_1"), Is.False);
        data.MarkOpportunityProcessed("OP_1");
        Assert.That(data.WasOpportunityProcessed("OP_1"), Is.True);
        Assert.That(data.WasOpportunityProcessed("OP_2"), Is.False);
    }

    [Test]
    public void EnsureInitialized_Recovers_From_Null_Collections()
    {
        // Симуляция старого save, десериализованного без этого поля.
        EncounterRuntimeStateData data = new EncounterRuntimeStateData
        {
            Entries = null,
            ProcessedOpportunities = null,
            PoolCooldowns = null
        };

        Assert.DoesNotThrow(() => data.EnsureInitialized());
        Assert.That(data.Entries, Is.Not.Null);
        Assert.That(data.ProcessedOpportunities, Is.Not.Null);
        Assert.That(data.PoolCooldowns, Is.Not.Null);
    }

    [Test]
    public void PoolCooldown_Roundtrip()
    {
        EncounterRuntimeStateData data = new EncounterRuntimeStateData();

        Assert.That(data.GetPoolLastTriggeredWorldHour("POOL_01"), Is.EqualTo(-1));
        data.MarkPoolTriggered("POOL_01", 48.0);
        Assert.That(data.GetPoolLastTriggeredWorldHour("POOL_01"), Is.EqualTo(48.0));

        data.MarkPoolTriggered("POOL_01", 72.0);
        Assert.That(data.GetPoolLastTriggeredWorldHour("POOL_01"), Is.EqualTo(72.0));
        Assert.That(data.PoolCooldowns, Has.Count.EqualTo(1));
    }

    [Test]
    public void ReactiveTrigger_Roundtrip_And_DayReset()
    {
        EncounterRuntimeStateData data = new EncounterRuntimeStateData();

        Assert.That(data.GetPoolLastReactiveTriggeredWorldHour("POOL_01"), Is.EqualTo(-1));
        Assert.That(data.GetReactiveCountForDay("POOL_01", 0), Is.EqualTo(0));

        data.MarkReactiveTriggered("POOL_01", 5, day: 0);
        Assert.That(data.GetPoolLastReactiveTriggeredWorldHour("POOL_01"), Is.EqualTo(5));
        Assert.That(data.GetReactiveCountForDay("POOL_01", 0), Is.EqualTo(1));

        data.MarkReactiveTriggered("POOL_01", 10, day: 0);
        Assert.That(data.GetReactiveCountForDay("POOL_01", 0), Is.EqualTo(2));

        // Новые сутки — счётчик за старый день не переносится.
        data.MarkReactiveTriggered("POOL_01", 26, day: 1);
        Assert.That(data.GetReactiveCountForDay("POOL_01", 1), Is.EqualTo(1));
        Assert.That(data.GetReactiveCountForDay("POOL_01", 0), Is.EqualTo(0));
    }
}
