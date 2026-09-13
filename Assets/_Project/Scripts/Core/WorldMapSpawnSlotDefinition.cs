using System;
using System.Collections.Generic;

// WM-07 (раздел 9.9 канона): расстановка части локаций — не точка ± небольшое
// случайное смещение, а авторски заданная зона ("где-то в этих холмах есть
// рудник"), внутри которой при старте новой партии выбирается конкретная
// точка. Как и WorldMapRegionDefinition — простой прямоугольник в процентах,
// без полигонов/маски: этого достаточно, полноценный редактор слотов не нужен
// на этом этапе.
public sealed class WorldMapSpawnSlotDefinition
{
    public string Id;
    public float MinXPercent;
    public float MaxXPercent;
    public float MinYPercent;
    public float MaxYPercent;

    public float PickXPercent(Random random) =>
        Lerp(MinXPercent, MaxXPercent, (float)random.NextDouble());

    public float PickYPercent(Random random) =>
        Lerp(MinYPercent, MaxYPercent, (float)random.NextDouble());

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

public static class WorldMapSpawnSlotRegistry
{
    // Те же три примерные области, что раньше были жёсткими
    // candidatePositions в GameState.CreateNewGame (запад/север/восток от
    // столицы), но теперь это зоны, а не точки с небольшим джиттером —
    // локация может оказаться в любой точке зоны, а не в узком пятачке
    // вокруг одной заранее заданной координаты.
    public static readonly IReadOnlyList<WorldMapSpawnSlotDefinition> StartingLocationSlots =
        new List<WorldMapSpawnSlotDefinition>
        {
            new WorldMapSpawnSlotDefinition
            {
                Id = "slot-west",
                MinXPercent = 6f,
                MaxXPercent = 22f,
                MinYPercent = 10f,
                MaxYPercent = 32f
            },
            new WorldMapSpawnSlotDefinition
            {
                Id = "slot-north",
                MinXPercent = 38f,
                MaxXPercent = 58f,
                MinYPercent = 4f,
                MaxYPercent = 22f
            },
            new WorldMapSpawnSlotDefinition
            {
                Id = "slot-east",
                MinXPercent = 70f,
                MaxXPercent = 92f,
                MinYPercent = 12f,
                MaxYPercent = 36f
            }
        };

    public static WorldMapSpawnSlotDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (WorldMapSpawnSlotDefinition slot in StartingLocationSlots)
        {
            if (slot.Id == id)
                return slot;
        }

        return null;
    }
}
