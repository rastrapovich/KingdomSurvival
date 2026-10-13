using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Затемнение краёв места: выключено — слоя нет; включено — прямоугольник во
    // весь рисунок места поверх всего (земли, предметов, людей), правка
    // настроек видна без пересборки.
    public sealed class LocationEdgeShadeTests
    {
        [Test]
        public void EdgeShade_CoversWholeLocation_AboveEverything()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_edge", DisplayName = "Поляна", CanvasWidth = 1920, CanvasHeight = 1080 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetActors(new[] { new LocationWorldRenderer.ActorFrame { Id = "hero", Pixel = new Vector2(960, 1070) } }, 0);
                renderer.SetTime(13, 0);
                Assert.That(renderer.EdgeShadeRenderer, Is.Null, "По умолчанию выключено.");

                visual.EdgeShade.Enabled = true;
                renderer.SetTime(13, .1f);
                SpriteRenderer edge = renderer.EdgeShadeRenderer;
                Assert.That(edge, Is.Not.Null, "Включили — слой появился без пересборки.");
                Vector2 min = LocationVisualGeometry.PixelToWorld(location, Vector2.zero), max = LocationVisualGeometry.PixelToWorld(location, new Vector2(1920, 1080));
                Assert.That(edge.bounds.size.x, Is.EqualTo(Mathf.Abs(max.x - min.x)).Within(1e-3f), "Во весь рисунок места по ширине.");
                Assert.That(edge.bounds.size.y, Is.EqualTo(Mathf.Abs(max.y - min.y)).Within(1e-3f), "И по высоте.");
                int highest = int.MinValue;
                foreach (SpriteRenderer image in renderer.Root.GetComponentsInChildren<SpriteRenderer>())
                    if (image != edge) highest = Mathf.Max(highest, image.sortingOrder);
                Assert.That(edge.sortingOrder, Is.GreaterThan(highest), "Поверх всего, включая людей у нижнего края.");

                visual.EdgeShade.Enabled = false;
                renderer.SetTime(13, .2f);
                Assert.That(renderer.EdgeShadeRenderer, Is.Null, "Выключили — слоя нет.");
            }
        }
    }
}
