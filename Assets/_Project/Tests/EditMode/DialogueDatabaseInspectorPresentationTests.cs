using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using static KingdomSurvival.DialogueDatabase.Editor.DialogueDatabaseWindow;

// "Производственная инструкция: читаемые ноды и удобные Свойства узла" —
// §43 (тестируемая часть). Сама раскладка Inspector'а (сплиттер, GUIStyle,
// реальная ширина панели) не проверяема в EditMode без Unity — здесь
// покрыты только чистые classification/clamp-функции, не зависящие от
// SerializedProperty/IMGUI.
public sealed class DialogueDatabaseInspectorPresentationTests
{
    [Test]
    public void ClampInspectorWidth_BelowMinimum_ClampsToMinimum()
    {
        // §38 инструкции "свободный граф": минимум поднят с 360 до 420 —
        // семантическим карточкам нужно больше места, чем голому полю.
        Assert.AreEqual(420f, ClampInspectorWidth(100f, 1200f));
    }

    [Test]
    public void ClampInspectorWidth_AboveMaximum_ClampsToWindowFraction()
    {
        // 1200 * 0.6 = 720
        Assert.AreEqual(720f, ClampInspectorWidth(2000f, 1200f));
    }

    [Test]
    public void ClampInspectorWidth_WithinRange_IsUnchanged()
    {
        Assert.AreEqual(480f, ClampInspectorWidth(480f, 1200f));
    }

    [Test]
    public void ClampInspectorWidth_NarrowWindow_MinimumWins()
    {
        // Окно 500px: 500*0.6=300 < минимума 420 — минимум главнее.
        Assert.AreEqual(420f, ClampInspectorWidth(500f, 500f));
    }

    [Test]
    public void ResolveLayoutMode_NarrowWidth_ReturnsNarrowInspector()
    {
        Assert.AreEqual(DialogueEditorLayoutMode.NarrowInspector, ResolveLayoutMode(400f));
    }

    [Test]
    public void ResolveLayoutMode_WideWidth_ReturnsNormal()
    {
        Assert.AreEqual(DialogueEditorLayoutMode.Normal, ResolveLayoutMode(900f));
    }

    [Test]
    public void ShouldStackLongField_NarrowInspector_ReturnsTrue()
    {
        Assert.IsTrue(ShouldStackLongField(DialogueEditorLayoutMode.NarrowInspector));
    }

    [Test]
    public void ShouldStackLongField_Normal_ReturnsFalse()
    {
        Assert.IsFalse(ShouldStackLongField(DialogueEditorLayoutMode.Normal));
    }

    // §28: какие поля ответа релевантны его виду.
    [TestCase(DialogueChoiceKind.ActiveReturnable, true)]
    [TestCase(DialogueChoiceKind.ActiveDecisive, true)]
    [TestCase(DialogueChoiceKind.Normal, false)]
    [TestCase(DialogueChoiceKind.Exit, false)]
    [TestCase(DialogueChoiceKind.Continue, false)]
    public void ShouldShowActiveCheckFields_OnlyForActiveKinds(DialogueChoiceKind kind, bool expected)
    {
        Assert.AreEqual(expected, ShouldShowActiveCheckFields(kind));
    }

    [TestCase(DialogueChoiceKind.Normal, true)]
    [TestCase(DialogueChoiceKind.Exit, false)]
    [TestCase(DialogueChoiceKind.ActiveReturnable, false)]
    [TestCase(DialogueChoiceKind.ActiveDecisive, false)]
    [TestCase(DialogueChoiceKind.Continue, true)]
    public void ShouldShowNormalTargetField_OnlyForNormalKind(DialogueChoiceKind kind, bool expected)
    {
        Assert.AreEqual(expected, ShouldShowNormalTargetField(kind));
    }

    // §18-25 "свободный граф": семь смысловых категорий должны быть
    // визуально различимы — проверяем то, что можно проверить без реального
    // рендера: результат детерминирован для темы теста (isProSkin решает
    // EditorGUIUtility в момент вызова тестового ранера) и разные категории
    // не совпадают между собой.
    [Test]
    public void GetSemanticAccentColor_ConditionAndEffectAreVisuallyDistinct()
    {
        // §25: "Condition (охра) и Effect (зелёный) — визуально
        // противоположные категории" — это единственная пара, для которой
        // инструкция явно требует контраста.
        Assert.AreNotEqual(
            GetSemanticAccentColor(SemanticCategory.Condition),
            GetSemanticAccentColor(SemanticCategory.Effect));
    }

    [Test]
    public void GetSemanticAccentColor_EffectAndErrorAreVisuallyDistinct()
    {
        // Успех/провал ответа с активной проверкой красятся Effect/Error —
        // если бы они совпадали, зелёно-красная пара из §25 не читалась бы.
        Assert.AreNotEqual(
            GetSemanticAccentColor(SemanticCategory.Effect),
            GetSemanticAccentColor(SemanticCategory.Error));
    }

    [Test]
    public void GetSemanticAccentColor_AllSevenCategoriesAreDistinct()
    {
        SemanticCategory[] categories =
        {
            SemanticCategory.Character, SemanticCategory.Text, SemanticCategory.Check,
            SemanticCategory.Condition, SemanticCategory.Effect, SemanticCategory.Choice,
            SemanticCategory.Error
        };

        for (int i = 0; i < categories.Length; i++)
        {
            for (int j = i + 1; j < categories.Length; j++)
            {
                Assert.AreNotEqual(
                    GetSemanticAccentColor(categories[i]),
                    GetSemanticAccentColor(categories[j]),
                    categories[i] + " и " + categories[j] + " должны иметь разные акцентные цвета");
            }
        }
    }

    [Test]
    public void GetSemanticAccentColor_IsFullyOpaque()
    {
        // Акцентная полоса рисуется поверх фона карточки — прозрачность
        // сделала бы её плохо заметной на некоторых темах.
        Assert.AreEqual(1f, GetSemanticAccentColor(SemanticCategory.Check).a);
    }
}
