using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Основание кистью: маска закрашенной земли ракурса хранится битами,
    // сливается в прямоугольники, растёт за край; в месте следует за
    // масштабом, отражением и растяжением экземпляра и даёт непроходимость
    // ровно по закрашенному — не по охвату.
    public sealed class ArtAssetFootprintMaskTests
    {
        private readonly List<Object> owned = new List<Object>();
        private ArtAssetDatabaseAsset catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            owned.Add(catalog);
            ArtAssetDatabaseAsset.Override = catalog;
        }

        [TearDown]
        public void TearDown()
        {
            ArtAssetDatabaseAsset.Override = null;
            foreach (Object item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        private static float Area(IEnumerable<Rect> rects) => rects.Sum(rect => rect.width * rect.height);

        [Test]
        public void Mask_PaintsErasesGrows_AndMergesIntoRects()
        {
            ArtAssetFootprintMask mask = new ArtAssetFootprintMask();
            Assert.That(mask.IsEmpty, Is.True);
            mask.Reset(new Rect(-1, -1, 2, 2), .1f);
            Assert.That(mask.Columns, Is.EqualTo(20));
            Assert.That(mask.IsEmpty, Is.True, "Сетка без клеток — основание прямоугольником.");

            Assert.That(mask.Paint(Vector2.zero, .5f, true), Is.True);
            int painted = mask.Count;
            Assert.That(painted, Is.InRange(70, 90), "Круг радиуса 0,5 при клетке 0,1 — около 78 клеток.");
            Assert.That(mask.Contains(Vector2.zero), Is.True);
            Assert.That(mask.Contains(new Vector2(.8f, .8f)), Is.False);
            Assert.That(Area(mask.Rects()), Is.EqualTo(painted * .01f).Within(1e-4f), "Прямоугольники покрывают ровно закрашенное.");
            Assert.That(mask.Rects().Count, Is.LessThan(painted / 2), "Отрезки рядов сливаются.");

            // Кисть за краем — сетка растёт, прежнее остаётся на месте.
            mask.Paint(new Vector2(1.6f, 0), .2f, true);
            Assert.That(mask.GridRect.xMax, Is.GreaterThanOrEqualTo(1.8f - 1e-4f));
            Assert.That(mask.Contains(Vector2.zero) && mask.Contains(new Vector2(1.6f, 0)), Is.True);

            // Стирание и запись — биты переживают JSON (как в ассете каталога).
            mask.Paint(Vector2.zero, .15f, false);
            Assert.That(mask.Contains(Vector2.zero), Is.False);
            ArtAssetViewSettings settings = new ArtAssetViewSettings { FootprintMask = mask };
            ArtAssetViewSettings copy = JsonUtility.FromJson<ArtAssetViewSettings>(JsonUtility.ToJson(settings));
            Assert.That(copy.UsesFootprintMask, Is.True);
            Assert.That(copy.FootprintMask.Count, Is.EqualTo(mask.Count));
            Assert.That(copy.FootprintMask.Contains(new Vector2(1.6f, 0)) && !copy.FootprintMask.Contains(Vector2.zero), Is.True);

            // Контур прямоугольника 3×2 клетки — четыре слитых отрезка по краю.
            mask.Reset(new Rect(0, 0, 1, 1), .1f);
            mask.Fill(point => point.x < .3f && point.y < .2f);
            List<Vector4> outline = mask.Outline();
            Assert.That(outline.Count, Is.EqualTo(4));
            Assert.That(outline.Sum(edge => Vector2.Distance(new Vector2(edge.x, edge.y), new Vector2(edge.z, edge.w))), Is.EqualTo(1f).Within(1e-4f));

            mask.Clear();
            Assert.That(mask.IsEmpty, Is.True);
            Assert.That(new ArtAssetViewSettings().UsesFootprintMask, Is.False, "Прежние ассеты — прямоугольником.");
        }

        [Test]
        public void Location_BlocksExactlyThePaintedGround_WithScaleFlipAndStretch()
        {
            Texture2D texture = new Texture2D(40, 40, TextureFormat.RGBA32, false);
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 40, 40), new Vector2(.5f, 0), 100);
            owned.Add(sprite);
            ArtAssetDefinition asset = new ArtAssetDefinition { Id = "zz_rock", Name = "Камень", PixelsPerUnit = 40, BlocksMovement = true };
            asset.MainPart.View(ArtAssetView.Front).Sprite = sprite;
            // «Г»: полоса вправо от опоры и столбик слева вглубь.
            ArtAssetFootprintMask mask = asset.Settings(ArtAssetView.Front).FootprintMask;
            mask.Reset(new Rect(-1, 0, 3, 2), .1f);
            mask.Fill(point => (point.y < .3f && point.x > -1 && point.x < 2) || (point.x < -.6f && point.y < 1.5f));
            catalog.assets.Add(asset);
            catalog.MarkChanged();

            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_mask", DisplayName = "Маска", CanvasWidth = 1920, CanvasHeight = 1080, HexesAcross = 160 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id };
            LocationVisualObject rock = new LocationVisualObject { Id = "rock", AssetId = asset.Id, Scale = 2, FlipX = true, Stretch = 1.5f, Position = new Vector2(.5f, .5f) };
            visual.Objects.Add(rock);

            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(rock);
            Assert.That(resolved.FootprintCells, Is.Not.Null);
            Assert.That(Area(resolved.FootprintCells), Is.EqualTo(Area(mask.Rects()) * 2 * 2 * 1.5f).Within(1e-3f), "Площадь — с масштабом и растяжением.");
            List<Rect> blocked = LocationVisualGeometry.BlockedAreas(visual, location);
            Assert.That(blocked.Count, Is.EqualTo(resolved.FootprintCells.Count));

            LocalLocationGeometry geometry = new LocalLocationGeometry(location, null, blocked);
            Vector2 anchor = new Vector2(960, 540);
            float unit = LocationVisualGeometry.PixelsPerUnit;
            // Отражено: столбик «Г» (слева у ассета) — справа от опоры; полоса — влево.
            Vector2 Pixel(float x, float y) => anchor + new Vector2(x * 2 * 1.5f, -y * 2) * unit;
            Assert.That(geometry.IsPassable(Pixel(.8f, 1.2f).x, Pixel(.8f, 1.2f).y), Is.False, "Столбик — непроходим (отражён вправо).");
            Assert.That(geometry.IsPassable(Pixel(-1.5f, .1f).x, Pixel(-1.5f, .1f).y), Is.False, "Полоса — непроходима (отражена влево).");
            Assert.That(geometry.IsPassable(Pixel(-.5f, 1.2f).x, Pixel(-.5f, 1.2f).y), Is.True, "Внутри охвата, но не закрашено — проход есть.");

            // Своя проходимость экземпляра — снова прямоугольник.
            rock.Overrides |= LocationAssetOverride.Passability;
            rock.BlocksMovement = true;
            rock.Footprint = new Vector2(1, 1);
            Assert.That(LocationVisualResolver.Resolve(rock).FootprintCells, Is.Null);
            Assert.That(LocationVisualGeometry.BlockedAreas(visual, location).Count, Is.EqualTo(1));
        }
    }
}
