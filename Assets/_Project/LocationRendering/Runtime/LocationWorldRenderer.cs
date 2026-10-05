using System;
using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12К (канон v1.54 §28.3): один рендерер места — для окна «База
    // локаций», тестовой сцены света и игры. Рисует рисунок места (или
    // заглушку по разметке местности), предметы художественной сборки со
    // светом и тенями и фигуры. Бой на месте идёт поверх той же картинки:
    // камера совмещает кадр арены на рисунке с полем боя на экране.
    // Не владеет сохранением и часами кампании.
    public sealed partial class LocationWorldRenderer : IDisposable
    {
        public enum ActorKind { Party, Retinue, Enemy }

        // Как показать одного участника в этом кадре.
        public struct ActorFrame
        {
            public string Id;
            public string UnitTypeId;
            public ActorKind Kind;
            // Опора фигуры — точка рисунка места (пиксели, Y вниз).
            public Vector2 Pixel;
            // Направление взгляда на рисунке (Y вниз); ноль — прежнее.
            public Vector2 Direction;
            public bool Walking;
            public bool Wounded;
        }

        private sealed class Placed
        {
            public LocationVisualObject Data;
            public Transform Anchor;
            public SpriteRenderer Image;
            public Light2D Light;
        }

        private sealed class Actor
        {
            public string Id;
            public string UnitTypeId;
            public Transform Anchor;
            public SpriteRenderer Image;
            public CreatureAnimationPlayer Player;
            public UnitDefinitionData Unit;
            public HexFacing Facing = HexFacing.East;
            public bool Seen;
        }

        // Высота фигуры в размерах клетки боя — как у поля боя.
        public const float FieldHeightInHexSizes = 1.35f;

        public GameObject Root { get; }
        public Camera Camera { get; }
        public Light2D GlobalLight { get; private set; }
        public LocalLocationGeometry Geometry { get; }
        public LocalLocationDefinition Location { get; }
        public LocationVisualDefinition Definition { get; }
        // Небо места: общий свет мира или своё.
        public LocationSky Sky { get; }
        public BattlefieldDefinitionData Field { get; }
        public float Hour { get; private set; } = 13;
        public RenderTexture Target { get; private set; }

        // Видимая область: центр и высота в пикселях рисунка.
        public Vector2 ViewCenter { get; private set; }
        public float ViewHeight { get; private set; }

        private readonly List<Placed> placed = new List<Placed>();
        private readonly Dictionary<string, Actor> actors = new Dictionary<string, Actor>(StringComparer.Ordinal);
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly List<Light2D> suppressed = new List<Light2D>();
        private readonly Material lit, unlit;
        private readonly CreatureAnimationDatabaseAsset animations;
        private readonly UnitDatabaseAsset units;
        private readonly Transform actorLayer;
        private Sprite placeholderActor, placeholderShadow;

        // world — общий свет мира; не задан — из Базы локаций.
        public LocationWorldRenderer(LocalLocationDefinition location, LocationVisualDefinition visual,
            BattlefieldDefinitionData field, Transform parent = null, LocationWorldLighting world = null)
        {
            Location = location ?? throw new ArgumentNullException(nameof(location));
            Definition = visual ?? new LocationVisualDefinition { LocationId = location.Id };
            Sky = new LocationSky(Definition, world ?? LocalLocationDatabaseAsset.LoadWorldLighting());
            Field = field;
            Geometry = new LocalLocationGeometry(location, field, LocationVisualGeometry.BlockedAreas(Definition, location));
            Root = new GameObject("Локация · " + location.DisplayName);
            if (parent != null) Root.transform.SetParent(parent, false);
            lit = Own(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")));
            unlit = Own(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")));
            Camera = Child("Камера").AddComponent<Camera>();
            Camera.orthographic = true;
            Camera.transform.localPosition = new Vector3(0, 0, -10);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.035f, .045f, .04f);
            Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            Camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            GlobalLight = Child("Общий свет").AddComponent<Light2D>();
            GlobalLight.lightType = Light2D.LightType.Global;
            InitializeLighting();

            BuildGround();
            foreach (LocationVisualObject item in Definition.Objects) AddObject(item);
            actorLayer = Child("Фигуры").transform;
            animations = Resources.Load<CreatureAnimationDatabaseAsset>(CreatureAnimationDatabaseAsset.ResourcesPath);
            units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            ShowWhole();
            SetTime(13, 0);
        }

        public Vector2 CanvasSize => LocationVisualGeometry.CanvasSize(Location);

        // Размер клетки боя на рисунке (пиксели) — от него рост фигур.
        public float HexSizePixels => Field != null ? Geometry.ArenaHexSize : 40f;

        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }

        private GameObject Child(string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(Root.transform, false);
            return child;
        }

        private SpriteRenderer Image(GameObject target, Sprite sprite, bool fullbright)
        {
            SpriteRenderer renderer = target.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = fullbright ? unlit : lit;
            return renderer;
        }

        // Рисунок места на весь размер рисунка; нет рисунка — заглушка по
        // разметке местности (камень, пол, осыпь, вода), чтобы стены были видны.
        private void BuildGround()
        {
            Sprite background = Definition.Background;
            bool placeholder = background == null;
            if (placeholder) background = TerrainPlaceholder();
            SpriteRenderer ground = Image(Child("Земля"), background, false);
            ground.sortingOrder = -30000;
            Vector2 size = background.bounds.size;
            Vector2 world = LocationVisualGeometry.WorldSize(Location);
            ground.transform.localScale = new Vector3(world.x / size.x, world.y / size.y, 1);
        }

        private Sprite TerrainPlaceholder()
        {
            WorldMapTerrainLayer layer = Location.CreateTerrainLayer();
            Vector2 canvas = CanvasSize;
            int width = Mathf.Clamp(Mathf.RoundToInt(canvas.x / 8), 8, 512);
            int height = Mathf.Clamp(Mathf.RoundToInt(canvas.y / 8), 8, 512);
            bool empty = layer.IsEmpty;
            Texture2D texture = Own(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.name = "Техническая заглушка места";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color[] colors = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double px = (x + .5) / width * canvas.x;
                    double py = (1 - (y + .5) / height) * canvas.y;
                    colors[y * width + x] = empty ? new Color(.40f, .43f, .34f) : TerrainColor(layer.GetAtPixel(px, py));
                }
            }
            texture.SetPixels(colors);
            texture.Apply();
            return Own(Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100));
        }

        public static Color TerrainColor(WorldMapGameplayTerrainType terrain)
        {
            switch (terrain)
            {
                case WorldMapGameplayTerrainType.Cliffs: return new Color(.10f, .095f, .09f);
                case WorldMapGameplayTerrainType.Mountains: return new Color(.22f, .21f, .20f);
                case WorldMapGameplayTerrainType.Hills: return new Color(.36f, .31f, .24f);
                case WorldMapGameplayTerrainType.Road: return new Color(.40f, .35f, .27f);
                case WorldMapGameplayTerrainType.Trail: return new Color(.33f, .29f, .23f);
                case WorldMapGameplayTerrainType.Field: return new Color(.36f, .38f, .22f);
                case WorldMapGameplayTerrainType.Forest: return new Color(.17f, .24f, .14f);
                case WorldMapGameplayTerrainType.Swamp: return new Color(.20f, .24f, .18f);
                case WorldMapGameplayTerrainType.Water: return new Color(.12f, .20f, .27f);
                default: return new Color(.27f, .24f, .20f);
            }
        }

        private void AddObject(LocationVisualObject item)
        {
            if (item == null || item.Hidden) return;
            Transform anchor = Child(item.Name).transform;
            anchor.localPosition = LocationVisualGeometry.ToWorld(Location, item.Position);
            SpriteRenderer image = null;
            if (!item.LightOnly)
            {
                GameObject imageObject = new GameObject("Рисунок");
                imageObject.transform.SetParent(anchor, false);
                Sprite sprite = item.ResolveSprite();
                if (sprite == null) sprite = PlaceholderSprite(item.Placeholder);
                image = Image(imageObject, sprite, item.Placeholder == LocationPlaceholder.Fire);
                Fit(image, item.Height, item.Pivot, item.FlipX, Vector2.zero);
                image.sortingOrder = LocationVisualGeometry.SortOrder(item.Band, anchor.localPosition.y, item.OrderOffset);
            }
            if (item.CastsShadow && !item.LightOnly)
            {
                // Форма у основания, независимая от рисунка и его прозрачных полей.
                // URP 17.6 не предоставляет runtime-setter контура. Используем
                // обычный авторский prefab с единичным основанием, масштабируем его.
                GameObject template = Resources.Load<GameObject>("LocationRendering/GroundShadowCaster");
                if (template != null)
                {
                    GameObject shadow = UnityEngine.Object.Instantiate(template, anchor, false);
                    shadow.transform.localScale = new Vector3(item.Footprint.x, item.Footprint.y, 1);
                }
            }
            Light2D light = item.Light.Enabled ? CreateLight(anchor, item.Light) : null;
            Placed entry = new Placed { Data = item, Anchor = anchor, Image = image, Light = light };
            placed.Add(entry);
            if (image != null && item.ProjectsShadow && item.Band == LocationVisualBand.World)
                AddCaster(anchor, image, item.ShadowSprite, item.Height, item.Pivot, item.FlipX, item.ShadowLength, null);
        }

        public void SetTime(float hours, float seconds)
        {
            Hour = Mathf.Repeat(hours, 24);
            Seconds = seconds;
            ApplyLighting();
        }

        public float Seconds { get; private set; }

        // Общий свет сцены, в которую встроено место (глобальная карта),
        // не должен складываться со светом места: на время показа гасится.
        public void SuppressOtherGlobalLights()
        {
            foreach (Light2D light in UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            {
                if (light == null || light == GlobalLight || !light.enabled || light.lightType != Light2D.LightType.Global ||
                    light.transform.IsChildOf(Root.transform))
                    continue;
                light.enabled = false;
                suppressed.Add(light);
            }
        }

        private void RestoreSuppressedLights()
        {
            foreach (Light2D light in suppressed)
            {
                if (light != null) light.enabled = true;
            }
            suppressed.Clear();
        }

        // ------------------------------------------------------------------
        // Камера: рисунок места ↔ кадр на экране
        // ------------------------------------------------------------------

        // Камера рисует в текстуру (экран исследования в UI Toolkit). Размер
        // меняется — текстура пересоздаётся.
        public RenderTexture EnsureTarget(int width, int height)
        {
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);
            if (Target != null && Target.width == width && Target.height == height)
                return Target;
            if (Target != null)
            {
                Camera.targetTexture = null;
                Target.Release();
                Destroy(Target);
                owned.Remove(Target);
            }
            Target = Own(new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Место · кадр" });
            Target.Create();
            Camera.targetTexture = Target;
            Camera.aspect = width / (float)height;
            ApplyView();
            return Target;
        }

        private float Aspect => Camera.aspect > 0.01f ? Camera.aspect : BattlefieldFrame.Aspect;

        // Весь рисунок по высоте.
        public void ShowWhole() => SetView(CanvasSize / 2, CanvasSize.y);

        public void SetView(Vector2 centerPixel, float heightPixels)
        {
            ViewHeight = Mathf.Max(32, heightPixels);
            ViewCenter = centerPixel;
            ApplyView();
        }

        // Следить за точкой; край рисунка не уходит внутрь кадра.
        public void LookAt(Vector2 pixel, float heightPixels)
        {
            ViewHeight = Mathf.Max(32, heightPixels);
            Vector2 canvas = CanvasSize;
            float halfH = ViewHeight / 2, halfW = halfH * Aspect;
            float x = canvas.x <= halfW * 2 ? canvas.x / 2 : Mathf.Clamp(pixel.x, halfW, canvas.x - halfW);
            float y = canvas.y <= halfH * 2 ? canvas.y / 2 : Mathf.Clamp(pixel.y, halfH, canvas.y - halfH);
            ViewCenter = new Vector2(x, y);
            ApplyView();
        }

        // Бой: прямоугольник рисунка canvasRect (кадр арены) должен совпасть
        // с прямоугольником viewportRect (доли кадра камеры, Y вниз), где
        // поле боя рисует свою арену.
        public void AlignFrame(Rect canvasRect, Rect viewportRect)
        {
            if (viewportRect.height <= 0.0001f || viewportRect.width <= 0.0001f)
                return;
            ViewHeight = canvasRect.height / viewportRect.height;
            float viewWidth = ViewHeight * Aspect;
            ViewCenter = new Vector2(
                canvasRect.center.x + (.5f - viewportRect.center.x) * viewWidth,
                canvasRect.center.y + (.5f - viewportRect.center.y) * ViewHeight);
            ApplyView();
        }

        private void ApplyView()
        {
            Camera.orthographicSize = ViewHeight / LocationVisualGeometry.PixelsPerUnit / 2;
            Vector2 world = LocationVisualGeometry.PixelToWorld(Location, ViewCenter);
            Camera.transform.localPosition = new Vector3(world.x, world.y, -10);
        }

        // Доли кадра камеры (Y вниз) ↔ пиксели рисунка места.
        public Vector2 ViewportToPixel(Vector2 viewport) => ViewCenter + new Vector2(
            (viewport.x - .5f) * ViewHeight * Aspect, (viewport.y - .5f) * ViewHeight);

        public Vector2 PixelToViewport(Vector2 pixel) => new Vector2(
            (pixel.x - ViewCenter.x) / (ViewHeight * Aspect) + .5f, (pixel.y - ViewCenter.y) / ViewHeight + .5f);

        // ------------------------------------------------------------------
        // Фигуры
        // ------------------------------------------------------------------

        public bool ActorsVisible
        {
            get => actorLayer.gameObject.activeSelf;
            set => actorLayer.gameObject.SetActive(value);
        }

        public void SetActors(IReadOnlyList<ActorFrame> frames, float seconds)
        {
            foreach (Actor actor in actors.Values) actor.Seen = false;
            float hexWorld = HexSizePixels / LocationVisualGeometry.PixelsPerUnit;
            foreach (ActorFrame frame in frames)
            {
                if (string.IsNullOrEmpty(frame.Id)) continue;
                if (!actors.TryGetValue(frame.Id, out Actor actor) || actor.UnitTypeId != (frame.UnitTypeId ?? string.Empty))
                {
                    if (actor != null) Destroy(actor.Anchor.gameObject);
                    actor = CreateActor(frame);
                    actors[frame.Id] = actor;
                }
                actor.Seen = true;
                LayoutActor(actor, frame, hexWorld, seconds);
            }
            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, Actor> entry in actors)
            {
                if (!entry.Value.Seen) gone.Add(entry.Key);
            }
            foreach (string id in gone)
            {
                Destroy(actors[id].Anchor.gameObject);
                actors.Remove(id);
            }
            UpdateShadows();
        }

        public bool HasActor(string id) => actors.ContainsKey(id);

        private Actor CreateActor(ActorFrame frame)
        {
            Transform anchor = new GameObject(frame.Id).transform;
            anchor.SetParent(actorLayer, false);
            GameObject imageObject = new GameObject("Фигура");
            imageObject.transform.SetParent(anchor, false);
            UnitDefinitionData unit = units != null && !string.IsNullOrEmpty(frame.UnitTypeId) ? units.FindById(frame.UnitTypeId) : null;
            Actor actor = new Actor
            {
                Id = frame.Id,
                UnitTypeId = frame.UnitTypeId ?? string.Empty,
                Anchor = anchor,
                Unit = unit,
                Facing = frame.Kind == ActorKind.Enemy ? HexFacing.West : HexFacing.East
            };
            actor.Image = Image(imageObject, unit?.BattlefieldSprite != null ? unit.BattlefieldSprite : PlaceholderActor(), false);
            CreatureAnimationSetData set = animations != null && unit != null ? animations.FindSet(unit.AnimationSetId) : null;
            if (set != null && set.HasAnyFrames)
            {
                float phase = (frame.Id.GetHashCode() & 0x7fff) / 32767f * 2f;
                actor.Player = new CreatureAnimationPlayer(set, DirectionFor(actor.Facing, frame.Kind), phase);
            }
            if (placeholderShadow == null) placeholderShadow = PlaceholderEllipse();
            SpriteRenderer shadow = Image(new GameObject("Тень под ногами"), placeholderShadow, false);
            shadow.transform.SetParent(anchor, false);
            shadow.color = new Color(0, 0, 0, .35f);
            shadow.sortingOrder = -24000;
            AddCaster(anchor, actor.Image, null, 0, Vector2.zero, false, 1, null, true);
            return actor;
        }

        private void LayoutActor(Actor actor, ActorFrame frame, float hexWorld, float seconds)
        {
            actor.Anchor.localPosition = LocationVisualGeometry.PixelToWorld(Location, frame.Pixel);
            if (frame.Direction.sqrMagnitude > 1e-6f)
                actor.Facing = FacingFrom(frame.Direction);
            bool mirrored = frame.Kind == ActorKind.Enemy;
            Transform shadow = actor.Anchor.Find("Тень под ногами");
            if (shadow != null) shadow.localScale = new Vector3(hexWorld * .9f, hexWorld * .27f, 1);

            float height = hexWorld * FieldHeightInHexSizes * (actor.Unit?.BattlefieldScale ?? 1);
            Vector2 pivot = new Vector2(.5f, .15f), offset = Vector2.zero;
            if (actor.Player != null)
            {
                actor.Player.SetDirection(DirectionFor(actor.Facing, frame.Kind));
                CreatureAnimationAction action = frame.Walking ? CreatureAnimationAction.Walk : CreatureAnimationAction.Idle;
                if (actor.Player.Action != action) actor.Player.Play(action, seconds);
                Sprite sprite = actor.Player.Evaluate(seconds) ?? actor.Player.Set.FindFirstFrame();
                if (sprite != null) actor.Image.sprite = sprite;
                height = hexWorld * FieldHeightInHexSizes * actor.Player.Set.FieldScale;
                pivot = actor.Player.Set.Pivot;
                offset = actor.Player.Clip?.Offset ?? Vector2.zero;
            }
            else if (actor.Unit?.BattlefieldSprite == null)
            {
                actor.Image.color = frame.Kind == ActorKind.Enemy ? new Color(.85f, .55f, .50f)
                    : frame.Kind == ActorKind.Retinue ? new Color(.70f, .76f, .85f) : Color.white;
            }
            Color tint = frame.Kind == ActorKind.Retinue ? new Color(.87f, .86f, .78f) : Color.white;
            if (frame.Wounded) tint *= new Color(.85f, .75f, .75f);
            if (actor.Player != null || actor.Unit?.BattlefieldSprite != null) actor.Image.color = tint;
            Fit(actor.Image, height, pivot, mirrored, offset);
            actor.Image.sortingOrder = LocationVisualGeometry.SortOrder(LocationVisualBand.World, actor.Anchor.localPosition.y);
        }

        private CreatureAnimationDirection DirectionFor(HexFacing facing, ActorKind kind)
        {
            // Противники на поле зеркальны — как в бою.
            if (kind == ActorKind.Enemy) facing = Mirror(facing);
            if (animations != null) return animations.GetDirection(facing);
            foreach (CreatureAnimationDirectionMapping entry in CreatureAnimationDatabaseAsset.DefaultDirectionMap())
            {
                if (entry.Facing == facing) return entry.Direction;
            }
            return CreatureAnimationDirection.Front;
        }

        // Направление на рисунке (Y вниз) → ракурс шестиугольника.
        public static HexFacing FacingFrom(Vector2 direction)
        {
            float angle = Mathf.Repeat(Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg, 360);
            if (angle < 30 || angle >= 330) return HexFacing.East;
            if (angle < 90) return HexFacing.NorthEast;
            if (angle < 150) return HexFacing.NorthWest;
            if (angle < 210) return HexFacing.West;
            if (angle < 270) return HexFacing.SouthWest;
            return HexFacing.SouthEast;
        }

        private static HexFacing Mirror(HexFacing facing)
        {
            switch (facing)
            {
                case HexFacing.East: return HexFacing.West;
                case HexFacing.West: return HexFacing.East;
                case HexFacing.NorthEast: return HexFacing.NorthWest;
                case HexFacing.NorthWest: return HexFacing.NorthEast;
                case HexFacing.SouthEast: return HexFacing.SouthWest;
                default: return HexFacing.SouthEast;
            }
        }

        private static void Fit(SpriteRenderer image, float height, Vector2 pivot, bool flip, Vector2 offset)
        {
            if (image.sprite == null) return;
            Vector2 size = image.sprite.bounds.size;
            float scale = height / Mathf.Max(.001f, size.y);
            float width = size.x * scale;
            image.transform.localScale = Vector3.one * scale;
            // Sprite pivot не меняется в importer: индивидуальная опора хранится у размещения.
            Vector2 originalPivot = image.sprite.pivot / image.sprite.rect.size;
            image.transform.localPosition = new Vector2(
                ((flip ? 1 - originalPivot.x : originalPivot.x) - (flip ? 1 - pivot.x : pivot.x) + (flip ? -offset.x : offset.x)) * width,
                (originalPivot.y - pivot.y + offset.y) * height);
            image.flipX = flip;
        }

        // ------------------------------------------------------------------
        // Предметы сборки (редактор)
        // ------------------------------------------------------------------

        public SpriteRenderer FindObject(string id) => placed.Find(item => item.Data.Id == id)?.Image;

        // Перемещение объекта без пересборки сцены (перетаскивание в редакторе);
        // свет и тень едут вместе с опорой. Проходимость пересчитывает пересборка.
        public void MoveObject(string id, Vector2 normalizedPosition)
        {
            Placed item = placed.Find(entry => entry.Data.Id == id);
            if (item == null) return;
            item.Anchor.localPosition = LocationVisualGeometry.ToWorld(Location, normalizedPosition);
            if (item.Image != null) item.Image.sortingOrder = LocationVisualGeometry.SortOrder(item.Data.Band, item.Anchor.localPosition.y, item.Data.OrderOffset);
        }

        // Смена состояния одного объекта, без пересборки фона и без изменения авторских данных.
        public bool SetObjectVariant(string objectId, string variantId)
        {
            Placed item = placed.Find(entry => entry.Data.Id == objectId);
            LocationVisualVariant variant = item?.Data.Variants?.Find(entry => entry.Id == variantId);
            if (variant?.Sprite == null || item.Image == null) return false;
            item.Image.sprite = variant.Sprite;
            Fit(item.Image, item.Data.Height, item.Data.Pivot, item.Data.FlipX, Vector2.zero);
            return true;
        }

        public void Dispose()
        {
            // Свой общий свет гаснет раньше, чем включается свет сцены: двух
            // общих источников на одном слое не бывает даже на кадр.
            if (GlobalLight != null) GlobalLight.enabled = false;
            RestoreSuppressedLights();
            if (Camera != null) Camera.targetTexture = null;
            Destroy(Root);
            foreach (UnityEngine.Object item in owned)
            {
                if (item is RenderTexture texture) texture.Release();
                Destroy(item);
            }
            owned.Clear();
            Target = null;
        }

        private static void Destroy(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }

        private Sprite MakeSprite(int width, int height, Func<float, float, Color> pixel)
        {
            Texture2D texture = Own(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.name = "Техническая заглушка";
            Color[] colors = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    colors[y * width + x] = pixel(x / (float)(width - 1), y / (float)(height - 1));
            texture.SetPixels(colors); texture.Apply();
            return Own(Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100));
        }

        private Sprite PlaceholderEllipse() => MakeSprite(64, 32, (x, y) =>
            Mathf.Pow((x - .5f) * 2, 2) + Mathf.Pow((y - .5f) * 2, 2) <= 1 ? Color.white : Color.clear);

        private Sprite PlaceholderActor()
        {
            if (placeholderActor != null) return placeholderActor;
            placeholderActor = MakeSprite(64, 100, (x, y) =>
            {
                if (Vector2.Distance(new Vector2(x, y), new Vector2(.5f, .80f)) < .14f) return new Color(.72f, .59f, .43f);
                if (y > .23f && y < .70f && Mathf.Abs(x - .5f) < .20f) return new Color(.43f, .45f, .35f);
                if (y > .04f && y < .28f && (Mathf.Abs(x - .37f) < .08f || Mathf.Abs(x - .63f) < .08f)) return new Color(.25f, .26f, .23f);
                return Color.clear;
            });
            return placeholderActor;
        }

        private Sprite PlaceholderSprite(LocationPlaceholder kind) => MakeSprite(128, 96, (x, y) =>
        {
            switch (kind)
            {
                case LocationPlaceholder.Tent:
                    if (y > .10f && y < .9f && Mathf.Abs(x - .5f) < (.9f - y) * .6f)
                        return x < .5f ? new Color(.70f, .63f, .46f) : new Color(.51f, .47f, .35f);
                    break;
                case LocationPlaceholder.Fire:
                    if (y < .23f && y > .1f && Mathf.Abs(x - .5f) < .42f) return new Color(.27f, .20f, .14f);
                    if (y > .2f && Mathf.Abs(x - .5f) < (.95f - y) * .28f) return new Color(1, .45f + y * .45f, .09f);
                    break;
                case LocationPlaceholder.Crate:
                    if (x > .14f && x < .86f && y > .14f && y < .86f)
                        return x < .25f || x > .75f || y < .25f || y > .75f ? new Color(.34f, .28f, .19f) : new Color(.53f, .43f, .29f);
                    break;
                case LocationPlaceholder.Rock:
                case LocationPlaceholder.Bush:
                    if (Mathf.Pow((x - .5f) * 2.1f, 2) + Mathf.Pow((y - .5f) * 2.7f, 2) < 1)
                        return kind == LocationPlaceholder.Rock ? new Color(.48f, .48f, .43f) : new Color(.28f, .36f, .24f);
                    break;
                default: return Color.white;
            }
            return Color.clear;
        });
    }
}
