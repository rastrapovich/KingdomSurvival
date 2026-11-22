using System.Collections;
using System.Linq;
using System.Reflection;
using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Бриф завершения, этап 14: каталог боя в карточке существа — весь банк
    // тегов виден, тег и способность назначаются без ручного ввода ID.
    // Работает на копии базы, авторскую не меняет.
    public sealed class UnitDatabaseCatalogTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void Catalog_ShowsWholeBank_AssignsTagAndAbility_WithoutTypingIds()
        {
            UnitDatabaseWindow window = ScriptableObject.CreateInstance<UnitDatabaseWindow>();
            UnitDatabaseAsset testDatabase = null;
            try
            {
                window.CreateGUI();
                UnitDatabaseAsset database = (UnitDatabaseAsset)typeof(UnitDatabaseWindow).GetField("database", Any).GetValue(window);
                testDatabase = Object.Instantiate(database);
                int militia = testDatabase.Units.ToList().FindIndex(unit => unit != null && unit.Id == "militia");
                Assert.GreaterOrEqual(militia, 0);
                SerializedObject serialized = new SerializedObject(testDatabase);
                typeof(UnitDatabaseWindow).GetField("database", Any).SetValue(window, testDatabase);
                typeof(UnitDatabaseWindow).GetField("serializedDatabase", Any).SetValue(window, serialized);
                typeof(UnitDatabaseWindow).GetField("unitsProperty", Any).SetValue(window, serialized.FindProperty("units"));
                typeof(UnitDatabaseWindow).GetField("tagsProperty", Any).SetValue(window, serialized.FindProperty("tags"));
                typeof(UnitDatabaseWindow).GetField("selectedUnitIndex", Any).SetValue(window, militia);
                typeof(UnitDatabaseWindow).GetField("catalogTab", Any).SetValue(window, 0);
                MethodInfo show = typeof(UnitDatabaseWindow).GetMethod("ShowSelectedUnit", Any);
                show.Invoke(window, null);

                VisualElement root = window.rootVisualElement;
                Assert.IsTrue(root.Query<Label>().ToList().Any(label => label.text.StartsWith("КАТАЛОГ БОЯ")), "карточка каталога");
                Assert.IsTrue(root.Query<Button>().ToList().Any(button => button.text == "Способности"), "вкладка способностей");

                IList tags = (IList)typeof(UnitDatabaseWindow).GetMethod("LoadCatalogTags", Any).Invoke(null, null);
                Assert.GreaterOrEqual(tags.Count, 457, "весь банк: активные, корзина, архив");
                object charge = tags.Cast<object>().First(tag => (string)tag.GetType().GetField("Id").GetValue(tag) == "passive.20261119.005");
                Assert.IsTrue((bool)charge.GetType().GetField("Executable").GetValue(charge), "исполнитель есть");
                object trash = tags.Cast<object>().FirstOrDefault(tag => (bool)tag.GetType().GetField("Trash").GetValue(tag));
                Assert.IsNotNull(trash, "корзина видна");

                typeof(UnitDatabaseWindow).GetMethod("SetUnitTag", Any).Invoke(window, new[] { charge, (object)true });
                Assert.IsTrue(testDatabase.Units[militia].HasTag("passive.20261119.005"), "тег назначен кнопкой");
                Assert.IsNotNull(testDatabase.FindTag("passive.20261119.005"), "тег внесён в справочник базы — проверка базы его знает");

                IList abilities = (IList)typeof(UnitDatabaseWindow).GetMethod("LoadCatalogAbilities", Any).Invoke(null, null);
                object heal = abilities.Cast<object>().First(item => item.GetType().GetField("Kind").GetValue(item).ToString() == "Heal");
                int before = testDatabase.Units[militia].Abilities.Count;
                typeof(UnitDatabaseWindow).GetMethod("AddCatalogAbility", Any).Invoke(window, new[] { heal });
                Assert.AreEqual(before + 1, testDatabase.Units[militia].Abilities.Count);
                UnitAbilityData added = testDatabase.Units[militia].Abilities.Last();
                Assert.AreEqual(UnitAbilityKind.Heal, added.Kind);
                Assert.IsTrue(added.IsActive, "способность из каталога сразу активна");
                Assert.Greater(added.Amount, 0, "числа по умолчанию из движка");
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
