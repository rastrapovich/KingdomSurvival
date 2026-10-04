using System;
using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // Один визуальный слой для редактора и теста. Не владеет сохранением/часами кампании.
    public sealed class LocationWorldRenderer : IDisposable
    {
        private sealed class Placed
        {
            public LocationVisualObject Data;
            public Transform Anchor;
            public SpriteRenderer Image;
            public Light2D Light;
        }
        private sealed class Actor
        {
            public Transform Anchor;
            public SpriteRenderer Image;
            public CreatureAnimationPlayer Player;
            public UnitDefinitionData Unit;
        }

        public GameObject Root { get; }
        public Camera Camera { get; }
        public Light2D GlobalLight { get; private set; }
        public LocalLocationGeometry Geometry { get; }
        public LocationVisualDefinition Definition { get; }
        public BattlefieldDefinitionData Field { get; }
        public float Hour { get; private set; } = 13;
        private readonly List<Placed> placed = new List<Placed>();
        private readonly List<Actor> actors = new List<Actor>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly Material lit, unlit;
        private readonly CreatureAnimationDatabaseAsset animations;

        public LocationWorldRenderer(LocalLocationDefinition location, LocationVisualDefinition definition,
            BattlefieldDefinitionData field, Transform parent = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Field = field;
            Geometry = new LocalLocationGeometry(location, field, LocationVisualGeometry.BlockedCells(definition, field));
            Root = new GameObject("Локация · " + location.DisplayName);
            if (parent != null) Root.transform.SetParent(parent, false);
            lit = Own(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")));
            unlit = Own(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")));
            Camera = Child("Камера").AddComponent<Camera>();
            Camera.orthographic = true;
            Camera.orthographicSize = LocationVisualDefinition.WorldHeight / 2;
            Camera.aspect = BattlefieldFrame.Aspect;
            Camera.transform.localPosition = new Vector3(0, 0, -10);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.035f, .045f, .04f);
            Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            Camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            GlobalLight = Child("Общий свет").AddComponent<Light2D>();
            GlobalLight.lightType = Light2D.LightType.Global;
            Sprite background = field?.Background;
            // Unity-ссылка может сохранять managed-обёртку после удаления native-объекта.
            if (background == null) background = PlaceholderSprite(LocationPlaceholder.None, true);
            SpriteRenderer ground = Image(Child("Земля"), background, false);
            ground.color = field?.Background != null ? Color.white : new Color(.40f, .43f, .34f);
            ground.sortingOrder = -30000;
            Vector2 size = background.bounds.size;
            ground.transform.localScale = new Vector3(LocationVisualDefinition.WorldWidth / size.x,
                LocationVisualDefinition.WorldHeight / size.y, 1);
            if (field?.Background != null)
            {
                ground.transform.localScale *= field.BackgroundScale;
                ground.transform.localPosition = new Vector3(field.BackgroundOffset.x * LocationVisualDefinition.WorldWidth,
                    -field.BackgroundOffset.y * LocationVisualDefinition.WorldHeight, 0);
            }
            foreach (LocationVisualObject item in definition.Objects) AddObject(item);
            animations = Resources.Load<CreatureAnimationDatabaseAsset>(CreatureAnimationDatabaseAsset.ResourcesPath);
            SetTime(13, 0);
        }

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
        private void AddObject(LocationVisualObject item)
        {
            if (item == null || item.Hidden) return;
            Transform anchor = Child(item.Name).transform;
            anchor.localPosition = LocationVisualGeometry.ToWorld(item.Position);
            GameObject imageObject = new GameObject("Рисунок");
            imageObject.transform.SetParent(anchor, false);
            Sprite sprite = item.ResolveSprite();
            if (sprite == null) sprite = PlaceholderSprite(item.Placeholder);
            SpriteRenderer image = Image(imageObject, sprite, item.Placeholder == LocationPlaceholder.Fire);
            Fit(image, item.Height, item.Pivot, item.FlipX, Vector2.zero);
            image.sortingOrder = LocationVisualGeometry.SortOrder(item.Band, anchor.localPosition.y, item.OrderOffset);
            if (item.CastsShadow)
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
            Light2D light = null;
            if (item.Light.Enabled)
            {
                GameObject source = new GameObject("Источник света");
                source.transform.SetParent(anchor, false);
                source.transform.localPosition = item.Light.Offset;
                light = source.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;
                light.pointLightOuterAngle = 360;
                light.pointLightInnerAngle = 360;
                light.pointLightOuterRadius = item.Light.Radius;
                light.pointLightInnerRadius = item.Light.Radius * (1 - item.Light.Softness);
                light.shadowsEnabled = item.Light.Shadows;
                light.shadowIntensity = item.Light.ShadowStrength;
                light.shadowSoftness = item.Light.ShadowSoftness;
            }
            placed.Add(new Placed { Data = item, Anchor = anchor, Image = image, Light = light });
        }

        public void SetTime(float hours, float seconds)
        {
            Hour = Mathf.Repeat(hours, 24);
            GlobalLight.color = Definition.Daylight.EvaluateColor(Hour);
            GlobalLight.intensity = Definition.Daylight.Evaluate(Hour);
            foreach (Placed item in placed)
            {
                if (item.Light == null) continue;
                item.Light.enabled = item.Data.Light.ActiveAt(Hour);
                item.Light.color = item.Data.Light.Color;
                float wave = Mathf.PerlinNoise(seconds * 1.8f, item.Data.Position.x * 31);
                item.Light.intensity = item.Data.Light.Intensity * (1 + (wave * 2 - 1) * item.Data.Light.Flicker);
            }
        }

        public void AddTestActors(int count)
        {
            UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            UnitDefinitionData unit = null;
            if (units != null)
                foreach (UnitDefinitionData candidate in units.Units)
                    if (candidate.Id == Definition.TestUnitId) { unit = candidate; break; }
            for (int i = 0; i < count; i++)
            {
                Transform anchor = Child(i == 0 ? "Командир" : "Спутник " + i).transform;
                GameObject imageObject = new GameObject("Персонаж");
                imageObject.transform.SetParent(anchor, false);
                Actor actor = new Actor { Anchor = anchor, Unit = unit,
                    Image = Image(imageObject, unit?.BattlefieldSprite ?? PlaceholderActor(), false) };
                CreatureAnimationSetData set = animations?.FindSet(unit?.AnimationSetId);
                if (set != null && set.HasAnyFrames)
                    actor.Player = new CreatureAnimationPlayer(set, CreatureAnimationDirection.Front, i * .3f);
                actor.Image.color = i == 0 ? Color.white : new Color(.87f, .86f, .78f);
                actors.Add(actor);
                SpriteRenderer shadow = Image(new GameObject("Тень под ногами"), PlaceholderEllipse(), false);
                shadow.transform.SetParent(anchor, false);
                shadow.transform.localScale = new Vector3(.65f, .19f, 1);
                shadow.color = new Color(0, 0, 0, .35f);
                shadow.sortingOrder = -24000;
            }
        }

        public void RenderActors(IReadOnlyList<LocalPartyMover.Member> members, float seconds)
        {
            float defaultHeight = LocationVisualGeometry.Layout(Field).Size * 1.35f;
            for (int i = 0; i < actors.Count && i < members.Count; i++)
            {
                Actor actor = actors[i];
                LocalPartyMover.Member member = members[i];
                actor.Anchor.localPosition = Vector2.Lerp(LocationVisualGeometry.CellPosition(Field, member.Cell),
                    LocationVisualGeometry.CellPosition(Field, member.Next), member.Stepping ? member.Progress : 0);
                float height = defaultHeight * (actor.Unit?.BattlefieldScale ?? 1);
                Vector2 pivot = new Vector2(.5f, .15f), offset = Vector2.zero;
                if (actor.Player != null)
                {
                    actor.Player.SetDirection(animations.GetDirection((HexFacing)member.Facing));
                    CreatureAnimationAction action = member.Stepping ? CreatureAnimationAction.Walk : CreatureAnimationAction.Idle;
                    if (actor.Player.Action != action) actor.Player.Play(action, seconds);
                    actor.Image.sprite = actor.Player.Evaluate(seconds) ?? actor.Player.Set.FindFirstFrame();
                    height = defaultHeight * actor.Player.Set.FieldScale;
                    pivot = actor.Player.Set.Pivot;
                    offset = actor.Player.Clip?.Offset ?? Vector2.zero;
                }
                Fit(actor.Image, height, pivot, false, offset);
                actor.Image.sortingOrder = LocationVisualGeometry.SortOrder(LocationVisualBand.World, actor.Anchor.localPosition.y);
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
                ((flip ? 1 - originalPivot.x : originalPivot.x) - pivot.x + offset.x) * width,
                (originalPivot.y - pivot.y + offset.y) * height);
            image.flipX = flip;
        }

        public SpriteRenderer FindObject(string id) => placed.Find(item => item.Data.Id == id)?.Image;
        // Смена состояния одного объекта, без пересборки фона и без изменения авторских данных.
        public bool SetObjectVariant(string objectId, string variantId)
        {
            Placed item = placed.Find(entry => entry.Data.Id == objectId);
            LocationVisualVariant variant = item?.Data.Variants?.Find(entry => entry.Id == variantId);
            if (variant?.Sprite == null) return false;
            item.Image.sprite = variant.Sprite;
            Fit(item.Image, item.Data.Height, item.Data.Pivot, item.Data.FlipX, Vector2.zero);
            return true;
        }
        public void Dispose()
        {
            Destroy(Root);
            foreach (UnityEngine.Object item in owned) Destroy(item);
            owned.Clear();
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
        private Sprite PlaceholderActor() => MakeSprite(64, 100, (x, y) =>
        {
            if (Vector2.Distance(new Vector2(x, y), new Vector2(.5f, .80f)) < .14f) return new Color(.72f, .59f, .43f);
            if (y > .23f && y < .70f && Mathf.Abs(x - .5f) < .20f) return new Color(.43f, .45f, .35f);
            if (y > .04f && y < .28f && (Mathf.Abs(x - .37f) < .08f || Mathf.Abs(x - .63f) < .08f)) return new Color(.25f, .26f, .23f);
            return Color.clear;
        });
        private Sprite PlaceholderSprite(LocationPlaceholder kind, bool ground = false) => MakeSprite(128, 96, (x, y) =>
        {
            if (ground) return Color.white;
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
