using System.Collections.Generic;

// 12Е-7: как сработавшая особенность выглядит для игрока. Одна форма везде:
// кто, какая особенность и что именно она дала. Строка итогов начинается с
// метки Tag — интерфейс выделяет её одним нейтральным цветом, одинаковым
// при любом исходе: выделение говорит «сработала особенность», а не
// «это правильный ответ».
public static class FeaturePresentation
{
    public const string Tag = "[ОСОБЕННОСТЬ]";

    // «Гаррик — «Зацепка»».
    public static string Title(GameState state, FeatureActivation activation)
    {
        if (activation == null)
            return string.Empty;
        string feature = "«" + FeatureCheckHooks.FeatureName(activation.FeatureId) + "»";
        string person = state != null && !string.IsNullOrEmpty(activation.PersonId)
            ? CharacterProgressionService.DisplayName(state, activation.PersonId)
            : string.Empty;
        return string.IsNullOrEmpty(person) ? feature : person + " — " + feature;
    }

    // Что дала особенность.
    public static string Body(FeatureActivation activation)
    {
        return activation?.Text ?? string.Empty;
    }

    // Строка итогов: «[ОСОБЕННОСТЬ] Гаррик — «Зацепка»: …».
    public static string Line(GameState state, FeatureActivation activation)
    {
        if (activation == null)
            return string.Empty;
        string body = Body(activation);
        return Tag + " " + Title(state, activation) + (string.IsNullOrEmpty(body) ? string.Empty : ": " + body);
    }

    public static List<string> Lines(GameState state, IEnumerable<FeatureActivation> activations)
    {
        List<string> lines = new List<string>();
        if (activations == null)
            return lines;
        foreach (FeatureActivation activation in activations)
        {
            if (activation != null)
                lines.Add(Line(state, activation));
        }
        return lines;
    }

    // Строки итогов для всего, что сработало после номера since.
    public static List<string> LinesSince(GameState state, int since)
    {
        return Lines(state, FeatureDispatcher.ActivationsSince(state, since));
    }

    public static bool IsFeatureLine(string line)
    {
        return line != null && line.StartsWith(Tag, System.StringComparison.Ordinal);
    }
}
