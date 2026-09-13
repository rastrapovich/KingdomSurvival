using System.Collections.Generic;

// AM-07 (раздел 12 инструкции по миграции): "Валидатор проверяет, что
// достоверная область включает реальную цель и следующая стадия не
// расширяется случайно." Не универсальный геометрический решатель —
// проверка на вложенных прямоугольниках, как и решено в инструкции.
public static class WorldMapSearchAreaValidator
{
    public static List<string> Validate(
        WorldMapSearchAreaDefinition search,
        float targetXPercent,
        float targetYPercent)
    {
        List<string> issues = new List<string>();

        if (search == null)
        {
            issues.Add("Пустое определение поиска.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(search.SearchId))
            issues.Add("У поиска не заполнен SearchId.");

        if (search.Stages == null || search.Stages.Count == 0)
        {
            issues.Add($"Поиск '{search.SearchId}' не содержит ни одной стадии.");
            return issues;
        }

        double previousArea = double.MaxValue;
        for (int i = 0; i < search.Stages.Count; i++)
        {
            WorldMapSearchStageDefinition stage = search.Stages[i];
            if (stage == null)
            {
                issues.Add($"Поиск '{search.SearchId}': пустая стадия #{i + 1}.");
                continue;
            }

            if (!stage.Contains(targetXPercent, targetYPercent))
            {
                issues.Add(
                    $"Поиск '{search.SearchId}', стадия '{stage.StageId}': область не включает " +
                    "реальную цель — по этому слуху место физически не найти.");
            }

            if (stage.Area > previousArea + 0.0001)
            {
                issues.Add(
                    $"Поиск '{search.SearchId}', стадия '{stage.StageId}': область больше " +
                    "предыдущей стадии — сведения должны сужать поиск, а не расширять его.");
            }

            previousArea = stage.Area;
        }

        return issues;
    }
}
