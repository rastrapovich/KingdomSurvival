using System.Linq;
using System.Reflection;
using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Окно Базы существ: переключатель видов строит карточки слота вида и
    // флажок статичной фигуры. Ничего не записывает в базу.
    public sealed class UnitDatabaseWindowViewsTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void ViewSwitch_ShowsViewCards_AndStaticFigureToggle()
        {
            UnitDatabaseWindow window = ScriptableObject.CreateInstance<UnitDatabaseWindow>();
            try
            {
                window.CreateGUI();
                UnitDatabaseAsset database = (UnitDatabaseAsset)typeof(UnitDatabaseWindow).GetField("database", Any).GetValue(window);
                int militia = database.Units.ToList().FindIndex(unit => unit != null && unit.Id == "militia");
                Assert.GreaterOrEqual(militia, 0);
                typeof(UnitDatabaseWindow).GetField("selectedUnitIndex", Any).SetValue(window, militia);
                MethodInfo show = typeof(UnitDatabaseWindow).GetMethod("ShowSelectedUnit", Any);
                show.Invoke(window, null);

                VisualElement root = window.rootVisualElement;
                Assert.IsTrue(root.Query<Button>().ToList().Any(button => button.text == "Мрачный"), "есть переключатель видов");
                Toggle staticToggle = root.Query<Toggle>().ToList().FirstOrDefault(toggle => toggle.label == "Статичная фигура вместо анимаций");
                Assert.IsNotNull(staticToggle);
                Assert.IsTrue(staticToggle.value, "у ополченца включена статичная фигура");

                typeof(UnitDatabaseWindow).GetField("editedViewId", Any).SetValue(window, UnitArtViews.GloomId);
                show.Invoke(window, null);
                Assert.IsTrue(root.Query<Label>().ToList().Any(label => label.text == "ПОРТРЕТ · МРАЧНЫЙ"), "карточка портрета вида");
                Assert.IsTrue(root.Query<Label>().ToList().Any(label => label.text == "Своего рисунка нет — в игре и здесь показан «Обычный»."),
                    "пустой слот вида объясняет подстановку");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}
