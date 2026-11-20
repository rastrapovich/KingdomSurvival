using System.Collections;
using System.Reflection;
using KingdomSurvival.LocationRendering.Editor;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Tests
{
    // Окно «Обработка кадра» открывается на генерируемой арене: арена
    // собирается, кадр рисуется без ошибок, панель — обработка кадра арены.
    public sealed class FrameStyleWindowArenaTests
    {
        [UnityTest]
        public IEnumerator FrameWindow_ShowsGeneratedArena()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            FrameStyleWindow.Open(FrameStyleWindow.ArenaId);
            FrameStyleWindow window = EditorWindow.GetWindow<FrameStyleWindow>();
            try
            {
                FieldInfo field = typeof(FrameStyleWindow).GetField("arena", BindingFlags.Instance | BindingFlags.NonPublic);
                double until = EditorApplication.timeSinceStartup + 90;
                MapLabArena arena = null;
                while (EditorApplication.timeSinceStartup < until)
                {
                    arena = (MapLabArena)field.GetValue(window);
                    if (arena != null && arena.Ready) break;
                    yield return null;
                }
                Assert.That(arena, Is.Not.Null, "арена окна создана");
                Assert.That(arena.Ready, Is.True, arena.Error);
                window.Repaint();
                for (int i = 0; i < 10; i++) yield return null;
                Assert.That(window.rootVisualElement.Query<Label>().ToList()
                    .Exists(label => label.text.StartsWith("Правится: обработка кадра арен")), Is.True, "панель — обработка кадра арены");
            }
            finally
            {
                window.Close();
            }
        }
    }
}
