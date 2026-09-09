using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Presentation-слой поверх NarrativeCheckSpec/NarrativeCheckResult — DTO
// между runtime и UI, не новая механика (§9 производственной инструкции
// "визуализация нарративных проверок"). Собирается один раз в момент
// расчёта проверки; UI (в т.ч. tooltip при наведении) только читает уже
// готовые данные и никогда не пересчитывает проверку заново — см. §8/§15.
public sealed class NarrativeCheckPresentationData
{
    public string CheckId;
    public NarrativeCheckKind Kind;

    public HeroQuality Quality;
    public string QualityLabel;
    public int QualityValue;

    // Пусто, если проверка не использует компетенцию.
    public bool HasCompetency;
    public string CompetencyId;
    public string CompetencyLabel;
    public int CompetencyValue;

    public bool Success;

    public int Difficulty;
    public string DifficultyLabel;

    public int DieOne;
    public int DieTwo;
    public bool HasDice;

    // Значим только для Kind == Passive.
    public int PassiveBase;

    // Только реально сработавшие модификаторы — см. §5/§8 инструкции:
    // игрок не должен видеть модификаторы, условие которых не выполнилось.
    public IReadOnlyList<NarrativeAppliedModifierSnapshot> AppliedModifiers;
    public int RawContextModifier;
    public int AppliedContextModifier;

    public int Total;
}

public static class NarrativeCheckPresentationBuilder
{
    public static NarrativeCheckPresentationData Build(NarrativeCheckSpec spec, NarrativeCheckResult result)
    {
        if (spec == null)
            throw new System.ArgumentNullException(nameof(spec));
        if (result == null)
            throw new System.ArgumentNullException(nameof(result));

        bool hasCompetency = !string.IsNullOrWhiteSpace(spec.CompetencyId);

        return new NarrativeCheckPresentationData
        {
            CheckId = spec.CheckId ?? string.Empty,
            Kind = spec.Kind,

            Quality = spec.Quality,
            QualityLabel = NarrativeQualityLabels.GetLabel(spec.Quality),
            QualityValue = result.QualityValue,

            HasCompetency = hasCompetency,
            CompetencyId = hasCompetency ? spec.CompetencyId : string.Empty,
            CompetencyLabel = hasCompetency ? NarrativeCompetencyLabels.GetLabel(spec.CompetencyId) : string.Empty,
            CompetencyValue = result.CompetencyValue,

            Success = result.Success,

            Difficulty = result.Difficulty,
            DifficultyLabel = NarrativeDifficultyLabels.GetLabel(result.Difficulty),

            DieOne = result.DieOne,
            DieTwo = result.DieTwo,
            HasDice = result.HasDice,

            PassiveBase = spec.Kind == NarrativeCheckKind.Passive ? NarrativeCheckMath.PassiveBase : 0,

            AppliedModifiers = result.AppliedModifiers ?? new List<NarrativeAppliedModifierSnapshot>(),
            RawContextModifier = result.RawContextModifier,
            AppliedContextModifier = result.AppliedContextModifier,

            Total = result.Total
        };
    }

    // Заголовок строки, которую видит игрок: "ЧУТЬЁ" либо
    // "ЧУТЬЁ + СЛЕДОПЫТСТВО" (§2/§13 инструкции). ID никогда не участвуют.
    public static string BuildSourceLabel(NarrativeCheckPresentationData data)
    {
        if (data == null)
            return string.Empty;

        return data.HasCompetency
            ? data.QualityLabel + " + " + data.CompetencyLabel
            : data.QualityLabel;
    }
}

// Текстовое представление подробной математики проверки — используется
// там, где нет собственного UI Toolkit tooltip (нативный tooltip
// EditorGUI в Preview Базы диалогов, §17). Runtime-tooltip (UI Toolkit)
// строит те же данные как отдельные строки/классы USS напрямую из
// NarrativeCheckPresentationData, не через этот форматтер.
public static class NarrativeCheckPresentationText
{
    public static string BuildFullBreakdown(NarrativeCheckPresentationData data)
    {
        if (data == null)
            return string.Empty;

        StringBuilder text = new StringBuilder();
        text.Append(data.Kind == NarrativeCheckKind.Passive ? "ПАССИВНАЯ ПРОВЕРКА" : "АКТИВНАЯ ПРОВЕРКА").Append('\n');
        text.Append("Сложность: ").Append(NarrativeDifficultyLabels.Describe(data.Difficulty)).Append('\n').Append('\n');

        if (data.Kind == NarrativeCheckKind.Passive)
        {
            text.Append("База пассивной проверки: ").Append(data.PassiveBase.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        else
        {
            text.Append("Кубики: ").Append(data.DieOne).Append(" + ").Append(data.DieTwo)
                .Append(" = ").Append(data.DieOne + data.DieTwo).Append('\n');
        }

        text.Append(data.QualityLabel).Append(": ").Append(data.QualityValue.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (data.HasCompetency)
            text.Append(data.CompetencyLabel).Append(": ").Append(data.CompetencyValue.ToString(CultureInfo.InvariantCulture)).Append('\n');

        if (data.AppliedModifiers != null && data.AppliedModifiers.Count > 0)
        {
            text.Append('\n').Append("Контекст:").Append('\n');
            for (int i = 0; i < data.AppliedModifiers.Count; i++)
            {
                NarrativeAppliedModifierSnapshot modifier = data.AppliedModifiers[i];
                if (modifier == null)
                    continue;
                text.Append("  ").Append(modifier.Label).Append(": ").Append(FormatSigned(modifier.Value)).Append('\n');
            }

            if (data.RawContextModifier != data.AppliedContextModifier)
            {
                text.Append('\n');
                text.Append("Сумма модификаторов: ").Append(FormatSigned(data.RawContextModifier)).Append('\n');
                text.Append("Применено: ").Append(FormatSigned(data.AppliedContextModifier)).Append('\n');
                text.Append("Лимит контекста: ±").Append(NarrativeCheckMath.MaxContextModifier.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        text.Append('\n');
        text.Append("Итого: ").Append(data.Total.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("Нужно: ").Append(data.Difficulty.ToString(CultureInfo.InvariantCulture)).Append('\n').Append('\n');

        string comparator = data.Total >= data.Difficulty ? " ≥ " : " < ";
        text.Append(data.Total.ToString(CultureInfo.InvariantCulture)).Append(comparator).Append(data.Difficulty.ToString(CultureInfo.InvariantCulture))
            .Append(" — ").Append(data.Success ? "УСПЕХ" : "ПРОВАЛ");

        return text.ToString();
    }

    private static string FormatSigned(int value)
    {
        return value >= 0
            ? "+" + value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
    }
}
