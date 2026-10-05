using System.Collections;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomSurvival.LocationRendering.Tests
{
    // ПР-12Н: экземпляры Базы ассетов в общем рендерере мест: десять
    // экземпляров — одна запись; правка каталога видна всем; смена шести
    // ракурсов не сдвигает опору; крыша сортируется с героем; свет с разных
    // сторон меняет поверхность по карте нормалей ракурса (и у отражённого).
    public sealed class ArtAssetPlacementPlayModeTests
    {
        private const int Width = 960, Height = 540;
        private readonly List<Object> owned = new List<Object>();
        private ArtAssetDatabaseAsset catalog;
        private Texture2D lastNormal;

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
            foreach (Object item in owned) if (item != null) Object.Destroy(item);
            owned.Clear();
        }

        private static LocalLocationDefinition Location() => new LocalLocationDefinition { Id = "zz_asset_test", DisplayName = "Ассеты" };

        private Texture2D Texture(int width, int height, System.Func<float, float, Color> pixel, bool linear = false)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            Color[] colors = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    colors[y * width + x] = pixel((x + .5f) / width, (y + .5f) / height);
            texture.SetPixels(colors);
            texture.Apply();
            owned.Add(texture);
            return texture;
        }

        // Рисунок с картой нормалей «полусфера» (как в тесте нормалей места).
        private Sprite Lit(int width, int height, Color color)
        {
            Texture2D albedo = Texture(width, height, (x, y) => color);
            Texture2D normal = Texture(width, height, (x, y) =>
            {
                float nx = x * 2 - 1, ny = y * 2 - 1;
                Vector3 n = new Vector3(nx, ny, Mathf.Sqrt(Mathf.Max(.05f, 1 - nx * nx - ny * ny))).normalized;
                return new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
            }, true);
            lastNormal = normal;
            Sprite sprite = Sprite.Create(albedo, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, Vector4.zero, false,
                new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } });
            owned.Add(sprite);
            return sprite;
        }

        private ArtAssetDefinition House()
        {
            ArtAssetDefinition house = new ArtAssetDefinition { Id = "zz_house", Name = "Дом", PixelsPerUnit = 64, BlocksMovement = true };
            ArtAssetPart roof = new ArtAssetPart { Name = "Крыша", OrderOffset = 1 };
            house.Parts.Add(roof);
            for (int i = 0; i < 6; i++)
            {
                ArtAssetView view = (ArtAssetView)i;
                // Ширина и поля разные у ракурсов: опора всё равно на месте.
                house.MainPart.View(view).Sprite = Lit(96 + i * 8, 128, new Color(.7f, .7f, .7f));
                house.MainPart.View(view).NormalMap = lastNormal;
                roof.View(view).Sprite = Lit(96 + i * 8, 48, new Color(.5f, .4f, .3f));
                roof.View(view).Offset = new Vector2(0, 80 / 64f);
                house.Settings(view).Pivot = new Vector2(.4f + i * .03f, .05f + i * .01f);
                house.Settings(view).FootprintSize = new Vector2(1.6f, .6f);
            }
            catalog.assets.Add(house);
            catalog.MarkChanged();
            return house;
        }

        private static RenderTexture Target(LocationWorldRenderer renderer)
        {
            RenderTexture target = new RenderTexture(Width, Height, 24);
            target.Create();
            renderer.Camera.targetTexture = target;
            renderer.Camera.aspect = Width / (float)Height;
            renderer.ShowWhole();
            return target;
        }

        private static float Brightness(RenderTexture target, Vector2 canvasPixel)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            int x = Mathf.RoundToInt(canvasPixel.x / 1920f * Width), y = Mathf.RoundToInt((1 - canvasPixel.y / 1080f) * Height);
            float sum = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    sum += texture.GetPixel(x + dx, y + dy).grayscale;
            Object.Destroy(texture);
            return sum / 25;
        }

        private static void Save(RenderTexture target, string name)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/LocationLighting"));
            Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            Object.Destroy(texture); RenderTexture.active = previous;
        }

        [UnityTest]
        public IEnumerator TenInstancesShareOneRecord_AndCatalogChangesReachThemAll()
        {
            ArtAssetDefinition house = House();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_asset_test", UseWorldLighting = false };
            for (int i = 0; i < 10; i++)
                visual.Objects.Add(new LocationVisualObject { Id = "house_" + i, Name = "Дом " + i, AssetId = house.Id, View = (ArtAssetView)(i % 6),
                    Position = new Vector2(.08f + i * .09f, .5f), Scale = 1 + i * .05f });
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
            {
                for (int i = 0; i < 10; i++)
                {
                    LocationResolvedVisual resolved = renderer.ResolvedObject("house_" + i);
                    Assert.That(resolved.Asset, Is.SameAs(house));
                    Assert.That(renderer.ObjectImages("house_" + i).Count, Is.EqualTo(2), "Основа и крыша — один экземпляр.");
                }
            }
            yield return null;

            // Замена рисунка в каталоге: экземпляры получают его без повторной расстановки.
            Sprite replacement = Lit(120, 140, Color.white);
            house.MainPart.View(ArtAssetView.Front).Sprite = replacement;
            catalog.MarkChanged();
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
            {
                for (int i = 0; i < 10; i++)
                {
                    LocationVisualObject item = visual.Objects[i];
                    Assert.That(item.Position, Is.EqualTo(new Vector2(.08f + i * .09f, .5f)));
                    Assert.That(item.View, Is.EqualTo((ArtAssetView)(i % 6)));
                    if (item.View == ArtAssetView.Front)
                        Assert.That(renderer.ObjectImages(item.Id)[0].sprite, Is.SameAs(replacement));
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SixViewsKeepTheWorldAnchor_AndRoofSortsWithTheHero()
        {
            ArtAssetDefinition house = House();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_asset_test", UseWorldLighting = false };
            visual.Objects.Add(new LocationVisualObject { Id = "house", Name = "Дом", AssetId = house.Id, Position = new Vector2(.5f, .5f), Scale = 1.3f });
            LocalLocationDefinition location = Location();
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                Vector2 anchor = LocationVisualGeometry.ToWorld(location, new Vector2(.5f, .5f));
                foreach (bool flip in new[] { false, true })
                {
                    visual.Objects[0].FlipX = flip;
                    foreach (ArtAssetView view in ArtAssetLabels.Views)
                    {
                        Assert.That(renderer.SetObjectView("house", view), Is.True);
                        SpriteRenderer image = renderer.ObjectImages("house")[0];
                        ArtAssetViewSettings settings = house.Settings(view);
                        Bounds bounds = image.bounds;
                        float pivotX = flip ? 1 - settings.Pivot.x : settings.Pivot.x;
                        Vector2 pivot = new Vector2(bounds.min.x + bounds.size.x * pivotX, bounds.min.y + bounds.size.y * settings.Pivot.y);
                        Assert.That(Vector2.Distance(pivot, anchor), Is.LessThan(.01f), "Опора на месте: " + view + (flip ? " (отражён)" : ""));
                        Assert.That(image.sprite, Is.SameAs(house.MainPart.FindView(view).Sprite), "Рисунок своего ракурса.");
                        Assert.That(renderer.ObjectImages("house")[1].sprite, Is.SameAs(house.Parts[1].FindView(view).Sprite), "Крыша меняет ракурс вместе с домом.");
                        Assert.That(image.bounds.size.y, Is.EqualTo(128 / 64f * 1.3f).Within(.01f), "Масштаб одинаков у всех ракурсов.");
                    }
                }
                visual.Objects[0].FlipX = false;
                Assert.That(visual.Objects[0].View, Is.EqualTo(ArtAssetView.Front), "Смена ракурса в показе не меняет авторские данные.");

                // Герой перед домом перекрывает его, за домом — скрыт и основой, и крышей.
                Vector2 pixel = LocationVisualGeometry.ToPixel(location, new Vector2(.5f, .5f));
                renderer.SetActors(new[]
                {
                    new LocationWorldRenderer.ActorFrame { Id = "front", Pixel = pixel + new Vector2(0, 40) },
                    new LocationWorldRenderer.ActorFrame { Id = "behind", Pixel = pixel - new Vector2(0, 40) }
                }, 0);
                int frontOrder = renderer.Root.transform.Find("Фигуры/front/Фигура").GetComponent<SpriteRenderer>().sortingOrder;
                int behindOrder = renderer.Root.transform.Find("Фигуры/behind/Фигура").GetComponent<SpriteRenderer>().sortingOrder;
                foreach (SpriteRenderer part in renderer.ObjectImages("house"))
                {
                    Assert.That(frontOrder, Is.GreaterThan(part.sortingOrder), "Перед домом герой виден.");
                    Assert.That(behindOrder, Is.LessThan(part.sortingOrder), "За домом герой скрыт и стеной, и крышей.");
                }

                // Основание — только своя область.
                LocalLocationGeometry geometry = new LocalLocationGeometry(location, null, LocationVisualGeometry.BlockedAreas(visual, location));
                Assert.That(geometry.IsPassable(pixel.x, pixel.y), Is.False);
                Assert.That(geometry.IsPassable(pixel.x, pixel.y + 70), Is.True);
            }
            yield return null;
        }

        // Свет слева и справа: сторона к огню заметно светлее — по нормалям
        // ракурса; после смены ракурса и у отражённого экземпляра тоже.
        [UnityTest]
        public IEnumerator LightFromEitherSideFollowsTheViewNormals()
        {
            ArtAssetDefinition ball = new ArtAssetDefinition { Id = "zz_ball", Name = "Шар", PixelsPerUnit = 16 };
            foreach (ArtAssetView view in ArtAssetLabels.Views)
            {
                ball.MainPart.View(view).Sprite = Lit(64, 64, new Color(.8f, .8f, .8f));
                ball.MainPart.ProjectsShadow = false;
                ball.Settings(view).Pivot = new Vector2(.5f, .5f);
            }
            catalog.assets.Add(ball);
            catalog.MarkChanged();
            float[] differences = new float[4];
            int run = 0;
            foreach ((float lightX, ArtAssetView view, bool flip) in new[]
                     { (.3f, ArtAssetView.Front, false), (.7f, ArtAssetView.Front, false), (.3f, ArtAssetView.BackLeft, false), (.3f, ArtAssetView.Back, true) })
            {
                LocationVisualDefinition visual = new LocationVisualDefinition
                {
                    LocationId = "zz_asset_test", UseWorldLighting = false, Lighting = LocationLightingMode.Indoor, IndoorIntensity = .05f
                };
                visual.Objects.Add(new LocationVisualObject { Id = "ball", Name = "Шар", AssetId = ball.Id, View = view, FlipX = flip, Position = new Vector2(.5f, .5f) });
                LocationVisualObject fire = new LocationVisualObject { Id = "fire", Name = "Огонь", LightOnly = true, Position = new Vector2(lightX, .5f) };
                fire.Light.Enabled = true; fire.Light.Radius = 12; fire.Light.Intensity = 1.5f; fire.Light.Softness = 0;
                fire.Light.Animation = LocationLightAnimation.None; fire.Light.Offset = Vector2.zero; fire.Light.Falloff = 0;
                fire.Light.NormalMaps = true; fire.Light.NormalMapsAccurate = true; fire.Light.NormalMapDistance = 1;
                fire.Light.ProjectsShadows = false; fire.Light.Shadows = false;
                visual.Objects.Add(fire);
                using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
                {
                    RenderTexture target = Target(renderer);
                    renderer.SetTime(1, 0);
                    yield return null; yield return null;
                    Save(target, "asset-normals-" + run);
                    differences[run] = Brightness(target, new Vector2(960 - 120, 540)) - Brightness(target, new Vector2(960 + 120, 540));
                    renderer.Camera.targetTexture = null;
                    Object.Destroy(target);
                }
                run++;
            }
            string all = string.Join(", ", differences);
            Assert.That(differences[0], Is.GreaterThan(.05f), "Огонь слева — левая сторона светлее: " + all);
            Assert.That(differences[1], Is.LessThan(-.05f), "Огонь справа — правая сторона светлее: " + all);
            Assert.That(differences[2], Is.GreaterThan(.05f), "После смены ракурса нормали этого ракурса: " + all);
            Assert.That(differences[3], Is.GreaterThan(.05f), "Отражённый экземпляр освещён со стороны огня: " + all);
        }
    }
}
