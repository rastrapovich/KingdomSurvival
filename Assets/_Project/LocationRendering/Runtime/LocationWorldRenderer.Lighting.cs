using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: свет места. Общий свет — по суткам (улица) или постоянный
    // (под крышей); источники — все виды 2D-света Unity (точка, конус,
    // произвольная форма, свет-рисунок) с жизнью огня, свечением воздуха,
    // нормалями и перекрытием предметами. Тень-силуэт от солнца рисует
    // LocationProjectedShadow (у общего света 2D Unity теней нет); тени от
    // огня и других источников — перекрытие света по контуру рисунка
    // (ShadowCaster2D): тёмный конус до края радиуса. Обработка кадра — URP
    // Volume своего слоя.
    public sealed partial class LocationWorldRenderer
    {
        // Слой обработки кадра места: камера сцены глобальной карты его не видит.
        public const int PostVolumeLayer = 31;
        private const int ShadowSortingOrder = -24500;
        // Мерцание огня меняет темноту тени только наполовину: иначе тень дёргается.
        private const float FlickerShadowShare = .5f;

        private sealed class Caster
        {
            public Transform Anchor;
            public SpriteRenderer Source;
            public Sprite Override;
            public float OverrideHeight;
            public Vector2 OverridePivot;
            public bool OverrideFlip;
            public float LengthScale;
            public bool People;
            public bool Battle;
            public bool Disabled;
            // Тень-силуэт от солнца (одна).
            public readonly SpriteRenderer[] Shadows = new SpriteRenderer[1];
            // Перекрытие света огня по контуру рисунка; нет — предмет сам светит.
            public ShadowCaster2D Contour;
            public Sprite ContourSprite;
            public bool ContourFlip;
            public float ContourBand;
        }


        private readonly List<Caster> casters = new List<Caster>();
        private Material shadowMaterial;
        private MaterialPropertyBlock shadowBlock;
        private Volume postVolume;
        private VolumeProfile postProfile;
        private Vector2 sunRaw;
        private float sunOpacity;

        private static readonly int ShadowVectorId = Shader.PropertyToID("_ShadowVector");
        private static readonly int GroundYId = Shader.PropertyToID("_GroundY");
        private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
        private static readonly int BlurId = Shader.PropertyToID("_Blur");
        private static readonly int UVRectId = Shader.PropertyToID("_UVRect");
        private static readonly FieldInfo NormalQualityField =
            typeof(Light2D).GetField("m_NormalMapQuality", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo LightLateUpdate =
            typeof(Light2D).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        // Вне Play Mode (окна баз, сцена предпросмотра) Unity не вызывает
        // LateUpdate у Light2D: его геометрия и границы не пересчитываются, и
        // URP отсекает местный свет целиком — в окне не видно ни огня, ни
        // нормалей. Пересчёт вручную перед кадром; в игре не нужен.
        public void RefreshLightsOutsidePlay()
        {
            if (Application.isPlaying || Root == null) return;
            // Перекрытие света: форма и группа теней обновляются в Update.
            foreach (ShadowCaster2D contour in Root.GetComponentsInChildren<ShadowCaster2D>())
                if (contour != null && contour.enabled) contour.Update();
            if (LightLateUpdate == null) return;
            foreach (Light2D light in Root.GetComponentsInChildren<Light2D>())
                if (light != null && light.lightType != Light2D.LightType.Global) LightLateUpdate.Invoke(light, null);
        }

        private static readonly FieldInfo NormalDistanceField =
            typeof(Light2D).GetField("m_NormalMapDistance", BindingFlags.Instance | BindingFlags.NonPublic);

        // Тень от солнца сейчас (для окна базы): направление на экране и длина.
        public Vector2 SunShadowVector => sunRaw;
        public float SunShadowOpacity => sunOpacity;
        // Доля дня 0..1 (по кривой яркости) — для обработки кадра.
        public float DayFactor { get; private set; } = 1;
        public bool Indoor => Definition.Lighting == LocationLightingMode.Indoor;

        private void InitializeLighting()
        {
            Shader shader = Resources.Load<Shader>("LocationRendering/LocationProjectedShadow");
            if (shader != null)
                shadowMaterial = Own(new Material(shader) { name = "Тень-силуэт места" });
            shadowBlock = new MaterialPropertyBlock();

            GameObject volumeObject = Child("Обработка кадра");
            volumeObject.layer = PostVolumeLayer;
            postVolume = volumeObject.AddComponent<Volume>();
            postVolume.isGlobal = true;
            postVolume.priority = 10;
            postProfile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            postProfile.Add<ColorAdjustments>(true);
            postProfile.Add<WhiteBalance>(true);
            postProfile.Add<Bloom>(true);
            postProfile.Add<Vignette>(true);
            postProfile.Add<FilmGrain>(true);
            postProfile.Add<Tonemapping>(true);
            postVolume.sharedProfile = postProfile;
            UniversalAdditionalCameraData data = Camera.GetUniversalAdditionalCameraData();
            data.volumeLayerMask = 1 << PostVolumeLayer;
        }

        // Источник любого вида по данным предмета.
        private Light2D CreateLight(Transform anchor, LocationLightDefinition data)
        {
            GameObject source = new GameObject("Источник света");
            source.transform.SetParent(anchor, false);
            source.transform.localPosition = data.Offset;
            Light2D light = source.AddComponent<Light2D>();
            ConfigureLight(light, data);
            return light;
        }

        public static void ConfigureLight(Light2D light, LocationLightDefinition data)
        {
            switch (data.Shape)
            {
                case LocationLightShape.Freeform:
                    light.lightType = Light2D.LightType.Freeform;
                    List<Vector3> path = new List<Vector3>();
                    foreach (Vector2 point in data.FreeformPoints ?? new List<Vector2>()) path.Add(point);
                    if (path.Count >= 3) light.SetShapePath(path.ToArray());
                    light.shapeLightFalloffSize = Mathf.Max(0, data.FreeformFalloff);
                    break;
                case LocationLightShape.Sprite:
                    light.lightType = Light2D.LightType.Sprite;
                    light.lightCookieSprite = data.Cookie;
                    if (data.Cookie != null)
                    {
                        Vector2 size = data.Cookie.bounds.size;
                        light.transform.localScale = new Vector3(data.CookieSize.x / Mathf.Max(.001f, size.x),
                            data.CookieSize.y / Mathf.Max(.001f, size.y), 1);
                    }
                    break;
                default:
                    light.lightType = Light2D.LightType.Point;
                    bool spot = data.Shape == LocationLightShape.Spot;
                    light.pointLightOuterAngle = spot ? Mathf.Clamp(data.OuterAngle, 1, 360) : 360;
                    light.pointLightInnerAngle = spot ? Mathf.Clamp(data.InnerAngle, 0, light.pointLightOuterAngle) : 360;
                    light.pointLightOuterRadius = Mathf.Max(.01f, data.Radius);
                    light.pointLightInnerRadius = data.Radius * (1 - Mathf.Clamp01(data.Softness));
                    // Конус Unity смотрит вверх; направление 0 — вправо.
                    light.transform.localRotation = spot ? Quaternion.Euler(0, 0, data.Direction - 90) : Quaternion.identity;
                    break;
            }
            light.color = data.Color;
            light.intensity = data.Intensity;
            light.falloffIntensity = Mathf.Clamp01(data.Falloff);
            light.shadowsEnabled = data.Shadows;
            light.shadowIntensity = data.ShadowStrength;
            light.shadowSoftness = data.ShadowSoftness;
            light.shadowSoftnessFalloffIntensity = Mathf.Clamp01(data.ShadowSoftnessFalloff);
            light.volumetricEnabled = data.Volumetric;
            light.volumeIntensity = data.VolumeIntensity;
            light.volumetricShadowsEnabled = data.Volumetric && data.VolumetricShadows;
            light.shadowVolumeIntensity = data.VolumeShadowIntensity;
            light.blendStyleIndex = Mathf.Clamp(data.BlendStyle, 0, 3);
            light.overlapOperation = data.AlphaOverlap ? Light2D.OverlapOperation.AlphaBlend : Light2D.OverlapOperation.Additive;
            light.lightOrder = data.Order;
            // Нормали: в URP 17 у этих полей нет публичной записи.
            NormalQualityField?.SetValue(light, !data.NormalMaps ? Light2D.NormalMapQuality.Disabled
                : data.NormalMapsAccurate ? Light2D.NormalMapQuality.Accurate : Light2D.NormalMapQuality.Fast);
            NormalDistanceField?.SetValue(light, Mathf.Max(.01f, data.NormalMapDistance));
        }

        private void ApplyLighting()
        {
            float hour = Hour;
            if (Indoor)
            {
                GlobalLight.color = Definition.IndoorColor;
                GlobalLight.intensity = Definition.IndoorIntensity;
                DayFactor = 1;
                sunOpacity = 0;
                sunRaw = Vector2.zero;
            }
            else
            {
                GlobalLight.color = Sky.Daylight.EvaluateColor(hour);
                GlobalLight.intensity = Sky.Daylight.Evaluate(hour);
                DayFactor = Mathf.Clamp01(Sky.Daylight.Brightness.Evaluate(hour));
                Sky.Sun.Evaluate(hour, out Vector2 direction, out float length, out float opacity);
                sunRaw = direction * length;
                sunOpacity = opacity;
            }

            foreach (Placed item in placed)
            {
                if (item.Light == null) continue;
                LocationLightDefinition data = item.Data.Light;
                bool on = data.ActiveAt(hour);
                item.Light.enabled = on;
                item.Light.color = data.Color;
                float intensity = data.AnimatedIntensity(Seconds, item.Data.Position.x);
                item.Light.intensity = intensity;
                // Днём своя тень огня слабее: её перебивает общий свет.
                float flicker = Mathf.Lerp(1, Mathf.Clamp01(intensity / Mathf.Max(.01f, data.Intensity)), FlickerShadowShare);
                UpdateRadialShadows(item, on ? flicker * (Indoor ? 1 : Mathf.Lerp(1, .25f, DayFactor)) : 0);
            }
            ApplyPost();
            UpdateShadows();
            RefreshLightsOutsidePlay();
        }

        // ------------------------------------------------------------------
        // Тени-силуэты
        // ------------------------------------------------------------------

        private Caster AddCaster(Transform anchor, SpriteRenderer source, Sprite overrideSprite, float height, Vector2 pivot,
            bool flip, float lengthScale, string battleId, bool people = false, bool contour = true)
        {
            if (shadowMaterial == null || source == null) return null;
            Caster caster = new Caster
            {
                Anchor = anchor, Source = source, Override = overrideSprite, OverrideHeight = height,
                OverridePivot = pivot, OverrideFlip = flip, LengthScale = lengthScale, People = people,
                Battle = battleId != null
            };
            if (contour) caster.Contour = AddContour(source);
            casters.Add(caster);
            return caster;
        }

        private void UpdateShadows()
        {
            if (shadowMaterial == null) return;
            for (int i = casters.Count - 1; i >= 0; i--)
            {
                Caster caster = casters[i];
                if (caster.Anchor == null || caster.Source == null)
                {
                    casters.RemoveAt(i);
                    continue;
                }
                bool enabled = !caster.Disabled && caster.Source.enabled && caster.Source.gameObject.activeInHierarchy &&
                               (!caster.People || Sky.PeopleCastShadows);
                float scale = caster.People ? Sky.PeopleShadowLength : caster.LengthScale;

                // Солнце (или луна) — тень-силуэт.
                CastShadow(caster, 0, enabled && sunOpacity > .001f, sunRaw * scale, Sky.Sun.Color, sunOpacity,
                    Sky.Sun.Softness);
                // Огонь и другие источники — тень по контуру рисунка до края света.
                UpdateContour(caster, enabled);
            }
        }

        // Тень-силуэт от солнца: гаснет, когда свет уходит вбок
        // (плоский рисунок сбоку дал бы линию).
        private void CastShadow(Caster caster, int index, bool visible, Vector2 vector, Color color, float opacity, float softness)
        {
            LocationShadowStyle style = Sky.ShadowStyle;
            float share = style.SilhouetteShare(vector);
            SetShadow(caster, index, visible, vector, color, opacity * share * Mathf.Max(0, style.SilhouetteOpacity), softness);
        }

        // ------------------------------------------------------------------
        // Тени от огня: перекрытие света по контуру рисунка
        // ------------------------------------------------------------------

        // 2D-свет Unity сам даёт от контура тёмный конус до края своего радиуса
        // (у источника — «Тени предметов и людей»). Контуры — по альфа-каналу
        // рисунка (LocationAlphaContours): свет проходит сквозь прозрачное —
        // между ногами, под рукой. Не вышло — выпуклая оболочка формы. Сам
        // предмет своей тенью не темнеет. Готовые контуры URP 17 принимает
        // только от своих источников формы, поэтому форма тени задаётся
        // напрямую (закрытые поля), а свой источник формы отключён.
        private static readonly FieldInfo ContourMeshField =
            typeof(ShadowCaster2D).GetField("m_ShadowMesh", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ContourProviderField =
            typeof(ShadowCaster2D).GetField("m_ShadowShape2DProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ContourComponentField =
            typeof(ShadowCaster2D).GetField("m_ShadowShape2DComponent", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ContourSourceProperty =
            typeof(ShadowCaster2D).GetProperty("shadowCastingSource", BindingFlags.Instance | BindingFlags.NonPublic);

        private static ShadowCaster2D AddContour(SpriteRenderer source)
        {
            if (ContourMeshField == null || ContourSourceProperty == null) return null;
            ShadowCaster2D contour = source.gameObject.GetComponent<ShadowCaster2D>();
            if (contour == null) contour = source.gameObject.AddComponent<ShadowCaster2D>();
            // Редактор Unity сам цепляет форму от сетки спрайта — отцепить
            // (выключение отписывает её от смены рисунка) и включить снова.
            contour.enabled = false;
            ContourProviderField?.SetValue(contour, null);
            ContourComponentField?.SetValue(contour, null);
            ContourSourceProperty.SetValue(contour, Enum.ToObject(ContourSourceProperty.PropertyType, 2));
            contour.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            contour.useRendererSilhouette = true;
            contour.enabled = true;
            return contour;
        }

        private void UpdateContour(Caster caster, bool enabled)
        {
            ShadowCaster2D contour = caster.Contour;
            if (contour == null) return;
            Sprite sprite = caster.Source.sprite;
            enabled &= sprite != null;
            if (contour.enabled != enabled) contour.enabled = enabled;
            LocationShadowStyle style = Sky.ShadowStyle;
            float band = Mathf.Clamp(caster.People ? style.FireBlockPeople : style.FireBlockObjects, .05f, 1);
            if (!enabled || (sprite == caster.ContourSprite && caster.Source.flipX == caster.ContourFlip && band == caster.ContourBand)) return;
            caster.ContourSprite = sprite;
            caster.ContourFlip = caster.Source.flipX;
            caster.ContourBand = band;
            SetContourShape(contour, SpriteContours(sprite, band), caster.ContourFlip);
        }

        // Контуры рисунка (нижняя доля band высоты): по альфе; не вышло — один выпуклый.
        public static List<Vector2[]> SpriteContours(Sprite sprite, float band = 1)
        {
            List<Vector2[]> loops = LocationAlphaContours.Of(sprite, band);
            if (loops.Count > 0) return loops;
            Vector3[] path = ContourPath(sprite, false);
            Vector2[] loop = path.Select(point => (Vector2)point).ToArray();
            if (LocationAlphaContours.Area(loop) < 0) Array.Reverse(loop);
            return new List<Vector2[]> { loop };
        }

        // Контуры — набором отрезков: внешние против часовой стрелки, дырки по
        // часовой. Отражённый рисунок — отражённые контуры, обход обратный.
        private static void SetContourShape(ShadowCaster2D contour, List<Vector2[]> loops, bool flip)
        {
            if (!(ContourMeshField.GetValue(contour) is ShadowShape2D shape)) return;
            int count = loops.Sum(loop => loop.Length);
            if (count < 3) return;
            NativeArray<Vector3> vertices = new NativeArray<Vector3>(count, Allocator.Temp);
            NativeArray<int> indices = new NativeArray<int>(count * 2, Allocator.Temp);
            int index = 0;
            foreach (Vector2[] loop in loops)
            {
                int start = index;
                for (int i = 0; i < loop.Length; i++, index++)
                {
                    vertices[index] = new Vector3(flip ? -loop[i].x : loop[i].x, loop[i].y, 0);
                    indices[index * 2] = index;
                    indices[index * 2 + 1] = i == loop.Length - 1 ? start : index + 1;
                }
            }
            shape.SetShape(vertices, indices, ShadowShape2D.OutlineTopology.Lines,
                flip ? ShadowShape2D.WindingOrder.Clockwise : ShadowShape2D.WindingOrder.CounterClockwise, false);
            vertices.Dispose();
            indices.Dispose();
        }

        // Контур рисунка в его единицах (от опоры спрайта): одна форма
        // physics shape — как есть; несколько или нет — выпуклая оболочка.
        public static Vector3[] ContourPath(Sprite sprite, bool flip)
        {
            List<Vector2> points = new List<Vector2>();
            int shapes = sprite.GetPhysicsShapeCount();
            if (shapes == 1)
                sprite.GetPhysicsShape(0, points);
            else
            {
                List<Vector2> shape = new List<Vector2>();
                for (int i = 0; i < shapes; i++)
                {
                    sprite.GetPhysicsShape(i, shape);
                    points.AddRange(shape);
                }
                if (points.Count < 3) points.AddRange(sprite.vertices);
                points = ConvexHull(points);
            }
            Vector3[] path = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                // Отражённый рисунок — отражённый контур; обход — в прежнюю сторону.
                Vector2 point = points[flip ? points.Count - 1 - i : i];
                path[i] = new Vector3(flip ? -point.x : point.x, point.y, 0);
            }
            return path;
        }

        public static List<Vector2> ConvexHull(List<Vector2> points)
        {
            List<Vector2> sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (sorted.Count < 3) return sorted;
            List<Vector2> hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                IEnumerable<Vector2> order = pass == 0 ? sorted : Enumerable.Reverse(sorted);
                foreach (Vector2 point in order)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        // Сколько предметов и людей сейчас перекрывают свет огня (для проверки).
        public int ContourCount => casters.Count(caster => caster.Contour != null && caster.Contour.enabled);

        private void SetShadow(Caster caster, int index, bool visible, Vector2 vector, Color color, float opacity, float softness)
        {
            SpriteRenderer shadow = caster.Shadows[index];
            if (!visible || opacity <= .001f || vector.sqrMagnitude < 1e-6f)
            {
                if (shadow != null) shadow.enabled = false;
                return;
            }
            if (shadow == null)
            {
                GameObject target = new GameObject(index == 0 ? "Тень от солнца" : "Тень от огня");
                target.transform.SetParent(caster.Anchor, false);
                shadow = target.AddComponent<SpriteRenderer>();
                shadow.sharedMaterial = shadowMaterial;
                shadow.sortingOrder = ShadowSortingOrder;
                caster.Shadows[index] = shadow;
            }
            shadow.enabled = true;
            if (caster.Override != null)
            {
                if (shadow.sprite != caster.Override)
                {
                    shadow.sprite = caster.Override;
                    Fit(shadow, caster.OverrideHeight, caster.OverridePivot, caster.OverrideFlip, Vector2.zero);
                }
            }
            else
            {
                shadow.sprite = caster.Source.sprite;
                shadow.flipX = caster.Source.flipX;
                Transform from = caster.Source.transform;
                if (from.parent == caster.Anchor)
                {
                    shadow.transform.localPosition = from.localPosition;
                    shadow.transform.localScale = from.localScale;
                }
                else
                {
                    shadow.transform.position = from.position;
                    shadow.transform.localScale = from.lossyScale;
                }
            }
            // Блок свойств — с чистого листа: старый блок мог нести текстуру
            // прошлого кадра анимации (другой страницы атласа).
            shadowBlock.Clear();
            Vector4 uvRect = SpriteUVRect(shadow.sprite);
            shadowBlock.SetVector(ShadowVectorId, vector);
            shadowBlock.SetFloat(GroundYId, caster.Anchor.position.y);
            shadowBlock.SetColor(ShadowColorId, new Color(color.r, color.g, color.b, Mathf.Clamp01(opacity)));
            // Размытие — в долях своего кадра, а не всей страницы атласа.
            shadowBlock.SetFloat(BlurId, Mathf.Clamp01(softness) * .05f * (uvRect.w - uvRect.y));
            shadowBlock.SetVector(UVRectId, uvRect);
            shadow.SetPropertyBlock(shadowBlock);
        }

        // ------------------------------------------------------------------
        // Своя тень светящего предмета во все стороны
        // ------------------------------------------------------------------

        private const int RadialLayers = 6;

        // Слои силуэта предмета, растянутые от точки огня на земле (опоры):
        // каждый следующий крупнее и светлее — камни кольца дают тени-лучи
        // наружу, в промежутки между камнями свет проходит. Лежат под
        // предметами, на земле.
        private void BuildRadialShadows(Placed entry)
        {
            if (shadowMaterial == null) return;
            LocationLightDefinition light = entry.Data.Light;
            for (int p = 0; p < entry.Images.Count; p++)
            {
                LocationResolvedPart part = entry.Parts[p];
                if (part.Placeholder || part.Band != LocationVisualBand.World) continue;
                SpriteRenderer image = entry.Images[p];
                for (int i = 1; i <= RadialLayers; i++)
                {
                    float scale = 1 + Mathf.Max(0, light.OwnShadowLength) * i / RadialLayers;
                    GameObject layer = new GameObject("Своя тень огня");
                    layer.transform.SetParent(entry.Anchor, false);
                    layer.transform.localPosition = image.transform.localPosition * scale;
                    layer.transform.localScale = image.transform.localScale * scale;
                    SpriteRenderer shadow = layer.AddComponent<SpriteRenderer>();
                    shadow.sprite = image.sprite;
                    shadow.flipX = image.flipX;
                    shadow.sharedMaterial = shadowMaterial;
                    shadow.sortingOrder = ShadowSortingOrder;
                    entry.RadialShadows.Add(shadow);
                }
            }
        }

        // share — 0..1: огонь горит, мерцание, день / ночь.
        private void UpdateRadialShadows(Placed entry, float share)
        {
            if (entry.RadialShadows.Count == 0 || shadowBlock == null) return;
            float alpha = Mathf.Clamp01(entry.Data.Light.OwnShadowOpacity) * .35f * Mathf.Clamp01(share);
            foreach (SpriteRenderer shadow in entry.RadialShadows)
            {
                if (shadow == null) continue;
                shadow.enabled = alpha > .001f;
                if (!shadow.enabled) continue;
                shadowBlock.Clear();
                Vector4 uvRect = SpriteUVRect(shadow.sprite);
                // Без проекции: силуэт как есть (высота — от «земли» далеко внизу).
                shadowBlock.SetVector(ShadowVectorId, new Vector4(0, 1, 0, 0));
                shadowBlock.SetFloat(GroundYId, -100000);
                shadowBlock.SetColor(ShadowColorId, new Color(0, 0, 0, alpha));
                shadowBlock.SetFloat(BlurId, .02f * (uvRect.w - uvRect.y));
                shadowBlock.SetVector(UVRectId, uvRect);
                shadow.SetPropertyBlock(shadowBlock);
            }
        }

        // Прямоугольник кадра в UV его текстуры (u, v мин; u, v макс).
        private static Vector4 SpriteUVRect(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return new Vector4(0, 0, 1, 1);
            Rect rect;
            try { rect = sprite.textureRect; }
            catch (System.Exception) { rect = sprite.rect; }
            float w = sprite.texture.width, h = sprite.texture.height;
            return new Vector4(rect.xMin / w, rect.yMin / h, rect.xMax / w, rect.yMax / h);
        }

        // Сколько сейчас видимых теней (для проверки и окна базы).
        public int VisibleShadowCount
        {
            get
            {
                int count = 0;
                foreach (Caster caster in casters)
                    foreach (SpriteRenderer shadow in caster.Shadows)
                        if (shadow != null && shadow.enabled) count++;
                return count;
            }
        }

        // ------------------------------------------------------------------
        // Обработка кадра
        // ------------------------------------------------------------------

        private void ApplyPost()
        {
            LocationPostEffects post = Sky.Post;
            bool enabled = post != null && post.Enabled;
            Camera.GetUniversalAdditionalCameraData().renderPostProcessing = enabled;
            postVolume.enabled = enabled;
            if (!enabled) return;

            LocationColorGrade grade = LocationColorGrade.Lerp(post.Night, post.Day, DayFactor);
            if (postProfile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.Override(grade.Exposure);
                color.contrast.Override(Mathf.Clamp(grade.Contrast, -100, 100));
                color.saturation.Override(Mathf.Clamp(grade.Saturation, -100, 100));
                color.colorFilter.Override(grade.Filter);
            }
            if (postProfile.TryGet(out WhiteBalance balance))
            {
                balance.temperature.Override(Mathf.Clamp(grade.Temperature, -100, 100));
                balance.tint.Override(Mathf.Clamp(grade.Tint, -100, 100));
            }
            if (postProfile.TryGet(out Bloom bloom))
            {
                bloom.active = post.Bloom;
                bloom.intensity.Override(Mathf.Max(0, post.BloomIntensity));
                bloom.threshold.Override(Mathf.Max(0, post.BloomThreshold));
                bloom.scatter.Override(Mathf.Clamp01(post.BloomScatter));
                bloom.tint.Override(post.BloomTint);
            }
            if (postProfile.TryGet(out Vignette vignette))
            {
                vignette.active = post.Vignette;
                vignette.intensity.Override(Mathf.Clamp01(post.VignetteIntensity));
                vignette.smoothness.Override(Mathf.Clamp(post.VignetteSmoothness, .01f, 1));
                vignette.color.Override(post.VignetteColor);
            }
            if (postProfile.TryGet(out FilmGrain grain))
            {
                grain.active = post.Grain > .001f;
                grain.intensity.Override(Mathf.Clamp01(post.Grain));
            }
            if (postProfile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping.active = post.Tonemapping != LocationTonemapping.None;
                tonemapping.mode.Override(post.Tonemapping == LocationTonemapping.ACES ? TonemappingMode.ACES : TonemappingMode.Neutral);
            }
        }
    }
}
