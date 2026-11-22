using System.Linq;
using System.Reflection;
using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Окно Базы существ: карточки портрета, миниатюры и
    // флажок статичной фигуры. Ничего не записывает в базу.
    public sealed class UnitDatabaseWindowViewsTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [TestCase(true)]
        [TestCase(false)]
        public void Cards_ShowStaticFigureToggle(bool staticFigureOnly)
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
                // Настройки художника не являются фикстурой: оба режима
                // проверяем на копии, не меняя авторскую базу.
                SerializedObject serialized = new SerializedObject(database);
                SerializedProperty units = serialized.FindProperty("units");
                SerializedProperty unit = units.GetArrayElementAtIndex(militia);
                unit.FindPropertyRelative("staticFigureOnly").boolValue = staticFigureOnly;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                typeof(UnitDatabaseWindow).GetField("database", Any).SetValue(window, database);
                typeof(UnitDatabaseWindow).GetField("serializedDatabase", Any).SetValue(window, serialized);
                typeof(UnitDatabaseWindow).GetField("unitsProperty", Any).SetValue(window, serialized.FindProperty("units"));
                typeof(UnitDatabaseWindow).GetField("tagsProperty", Any).SetValue(window, serialized.FindProperty("tags"));
                typeof(UnitDatabaseWindow).GetField("selectedUnitIndex", Any).SetValue(window, militia);
                MethodInfo show = typeof(UnitDatabaseWindow).GetMethod("ShowSelectedUnit", Any);
                show.Invoke(window, null);

                VisualElement root = window.rootVisualElement;
                Toggle staticToggle = root.Query<Toggle>().ToList().FirstOrDefault(toggle => toggle.label == "Статичная фигура вместо анимаций");
                Assert.IsNotNull(staticToggle);
                Assert.AreEqual(staticFigureOnly, staticToggle.value, "флажок отражает настройку существа");
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
