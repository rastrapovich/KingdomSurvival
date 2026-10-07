using System.Collections;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomSurvival.LocationRendering.Tests
{
    // ПР-12О: место из участков на общем рендерере — швы в кадре, отряд
    // через границы участков без пересборки, камера в пределах карты,
    // высота под ногами без двойного смещения фигуры.
    public sealed class LocationGroundPlayModeTests
    {
        private const int TileW = 640, TileH = 360, Columns = 3, Rows = 2;
        private readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in owned) if (item != null) Object.Destroy(item);
            owned.Clear();
        }

        // Высота (BU) в точке виртуальной карты — непрерывный наклон через швы.
        private static double Surface(double x, double y) => x * .001 + y * .002;

        private LocationVisualDefinition BuildVisual(LocalLocationDefinition location, bool heights = true)
        {
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, TechnicalTest = true };
            visual.Ground = new LocationGroundDefinition
            {
                Version = 1, Mode = LocationGroundMode.Tiles, MapId = "PlayMode", Columns = Columns, Rows = Rows, TileWidth = TileW, TileHeight = TileH,
                HeightEnabled = heights, HeightMin = -1, HeightMax = 5, MetersPerBlenderUnit = 1, SeamlessMeters = 10
            };
            visual.Camera = LocationCameraSettings.ForLargeMap();
            for (int ty = 0; ty < Rows; ty++)
            {
                for (int tx = 0; tx < Columns; tx++)
                {
                    // Горизонтальный градиент яркости по всей карте — непрерывен через швы.
                    Texture2D texture = new Texture2D(TileW, TileH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    Color32[] pixels = new Color32[TileW * TileH];
                    for (int y = 0; y < TileH; y++)
                        for (int x = 0; x < TileW; x++)
                        {
                            byte v = (byte)Mathf.RoundToInt(40 + 170f * (tx * TileW + x) / (Columns * TileW));
                            pixels[y * TileW + x] = new Color32(v, v, v, 255);
                        }
                    texture.SetPixels32(pixels); texture.Apply();
                    owned.Add(texture);
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, TileW, TileH), new Vector2(.5f, .5f), LocationVisualGeometry.PixelsPerUnit, 0, SpriteMeshType.FullRect);
                    owned.Add(sprite);
                    LocationGroundTile tile = new LocationGroundTile { X = tx, Y = ty, Color = sprite };
                    if (heights)
                    {
                        ushort[] values = new ushort[TileW * TileH];
                        byte[] coverage = new byte[TileW * TileH];
                        for (int y = 0; y < TileH; y++)
                            for (int x = 0; x < TileW; x++)
                            {
                                double h = Surface(tx * TileW + x + .5, ty * TileH + y + .5);
                                values[y * TileW + x] = (ushort)Mathf.RoundToInt((float)((h + 1) / 6 * 65535));
                                coverage[y * TileW + x] = 255;
                            }
                        byte[] data = new LocationHeightTileData(TileW, TileH, 16, -1, 5, values, coverage).Serialize();
                        TextAsset asset = new TextAsset(data);
                        owned.Add(asset);
                        tile.Height = asset;
                    }
                    visual.Ground.Tiles.Add(tile);
                }
            }
            return visual;
        }

        private static LocalLocationDefinition BuildLocation() => new LocalLocationDefinition
        {
            Id = "playmode_tiles", DisplayName = "Участки", CanvasWidth = Columns * TileW, CanvasHeight = Rows * TileH, HexesAcross = 60, BattleFrameWidth = 1920,
            BattlefieldId = "technical_lighting_camp_field"
        };

        private static BattlefieldDefinitionData Field()
        {
            BattlefieldDatabaseAsset fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            return fields.FindById("technical_lighting_camp_field") ?? fields.Battlefields[0];
        }

        [UnityTest]
        public IEnumerator SeamsRenderWithoutJumpAndTilesShareOneRoot()
        {
            LocalLocationDefinition location = BuildLocation();
            LocationVisualDefinition visual = BuildVisual(location, false);
            Assert.That(LocationGroundLayout.RuntimeErrors(visual, location), Is.Empty);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, Field()))
            {
                Assert.That(renderer.GroundPieceCount, Is.EqualTo(Columns * Rows));
                Transform ground = renderer.Root.transform.Find("Земля");
                Assert.That(ground.childCount, Is.EqualTo(Columns * Rows));
                // Участок X000_Y000 — снизу слева, X002_Y001 — сверху справа.
                Assert.That(renderer.GroundRenderer(0, 0).bounds.min.x, Is.EqualTo(-Columns * TileW / 2f / LocationVisualGeometry.PixelsPerUnit).Within(1e-3f));
                Assert.That(renderer.GroundRenderer(0, 0).bounds.min.y, Is.EqualTo(-Rows * TileH / 2f / LocationVisualGeometry.PixelsPerUnit).Within(1e-3f));
                Assert.That(renderer.GroundRenderer(2, 1).bounds.max.x, Is.EqualTo(Columns * TileW / 2f / LocationVisualGeometry.PixelsPerUnit).Within(1e-3f));
                Assert.That(renderer.GroundRenderer(1, 0).bounds.min.x, Is.EqualTo(renderer.GroundRenderer(0, 0).bounds.max.x).Within(1e-4f), "участки встык, без зазора и наложения");
                renderer.SetTime(13, 0);
                renderer.ActorsVisible = false;
                RenderTexture target = renderer.EnsureTarget(640, 360);
                // Кадр 1:1 на шве X000|X001 при дробном центре.
                renderer.SetView(new Vector2(TileW + .37f, TileH * 1.5f), 360);
                renderer.Camera.Render();
                yield return null;
                RenderTexture.active = target;
                Texture2D read = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); read.Apply();
                RenderTexture.active = null;
                owned.Add(read);
                float maxStep = 0, sum = 0;
                int count = 0;
                for (int x = 1; x < 640; x++)
                {
                    float step = Mathf.Abs(read.GetPixel(x, 180).r - read.GetPixel(x - 1, 180).r);
                    maxStep = Mathf.Max(maxStep, step); sum += step; count++;
                }
                float mean = sum / count;
                float left = read.GetPixel(2, 180).r, right = read.GetPixel(637, 180).r;
                Assert.That(left, Is.GreaterThan(.05f), "кадр не пустой: земля видна");
                Assert.That(right - left, Is.GreaterThan(.02f), "градиент виден слева направо — ориентация участков верная");
                Assert.That(maxStep, Is.LessThan(mean * 4 + 2 / 255f), "на шве нет скачка яркости сильнее обычного шага градиента");
                TestContext.WriteLine("Шов: средний шаг " + mean + ", наибольший " + maxStep + ", края " + left + " → " + right);
            }
        }

        [UnityTest]
        public IEnumerator PartyCrossesSeamsWithOneRendererCameraAndHeight()
        {
            LocalLocationDefinition location = BuildLocation();
            LocationVisualDefinition visual = BuildVisual(location);
            GameObject host = new GameObject("Место далеко");
            host.transform.position = new Vector3(10000, 10000, 0);
            owned.Add(host);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, Field(), host.transform))
            {
                Assert.That(renderer.Height, Is.Not.Null);
                Assert.That(renderer.Height.LoadedTiles, Is.EqualTo(Columns * Rows));
                renderer.EnsureTarget(1280, 720);
                GameObject root = renderer.Root;
                float spacing = renderer.HexSizePixels * 1.2f;
                List<KeyValuePair<string, LocalPointData>> members = new List<KeyValuePair<string, LocalPointData>>();
                List<LocalPointData> points = renderer.Geometry.SpreadAround(new LocalPointData(200, 600), 3, spacing);
                for (int i = 0; i < 3; i++) members.Add(new KeyValuePair<string, LocalPointData>("member_" + i, points[i]));
                LocalFreeMover mover = new LocalFreeMover(renderer.Geometry.Layer, renderer.Geometry.Rules, members, spacing);
                Assert.That(mover.MoveLeaderTo(1700, 150), Is.True);
                Vector2 previousView = default;
                bool first = true, crossedColumn = false, crossedRow = false;
                int startColumn = -1, startRow = -1;
                Rect bounds = renderer.CameraBounds;
                for (int frame = 0; frame < 900 && mover.Leader.Walking || frame == 0; frame++)
                {
                    mover.Tick(1 / 60f, out _);
                    List<LocationWorldRenderer.ActorFrame> frames = new List<LocationWorldRenderer.ActorFrame>();
                    foreach (LocalFreeMover.Member member in mover.Members)
                        frames.Add(new LocationWorldRenderer.ActorFrame { Id = member.Id, Pixel = new Vector2((float)member.X, (float)member.Y), Walking = member.Walking });
                    renderer.SetActors(frames, frame / 60f);
                    Vector2 leader = new Vector2((float)mover.Leader.X, (float)mover.Leader.Y);
                    renderer.Follow(leader, 720, 1 / 60f, first);

                    // Кадр не выходит за карту и не прыгает.
                    float halfW = renderer.ViewHeight * renderer.Camera.aspect / 2, halfH = renderer.ViewHeight / 2;
                    Assert.That(renderer.ViewCenter.x - halfW, Is.GreaterThanOrEqualTo(bounds.xMin - 1e-2f));
                    Assert.That(renderer.ViewCenter.x + halfW, Is.LessThanOrEqualTo(bounds.xMax + 1e-2f));
                    Assert.That(renderer.ViewCenter.y - halfH, Is.GreaterThanOrEqualTo(bounds.yMin - 1e-2f));
                    Assert.That(renderer.ViewCenter.y + halfH, Is.LessThanOrEqualTo(bounds.yMax + 1e-2f));
                    if (!first) Assert.That((renderer.ViewCenter - previousView).magnitude, Is.LessThan(40), "кадр " + frame + ": камера не прыгает на шве");
                    previousView = renderer.ViewCenter;
                    first = false;

                    // Высота под ногами: опора фигуры — точка мира без сдвига по высоте.
                    Assert.That(renderer.TrySampleActorHeight(mover.Leader.Id, out HeightSample sample), Is.True, sample.Reason);
                    double vy = location.CanvasHeight - leader.y;
                    Assert.That(sample.Blender, Is.EqualTo(Surface(leader.x, vy)).Within(.002), "высота под ногами — та же поверхность");
                    Assert.That(renderer.TrySampleHeight(root.transform.TransformPoint(LocationVisualGeometry.PixelToWorld(location, leader)), out HeightSample world), Is.True);
                    Assert.That(world.Blender, Is.EqualTo(sample.Blender).Within(1e-6));
                    if (startColumn < 0) { startColumn = sample.Column; startRow = sample.Row; }
                    crossedColumn |= sample.Column != startColumn;
                    crossedRow |= sample.Row != startRow;
                    Assert.That(renderer.Root, Is.SameAs(root), "место не пересобирается на шве");
                    if (frame % 30 == 0) yield return null;
                }
                Assert.That(crossedColumn && crossedRow, Is.True, "командир прошёл через границы участков по X и по Y");
                Assert.That(Vector2.Distance(new Vector2((float)mover.Leader.X, (float)mover.Leader.Y), new Vector2(1700, 150)), Is.LessThan(spacing), "маршрут не прервался на шве");
                // Высота вне карты — недействительна, не ноль.
                Assert.That(renderer.TrySampleHeightAtPixel(new Vector2(-10, 10), out HeightSample outside), Is.False);
                Assert.That(outside.Valid, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator CameraClampsWholeViewportForAspectsAndSmallMap()
        {
            LocalLocationDefinition location = BuildLocation();
            LocationVisualDefinition visual = BuildVisual(location, false);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, Field()))
            {
                foreach (Vector2Int size in new[] { new Vector2Int(1600, 900), new Vector2Int(1600, 1000), new Vector2Int(2520, 1080) })
                {
                    renderer.EnsureTarget(size.x, size.y);
                    foreach (Vector2 leader in new[] { new Vector2(0, 0), new Vector2(location.CanvasWidth, location.CanvasHeight), new Vector2(location.CanvasWidth, 0) })
                    {
                        renderer.Follow(leader, 600, 1 / 60f, true);
                        float halfW = renderer.ViewHeight * renderer.Camera.aspect / 2;
                        Assert.That(renderer.ViewCenter.x - halfW, Is.GreaterThanOrEqualTo(-1e-2f), size + " " + leader);
                        Assert.That(renderer.ViewCenter.x + halfW, Is.LessThanOrEqualTo(location.CanvasWidth + 1e-2f), size + " " + leader);
                        Assert.That(renderer.Camera.orthographicSize, Is.EqualTo(600 / LocationVisualGeometry.PixelsPerUnit / 2).Within(1e-4f), "следование не меняет масштаб");
                    }
                }
                // Кадр выше карты — по центру по вертикали, без скрытого приближения.
                renderer.EnsureTarget(1600, 900);
                renderer.Follow(new Vector2(100, 100), 2000, 1 / 60f, true);
                Assert.That(renderer.ViewCenter.y, Is.EqualTo(location.CanvasHeight / 2).Within(1e-3f));
                Assert.That(renderer.ViewHeight, Is.EqualTo(2000));
                yield return null;
            }
        }

        // Нормали участка (R вправо, G вверх, B к зрителю — контракт экспорта)
        // под 2D-светом URP: склон, повёрнутый к источнику, светлее. Источник
        // слева/справа/сверху/снизу; ночь под крышей — общий свет слабый.
        [UnityTest]
        public IEnumerator TileNormalsFaceTheLightFromFourSides()
        {
            Vector3 right = new Vector3(.7f, 0, .7f).normalized, up = new Vector3(0, .7f, .7f).normalized;
            Vector3 left = new Vector3(-right.x, 0, right.z), down = new Vector3(0, -up.y, up.z);
            // Точки места — доли рисунка, Y вниз: «сверху» — y = 0,2.
            Vector2 lightLeft = new Vector2(.2f, .5f), lightRight = new Vector2(.8f, .5f), lightTop = new Vector2(.5f, .2f), lightBottom = new Vector2(.5f, .8f);
            float[] value = new float[1];
            float Measure() => value[0];
            yield return Brightness(right, lightRight, value); float facingRight = Measure();
            yield return Brightness(right, lightLeft, value); float awayRight = Measure();
            yield return Brightness(up, lightTop, value); float facingUp = Measure();
            yield return Brightness(up, lightBottom, value); float awayUp = Measure();
            yield return Brightness(left, lightLeft, value); float facingLeft = Measure();
            yield return Brightness(left, lightRight, value); float awayLeft = Measure();
            yield return Brightness(down, lightBottom, value); float facingDown = Measure();
            yield return Brightness(down, lightTop, value); float awayDown = Measure();
            TestContext.WriteLine("Склон вправо: свет справа " + facingRight + ", слева " + awayRight + "; вверх: сверху " + facingUp + ", снизу " + awayUp +
                                  "; влево: слева " + facingLeft + ", справа " + awayLeft + "; вниз: снизу " + facingDown + ", сверху " + awayDown);
            Assert.That(facingRight, Is.GreaterThan(awayRight + .02f), "склон вправо светлее под светом справа");
            Assert.That(facingUp, Is.GreaterThan(awayUp + .02f), "склон вверх (G) светлее под светом сверху");
            Assert.That(facingLeft, Is.GreaterThan(awayLeft + .02f), "склон влево светлее под светом слева");
            Assert.That(facingDown, Is.GreaterThan(awayDown + .02f), "склон вниз светлее под светом снизу");
        }

        private IEnumerator Brightness(Vector3 normal, Vector2 lightAt, float[] output)
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "normals", CanvasWidth = 1080, CanvasHeight = 1080 };
            LocationVisualDefinition visual = new LocationVisualDefinition
            {
                LocationId = location.Id, UseWorldLighting = false, Lighting = LocationLightingMode.Indoor, IndoorIntensity = .05f, PeopleCastShadows = false
            };
            Texture2D color = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Texture2D normals = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            Color32[] white = new Color32[64 * 64], encoded = new Color32[64 * 64];
            Color32 n = new Color32((byte)Mathf.RoundToInt((normal.x * .5f + .5f) * 255), (byte)Mathf.RoundToInt((normal.y * .5f + .5f) * 255),
                (byte)Mathf.RoundToInt((normal.z * .5f + .5f) * 255), 255);
            for (int i = 0; i < white.Length; i++) { white[i] = new Color32(255, 255, 255, 255); encoded[i] = n; }
            color.SetPixels32(white); color.Apply();
            normals.SetPixels32(encoded); normals.Apply();
            owned.Add(color); owned.Add(normals);
            Sprite sprite = Sprite.Create(color, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, Vector4.zero, false,
                new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normals } });
            owned.Add(sprite);
            visual.Ground = new LocationGroundDefinition { Version = 1, Mode = LocationGroundMode.Tiles, Columns = 1, Rows = 1, TileWidth = 1080, TileHeight = 1080 };
            visual.Ground.Tiles.Add(new LocationGroundTile { X = 0, Y = 0, Color = sprite });
            LocationVisualObject light = new LocationVisualObject { Id = "light", Name = "Свет", LightOnly = true, Position = lightAt };
            light.Light.Enabled = true; light.Light.Radius = 6; light.Light.Intensity = 1.5f; light.Light.Color = Color.white;
            light.Light.Animation = LocationLightAnimation.None; light.Light.Offset = Vector2.zero; light.Light.NormalMaps = true;
            light.Light.ProjectsShadows = false; light.Light.Shadows = false;
            visual.Objects.Add(light);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                RenderTexture target = renderer.EnsureTarget(128, 128);
                renderer.SetView(new Vector2(540, 540), 1080);
                renderer.SetTime(1, 0);
                // Источники 2D-света обновляются в LateUpdate — после кадра.
                yield return null;
                yield return null;
                renderer.Camera.Render();
                RenderTexture.active = target;
                Texture2D read = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); read.Apply();
                RenderTexture.active = null;
                float sum = 0;
                for (int y = 54; y < 74; y++) for (int x = 54; x < 74; x++) sum += read.GetPixel(x, y).grayscale;
                Object.Destroy(read);
                output[0] = sum / 400;
            }
        }

        [UnityTest]
        public IEnumerator DisposeReleasesHeightAndRoot()
        {
            LocalLocationDefinition location = BuildLocation();
            LocationVisualDefinition visual = BuildVisual(location);
            LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, Field());
            GameObject root = renderer.Root;
            Assert.That(renderer.Height.MemoryBytes, Is.EqualTo((long)Columns * Rows * TileW * TileH * 3));
            renderer.Dispose();
            yield return null;
            Assert.That(renderer.Height, Is.Null);
            Assert.That(root == null, Is.True);
        }
    }
}
