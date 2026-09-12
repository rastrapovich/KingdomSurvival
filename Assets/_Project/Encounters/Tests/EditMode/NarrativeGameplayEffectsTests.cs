using NUnit.Framework;

// Минимальный Gameplay Effects слой (§63/§106 инструкции по Encounter-
// системе): NarrativeEffectType.ChangeFood/ChangeSupplies/GrantItem/RemoveItem,
// добавленные поверх существующего NarrativeEffect (Scripts/Core/NarrativeEffects.cs).
// Лежит в Encounters.Tests, а не в общей Core.Tests, т.к. появились ради
// Encounter-контента, но тестируют общий Core-класс напрямую.
public sealed class NarrativeGameplayEffectsTests
{
    private static NarrativeEvaluationContext MakeContext(GameState gameState = null)
    {
        return new NarrativeEvaluationContext(new HeroProfileData(), new NarrativeStateData(), gameState: gameState);
    }

    [Test]
    public void ChangeFood_Applies_Delta_To_GameState()
    {
        GameState state = new GameState { Food = 10 };
        NarrativeEvaluationContext context = MakeContext(state);

        new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ChangeFood, IntParam = -3 }
            .Apply(context);

        Assert.That(state.Food, Is.EqualTo(7));
    }

    [Test]
    public void ChangeFood_Clamps_At_Zero()
    {
        GameState state = new GameState { Food = 2 };
        NarrativeEvaluationContext context = MakeContext(state);

        new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ChangeFood, IntParam = -10 }
            .Apply(context);

        Assert.That(state.Food, Is.EqualTo(0));
    }

    [Test]
    public void ChangeFood_Without_GameState_In_Context_Does_Not_Throw()
    {
        NarrativeEvaluationContext context = MakeContext(gameState: null);

        Assert.DoesNotThrow(() =>
            new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ChangeFood, IntParam = -3 }
                .Apply(context));
    }

    [Test]
    public void ChangeSupplies_Applies_Delta_And_Clamps_At_Zero()
    {
        GameState state = new GameState { ArmySupply = 5 };
        NarrativeEvaluationContext context = MakeContext(state);

        new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ChangeSupplies, IntParam = 4 }
            .Apply(context);
        Assert.That(state.ArmySupply, Is.EqualTo(9));

        new NarrativeEffect { EffectExecutionId = "e2", Type = NarrativeEffectType.ChangeSupplies, IntParam = -100 }
            .Apply(context);
        Assert.That(state.ArmySupply, Is.EqualTo(0));
    }

    [Test]
    public void GrantItem_And_RemoveItem_Roundtrip()
    {
        NarrativeStateData narrativeState = new NarrativeStateData();
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(new HeroProfileData(), narrativeState);

        new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.GrantItem, StringParam = "old_coin" }
            .Apply(context);
        Assert.That(narrativeState.HasItem("old_coin"), Is.True);

        new NarrativeEffect { EffectExecutionId = "e2", Type = NarrativeEffectType.RemoveItem, StringParam = "old_coin" }
            .Apply(context);
        Assert.That(narrativeState.HasItem("old_coin"), Is.False);
    }

    [Test]
    public void ShortcutRouteCells_Advances_RouteIndex()
    {
        GameState state = new GameState
        {
            ActiveExpedition = new ExpeditionData
            {
                IsActive = true,
                Route = new System.Collections.Generic.List<MapPointData>
                {
                    new MapPointData(0f, 0f),
                    new MapPointData(1f, 1f),
                    new MapPointData(2f, 2f)
                },
                RouteIndex = 0
            }
        };
        NarrativeEvaluationContext context = MakeContext(state);

        new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ShortcutRouteCells, IntParam = 1 }
            .Apply(context);

        Assert.That(state.ActiveExpedition.RouteIndex, Is.EqualTo(1));
    }

    [Test]
    public void ShortcutRouteCells_Without_ActiveExpedition_Does_Not_Throw()
    {
        GameState state = new GameState();
        NarrativeEvaluationContext context = MakeContext(state);

        Assert.DoesNotThrow(() =>
            new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ShortcutRouteCells, IntParam = 1 }
                .Apply(context));
    }

    [Test]
    public void ChangeFood_Applied_Twice_With_Same_ExecutionId_Applies_Once()
    {
        GameState state = new GameState { Food = 10 };
        NarrativeEvaluationContext context = MakeContext(state);
        NarrativeEffect effect = new NarrativeEffect { EffectExecutionId = "e1", Type = NarrativeEffectType.ChangeFood, IntParam = -3 };

        effect.Apply(context);
        effect.Apply(context);

        Assert.That(state.Food, Is.EqualTo(7), "Повторный рендер текста не должен применить эффект дважды.");
    }
}
