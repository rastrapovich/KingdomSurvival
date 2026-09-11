using System;
using System.Collections.Generic;

/// <summary>
/// Канонический размер портретной рамки в reference-resolution 1920x1080.
/// Размер принадлежит месту показа, а не персонажу или его Sprite.
/// </summary>
public enum PortraitSize
{
    // Значения XS–XL уже сериализованы в Unity assets. Не менять их номера:
    // новый preset добавляется отдельным значением, а логический порядок
    // задаётся PortraitSizeTable.
    XS = 0,
    S = 1,
    M = 2,
    L = 3,
    XL = 4,
    ML = 5
}

/// <summary>
/// Способ вписывания полного портретного Sprite в рамку. Stretch намеренно
/// отсутствует: портрет всегда сохраняет исходные пропорции.
/// </summary>
public enum PortraitFitMode
{
    Cover = 0,
    Contain = 1
}

public readonly struct PortraitSizeDefinition
{
    public PortraitSizeDefinition(PortraitSize size, int width, int height)
    {
        Size = size;
        Width = width;
        Height = height;
    }

    public PortraitSize Size { get; }
    public int Width { get; }
    public int Height { get; }
}

/// <summary>
/// Единственный источник размеров портретных рамок для баз и интерфейсов.
/// Все записи имеют строгое отношение сторон 5:7.
/// </summary>
public static class PortraitSizeTable
{
    public const int AspectWidth = 5;
    public const int AspectHeight = 7;

    // Логический UI-порядок намеренно отличается от числового порядка enum:
    // ML добавлен после уже сериализованных XS–XL, но визуально находится
    // между M и L.
    private static readonly PortraitSizeDefinition[] Definitions =
    {
        new PortraitSizeDefinition(PortraitSize.XS, 100, 140),
        new PortraitSizeDefinition(PortraitSize.S, 150, 210),
        new PortraitSizeDefinition(PortraitSize.M, 200, 280),
        new PortraitSizeDefinition(PortraitSize.ML, 250, 350),
        new PortraitSizeDefinition(PortraitSize.L, 300, 420),
        new PortraitSizeDefinition(PortraitSize.XL, 400, 560)
    };

    private static readonly IReadOnlyList<PortraitSizeDefinition> ReadOnlyDefinitions =
        Array.AsReadOnly(Definitions);

    public static IReadOnlyList<PortraitSizeDefinition> All => ReadOnlyDefinitions;

    public static PortraitSizeDefinition Get(PortraitSize size)
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            if (Definitions[i].Size == size)
                return Definitions[i];
        }

        return Definitions[2]; // M — безопасный fallback для неизвестного значения.
    }

    public static bool IsDefined(PortraitSize size)
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            if (Definitions[i].Size == size)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Выбирает ближайший preset для миграции свободной рамки. Сравнение
    /// идёт по обеим сторонам, без изменения центра исходного Rect.
    /// </summary>
    public static PortraitSize FindNearest(double width, double height)
    {
        PortraitSize best = PortraitSize.M;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < Definitions.Length; i++)
        {
            PortraitSizeDefinition definition = Definitions[i];
            double dx = width - definition.Width;
            double dy = height - definition.Height;
            double distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = definition.Size;
            }
        }

        return best;
    }
}
