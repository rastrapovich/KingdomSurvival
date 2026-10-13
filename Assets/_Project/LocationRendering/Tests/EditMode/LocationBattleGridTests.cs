using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Сетка боя рисуется в мире места — над землёй, под предметами и людьми,
    // — а не элементом поля поверх картинки: линии не ложатся на фигуры.
    public sealed class LocationBattleGridTests
    {
        [Test]
        public void BattleGrid_LiesUnderPeopleAndObjects()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_grid", DisplayName = "Поле", CanvasWidth = 1920, CanvasHeight = 1080 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            visual.Objects.Add(new LocationVisualObject { Id = "crate", Name = "crate", Placeholder = LocationPlaceholder.Crate, Position = new Vector2(.5f, .5f) });
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetActors(new[] { new LocationWorldRenderer.ActorFrame { Id = "hero", Pixel = new Vector2(960, 1075) } }, 0);
                List<Vector2> centers = new List<Vector2> { new Vector2(900, 540), new Vector2(980, 540) };
                renderer.SetBattleGrid(centers, 40, BattlefieldFrame.VerticalScale, new Color(0, 0, 0, .1f), new Color(1, 1, 1, .5f), 2);
                Assert.That(renderer.BattleGridVisible, Is.True);
                MeshRenderer grid = renderer.Root.transform.Find("Сетка боя").GetComponent<MeshRenderer>();
                int lowestAbove = int.MaxValue;
                foreach (SpriteRenderer image in renderer.Root.GetComponentsInChildren<SpriteRenderer>())
                    if (image.name == "Рисунок" || image.name == "Фигура") lowestAbove = Mathf.Min(lowestAbove, image.sortingOrder);
                Assert.That(grid.sortingOrder, Is.LessThan(lowestAbove), "Под предметами и людьми.");
                Bounds bounds = grid.GetComponent<MeshFilter>().sharedMesh.bounds;
                Vector2 left = LocationVisualGeometry.PixelToWorld(location, new Vector2(900 - 40, 540));
                Vector2 right = LocationVisualGeometry.PixelToWorld(location, new Vector2(980 + 40, 540));
                Assert.That(bounds.min.x, Is.EqualTo(left.x).Within(.05f), "Гексы — там, куда их положило поле.");
                Assert.That(bounds.max.x, Is.EqualTo(right.x).Within(.05f));

                renderer.SetBattleGrid(null, 0, 0, Color.clear, Color.clear, 0);
                Assert.That(renderer.BattleGridVisible, Is.False, "Без клеток — скрыта.");
            }
        }
    }
}
