using System.Linq;
using System.Reflection;
using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Окно Базы существ: переключатель видов строит карточки слота вида и
    // флажок статичной фигуры. Ничего не записывает в базу.
    public sealed class UnitDatabaseWindowViewsTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [TestCase(true)]
        [TestCase(false)]
        public void ViewSwitch_ShowsViewCards_AndStaticFigureToggle(bool staticFigureOnly)
        {
            UnitDatabaseWindow window = ScriptableObject.CreateInstance<UnitDatabaseWindow>();
            UnitDatabaseAsset testDatabase = null;
            try
            {
                window.CreateGUI();
                UnitDatabaseAsset database = (UnitDatabaseAsset)typeof(UnitDatabaseWindow).GetField("database", Any).GetValue(window);
                testDatabase = Object.Instantiate(database);
                database = testDatabase;
                int militia = database.Units.ToList().FindIndex(unit => unit != null && unit.Id == "militia");
                Assert.GreaterOrEqual(militia, 0);
                // Настройки художника не являются фикстурой: оба режима и пустой
                // слот вида проверяем на копии, не меняя авторскую базу.
                SerializedObject serialized = new SerializedObject(database);
                SerializedProperty units = serialized.FindProperty("units");
                SerializedProperty unit = units.GetArrayElementAtIndex(militia);
                unit.FindPropertyRelative("staticFigureOnly").boolValue = staticFigureOnly;
                unit.FindPropertyRelative("viewArt").ClearArray();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                typeof(UnitDatabaseWindow).GetField("database", Any).SetValue(window, database);
                typeof(UnitDatabaseWindow).GetField("serializedDatabase", Any).SetValue(window, serialized);
                typeof(UnitDatabaseWindow).GetField("unitsProperty", Any).SetValue(window, serialized.FindProperty("units"));
                typeof(UnitDatabaseWindow).GetField("tagsProperty", Any).SetValue(window, serialized.FindProperty("tags"));
                typeof(UnitDatabaseWindow).GetField("selectedUnitIndex", Any).SetValue(window, militia);
                MethodInfo show = typeof(UnitDatabaseWindow).GetMethod("ShowSelectedUnit", Any);
                show.Invoke(window, null);

                VisualElement root = window.rootVisualElement;
                Assert.IsTrue(root.Query<Button>().ToList().Any(button => button.text == "Мрачный"), "есть переключатель видов");
                Toggle staticToggle = root.Query<Toggle>().ToList().FirstOrDefault(toggle => toggle.label == "Статичная фигура вместо анимаций");
                Assert.IsNotNull(staticToggle);
                Assert.AreEqual(staticFigureOnly, staticToggle.value, "флажок отражает настройку существа");

                typeof(UnitDatabaseWindow).GetField("editedViewId", Any).SetValue(window, UnitArtViews.GloomId);
                show.Invoke(window, null);
                Assert.IsTrue(root.Query<Label>().ToList().Any(label => label.text == "ПОРТРЕТ · МРАЧНЫЙ"), "карточка портрета вида");
                Assert.IsTrue(root.Query<Label>().ToList().Any(label => label.text == "Своего рисунка нет — в игре и здесь показан «Обычный»."),
                    "пустой слот вида объясняет подстановку");
            }
            finally
            {
                Object.DestroyImmediate(window);
                if (testDatabase != null)
                    Object.DestroyImmediate(testDatabase);
            }
        }
    }
}
