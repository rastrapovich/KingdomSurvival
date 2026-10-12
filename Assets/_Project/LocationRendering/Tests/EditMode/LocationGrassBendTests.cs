using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Реакция растительности на проход: закон прогиба (спад, закреплённая
    // часть, ноль без NaN, отрезок движения, сложение источников, сглаживание
    // при разном шаге), совместимость данных и работа в рендерере — ковёр и
    // объекты гнутся от ног, корни на месте, камни не гнутся, после ухода
    // форма точно исходная; замер цены подсистемы на плотной сцене.
    public sealed class LocationGrassBendTests
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

        private void Asset(string id)
        {
            Texture2D texture = new Texture2D(16, 32, TextureFormat.RGBA32, false);
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), new Vector2(.5f, 0), 100);
            owned.Add(sprite);
            ArtAssetDefinition asset = new ArtAssetDefinition { Id = id, Name = id, PixelsPerUnit = 40 };
            asset.MainPart.View(ArtAssetView.Front).Sprite = sprite;
            asset.Settings(ArtAssetView.Front).Pivot = new Vector2(.5f, .1f);
            catalog.assets.Add(asset);
            catalog.MarkChanged();
        }

        private static LocalLocationDefinition Location() =>
            new LocalLocationDefinition { Id = "zz_bend", DisplayName = "Трава", CanvasWidth = 1920, CanvasHeight = 1080 };

        private static LocationScatterLayer Layer(LocationScatterMode mode, params string[] assets)
        {
            LocationScatterLayer layer = new LocationScatterLayer { Name = "Трава", Mode = mode, Band = LocationVisualBand.GroundDetail };
            foreach (string id in assets) layer.Assets.Add(new LocationScatterEntry { AssetId = id });
            layer.Reaction.Enabled = true;
            layer.Reaction.Radius = 1;
            layer.Reaction.Bend = .2f;
            layer.Reaction.Jitter = 0;
            return layer;
        }

        private static LocationScatterInstance Put(LocationScatterLayer layer, string assetId, Vector2 pixel, bool flip = false, float rotation = 0)
        {
            LocationScatterInstance instance = new LocationScatterInstance
            {
                Key = layer.NextKey++, AssetId = assetId, Position = new Vector2(pixel.x / 1920, pixel.y / 1080), FlipX = flip, Rotation = rotation
            };
            layer.Instances.Add(instance);
            return instance;
        }

        private static LocationWorldRenderer.ActorFrame Actor(string id, Vector2 pixel) =>
            new LocationWorldRenderer.ActorFrame { Id = id, Pixel = pixel };

        private static void Run(LocationWorldRenderer renderer, float seconds, float step = 1 / 60f)
        {
            for (float time = 0; time < seconds; time += step) renderer.StepVegetation(step);
        }

        // ------------------------------------------------------------------
        // Закон прогиба
        // ------------------------------------------------------------------

        [Test]
        public void Falloff_IsFullAtFeet_FadesSmoothly_AndZeroBeyondRadius()
        {
            Assert.That(LocationGrassBend.Falloff(0, 10), Is.EqualTo(1));
            float previous = 1;
            for (float d = .5f; d < 10; d += .5f)
            {
                float value = LocationGrassBend.Falloff(d, 10);
                Assert.That(value, Is.LessThan(previous), "Спад монотонный.");
                previous = value;
            }
            Assert.That(LocationGrassBend.Falloff(9.9f, 10), Is.LessThan(.001f), "У края — почти ноль, без видимой границы.");
            Assert.That(LocationGrassBend.Falloff(10, 10), Is.EqualTo(0));
            Assert.That(LocationGrassBend.Falloff(25, 10), Is.EqualTo(0));
            Assert.That(LocationGrassBend.Falloff(1, 0), Is.EqualTo(0), "Нулевой радиус — без деления на ноль.");
        }

        [Test]
        public void Weight_PinsRoots_AndGrowsToTop()
        {
            Assert.That(LocationGrassBend.Weight(-.2f, .25f), Is.EqualTo(0), "Ниже опоры — неподвижно.");
            Assert.That(LocationGrassBend.Weight(0, .25f), Is.EqualTo(0), "Опора — на месте.");
            Assert.That(LocationGrassBend.Weight(.25f, .25f), Is.EqualTo(0), "Закреплённая часть — на месте.");
            Assert.That(LocationGrassBend.Weight(1, .25f), Is.EqualTo(1).Within(1e-5f));
            float middle = LocationGrassBend.Weight(.625f, .25f);
            Assert.That(middle, Is.LessThan(.5f), "Верх гнётся сильнее середины.");
            Assert.That(LocationGrassBend.Weight(1.5f, .25f), Is.EqualTo(1), "Выше верха — не больше полного.");
        }

        [Test]
        public void Push_AtZeroDistance_HasNoNaN_AndUsesMovementOrFallback()
        {
            Vector2 still = LocationGrassBend.Push(Vector2.zero, Vector2.zero, Vector2.zero, 10, 1, Vector2.zero, Vector2.zero);
            Assert.That(float.IsNaN(still.x) || float.IsNaN(still.y), Is.False);
            Assert.That(still.magnitude, Is.EqualTo(1).Within(1e-4f));
            Vector2 moving = LocationGrassBend.Push(Vector2.zero, Vector2.zero, Vector2.zero, 10, 1, Vector2.up, Vector2.right);
            Assert.That(moving.normalized.y, Is.GreaterThan(.99f), "У самых ног — по движению.");
            Vector2 fallback = LocationGrassBend.Push(Vector2.zero, Vector2.zero, Vector2.zero, 10, 1, Vector2.zero, Vector2.left);
            Assert.That(fallback.normalized.x, Is.LessThan(-.99f), "Без движения — прежний наклон.");
        }

        [Test]
        public void Push_PartsGrassToBothSides()
        {
            Vector2 left = LocationGrassBend.Push(new Vector2(-4, 0), Vector2.zero, Vector2.zero, 10, 1, Vector2.zero, Vector2.zero);
            Vector2 right = LocationGrassBend.Push(new Vector2(4, 0), Vector2.zero, Vector2.zero, 10, 1, Vector2.zero, Vector2.zero);
            Assert.That(left.x, Is.LessThan(0));
            Assert.That(right.x, Is.GreaterThan(0));
            Assert.That(left.magnitude, Is.EqualTo(right.magnitude).Within(1e-5f));
            Vector2 far = LocationGrassBend.Push(new Vector2(12, 0), Vector2.zero, Vector2.zero, 10, 1, Vector2.zero, Vector2.zero);
            Assert.That(far, Is.EqualTo(Vector2.zero), "За радиусом реакции нет.");
        }

        [Test]
        public void Push_UsesSegment_FastStepDoesNotSkipTuft()
        {
            Vector2 plant = new Vector2(100, 3);
            Vector2 end = LocationGrassBend.Push(plant, new Vector2(200, 0), new Vector2(200, 0), 10, 1, Vector2.zero, Vector2.zero);
            Assert.That(end, Is.EqualTo(Vector2.zero), "Только по конечной точке пучок был бы пропущен.");
            Vector2 swept = LocationGrassBend.Push(plant, Vector2.zero, new Vector2(200, 0), 10, 1, Vector2.right, Vector2.zero);
            Assert.That(swept.magnitude, Is.GreaterThan(.5f), "Отрезок шага задевает пучок.");
            Assert.That(swept.y, Is.GreaterThan(0), "Пучок сбоку от пути отходит в свою сторону.");
        }

        [Test]
        public void Sources_AddUp_WithLimit()
        {
            Vector2 a = LocationGrassBend.Push(Vector2.zero, new Vector2(-1, 0), new Vector2(-1, 0), 10, 1, Vector2.zero, Vector2.zero);
            Vector2 b = LocationGrassBend.Push(Vector2.zero, new Vector2(0, -1), new Vector2(0, -1), 10, 1, Vector2.zero, Vector2.zero);
            Assert.That(LocationGrassBend.Limit(a + b).magnitude, Is.LessThanOrEqualTo(1 + 1e-5f), "Сумма — не больше полного прогиба.");
            Vector2 c = LocationGrassBend.Push(Vector2.zero, new Vector2(1, 0), new Vector2(1, 0), 10, 1, Vector2.zero, Vector2.zero);
            Assert.That(LocationGrassBend.Limit(a + c).magnitude, Is.LessThan(.01f), "Встречные — гасят друг друга, без переворота.");
        }

        [Test]
        public void Approach_IsFrameRateIndependent_RecoverSlowerThanReact()
        {
            Vector2 fine = Vector2.zero, coarse = Vector2.zero;
            for (int i = 0; i < 60; i++) fine = LocationGrassBend.Approach(fine, Vector2.right, 1 / 60f, .12f, .6f);
            for (int i = 0; i < 6; i++) coarse = LocationGrassBend.Approach(coarse, Vector2.right, 1 / 6f, .12f, .6f);
            Assert.That(fine.x, Is.EqualTo(coarse.x).Within(1e-4f), "60 и 6 шагов за секунду — одно и то же.");
            Vector2 bent = LocationGrassBend.Approach(Vector2.zero, Vector2.right, .12f, .12f, .6f);
            Assert.That(bent.x, Is.EqualTo(.95f).Within(.01f), "За время реакции — ≈95 %.");
            Vector2 back = LocationGrassBend.Approach(Vector2.right, Vector2.zero, .12f, .12f, .6f);
            Assert.That(back.x, Is.GreaterThan(.5f), "Возврат медленнее прогиба.");
            Assert.That(LocationGrassBend.Approach(new Vector2(.4f, 0), Vector2.right, 0, .12f, .6f), Is.EqualTo(new Vector2(.4f, 0)), "Нулевой шаг — без изменений.");
            Vector2 resumed = LocationGrassBend.Approach(new Vector2(.4f, 0), Vector2.right, 1 / 60f, .12f, .6f);
            Assert.That(resumed.x, Is.GreaterThan(.4f).And.LessThan(.9f), "Новое воздействие — от текущего состояния, без скачка.");
        }

        [Test]
        public void Strength_StandingKeepsSoftContact_WalkingFull()
        {
            Assert.That(LocationGrassBend.Strength(.3f, 0), Is.EqualTo(.7f).Within(1e-5f));
            Assert.That(LocationGrassBend.Strength(.3f, 1), Is.EqualTo(1));
            Assert.That(LocationGrassBend.Strength(.3f, 5), Is.EqualTo(1), "Бег — не больше полного.");
        }

        [Test]
        public void OldData_ReactionOff_EntriesBendByDefault_PresetsInRange()
        {
            LocationScatterLayer layer = JsonUtility.FromJson<LocationScatterLayer>("{\"Id\":\"old\",\"Name\":\"Старая\",\"Assets\":[{\"AssetId\":\"g\"}]}");
            Assert.That(layer.Reaction, Is.Not.Null);
            Assert.That(layer.Reaction.Enabled, Is.False, "У старых данных эффект выключен.");
            Assert.That(layer.Assets[0].Bends, Is.True);
            Assert.That(layer.Bends("g"), Is.False);
            layer.Reaction.Enabled = true;
            layer.Assets[0].Bends = false;
            Assert.That(layer.Bends("g"), Is.False, "Ассет со снятым «гнётся» — как камень.");
            Assert.That(layer.Bends("другой"), Is.True, "Ассета нет в кисти — как у слоя.");
            foreach (LocationScatterReactionPreset preset in System.Enum.GetValues(typeof(LocationScatterReactionPreset)))
            {
                layer.Reaction.Apply(preset);
                Assert.That(layer.Reaction.RecoverTime, Is.GreaterThan(layer.Reaction.ReactTime), preset + ": возврат дольше прогиба.");
                Assert.That(layer.Reaction.Bend, Is.EqualTo(layer.Reaction.SafeBend), preset + ": в допустимых пределах.");
            }
            layer.Reaction.Bend = 7;
            layer.Reaction.RecoverTime = -1;
            Assert.That(layer.Reaction.SafeBend, Is.EqualTo(LocationScatterReaction.MaxBend));
            Assert.That(layer.Reaction.SafeRecoverTime, Is.EqualTo(LocationScatterReaction.MinRecoverTime));
        }

        // ------------------------------------------------------------------
        // Рендерер
        // ------------------------------------------------------------------

        [Test]
        public void Carpet_BendsAwayFromFeet_RootsStay_StonesStay_AndRecoversExactly()
        {
            Asset("grass");
            Asset("stone");
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            LocationScatterLayer layer = Layer(LocationScatterMode.Carpet, "grass", "stone");
            layer.Assets[1].Bends = false;
            LocationScatterInstance left = Put(layer, "grass", new Vector2(480, 500));
            LocationScatterInstance right = Put(layer, "grass", new Vector2(520, 500), flip: true, rotation: 10);
            Put(layer, "stone", new Vector2(510, 505));
            LocationScatterInstance far = Put(layer, "grass", new Vector2(900, 500));
            visual.ScatterLayers.Add(layer);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                LocationScatterCarpet carpet = renderer.ScatterCarpet(layer.Id);
                Assert.That(carpet.BendingCount, Is.EqualTo(3), "Камень не гнётся — без лишних вершин.");
                Assert.That(carpet.VertexCount - 4, Is.GreaterThanOrEqualTo(3 * (LocationScatterCarpet.BendRows + 2) * 2), "Ряды вершин — только у гнущихся.");
                Assert.That((carpet.VertexCount - 4) % 3, Is.EqualTo(0), "Камень — прежние четыре вершины.");
                LocationScatterCarpet.BendingPlant Plant(LocationScatterInstance instance) => carpet.BendingPlants.First(item => item.Key == instance.Key);
                Vector3[] leftBase = carpet.QuadPositions(Plant(left).Quads[0]);
                Vector3[] rightBase = carpet.QuadPositions(Plant(right).Quads[0]);
                Vector3[] farBase = carpet.QuadPositions(Plant(far).Quads[0]);

                // В окне базы (реакция выключена) стоящая фигура траву не трогает.
                renderer.SetActors(new[] { Actor("hero", new Vector2(500, 500)) }, 0);
                Run(renderer, .5f);
                Assert.That(carpet.QuadPositions(Plant(left).Quads[0]), Is.EqualTo(leftBase));

                renderer.VegetationReacts = true;
                Run(renderer, .5f);
                Vector3[] leftBent = carpet.QuadPositions(Plant(left).Quads[0]);
                Vector3[] rightBent = carpet.QuadPositions(Plant(right).Quads[0]);
                int top = leftBent.Length - 1;
                Assert.That(leftBent[top].x, Is.LessThan(leftBase[top].x - .005f), "Левый пучок — влево от ног.");
                Assert.That(rightBent[top].x, Is.GreaterThan(rightBase[top].x + .005f), "Правый (отражён, повёрнут) — вправо.");
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(leftBent[i], Is.EqualTo(leftBase[i]), "Низ рисунка и опора — на месте.");
                    Assert.That(rightBent[i], Is.EqualTo(rightBase[i]));
                }
                Assert.That(carpet.QuadPositions(Plant(far).Quads[0]), Is.EqualTo(farBase), "Вне радиуса — без реакции.");
                float shift = leftBase[top].x - leftBent[top].x;
                float height = Plant(left).Height;
                Assert.That(shift, Is.LessThanOrEqualTo(layer.Reaction.Bend * height + 1e-4f), "Прогиб ограничен силой.");

                // Уход: форма возвращается плавно и точно.
                renderer.SetActors(new LocationWorldRenderer.ActorFrame[0], 0);
                renderer.StepVegetation(1 / 60f);
                Vector3[] leaving = carpet.QuadPositions(Plant(left).Quads[0]);
                Assert.That(leaving[top].x, Is.LessThan(leftBase[top].x), "Без скачка в исходную позу.");
                Run(renderer, 3);
                Assert.That(carpet.QuadPositions(Plant(left).Quads[0]), Is.EqualTo(leftBase), "После ухода — исходная форма без остатка.");
                Assert.That(renderer.ActivePlantCount, Is.EqualTo(0), "Восстановившиеся выходят из активного набора.");
            }
        }

        [Test]
        public void StandingFigure_HoldsSoftBend_WithoutJitter()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            LocationScatterLayer layer = Layer(LocationScatterMode.Carpet, "grass");
            LocationScatterInstance tuft = Put(layer, "grass", new Vector2(510, 500));
            visual.ScatterLayers.Add(layer);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null) { VegetationReacts = true })
            {
                LocationScatterCarpet carpet = renderer.ScatterCarpet(layer.Id);
                Vector2Int quad = carpet.BendingPlants.First().Quads[0];
                // Идёт мимо, останавливается рядом.
                for (int i = 0; i <= 30; i++)
                {
                    renderer.SetActors(new[] { Actor("hero", new Vector2(470 + i, 500)) }, 0);
                    renderer.StepVegetation(1 / 60f);
                }
                Run(renderer, 2);
                float settled = carpet.QuadPositions(quad).Last().x;
                renderer.StepVegetation(1 / 60f);
                Assert.That(carpet.QuadPositions(quad).Last().x, Is.EqualTo(settled).Within(1e-5f), "Стоящий — без бесконечной дрожи.");
                Assert.That(renderer.ActivePlantCount, Is.EqualTo(1), "Мягкий прогиб удерживается, пока стоит рядом.");
                // Перенос через полкарты — без полосы прогиба по пути.
                renderer.SetActors(new[] { Actor("hero", new Vector2(1500, 500)) }, 0);
                renderer.StepVegetation(1 / 60f);
                renderer.SetActors(new[] { Actor("hero", new Vector2(100, 500)) }, 0);
                renderer.StepVegetation(1 / 60f);
                Run(renderer, 3);
                Assert.That(renderer.ActivePlantCount, Is.EqualTo(0), "Скачок — не отрезок через всю карту.");
                Assert.That(tuft, Is.Not.Null);
            }
        }

        [Test]
        public void Objects_BendInShader_RootsStay_AndRecover()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            LocationScatterLayer layer = Layer(LocationScatterMode.Objects, "grass");
            layer.Band = LocationVisualBand.World;
            LocationScatterInstance tuft = Put(layer, "grass", new Vector2(520, 500));
            LocationScatterLayer plain = Layer(LocationScatterMode.Objects, "grass");
            plain.Reaction.Enabled = false;
            plain.Band = LocationVisualBand.World;
            LocationScatterInstance still = Put(plain, "grass", new Vector2(480, 500));
            visual.ScatterLayers.Add(layer);
            visual.ScatterLayers.Add(plain);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null) { VegetationReacts = true })
            {
                SpriteRenderer image = renderer.ScatterImages(layer.Id, tuft.Key)[0];
                SpriteRenderer stillImage = renderer.ScatterImages(plain.Id, still.Key)[0];
                Assert.That(image.sharedMaterial.shader.name, Is.EqualTo("Kingdom Survival/Location Sprite Adjust"), "Гнущийся — материалом с прогибом.");
                Assert.That(stillImage.sharedMaterial.shader.name, Is.Not.EqualTo("Kingdom Survival/Location Sprite Adjust"), "Слой без реакции — как раньше.");
                Vector3 anchor = image.transform.parent.position;
                renderer.SetActors(new[] { Actor("hero", new Vector2(500, 500)) }, 0);
                Run(renderer, .5f);
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                image.GetPropertyBlock(block);
                Vector4 bend = block.GetVector("_KsBend"), shape = block.GetVector("_KsBendShape");
                Assert.That(bend.x, Is.GreaterThan(.005f), "Верх — вправо, от ног слева.");
                Assert.That(shape.y, Is.GreaterThan(0), "Форма прогиба задана.");
                Assert.That(shape.z, Is.EqualTo(layer.Reaction.SafePinned).Within(1e-5f));
                // Опора в пространстве рисунка: вес прогиба там нулевой.
                float rootT = (image.transform.InverseTransformPoint(anchor).y - shape.x) * shape.y;
                Assert.That(LocationGrassBend.Weight(rootT, shape.z), Is.EqualTo(0).Within(1e-5f), "Корни на месте.");
                Assert.That(image.transform.parent.position, Is.EqualTo(anchor), "Опора и сортировка не двигаются.");
                Assert.That(image.localBounds.size.y, Is.GreaterThan(image.sprite.bounds.size.y), "Запас границ под прогиб.");
                renderer.SetActors(new LocationWorldRenderer.ActorFrame[0], 0);
                Run(renderer, 4);
                image.GetPropertyBlock(block);
                Assert.That((Vector2)block.GetVector("_KsBend"), Is.EqualTo(Vector2.zero), "Исходная форма.");
                Assert.That(block.GetVector("_KsHsv").z, Is.EqualTo(1).Within(1e-5f), "Цвет экземпляра в блоке не потерян.");
            }
        }

        [Test]
        public void BattleFigures_AreSources_CorpsesAreNot()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            LocationScatterLayer layer = Layer(LocationScatterMode.Carpet, "grass");
            Put(layer, "grass", new Vector2(510, 500));
            visual.ScatterLayers.Add(layer);
            Texture2D texture = new Texture2D(8, 8);
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.zero);
            owned.Add(sprite);
            LocationWorldRenderer.BattleFigureFrame Figure(bool corpse) => new LocationWorldRenderer.BattleFigureFrame
            {
                Id = "enemy", Sprite = sprite, Rect = new Rect(480, 440, 40, 60), Ground = new Vector2(500, 500), Tint = Color.white, Corpse = corpse
            };
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null) { VegetationReacts = true })
            {
                renderer.SetBattleFigures(new[] { Figure(true) });
                Run(renderer, .3f);
                Assert.That(renderer.ActivePlantCount, Is.EqualTo(0), "Павший не гнёт траву.");
                renderer.SetBattleFigures(new[] { Figure(false) });
                Run(renderer, .3f);
                Assert.That(renderer.ActivePlantCount, Is.EqualTo(1), "Живая фигура боя — источник.");
            }
        }

        [Test]
        public void HiddenAndDisabledLayers_DoNotBend_RebuildKeepsLayout()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            LocationScatterLayer layer = Layer(LocationScatterMode.Carpet, "grass");
            Put(layer, "grass", new Vector2(510, 500));
            visual.ScatterLayers.Add(layer);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null) { VegetationReacts = true })
            {
                Assert.That(renderer.BendingPlantCount, Is.EqualTo(1));
                layer.Reaction.Enabled = false;
                renderer.RebuildScatterLayer(layer);
                Assert.That(renderer.BendingPlantCount, Is.EqualTo(0));
                Assert.That(renderer.ScatterCarpet(layer.Id).VertexCount, Is.EqualTo(4), "Выключено — прежние четыре вершины.");
                layer.Reaction.Enabled = true;
                layer.Hidden = true;
                renderer.RebuildScatterLayer(layer);
                Assert.That(renderer.BendingPlantCount, Is.EqualTo(0));
                layer.Hidden = false;
                renderer.RebuildScatterLayer(layer);
                Assert.That(renderer.BendingPlantCount, Is.EqualTo(1));
                visual.ScatterLayers.Remove(layer);
            }
        }

        // ------------------------------------------------------------------
        // Расширенные настройки
        // ------------------------------------------------------------------

        [Test]
        public void ZoneAndShape_Settings_ChangeLawAsDescribed()
        {
            Assert.That(LocationGrassBend.Falloff(3, 10, core: .4f), Is.EqualTo(1), "Внутри ядра — полная сила.");
            Assert.That(LocationGrassBend.Falloff(5, 10, softness: 4), Is.GreaterThan(LocationGrassBend.Falloff(5, 10)), "Мягкость края держит силу дольше.");
            Assert.That(LocationGrassBend.Falloff(9.99f, 10, .5f, 4), Is.LessThan(.01f), "У края — к нулю при любой форме.");
            Assert.That(LocationGrassBend.Weight(.6f, 0, 1), Is.EqualTo(.6f).Within(1e-5f), "Изгиб 1 — прямой наклон.");
            Assert.That(LocationGrassBend.Weight(.6f, 0, 3), Is.LessThan(LocationGrassBend.Weight(.6f, 0, 2)), "Больше — гнётся верхушка.");

            LocationGrassBend.Zone flat = LocationGrassBend.Zone.Default;
            flat.DepthStretch = .3f;
            Vector2 below = new Vector2(0, 6);
            Assert.That(LocationGrassBend.Push(below, Vector2.zero, Vector2.zero, 10, 1, Vector2.zero, Vector2.zero, flat), Is.EqualTo(Vector2.zero),
                "Плоская зона не задевает спереди.");
            LocationGrassBend.Zone lead = LocationGrassBend.Zone.Default;
            lead.Lead = .5f;
            Vector2 ahead = new Vector2(12, 0);
            Assert.That(LocationGrassBend.Push(ahead, Vector2.zero, Vector2.zero, 10, 1, Vector2.right, Vector2.zero, lead).magnitude, Is.GreaterThan(0),
                "Опережение задевает впереди по ходу.");
            LocationGrassBend.Zone forward = LocationGrassBend.Zone.Default;
            forward.Forward = 1;
            Vector2 side = LocationGrassBend.Push(new Vector2(0, 5), Vector2.zero, Vector2.zero, 10, 1, Vector2.right, Vector2.zero, forward);
            Assert.That(side.normalized.x, Is.GreaterThan(.99f), "«По ходу» — клонится по направлению шага.");
            Assert.That(LocationGrassBend.Limit(new Vector2(2, 0), .5f).x, Is.EqualTo(.5f).Within(1e-5f));

            LocationGrassBend.TopOffset(new Vector2(0, 1), 1, .2f, 0, .75f, out Vector2 lean, out _, depthLean: 0);
            Assert.That(lean, Is.EqualTo(Vector2.zero), "Наклон вглубь 0 — только вбок.");
            LocationGrassBend.TopOffset(Vector2.right, 1, .2f, 0, .75f, out _, out float noDrop, arcDrop: 0);
            Assert.That(noDrop, Is.EqualTo(0), "Без опускания дуги.");
        }

        [Test]
        public void Spring_Overshoots_ThenSettles_ZeroIsSmooth()
        {
            Vector2 Run(float amount, out float minimum)
            {
                Vector2 bend = Vector2.right, velocity = Vector2.zero;
                minimum = 1;
                for (int i = 0; i < 300; i++)
                {
                    LocationGrassBend.SpringStep(ref bend, ref velocity, Vector2.zero, 1 / 60f, 2.5f, amount);
                    minimum = Mathf.Min(minimum, bend.x);
                }
                return bend;
            }
            Vector2 springy = Run(.6f, out float springyMin);
            Assert.That(springyMin, Is.LessThan(-.05f), "Покачивание проскакивает исходное положение.");
            Assert.That(springy.magnitude, Is.LessThan(.01f), "И затухает.");
            Run(0, out float smoothMin);
            Assert.That(smoothMin, Is.GreaterThan(-.01f), "Без покачивания — без перехлёста.");
        }

        [Test]
        public void Enemies_Bend_SpringOvershoots_StrengthScales()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            LocationScatterLayer layer = Layer(LocationScatterMode.Carpet, "grass");
            Put(layer, "grass", new Vector2(510, 500));
            visual.ScatterLayers.Add(layer);
            float Bent(float bend, float spring, out bool overshoot, out bool settled)
            {
                layer.Reaction.Bend = bend;
                layer.Reaction.Spring = spring;
                using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null) { VegetationReacts = true })
                {
                    LocationScatterCarpet carpet = renderer.ScatterCarpet(layer.Id);
                    Vector2Int quad = carpet.BendingPlants.First().Quads[0];
                    float Top() => carpet.QuadPositions(quad).Last().x;
                    float rest = Top();
                    renderer.SetActors(new[] { new LocationWorldRenderer.ActorFrame { Id = "wolf", Pixel = new Vector2(500, 500), Kind = LocationWorldRenderer.ActorKind.Enemy } }, 0);
                    Run(renderer, 1);
                    float shift = Top() - rest;
                    renderer.SetActors(new LocationWorldRenderer.ActorFrame[0], 0);
                    overshoot = false;
                    for (int i = 0; i < 240; i++)
                    {
                        renderer.StepVegetation(1 / 60f);
                        overshoot |= Top() < rest - .002f;
                    }
                    settled = Top() == rest;
                    return shift;
                }
            }
            float soft = Bent(.2f, 0, out bool smoothOvershoot, out bool smoothSettled);
            Assert.That(soft, Is.GreaterThan(.005f), "Противник гнёт траву.");
            Assert.That(smoothOvershoot, Is.False, "Без покачивания — без перехлёста.");
            Assert.That(smoothSettled, Is.True, "Возвращается точно.");
            float strong = Bent(.4f, .8f, out bool springOvershoot, out bool springSettled);
            Assert.That(strong, Is.EqualTo(soft * 2).Within(.002f), "Сила прогиба вдвое — смещение вдвое.");
            Assert.That(springOvershoot, Is.True, "Покачивание проскакивает исходное положение.");
            Assert.That(springSettled, Is.True, "И затухает до исходной формы.");
        }


        // Основание кистью у экземпляра раскидки «не мешает» сохраняется: и
        // окклюзия, и перекрытие света — по клеткам, а не по прямоугольнику.
        [Test]
        public void ScatterInstance_KeepsPaintedFootprint_LightBlockedPerCell()
        {
            Asset("bush");
            ArtAssetDefinition asset = catalog.Find("bush");
            asset.OccludesLight = true;
            ArtAssetFootprintMask mask = asset.Settings(ArtAssetView.Front).FootprintMask;
            mask.Reset(new Rect(-.5f, -.2f, 1, .4f), .1f);
            mask.Fill(point => Mathf.Abs(point.x) < .1f || Mathf.Abs(point.y) < .05f);
            catalog.MarkChanged();
            LocationScatterLayer layer = Layer(LocationScatterMode.Objects, "bush");
            layer.Reaction.Enabled = false;
            LocationScatterInstance instance = Put(layer, "bush", new Vector2(500, 500));
            LocationVisualObject item = layer.ToObject(instance);
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(item);
            Assert.That(resolved.BlocksMovement, Is.False, "Слой «не мешает».");
            Assert.That(resolved.FootprintCells, Is.Not.Null.And.Not.Empty, "Форма основания ассета — кистью, а не прямоугольник по умолчанию.");

            asset.OccludesLight = true;
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_bend", UseWorldLighting = false };
            visual.Objects.Add(new LocationVisualObject { Id = "bush", Name = "Куст", AssetId = "bush", Position = new Vector2(.3f, .5f) });
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
            {
                Transform anchor = renderer.FindObject("bush").transform.parent;
                List<Transform> blockers = anchor.Cast<Transform>().Where(child => child.name == "Перекрытие света").ToList();
                Assert.That(blockers.Count, Is.EqualTo(mask.Rects().Count), "По прямоугольнику на полосу клеток маски.");
                float area = blockers.Sum(child => child.localScale.x * child.localScale.y);
                Assert.That(area, Is.EqualTo(mask.Count * .01f).Within(1e-3f), "Свет закрыт ровно нарисованной опорой, не габаритом.");
            }
        }

        // Трава на высоком холсте PNG (сверху пусто): высота растения — по
        // непрозрачной части, а не по холсту, иначе вся видимая трава попадает
        // в закреплённую нижнюю часть и не гнётся.
        [Test]
        public void TallTransparentCanvas_BendsByOpaqueGrass()
        {
            Texture2D texture = new Texture2D(16, 128, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[16 * 128];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 16; x++)
                    pixels[y * 16 + x] = y < 32 ? Color.green : Color.clear;
            texture.SetPixels(pixels);
            texture.Apply();
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 128), new Vector2(.5f, 0), 100);
            owned.Add(sprite);
            Assert.That(LocationAlphaContours.OpaqueTop(sprite), Is.EqualTo(.25f).Within(.04f), "Трава — нижняя четверть холста.");
            ArtAssetDefinition asset = new ArtAssetDefinition { Id = "tall", Name = "tall", PixelsPerUnit = 40 };
            asset.MainPart.View(ArtAssetView.Front).Sprite = sprite;
            asset.Settings(ArtAssetView.Front).Pivot = new Vector2(.5f, 0);
            catalog.assets.Add(asset);
            catalog.MarkChanged();
            foreach (LocationScatterMode mode in new[] { LocationScatterMode.Carpet, LocationScatterMode.Objects })
            {
                LocalLocationDefinition location = Location();
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
                LocationScatterLayer layer = Layer(mode, "tall");
                layer.Band = LocationVisualBand.World;
                LocationScatterInstance tuft = Put(layer, "tall", new Vector2(520, 500));
                visual.ScatterLayers.Add(layer);
                using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null) { VegetationReacts = true })
                {
                    renderer.SetActors(new[] { Actor("hero", new Vector2(500, 500)) }, 0);
                    Run(renderer, .5f);
                    if (mode == LocationScatterMode.Carpet)
                    {
                        LocationScatterCarpet carpet = renderer.ScatterCarpet(layer.Id);
                        LocationScatterCarpet.BendingPlant plant = carpet.BendingPlants.First();
                        Assert.That(plant.Height, Is.EqualTo(3.2f * .25f).Within(.15f), "Высота растения — по траве, не по холсту.");
                        Vector3[] positions = carpet.QuadPositions(plant.Quads[0]);
                        // Ряд на верхушке травы (последний ряд — край пустого холста).
                        Assert.That(positions[positions.Length - 3].x - positions[1].x, Is.GreaterThan(.05f), "Верхушка травы гнётся (правый край верха травы против правого края опоры).");
                    }
                    else
                    {
                        SpriteRenderer image = renderer.ScatterImages(layer.Id, tuft.Key)[0];
                        MaterialPropertyBlock block = new MaterialPropertyBlock();
                        image.GetPropertyBlock(block);
                        Vector4 shape = block.GetVector("_KsBendShape");
                        float grassTop = (image.sprite.bounds.min.y + image.sprite.bounds.size.y * .25f - shape.x) * shape.y;
                        Assert.That(grassTop, Is.EqualTo(1).Within(.15f), "Верх травы — верх растения для шейдера.");
                    }
                }
            }
        }

        [Test]
        public void HexGrid_BuildsWithoutWalls_AndToggles()
        {
            LocalLocationDefinition location = Location();
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, new LocationVisualDefinition { LocationId = location.Id }, null))
            {
                Assert.That(renderer.HexGridVisible, Is.False, "По умолчанию сетки нет.");
                renderer.HexGridVisible = true;
                MeshFilter grid = renderer.Root.GetComponentsInChildren<MeshFilter>(true).First(item => item.name == "Сетка гексов");
                Assert.That(grid.sharedMesh.vertexCount, Is.GreaterThan(100));
                Assert.That(grid.GetComponent<MeshRenderer>().sortingOrder, Is.LessThan(LocationWorldRenderer.UpperSortingFrom), "Под предметами и людьми.");
                renderer.HexGridVisible = false;
                Assert.That(renderer.HexGridVisible, Is.False);
                Assert.That(grid.gameObject.activeSelf, Is.False);
            }
        }

        // Замер цены подсистемы: CPU шага и отсутствие мусора после прогрева.
        // Отрисовки ковра (группы) и вершины — для отчёта.
        [Test]
        public void DenseCarpet_StepCost_AndNoGarbageAfterWarmup()
        {
            Asset("grass");
            foreach (int count in new[] { 1000, 3000, 5000 })
                foreach (int walkers in new[] { 1, 5 })
                {
                    LocalLocationDefinition location = Location();
                    LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
                    LocationScatterLayer layer = Layer(LocationScatterMode.Carpet, "grass");
                    layer.Reaction.Radius = .45f;
                    System.Random random = new System.Random(3);
                    for (int i = 0; i < count; i++)
                        Put(layer, "grass", new Vector2((float)random.NextDouble() * 1000 + 400, (float)random.NextDouble() * 500 + 250));
                    visual.ScatterLayers.Add(layer);
                    using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null) { VegetationReacts = true })
                    {
                        LocationWorldRenderer.ActorFrame[] frames = new LocationWorldRenderer.ActorFrame[walkers];
                        void Place(int frame)
                        {
                            for (int w = 0; w < walkers; w++)
                                frames[w] = Actor("walker" + w, new Vector2(450 + (frame * 2 + w * 37) % 900, 300 + w * 80));
                            renderer.SetActors(frames, 0);
                        }
                        for (int frame = 0; frame < 120; frame++) { Place(frame); renderer.StepVegetation(1 / 60f); }
                        double total = 0, worst = 0;
                        const int measured = 300;
                        for (int frame = 0; frame < measured; frame++)
                        {
                            Place(120 + frame);
                            renderer.StepVegetation(1 / 60f);
                            total += renderer.LastVegetationMilliseconds;
                            worst = System.Math.Max(worst, renderer.LastVegetationMilliseconds);
                        }
                        Assert.That(() => renderer.StepVegetation(1 / 60f), UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not),
                            "Шаг реакции после прогрева не создаёт мусора.");
                        LocationScatterCarpet carpet = renderer.ScatterCarpet(layer.Id);
                        UnityEngine.Debug.Log("Реакция травы: " + count + " шт., фигур " + walkers + " — шаг в среднем " + (total / measured).ToString("0.000") +
                                                       " мс, худший " + worst.ToString("0.000") + " мс, активных " + renderer.ActivePlantCount +
                                                       ", групп ковра (отрисовок) " + carpet.GroupCount + ", вершин " + carpet.VertexCount);
                        Assert.That(total / measured, Is.LessThan(2), "Шаг реакции — доли миллисекунды, не полный перебор.");
                    }
                }
        }
    }
}
