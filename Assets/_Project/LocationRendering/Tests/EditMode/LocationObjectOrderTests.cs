using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering
{
    // Порядок предметов в окне Базы локаций: список сверху вниз совпадает с
    // порядком рендерера, перестановка в списке и шаг выше/ниже меняют
    // порядок в кадре, свет без рисунка — в конце списка.
    public sealed class LocationObjectOrderTests
    {
        private static LocalLocationDefinition Location() =>
            new LocalLocationDefinition { Id = "zz_order", DisplayName = "Поляна", CanvasWidth = 1920, CanvasHeight = 1080 };

        private static LocationVisualObject Box(string id, float y) => new LocationVisualObject
        {
            Id = id, Name = id, Placeholder = LocationPlaceholder.Crate, Position = new Vector2(.5f, y), Band = LocationVisualBand.World
        };

        [Test]
        public void TopFirst_MatchesRenderer_AndReorderChangesDrawOrder()
        {
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            // Ниже на рисунке (больше y) — ближе к зрителю, поверх.
            LocationVisualObject far = Box("far", .3f), middle = Box("middle", .5f), near = Box("near", .7f);
            visual.Objects.AddRange(new[] { far, middle, near });
            visual.Objects.Add(new LocationVisualObject { Id = "lamp", Name = "lamp", LightOnly = true, Position = new Vector2(.5f, .5f) });

            List<LocationVisualObject> list = LocationObjectOrder.TopFirst(location, visual);
            Assert.That(list.ConvertAll(item => item.Id), Is.EqualTo(new[] { "near", "middle", "far", "lamp" }), "Сверху — ближний; свет — в конце.");
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
                foreach (LocationVisualObject item in new[] { far, middle, near })
                    Assert.That(renderer.ObjectSortingOrder(item.Id), Is.EqualTo(LocationObjectOrder.DrawOrder(location, item)), "Порядок как у рендерера.");

            // Перетащили дальний наверх списка — рисуется поверх всех.
            list.Remove(far);
            list.Insert(0, far);
            LocationObjectOrder.PlaceAt(location, list, 0);
            Assert.That(LocationObjectOrder.TopFirst(location, visual)[0], Is.SameAs(far));
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
                Assert.That(renderer.ObjectSortingOrder("far"), Is.GreaterThan(renderer.ObjectSortingOrder("near")), "В кадре — поверх.");

            // Между двумя — строго между (соседи сверху сдвигаются, если тесно).
            list = LocationObjectOrder.TopFirst(location, visual);
            list.Remove(near);
            list.Insert(1, near);
            LocationObjectOrder.PlaceAt(location, list, 1);
            Assert.That(LocationObjectOrder.TopFirst(location, visual).GetRange(0, 3).ConvertAll(item => item.Id), Is.EqualTo(new[] { "far", "near", "middle" }));

            // Шаг ниже и к краю слоя.
            Assert.That(LocationObjectOrder.Step(location, visual, far, -1), Is.True);
            Assert.That(LocationObjectOrder.TopFirst(location, visual)[1], Is.SameAs(far));
            Assert.That(LocationObjectOrder.ToEdgeOfBand(location, visual, far, false), Is.True);
            Assert.That(LocationObjectOrder.TopFirst(location, visual)[2], Is.SameAs(far), "Под всеми в своём слое.");
            Assert.That(LocationObjectOrder.Step(location, visual, far, -1), Is.False, "Ниже некуда: дальше только свет.");
        }

        // Вкладка «Предметы» с выбранным предметом: список в порядке
        // отрисовки, перетаскиваемый, выбранный — выделен.
        [Test]
        public void ArtTab_ShowsReorderableObjectList()
        {
            LocationDatabaseWindow window = UnityEditor.EditorWindow.GetWindow<LocationDatabaseWindow>();
            try
            {
                window.CreateGUI();
                System.Type type = typeof(LocationDatabaseWindow);
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                LocationVisualDefinition visual = (LocationVisualDefinition)type.GetProperty("Visual", flags).GetValue(window);
                Assume.That(visual != null && visual.Objects.Count > 1, "У места по умолчанию нет предметов.");
                LocationVisualObject first = visual.Objects.Find(item => !item.LightOnly);
                System.Reflection.FieldInfo tab = type.GetField("tab", flags), kind = type.GetField("selectedKind", flags);
                tab.SetValue(window, System.Enum.Parse(tab.FieldType, "Art"));
                kind.SetValue(window, System.Enum.Parse(kind.FieldType, "Art"));
                type.GetField("selectedElementId", flags).SetValue(window, first.Id);
                type.GetMethod("BuildSettings", flags).Invoke(window, null);
                UnityEngine.UIElements.ScrollView settings = (UnityEngine.UIElements.ScrollView)type.GetField("settings", flags).GetValue(window);
                UnityEngine.UIElements.ListView list = settings.Q<UnityEngine.UIElements.ListView>();
                Assert.That(list, Is.Not.Null, "Список предметов.");
                Assert.That(list.reorderable, Is.True, "Перетаскивается.");
                Assert.That(list.itemsSource.Count, Is.EqualTo(visual.Objects.Count));
                Assert.That(list.selectedItem, Is.SameAs(first), "Выбранный выделен.");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void Reorder_AssetInstance_OverridesLayerOfMainPart()
        {
            LocalLocationDefinition location = Location();
            LocationVisualObject a = new LocationVisualObject { Id = "a", Name = "a", AssetId = "zz_missing", Position = new Vector2(.5f, .5f) };
            LocationObjectOrder.SetOrder(location, a, LocationVisualBand.Foreground, 25010);
            Assert.That(a.IsOverridden(LocationAssetOverride.Layer), Is.True, "Свой слой основы у экземпляра.");
            Assert.That(a.Band, Is.EqualTo(LocationVisualBand.Foreground));
            Assert.That(a.OrderOffset, Is.EqualTo(10));
        }
    }
}
