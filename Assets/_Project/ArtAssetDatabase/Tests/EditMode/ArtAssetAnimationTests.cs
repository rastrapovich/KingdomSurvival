using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Tests
{
    // ПР-12П: покадровая анимация ассетов — порядок кадров по кругу и
    // туда-обратно, устойчивая фаза экземпляра, разрешение кадров частей
    // (состояние объекта анимацию основы снимает) и смена кадра в общем
    // рендерере мест по времени места.
    public sealed class ArtAssetAnimationTests
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

        private Sprite Frame(Color color)
        {
            Texture2D texture = new Texture2D(32, 48, TextureFormat.RGBA32, false);
            Color[] colors = new Color[32 * 48];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            texture.SetPixels(colors);
            texture.Apply();
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 32, 48), new Vector2(.5f, 0), 100);
            owned.Add(sprite);
            return sprite;
        }

        private ArtAssetDefinition Grass(int frames, ArtAssetPlayback playback = ArtAssetPlayback.Loop, bool randomPhase = false)
        {
            ArtAssetDefinition asset = new ArtAssetDefinition
            {
                Id = "zz_grass", Name = "Трава", PixelsPerUnit = 48, FramesPerSecond = 4, Playback = playback, RandomPhase = randomPhase
            };
            ArtAssetPartView slot = asset.MainPart.View(ArtAssetView.Front);
            slot.Sprite = Frame(new Color(.1f, .5f, .1f));
            for (int i = 1; i < frames; i++) slot.Frames.Add(new ArtAssetFrame { Sprite = Frame(new Color(.1f, .5f + i * .1f, .1f)) });
            catalog.assets.Add(asset);
            catalog.MarkChanged();
            return asset;
        }

        private static int[] Sequence(int count, ArtAssetPlayback playback, int steps, float phase = 0)
        {
            int[] result = new int[steps];
            for (int i = 0; i < steps; i++) result[i] = ArtAssetAnimation.FrameIndex(count, 1, playback, i + .5, phase);
            return result;
        }

        [Test]
        public void Loop_GoesRound_PingPong_ReturnsWithoutRepeatingEnds()
        {
            Assert.That(Sequence(3, ArtAssetPlayback.Loop, 7), Is.EqualTo(new[] { 0, 1, 2, 0, 1, 2, 0 }));
            Assert.That(Sequence(4, ArtAssetPlayback.PingPong, 9), Is.EqualTo(new[] { 0, 1, 2, 3, 2, 1, 0, 1, 2 }),
                "Туда-обратно: крайние кадры не повторяются — нет «залипания» на конце.");
            Assert.That(Sequence(2, ArtAssetPlayback.PingPong, 4), Is.EqualTo(new[] { 0, 1, 0, 1 }));
            Assert.That(Sequence(1, ArtAssetPlayback.Loop, 3), Is.EqualTo(new[] { 0, 0, 0 }), "Один кадр — неподвижный рисунок.");
            Assert.That(ArtAssetAnimation.FrameIndex(4, 0, ArtAssetPlayback.Loop, 10, 0), Is.EqualTo(0), "Скорость 0 — первый кадр.");
            Assert.That(ArtAssetAnimation.FrameIndex(4, 1, ArtAssetPlayback.Loop, -2.5, 0), Is.InRange(0, 3), "Отрицательное время не ломает индекс.");
        }

        [Test]
        public void Phase_ShiftsTheCycle_AndIsStablePerInstanceId()
        {
            Assert.That(Sequence(4, ArtAssetPlayback.Loop, 4, .5f), Is.EqualTo(new[] { 2, 3, 0, 1 }), "Фаза — доля полного цикла.");
            float a = ArtAssetAnimation.StablePhase("bush_1"), b = ArtAssetAnimation.StablePhase("bush_2");
            Assert.That(a, Is.EqualTo(ArtAssetAnimation.StablePhase("bush_1")), "Один ID — одна фаза при каждом запуске.");
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a, Is.InRange(0f, 1f));
            Assert.That(ArtAssetAnimation.StablePhase(null), Is.EqualTo(0));
        }

        [Test]
        public void Resolver_GivesFramesOfAnimatedPart_VariantStopsMainAnimation()
        {
            ArtAssetDefinition asset = Grass(3, randomPhase: true);
            Assert.That(asset.IsAnimated, Is.True);
            Assert.That(asset.FrameCountIn(ArtAssetView.Front), Is.EqualTo(3));
            Assert.That(asset.MainPart.View(ArtAssetView.Front).FrameSprites().Length, Is.EqualTo(3));

            LocationVisualObject item = new LocationVisualObject { Id = "tuft_7", AssetId = asset.Id, View = ArtAssetView.Front };
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(item, null, catalog);
            Assert.That(resolved.Main.Animated, Is.True);
            Assert.That(resolved.Main.Frames.Length, Is.EqualTo(3));
            Assert.That(resolved.Main.Frames[0], Is.SameAs(resolved.Main.Sprite), "Первый кадр — рисунок ракурса: размер и опора от него.");
            Assert.That(resolved.FramesPerSecond, Is.EqualTo(4));
            Assert.That(resolved.Phase, Is.EqualTo(ArtAssetAnimation.StablePhase("tuft_7")));

            // Запасной ракурс показывает кадры ракурса, который есть.
            item.View = ArtAssetView.Back;
            Assert.That(LocationVisualResolver.Resolve(item, null, catalog).Main.Frames.Length, Is.EqualTo(3));

            // Состояние объекта заменяет рисунок основы — анимации основы нет.
            Sprite broken = Frame(Color.gray);
            item.Variants.Add(new LocationVisualVariant { Id = "broken", Name = "Сломано", Sprite = broken });
            LocationResolvedVisual variant = LocationVisualResolver.Resolve(item, "broken", catalog);
            Assert.That(variant.Main.Sprite, Is.SameAs(broken));
            Assert.That(variant.Main.Animated, Is.False);

            asset.RandomPhase = false;
            Assert.That(LocationVisualResolver.Resolve(item, null, catalog).Phase, Is.EqualTo(0));
        }

        [Test]
        public void StillAsset_IsNotAnimated_AndKeepsItsSprite()
        {
            ArtAssetDefinition asset = Grass(1);
            Assert.That(asset.IsAnimated, Is.False);
            LocationVisualObject item = new LocationVisualObject { Id = "rock", AssetId = asset.Id };
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(item, null, catalog);
            Assert.That(resolved.Main.Frames, Is.Null);
            Assert.That(LocationVisualResolver.FrameAt(resolved, resolved.Main, 123.4), Is.SameAs(resolved.Main.Sprite));
        }

        [Test]
        public void Renderer_ChangesFrameWithLocationTime_PerInstancePhase()
        {
            ArtAssetDefinition asset = Grass(4, ArtAssetPlayback.Loop, randomPhase: true);
            Sprite[] frames = asset.MainPart.View(ArtAssetView.Front).FrameSprites();
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_grass_test", DisplayName = "Трава" };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            visual.Objects.Add(new LocationVisualObject { Id = "a", Name = "Куст А", AssetId = asset.Id, Position = new Vector2(.4f, .6f) });
            visual.Objects.Add(new LocationVisualObject { Id = "b", Name = "Куст Б", AssetId = asset.Id, Position = new Vector2(.6f, .6f) });
            LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null);
            try
            {
                LocationResolvedVisual a = renderer.ResolvedObject("a"), b = renderer.ResolvedObject("b");
                for (int step = 0; step < 8; step++)
                {
                    float seconds = 10 + step * .25f;
                    renderer.SetTime(13, seconds);
                    Assert.That(renderer.ObjectSprite("a"), Is.SameAs(frames[ArtAssetAnimation.FrameIndex(4, 4, ArtAssetPlayback.Loop, seconds, a.Phase)]));
                    Assert.That(renderer.ObjectSprite("b"), Is.SameAs(frames[ArtAssetAnimation.FrameIndex(4, 4, ArtAssetPlayback.Loop, seconds, b.Phase)]));
                }
                HashSet<Sprite> seen = new HashSet<Sprite>();
                for (int step = 0; step < 4; step++)
                {
                    renderer.SetTime(13, 20 + step * .25f);
                    seen.Add(renderer.ObjectSprite("a"));
                }
                Assert.That(seen.Count, Is.EqualTo(4), "За один цикл показаны все кадры.");
            }
            finally
            {
                renderer.Dispose();
            }
        }
    }
}
