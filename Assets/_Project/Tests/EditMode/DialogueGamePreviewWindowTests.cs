using System;
using System.Linq;
using System.Reflection;
using KingdomSurvival.DialogueDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Превью диалога «как в игре» (UI/Editor/DialogueGamePreviewWindow.cs,
// сборка Assembly-CSharp-Editor — поэтому через рефлексию): окно строит
// настоящее игровое окно диалога и проходит разговор из базы.
public sealed class DialogueGamePreviewWindowTests
{
    private const string DialogueId = "road_rats_01";

    private static Type WindowType =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("DialogueGamePreviewWindow"))
            .FirstOrDefault(type => type != null);

    [Test]
    public void Preview_IsRegistered_ForDialogueDatabase()
    {
        Assert.IsNotNull(WindowType, "Окно превью не скомпилировано.");
        Assert.IsNotNull(DialogueGamePreview.Opener, "База диалогов не может открыть превью.");
    }

    [Test]
    public void Preview_RendersGameDialogueWindow_AndAdvancesOnChoice()
    {
        Type type = WindowType;
        Assert.IsNotNull(type);
        EditorWindow window = (EditorWindow)ScriptableObject.CreateInstance(type);
        try
        {
            type.GetField("dialogueId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, DialogueId);
            type.GetMethod("CreateGUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

            VisualElement root = window.rootVisualElement;
            VisualElement overlay = root.Q<VisualElement>("narrative-dialogue-overlay");
            Assert.IsNotNull(overlay, "Игровое окно диалога построено из Prototype_Main.uxml.");
            Assert.AreNotEqual(DisplayStyle.None, overlay.style.display.value);

            VisualElement history = root.Q<VisualElement>("narrative-dialogue-history");
            Assert.Greater(history.childCount, 0, "Первая реплика показана.");
            Assert.IsFalse(string.IsNullOrEmpty(root.Q<Label>("narrative-dialogue-speaker").text), "Имя говорящего показано.");

            Button[] choices = root.Q<VisualElement>("narrative-dialogue-choices").Query<Button>().ToList().ToArray();
            Assert.Greater(choices.Length, 0, "Есть кнопки ответа.");

            // Главное меню и прочие экраны игры скрыты — видно только окно диалога.
            foreach (VisualElement sibling in overlay.parent.Children().Where(element => element != overlay))
                Assert.AreEqual(DisplayStyle.None, sibling.style.display.value, sibling.name + " должен быть скрыт в превью.");

            int before = history.childCount;
            Button first = choices.First(button => button.enabledSelf);
            // Окно не показано на экране (нет панели для событий) — вызываем
            // обработчик кнопки так же, как его вызвал бы клик.
            MethodInfo invoke = typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(EventBase) }, null);
            Assert.IsNotNull(invoke);
            using (ClickEvent click = ClickEvent.GetPooled())
                invoke.Invoke(first.clickable, new object[] { click });
            Assert.Greater(history.childCount, before, "Выбор ответа продолжает разговор.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }
}
