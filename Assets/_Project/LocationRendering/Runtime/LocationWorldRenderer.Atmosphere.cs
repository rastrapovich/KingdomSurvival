using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: атмосфера места — частицы «Обработки кадра»: пыль в лучах,
    // мошкара роями, дождь, падающие листья, искры над огнём, пар изо рта
    // ночью. Одна система Unity на слой; частицы выпускает рендерер по
    // своим часам (SetTime) и сам двигает систему — одинаково в игре и в
    // окне базы (вне Play Mode Unity частицы не двигает). Пыль, мошкара,
    // дождь и листья — по всему рисунку места, искры — над источниками с
    // «мерцанием», пар — у голов людей. Цвет частиц, кроме искр, — по
    // общему свету (ночью темнее). Только картинка.
    public sealed partial class LocationWorldRenderer
    {
        private const string ParticleShaderPath = "LocationRendering/LocationParticle";
        // Не больше стольких частиц у слоя (большие места, высокая плотность).
        private const int MaxParticlesPerLayer = 4000;
        // За шаг часов не больше (окно в фоне, пауза): не выпускать пачку разом.
        private const float MaxAtmosphereStep = .2f;

        private enum AtmosphereKind { Dust, Midges, Rain, Leaves, Sparks, Breath }

        private sealed class AtmosphereView
        {
            public AtmosphereKind Kind;
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
            public Material Material;
            public float Carry;
            public string Signature;
        }

        private readonly AtmosphereView[] atmosphere = new AtmosphereView[6];
        private Transform atmosphereRoot;
        private float atmosphereClock = float.NaN;
        private readonly System.Random atmosphereRandom = new System.Random(7);
        private Vector2[] swarmCenters = Array.Empty<Vector2>();

        // Частиц сейчас в слоях атмосферы (проверки и окно).
        public int AtmosphereParticleCount
        {
            get
            {
                int count = 0;
                foreach (AtmosphereView view in atmosphere)
                    if (view?.System != null) count += view.System.particleCount;
                return count;
            }
        }

        private void UpdateAtmosphere()
        {
            LocationPostEffects post = Post;
            LocationAtmosphereEffects settings = post?.Atmosphere;
            float step = float.IsNaN(atmosphereClock) ? 0 : Mathf.Clamp(Seconds - atmosphereClock, 0, MaxAtmosphereStep);
            atmosphereClock = Seconds;
            bool on = PostActive && settings != null;
            if (!on && atmosphereRoot == null) return;
            settings?.Repair();
            Rect area = WorldArea();
            for (int i = 0; i < atmosphere.Length; i++)
            {
                AtmosphereKind kind = (AtmosphereKind)i;
                LocationParticleLayer layer = on ? Layer(settings, kind) : null;
                bool active = layer != null && layer.Enabled;
                AtmosphereView view = atmosphere[i];
                if (!active)
                {
                    if (view?.System != null && view.System.particleCount > 0) view.System.Clear();
                    continue;
                }
                view ??= atmosphere[i] = CreateAtmosphere(kind);
                if (view == null) continue;
                Configure(view, layer);
                if (step > 0) Emit(view, layer, area, step);
                if (step > 0) view.System.Simulate(step, false, false, false);
            }
            UpdateAtmosphereVisibility();
        }

        private static LocationParticleLayer Layer(LocationAtmosphereEffects settings, AtmosphereKind kind)
        {
            switch (kind)
            {
                case AtmosphereKind.Dust: return settings.Dust;
                case AtmosphereKind.Midges: return settings.Midges;
                case AtmosphereKind.Rain: return settings.Rain;
                case AtmosphereKind.Leaves: return settings.Leaves;
                case AtmosphereKind.Sparks: return settings.Sparks;
                default: return settings.Breath;
            }
        }

        private void UpdateAtmosphereVisibility()
        {
            foreach (AtmosphereView view in atmosphere)
                if (view?.Renderer != null) view.Renderer.enabled = PostActive;
        }

        // Весь рисунок места в мире.
        private Rect WorldArea()
        {
            Vector2 a = Root.transform.TransformPoint(LocationVisualGeometry.PixelToWorld(Location, Vector2.zero));
            Vector2 b = Root.transform.TransformPoint(LocationVisualGeometry.PixelToWorld(Location, CanvasSize));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private AtmosphereView CreateAtmosphere(AtmosphereKind kind)
        {
            Shader shader = Resources.Load<Shader>(ParticleShaderPath);
            if (shader == null) return null;
            if (atmosphereRoot == null) atmosphereRoot = Child("Атмосфера").transform;
            GameObject target = new GameObject(AtmosphereName(kind));
            target.transform.SetParent(atmosphereRoot, false);
            ParticleSystem system = target.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticlesPerLayer;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = target.GetComponent<ParticleSystemRenderer>();
            Material material = Own(new Material(shader) { name = "Атмосфера · " + AtmosphereName(kind) });
            renderer.sharedMaterial = material;
            renderer.sortingOrder = kind == AtmosphereKind.Sparks || kind == AtmosphereKind.Breath ? 21000 : 30000;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.minParticleSize = 0;
            renderer.maxParticleSize = 10;
            // Система стоит на паузе: её двигает рендерер (Simulate), выпуск — Emit.
            system.Simulate(0, false, true, false);
            return new AtmosphereView { Kind = kind, System = system, Renderer = renderer, Material = material };
        }

        private static string AtmosphereName(AtmosphereKind kind)
        {
            switch (kind)
            {
                case AtmosphereKind.Dust: return "Пыль в лучах";
                case AtmosphereKind.Midges: return "Мошкара";
                case AtmosphereKind.Rain: return "Дождь";
                case AtmosphereKind.Leaves: return "Листья";
                case AtmosphereKind.Sparks: return "Искры";
                default: return "Пар изо рта";
            }
        }

        // Модули системы — по настройкам слоя (только когда настройки сменились).
        private void Configure(AtmosphereView view, LocationParticleLayer layer)
        {
            string signature = JsonUtility.ToJson(layer) + WindSignature();
            if (signature == view.Signature) return;
            view.Signature = signature;
            ParticleSystem system = view.System;
            AtmosphereKind kind = view.Kind;
            ParticleSystem.MainModule main = system.main;
            main.gravityModifier = layer.Gravity;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = layer.Wander > .0001f;
            noise.strength = layer.Wander;
            noise.frequency = Mathf.Max(.01f, layer.WanderFrequency);
            noise.scrollSpeed = layer.WanderFrequency * .5f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Появляется и тает плавно; искры гаснут, пар растёт.
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            float fadeIn = kind == AtmosphereKind.Rain || kind == AtmosphereKind.Sparks || kind == AtmosphereKind.Breath ? .05f : .2f;
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, fadeIn), new GradientAlphaKey(1, .7f), new GradientAlphaKey(0, 1) });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = kind == AtmosphereKind.Sparks || kind == AtmosphereKind.Breath;
            if (kind == AtmosphereKind.Sparks) size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, .2f));
            if (kind == AtmosphereKind.Breath) size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 1.3f));

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = kind == AtmosphereKind.Leaves;
            if (rotation.enabled) rotation.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            Vector2 wind = ParticleWind() * layer.WindShare;
            velocity.enabled = wind.sqrMagnitude > 1e-6f;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(wind.x);
            velocity.y = new ParticleSystem.MinMaxCurve(wind.y);
            velocity.z = new ParticleSystem.MinMaxCurve(0);

            ParticleSystemRenderer renderer = view.Renderer;
            bool streak = kind == AtmosphereKind.Rain;
            renderer.renderMode = streak ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = streak ? Mathf.Max(1, layer.Stretch) : 1;
            renderer.velocityScale = 0;
            float shapeId = layer.Texture != null ? 4 : kind == AtmosphereKind.Rain ? 1 : kind == AtmosphereKind.Leaves ? 2 : kind == AtmosphereKind.Breath ? 3 : 0;
            view.Material.SetFloat("_KsShape", shapeId);
            view.Material.SetTexture("_MainTex", layer.Texture != null ? layer.Texture : Texture2D.whiteTexture);
            bool additive = kind == AtmosphereKind.Sparks;
            view.Material.SetFloat("_SrcBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.SrcAlpha));
            view.Material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        }

        private string WindSignature()
        {
            LocationSpriteEffects s = Post?.Sprites;
            return s == null || !s.Wind ? "|-" : "|" + s.WindStrength + ";" + s.WindDirection + ";" + s.WindGusts;
        }

        // Ветер для частиц: тот же, что качает рисунки (направление и сила), в единицах мира в секунду.
        private Vector2 ParticleWind()
        {
            LocationSpriteEffects s = PostActive ? Post.Sprites : null;
            if (s == null || !s.Wind) return Vector2.zero;
            return new Vector2(Mathf.Clamp(s.WindDirection, -1, 1) * Mathf.Max(0, s.WindStrength) * 10 * (.6f + Mathf.Clamp01(s.WindGusts) * .4f), 0);
        }

        private float Random01() => (float)atmosphereRandom.NextDouble();
        private float RandomRange(float a, float b) => a + (b - a) * Random01();

        private void Emit(AtmosphereView view, LocationParticleLayer layer, Rect area, float step)
        {
            float visible = layer.Visibility(Hour, DayFactor);
            if (visible <= .001f) return;
            switch (view.Kind)
            {
                case AtmosphereKind.Sparks:
                    foreach (Vector2 fire in FireSources())
                        EmitAt(view, layer, fire, .12f, layer.Density * step * visible);
                    break;
                case AtmosphereKind.Breath:
                    foreach (Vector2 head in Heads())
                        EmitAt(view, layer, head, .03f, layer.Density * step * visible);
                    break;
                case AtmosphereKind.Midges:
                    UpdateSwarms(layer, area);
                    foreach (Vector2 center in swarmCenters)
                        EmitAt(view, layer, center, .6f, layer.Density * step * visible);
                    break;
                default:
                    float rate = layer.Density * area.width * area.height / 100f * step * visible;
                    EmitArea(view, layer, area, rate);
                    break;
            }
        }

        private void EmitArea(AtmosphereView view, LocationParticleLayer layer, Rect area, float count)
        {
            view.Carry += count;
            int whole = Mathf.FloorToInt(view.Carry);
            view.Carry -= whole;
            whole = Mathf.Min(whole, MaxParticlesPerLayer - view.System.particleCount);
            for (int i = 0; i < whole; i++)
                EmitOne(view, layer, new Vector2(RandomRange(area.xMin, area.xMax), RandomRange(area.yMin, area.yMax)));
        }

        private void EmitAt(AtmosphereView view, LocationParticleLayer layer, Vector2 center, float radius, float count)
        {
            view.Carry += count;
            int whole = Mathf.FloorToInt(view.Carry);
            view.Carry -= whole;
            whole = Mathf.Min(whole, MaxParticlesPerLayer - view.System.particleCount);
            for (int i = 0; i < whole; i++)
            {
                float angle = Random01() * Mathf.PI * 2, distance = Mathf.Sqrt(Random01()) * radius;
                EmitOne(view, layer, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
            }
        }

        private void EmitOne(AtmosphereView view, LocationParticleLayer layer, Vector2 position)
        {
            float angle = (layer.Angle + RandomRange(-.5f, .5f) * Mathf.Clamp(layer.Spread, 0, 360)) * Mathf.Deg2Rad;
            float speed = Mathf.Max(0, layer.Speed) * RandomRange(.7f, 1.3f);
            float size = Mathf.Max(.002f, layer.Size) * (1 + RandomRange(-1, 1) * Mathf.Clamp01(layer.SizeJitter));
            Color color = Color.Lerp(layer.Color, layer.ColorB, Random01());
            // Пар ночью и мошкара в сумерках видны в отсветах (порог): иначе темнеют вместе с небом.
            if (view.Kind != AtmosphereKind.Sparks) color = AmbientTint(color, view.Kind == AtmosphereKind.Breath ? .45f : view.Kind == AtmosphereKind.Midges ? .55f : 0);
            color.a = Mathf.Clamp01(layer.Opacity);
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, 0),
                velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * speed,
                startSize = size,
                startLifetime = Mathf.Max(.05f, layer.Lifetime) * RandomRange(.75f, 1.25f),
                startColor = color,
                rotation = view.Kind == AtmosphereKind.Leaves ? RandomRange(0, 360) : 0,
                applyShapeToPosition = false
            };
            view.System.Emit(emit, 1);
        }

        // Цвет частицы под общим светом места (ночью темнее, в цвет неба).
        private Color AmbientTint(Color color, float floor = 0)
        {
            if (GlobalLight == null) return color;
            Color light = GlobalLight.color * Mathf.Clamp(GlobalLight.intensity, .12f, 1.2f);
            light = new Color(Mathf.Max(light.r, floor), Mathf.Max(light.g, floor), Mathf.Max(light.b, floor));
            return new Color(color.r * light.r, color.g * light.g, color.b * light.b, color.a);
        }

        // Источники огня (свет с мерцанием) — над ними искры.
        private IEnumerable<Vector2> FireSources()
        {
            foreach (Placed item in placed)
            {
                if (item.Light == null || !item.Light.enabled || item.Data?.Light == null) continue;
                if (item.Data.Light.Animation != LocationLightAnimation.Flicker) continue;
                yield return (Vector2)item.Light.transform.position + new Vector2(0, .05f);
            }
        }

        // Головы людей: исследование и живые фигуры боя (у рта, к лицу).
        private IEnumerable<Vector2> Heads()
        {
            foreach (Actor actor in actors.Values)
                if (actor.Image != null && actor.Image.enabled && actor.Image.sprite != null)
                    yield return Head(actor.Image);
            foreach (BattleFigure figure in battleFigures.Values)
                if (!figure.Corpse && figure.Image != null && figure.Image.sprite != null)
                    yield return Head(figure.Image);
        }

        // Рот — по непрозрачной части рисунка (у кадров фигур сверху бывает
        // пустое поле): чуть ниже макушки, к лицу.
        private static Vector2 Head(SpriteRenderer image)
        {
            Rect opaque = LocationAlphaContours.OpaqueRect(image.sprite);
            if (opaque.width <= 0 || opaque.height <= 0)
            {
                Bounds bounds = image.bounds;
                return new Vector2(bounds.center.x, bounds.max.y - bounds.size.y * .16f);
            }
            float side = image.flipX ? -1 : 1;
            float x = opaque.center.x + opaque.width * .18f, y = opaque.yMax - opaque.height * .13f;
            return image.transform.TransformPoint(new Vector3(x * side, y, 0));
        }

        // Рои мошкары медленно плавают по месту (устойчиво по времени).
        private void UpdateSwarms(LocationParticleLayer layer, Rect area)
        {
            int groups = Mathf.Clamp(layer.Groups, 1, 12);
            if (swarmCenters.Length != groups) swarmCenters = new Vector2[groups];
            for (int i = 0; i < groups; i++)
            {
                float seed = i * 12.9898f;
                float x = Mathf.PerlinNoise(seed, Seconds * .05f), y = Mathf.PerlinNoise(seed + 5.3f, Seconds * .05f + 3.7f);
                swarmCenters[i] = new Vector2(Mathf.Lerp(area.xMin, area.xMax, .1f + .8f * x), Mathf.Lerp(area.yMin, area.yMax, .1f + .8f * y));
            }
        }

        private void DisposeAtmosphere()
        {
            for (int i = 0; i < atmosphere.Length; i++) atmosphere[i] = null;
            atmosphereRoot = null;
        }
    }
}
