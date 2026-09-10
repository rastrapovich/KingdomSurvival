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
        Assert.AreEqual(360f, ClampInspectorWidth(100f, 1200f));
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
        // Окно 500px: 500*0.6=300 < минимума 360 — минимум главнее.
        Assert.AreEqual(360f, ClampInspectorWidth(500f, 500f));
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
    public void ShouldShowActiveCheckFields_OnlyForActiveKinds(DialogueChoiceKind kind, bool expected)
    {
        Assert.AreEqual(expected, ShouldShowActiveCheckFields(kind));
    }

    [TestCase(DialogueChoiceKind.Normal, true)]
    [TestCase(DialogueChoiceKind.Exit, false)]
    [TestCase(DialogueChoiceKind.ActiveReturnable, false)]
    [TestCase(DialogueChoiceKind.ActiveDecisive, false)]
    public void ShouldShowNormalTargetField_OnlyForNormalKind(DialogueChoiceKind kind, bool expected)
    {
        Assert.AreEqual(expected, ShouldShowNormalTargetField(kind));
    }
}
