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
        // Стиль смешивания 2D-света для местных источников с «обычным» смешиванием.
        public const int LocalLightBlendStyle = 2;
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
            // Тень-силуэт от солнца есть (часть ассета: «Тень от солнца»).
            public bool Sun = true;
            // Тень-силуэт от солнца (одна).
            public readonly SpriteRenderer[] Shadows = new SpriteRenderer[1];
            // Перекрытие света огня по контуру рисунка; нет — предмет сам светит.
            public ShadowCaster2D Contour;
            public Sprite ContourSprite;
            public bool ContourFlip;
            public float ContourBand;
            // Что уже записано в блок свойств тени: без изменений — не трогать (сотни предметов за кадр).
            public bool ShadowShown;
            public Sprite ShadowSprite;
            public Vector2 ShadowVector;
            public float ShadowGroundY, ShadowOpacity, ShadowSoftness;
            public Color ShadowColor;
            // Окклюзия: пятно на земле по основанию и затемнение низа рисунка.
            public SpriteRenderer Occlusion;
            public SpriteRenderer OcclusionOverlay;
            public List<Rect> Footprint;
            // Предмет со своим светом (костёр) и люди окклюзии не дают.
            public bool NoOcclusion;
            public List<Rect> FallbackFootprint;
            public Vector4 OcclusionBase, OcclusionShape;
            // Пятно уже построено для этой формы, радиуса и размытия — не искать заново.
            public List<Rect> OcclusionFor;
            public float OcclusionRadius = -1, OcclusionBlur = -1;
            public Sprite OcclusionSprite;
        }

        // Местный источник, дающий тени по контуру: где он светит.
        private struct ShadowLight
        {
            public Vector2 Position;
            public float Radius;
            // Доля силы огня (мерцание, день) 0..1.
            public float Weight;
        }

        private readonly List<ShadowLight> shadowLights = new List<ShadowLight>();
        private bool anyShadowLight;
        private ShadowCaster2D[] cachedContours = Array.Empty<ShadowCaster2D>();
        private Light2D[] cachedLights = Array.Empty<Light2D>();
        private int cachedHierarchy = -1;


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
        // Списки компонентов кэшируются: поиск по сотням предметов каждый кадр
        // дорог; состав сцены меняется — меняется число узлов иерархии.
        public void RefreshLightsOutsidePlay()
        {
            if (Application.isPlaying || Root == null) return;
            int hierarchy = Root.transform.hierarchyCount;
            if (hierarchy != cachedHierarchy)
            {
                cachedHierarchy = hierarchy;
                cachedContours = Root.GetComponentsInChildren<ShadowCaster2D>();
                cachedLights = Root.GetComponentsInChildren<Light2D>();
            }
            // Перекрытие света: форма и группа теней обновляются в Update —
            // только если есть местный свет с тенями (иначе контуры выключены).
            if (anyShadowLight)
                foreach (ShadowCaster2D contour in cachedContours)
                    if (contour != null && contour.enabled) contour.Update();
            if (LightLateUpdate == null) return;
            foreach (Light2D light in cachedLights)
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
            BuildShadowResolve();

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
            // Тень перекрывает свет целиком: частичная пропускала свет огня
            // сквозь предметы, и там, где тени предметов и оснований
            // накладываются по-разному, у предмета в тени светлел ореол.
            // Глубину тени даёт окклюзия у основания (стиль теней).
            light.shadowIntensity = 1;
            light.shadowSoftness = data.ShadowSoftness;
            light.shadowSoftnessFalloffIntensity = Mathf.Clamp01(data.ShadowSoftnessFalloff);
            light.volumetricEnabled = data.Volumetric;
            light.volumeIntensity = data.VolumeIntensity;
            light.volumetricShadowsEnabled = data.Volumetric && data.VolumetricShadows;
            light.shadowVolumeIntensity = data.VolumeShadowIntensity;
            // Стиль 0 — только солнце (общий свет): местный «обычный» свет —
            // в стиле 2 (умножение с маской, без маски — как стиль 0), чтобы
            // рисунок мог брать солнце и огонь по отдельности (LocationSpriteAdjust).
            int style = Mathf.Clamp(data.BlendStyle, 0, 3);
            light.blendStyleIndex = style == 0 ? LocalLightBlendStyle : style;
            light.overlapOperation = data.AlphaOverlap ? Light2D.OverlapOperation.AlphaBlend : Light2D.OverlapOperation.Additive;
            light.lightOrder = data.Order;
            // Нормали: в URP 17 у этих полей нет публичной записи.
            NormalQualityField?.SetValue(light, !data.NormalMaps ? Light2D.NormalMapQuality.Disabled
                : data.NormalMapsAccurate ? Light2D.NormalMapQuality.Accurate : Light2D.NormalMapQuality.Fast);
            NormalDistanceField?.SetValue(light, Mathf.Max(.01f, data.NormalMapDistance));
        }

        // Тени на земле без наложения (LocationShadowResolve): перед тенями —
        // «свет не закрыт» в альфе кадра, после них — одно затемнение земли
        // по самой тёмной тени в точке. Прямоугольники во весь рисунок с запасом.
        private Material shadowApply;
        private static readonly int ShadowTintId = Shader.PropertyToID("_ShadowTint");

        private void BuildShadowResolve()
        {
            Shader shader = Resources.Load<Shader>("LocationRendering/LocationShadowResolve");
            if (shader == null) return;
            Material clear = Own(new Material(shader) { name = "Тени · начало" });
            clear.SetFloat("_KsColorMask", (float)ColorWriteMask.Alpha);
            clear.SetFloat("_KsSrc", (float)BlendMode.One);
            clear.SetFloat("_KsDst", (float)BlendMode.Zero);
            shadowApply = Own(new Material(shader) { name = "Тени · затемнение" });
            shadowApply.SetFloat("_KsColorMask", (float)ColorWriteMask.All);
            shadowApply.SetFloat("_KsSrc", (float)BlendMode.OneMinusDstAlpha);
            shadowApply.SetFloat("_KsDst", (float)BlendMode.DstAlpha);
            Texture2D white = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Тени · прямоугольник" });
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            white.SetPixels(pixels);
            white.Apply();
            Sprite sprite = Own(Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4, 0, SpriteMeshType.FullRect));
            Vector2 cover = LocationVisualGeometry.WorldSize(Location) * 6;
            void Quad(string name, Material material, int order)
            {
                SpriteRenderer quad = Child(name).AddComponent<SpriteRenderer>();
                quad.sprite = sprite;
                quad.sharedMaterial = material;
                quad.sortingOrder = order;
                quad.transform.localScale = new Vector3(cover.x, cover.y, 1);
            }
            Quad("Тени · начало", clear, ShadowSortingOrder - 1);
            Quad("Тени · затемнение", shadowApply, ShadowSortingOrder + 1);
        }

        private void ApplyLighting()
        {
            // Цвет, к которому темнеет земля в тени (общий для всех теней).
            if (shadowApply != null) shadowApply.SetColor(ShadowTintId, Sky.Sun.Color);
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
                // Точка огня дрожит; тени от этого света ходят вместе с ней.
                item.Light.transform.localPosition = data.AnimatedOffset(Seconds, item.Data.Position.x);
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
            bool flip, float lengthScale, string battleId, bool people = false, bool contour = true, bool sun = true,
            List<Rect> footprint = null, bool occlusion = true)
        {
            if (shadowMaterial == null || source == null) return null;
            Caster caster = new Caster
            {
                Anchor = anchor, Source = source, Override = overrideSprite, OverrideHeight = height,
                OverridePivot = pivot, OverrideFlip = flip, LengthScale = lengthScale, People = people,
                Battle = battleId != null, Sun = sun, Footprint = footprint, NoOcclusion = !occlusion || people
            };
            if (contour) caster.Contour = AddContour(source);
            casters.Add(caster);
            return caster;
        }

        // Замер: сколько всего ушло на обновление теней (мс, с начала).
        public double ShadowMilliseconds { get; private set; }
        private readonly System.Diagnostics.Stopwatch shadowWatch = new System.Diagnostics.Stopwatch();

        private void UpdateShadows()
        {
            if (shadowMaterial == null) return;
            shadowWatch.Restart();
            CollectShadowLights();
            occludedAnchors.Clear();
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
                CastShadow(caster, 0, enabled && caster.Sun && sunOpacity > .001f, sunRaw * scale, Sky.Sun.Color, sunOpacity,
                    Sky.Sun.Softness);
                // Огонь и другие источники — тень по контуру рисунка до края света;
                // вдали от всех таких источников контур выключен (его тени не видно).
                UpdateContour(caster, enabled && NearShadowLight(caster));
                // Пятно на земле — одно на предмет (части — одна опора); затемнение
                // низа — у каждой части.
                UpdateOcclusion(caster, enabled, enabled && occludedAnchors.Add(caster.Anchor));
            }
            ShadowMilliseconds += shadowWatch.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------------
        // Окклюзия в тени
        // ------------------------------------------------------------------

        private readonly HashSet<Transform> occludedAnchors = new HashSet<Transform>();
        private readonly Dictionary<string, Sprite> occlusionSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private Material occlusionMaterial, occlusionObjectMaterial;
        private MaterialPropertyBlock occlusionBlock;
        private static readonly int OcclusionBaseId = Shader.PropertyToID("_OccBase");
        private static readonly int OcclusionShapeId = Shader.PropertyToID("_OccShape");
        // Пятно на земле сдвинуто в тень на долю радиуса.
        private const float OcclusionShift = .5f;

        // Окклюзия у основания: на земле — по контуру основания предмета
        // (прямоугольник или основание кистью) с мягким спадом наружу на
        // «радиус» и размытием, сдвинутая в тень: от ближайшего огня с
        // тенями, иначе по солнцу; без того и другого — вокруг основания. На
        // самом предмете — затемнение низа рисунка со стороны тени. Только
        // темнит (умножение), не светлит и не перекрашивает.
        private void UpdateOcclusion(Caster caster, bool enabled, bool ground)
        {
            LocationShadowStyle style = Sky.ShadowStyle;
            float strength = Mathf.Clamp01(style.Occlusion);
            float radius = Mathf.Max(0, style.OcclusionRadius);
            enabled &= !caster.NoOcclusion && strength > .001f && radius > 1e-3f && OcclusionMaterial() != null;
            Vector2 direction = enabled ? ShadowDirection(caster) : Vector2.zero;
            List<Rect> footprint = enabled ? Footprint(caster) : null;
            enabled &= footprint != null && footprint.Count > 0;
            UpdateGroundOcclusion(caster, enabled && ground, footprint, radius, strength, direction);
            UpdateObjectOcclusion(caster, enabled, footprint, radius, strength, direction);
        }

        private void UpdateGroundOcclusion(Caster caster, bool enabled, List<Rect> footprint, float radius, float strength, Vector2 direction)
        {
            SpriteRenderer blot = caster.Occlusion;
            if (!enabled)
            {
                if (blot != null && blot.enabled) blot.enabled = false;
                return;
            }
            if (blot == null)
            {
                GameObject target = new GameObject("Окклюзия");
                target.transform.SetParent(caster.Anchor, false);
                blot = target.AddComponent<SpriteRenderer>();
                blot.sharedMaterial = occlusionMaterial;
                blot.sortingOrder = ShadowSortingOrder;
                caster.Occlusion = blot;
            }
            if (!blot.enabled) blot.enabled = true;
            float blur = Mathf.Max(0, Sky.ShadowStyle.OcclusionBlur);
            if (caster.OcclusionSprite == null || caster.OcclusionFor != footprint || caster.OcclusionRadius != radius || caster.OcclusionBlur != blur)
            {
                caster.OcclusionSprite = OcclusionSprite(footprint, radius, blur);
                caster.OcclusionFor = footprint;
                caster.OcclusionRadius = radius;
                caster.OcclusionBlur = blur;
            }
            Sprite shape = caster.OcclusionSprite;
            if (blot.sprite != shape) blot.sprite = shape;
            blot.transform.localPosition = direction * (radius * OcclusionShift);
            blot.color = new Color(0, 0, 0, strength);
        }

        // Низ рисунка темнеет к земле (на высоту полутора радиусов) и к
        // стороне тени: у стороны к огню — светлее, в середине — наполовину.
        private void UpdateObjectOcclusion(Caster caster, bool enabled, List<Rect> footprint, float radius, float strength, Vector2 direction)
        {
            SpriteRenderer overlay = caster.OcclusionOverlay;
            if (!enabled || caster.Source.sprite == null || occlusionObjectMaterial == null)
            {
                if (overlay != null && overlay.enabled) overlay.enabled = false;
                return;
            }
            if (overlay == null)
            {
                GameObject target = new GameObject("Окклюзия на предмете");
                target.transform.SetParent(caster.Source.transform, false);
                // Чуть ближе к камере: рисуется сразу после своего рисунка.
                target.transform.localPosition = new Vector3(0, 0, -.001f);
                overlay = target.AddComponent<SpriteRenderer>();
                overlay.sharedMaterial = occlusionObjectMaterial;
                caster.OcclusionOverlay = overlay;
            }
            if (!overlay.enabled) overlay.enabled = true;
            SpriteRenderer source = caster.Source;
            if (overlay.sprite != source.sprite) overlay.sprite = source.sprite;
            if (overlay.flipX != source.flipX) overlay.flipX = source.flipX;
            if (overlay.sortingOrder != source.sortingOrder) overlay.sortingOrder = source.sortingOrder;
            if (overlay.sortingLayerID != source.sortingLayerID) overlay.sortingLayerID = source.sortingLayerID;
            float halfWidth = radius;
            foreach (Rect cell in footprint)
                // Два аргумента: Max с тремя создаёт массив на каждый вызов (мусор каждый кадр).
                halfWidth = Mathf.Max(halfWidth, Mathf.Max(Mathf.Abs(cell.xMin), Mathf.Abs(cell.xMax)));
            Vector2 foot = caster.Anchor.position;
            Vector4 shapeData = new Vector4(direction.x, direction.y, halfWidth, radius * 1.5f);
            Vector4 baseData = new Vector4(foot.x, foot.y, strength, 0);
            if (caster.OcclusionShape == shapeData && caster.OcclusionBase == baseData) return;
            caster.OcclusionShape = shapeData;
            caster.OcclusionBase = baseData;
            occlusionBlock ??= new MaterialPropertyBlock();
            occlusionBlock.Clear();
            occlusionBlock.SetVector(OcclusionBaseId, baseData);
            occlusionBlock.SetVector(OcclusionShapeId, shapeData);
            overlay.SetPropertyBlock(occlusionBlock);
        }

        // Основание от опоры (единицы мира, Y вверх): кистью — клетки,
        // прямоугольником — он; без основания — полоса по ширине
        // непрозрачного низа рисунка. У людей окклюзии нет.
        private List<Rect> Footprint(Caster caster)
        {
            if (caster.Footprint != null && caster.Footprint.Count > 0) return caster.Footprint;
            if (caster.FallbackFootprint == null && OpaqueBaseExtent(caster, out float left, out float right))
            {
                // Ширина — по непрозрачному низу рисунка, а не по холсту кадра.
                float width = right - left;
                float depth = Mathf.Max(.03f, width * .25f);
                float center = (left + right) / 2;
                caster.FallbackFootprint = new List<Rect> { new Rect(center - width / 2, -depth / 2, width, depth) };
            }
            return caster.FallbackFootprint ?? new List<Rect>();
        }

        // Левый и правый край непрозрачного низа рисунка (доля высоты — та же,
        // что перекрывает огонь) от опоры, единицы мира. Альфу не прочитать —
        // половина ширины рисунка по центру.
        private bool OpaqueBaseExtent(Caster caster, out float left, out float right)
        {
            SpriteRenderer source = caster.Source;
            left = right = 0;
            if (source.sprite == null) return false;
            LocationShadowStyle style = Sky.ShadowStyle;
            float band = Mathf.Clamp(caster.People ? style.FireBlockPeople : style.FireBlockObjects, .05f, 1);
            float anchorX = caster.Anchor.position.x;
            bool any = false;
            foreach (Vector2[] loop in LocationAlphaContours.Of(source.sprite, band))
                foreach (Vector2 point in loop)
                {
                    float x = source.transform.TransformPoint(new Vector3(source.flipX ? -point.x : point.x, point.y, 0)).x - anchorX;
                    left = any ? Mathf.Min(left, x) : x;
                    right = any ? Mathf.Max(right, x) : x;
                    any = true;
                }
            if (any && right - left > 1e-3f) return true;
            Bounds bounds = source.bounds;
            left = bounds.center.x - anchorX - bounds.extents.x * .5f;
            right = bounds.center.x - anchorX + bounds.extents.x * .5f;
            return right - left > 1e-3f;
        }

        // Пятно по форме основания: внутри — полная темнота, снаружи спадает
        // до нуля на radius; blur — размытие формы (углы основания и ступеньки
        // кисти скругляются). Опора спрайта — опора предмета. Одинаковые формы
        // делят одно пятно.
        private Sprite OcclusionSprite(List<Rect> cells, float radius, float blur)
        {
            System.Text.StringBuilder key = new System.Text.StringBuilder(radius.ToString("0.###")).Append('~').Append(blur.ToString("0.###"));
            foreach (Rect cell in cells)
                key.Append('|').Append(cell.x.ToString("0.###")).Append(',').Append(cell.y.ToString("0.###"))
                    .Append(',').Append(cell.width.ToString("0.###")).Append(',').Append(cell.height.ToString("0.###"));
            string id = key.ToString();
            if (occlusionSprites.TryGetValue(id, out Sprite cached) && cached != null) return cached;

            Rect bounds = cells[0];
            foreach (Rect cell in cells)
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, cell.xMin), Mathf.Min(bounds.yMin, cell.yMin),
                    Mathf.Max(bounds.xMax, cell.xMax), Mathf.Max(bounds.yMax, cell.yMax));
            float pad = radius + blur * 1.5f;
            Rect area = Rect.MinMaxRect(bounds.xMin - pad, bounds.yMin - pad, bounds.xMax + pad, bounds.yMax + pad);
            const int MaxSide = 160;
            float texel = Mathf.Max(Mathf.Min(radius, blur > 1e-3f ? blur : radius) / 8, area.width / MaxSide, area.height / MaxSide);
            int width = Mathf.Clamp(Mathf.CeilToInt(area.width / texel), 2, MaxSide);
            int height = Mathf.Clamp(Mathf.CeilToInt(area.height / texel), 2, MaxSide);
            // Расстояние до основания (в текселях): два прохода фаски.
            float[] distance = new float[width * height];
            const float Far = 1e6f, Diagonal = 1.41421356f;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 point = new Vector2(area.xMin + (x + .5f) * texel, area.yMin + (y + .5f) * texel);
                    bool inside = false;
                    foreach (Rect cell in cells)
                        if (cell.Contains(point)) { inside = true; break; }
                    distance[y * width + x] = inside ? 0 : Far;
                }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float d = distance[y * width + x];
                    if (x > 0) d = Mathf.Min(d, distance[y * width + x - 1] + 1);
                    if (y > 0) d = Mathf.Min(d, distance[(y - 1) * width + x] + 1);
                    if (x > 0 && y > 0) d = Mathf.Min(d, distance[(y - 1) * width + x - 1] + Diagonal);
                    if (x < width - 1 && y > 0) d = Mathf.Min(d, distance[(y - 1) * width + x + 1] + Diagonal);
                    distance[y * width + x] = d;
                }
            for (int y = height - 1; y >= 0; y--)
                for (int x = width - 1; x >= 0; x--)
                {
                    float d = distance[y * width + x];
                    if (x < width - 1) d = Mathf.Min(d, distance[y * width + x + 1] + 1);
                    if (y < height - 1) d = Mathf.Min(d, distance[(y + 1) * width + x] + 1);
                    if (x < width - 1 && y < height - 1) d = Mathf.Min(d, distance[(y + 1) * width + x + 1] + Diagonal);
                    if (x > 0 && y < height - 1) d = Mathf.Min(d, distance[(y + 1) * width + x - 1] + Diagonal);
                    distance[y * width + x] = d;
                }
            float[] value = new float[width * height];
            for (int i = 0; i < value.Length; i++)
            {
                float t = Mathf.Clamp01(1 - distance[i] * texel / radius);
                value[i] = t * t;
            }
            // Размытие: три прохода прямоугольного окна по строкам и столбцам
            // (почти гаусс).
            int reach = Mathf.RoundToInt(blur / texel / 1.7f);
            if (reach > 0)
                for (int pass = 0; pass < 3; pass++)
                {
                    BoxBlur(value, width, height, reach, true);
                    BoxBlur(value, width, height, reach, false);
                }
            byte[] alpha = new byte[width * height];
            for (int i = 0; i < alpha.Length; i++)
                alpha[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(value[i]) * 255);
            Texture2D texture = Own(new Texture2D(width, height, TextureFormat.Alpha8, false)
            {
                name = "Окклюзия основания", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            });
            texture.LoadRawTextureData(alpha);
            texture.Apply();
            Vector2 pivot = new Vector2(-area.xMin / (width * texel), -area.yMin / (height * texel));
            Sprite sprite = Own(Sprite.Create(texture, new Rect(0, 0, width, height), pivot, 1 / texel, 0, SpriteMeshType.FullRect));
            if (occlusionSprites.Count > 512) occlusionSprites.Clear();
            occlusionSprites[id] = sprite;
            return sprite;
        }

        // Среднее по окну 2·reach+1 вдоль строк (rows) или столбцов, на месте.
        private static void BoxBlur(float[] value, int width, int height, int reach, bool rows)
        {
            int lines = rows ? height : width, length = rows ? width : height;
            float[] line = new float[length];
            for (int l = 0; l < lines; l++)
            {
                for (int i = 0; i < length; i++)
                    line[i] = value[rows ? l * width + i : i * width + l];
                float sum = 0;
                for (int i = -reach; i <= reach; i++)
                    sum += i >= 0 && i < length ? line[i] : 0;
                for (int i = 0; i < length; i++)
                {
                    value[rows ? l * width + i : i * width + l] = sum / (2 * reach + 1);
                    int add = i + reach + 1, remove = i - reach;
                    if (add < length) sum += line[add];
                    if (remove >= 0) sum -= line[remove];
                }
            }
        }

        // Куда от основания уходит тень предмета и насколько она выражена:
        // смесь направлений от огней (вклад спадает к краю радиуса и с силой
        // огня) и от солнца (по силе его тени). Длина 0..1 — плавно, без
        // скачка на краю света и при смене суток; 0 — тени нет, окклюзия ровно
        // вокруг основания.
        private Vector2 ShadowDirection(Caster caster)
        {
            Vector2 foot = caster.Anchor.position;
            Vector2 sum = Vector2.zero;
            float total = 0;
            foreach (ShadowLight light in shadowLights)
            {
                if (float.IsInfinity(light.Radius)) continue;
                Vector2 away = foot - light.Position;
                float distance = away.magnitude;
                if (distance >= light.Radius || distance < 1e-4f) continue;
                float t = 1 - distance / light.Radius;
                float weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t * 3)) * light.Weight;
                sum += away / distance * weight;
                total += weight;
            }
            if (caster.Sun && sunRaw.sqrMagnitude > 1e-8f)
            {
                float weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(sunOpacity * 3));
                sum += sunRaw.normalized * weight;
                total += weight;
            }
            if (total < 1e-4f) return Vector2.zero;
            // Направление — средневзвешенное, выраженность — до 1.
            return sum / Mathf.Max(1, total);
        }

        private Material OcclusionMaterial()
        {
            if (occlusionMaterial != null) return occlusionMaterial;
            Shader shader = Resources.Load<Shader>("LocationRendering/LocationOcclusion");
            if (shader == null) return null;
            occlusionMaterial = Own(new Material(shader) { name = "Окклюзия места" });
            Shader onObject = Resources.Load<Shader>("LocationRendering/LocationOcclusionObject");
            if (onObject != null) occlusionObjectMaterial = Own(new Material(onObject) { name = "Окклюзия на предмете" });
            return occlusionMaterial;
        }

        // Сколько сейчас видимых пятен окклюзии на земле и на предметах (для проверки).
        public int VisibleOcclusionCount => casters.Count(caster => caster.Occlusion != null && caster.Occlusion.enabled);
        public int VisibleObjectOcclusionCount => casters.Count(caster => caster.OcclusionOverlay != null && caster.OcclusionOverlay.enabled);

        // Включённые местные источники с тенями: позиция и радиус в мире.
        // Свет формы и свет-рисунок — без радиуса (действует везде).
        private void CollectShadowLights()
        {
            shadowLights.Clear();
            anyShadowLight = false;
            foreach (Placed item in placed)
            {
                Light2D light = item.Light;
                if (light == null || !light.enabled || !light.shadowsEnabled || light.intensity <= 0) continue;
                anyShadowLight = true;
                bool point = light.lightType == Light2D.LightType.Point;
                Vector3 scale = light.transform.lossyScale;
                shadowLights.Add(new ShadowLight
                {
                    Position = light.transform.position,
                    Radius = point ? light.pointLightOuterRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) : float.PositiveInfinity,
                    Weight = Mathf.Clamp01(light.intensity)
                });
            }
        }

        private bool NearShadowLight(Caster caster)
        {
            if (shadowLights.Count == 0) return false;
            Bounds bounds = caster.Source.bounds;
            Vector2 center = bounds.center;
            float extent = bounds.extents.magnitude;
            foreach (ShadowLight light in shadowLights)
            {
                float reach = light.Radius + extent;
                if (float.IsInfinity(reach) || (center - light.Position).sqrMagnitude <= reach * reach) return true;
            }
            return false;
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
            if (contour.enabled != enabled)
            {
                contour.enabled = enabled;
                // Снова включён (подошёл к огню, наступила ночь) — форму задать заново.
                if (enabled) caster.ContourSprite = null;
            }
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
                if (shadow != null && shadow.enabled) shadow.enabled = false;
                caster.ShadowShown = false;
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
            if (!shadow.enabled) shadow.enabled = true;
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
                // Растянутый и повёрнутый рисунок (ПР-12Р) — такой же силуэт.
                if (from.parent == caster.Anchor)
                {
                    shadow.transform.localPosition = from.localPosition;
                    shadow.transform.localScale = from.localScale;
                    shadow.transform.localRotation = from.localRotation;
                }
                else
                {
                    shadow.transform.position = from.position;
                    shadow.transform.localScale = from.lossyScale;
                    shadow.transform.rotation = from.rotation;
                }
            }
            // Ничего не изменилось с прошлого кадра — блок свойств прежний.
            float groundY = caster.Anchor.position.y;
            if (caster.ShadowShown && caster.ShadowSprite == shadow.sprite && (caster.ShadowVector - vector).sqrMagnitude < 1e-8f &&
                Mathf.Abs(caster.ShadowGroundY - groundY) < 1e-5f && Mathf.Abs(caster.ShadowOpacity - opacity) < 1e-4f &&
                Mathf.Abs(caster.ShadowSoftness - softness) < 1e-4f && caster.ShadowColor == color)
                return;
            caster.ShadowShown = true;
            caster.ShadowSprite = shadow.sprite;
            caster.ShadowVector = vector;
            caster.ShadowGroundY = groundY;
            caster.ShadowOpacity = opacity;
            caster.ShadowSoftness = softness;
            caster.ShadowColor = color;
            // Блок свойств — с чистого листа: старый блок мог нести текстуру
            // прошлого кадра анимации (другой страницы атласа).
            shadowBlock.Clear();
            Vector4 uvRect = SpriteUVRect(shadow.sprite);
            shadowBlock.SetVector(ShadowVectorId, vector);
            shadowBlock.SetFloat(GroundYId, groundY);
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
                    layer.transform.localRotation = image.transform.localRotation;
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
