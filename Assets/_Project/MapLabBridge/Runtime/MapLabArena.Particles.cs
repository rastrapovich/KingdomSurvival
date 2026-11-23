using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge
{
    // 23.11.2026: частицы атмосферы на арене — те же слои «Обработки кадра»
    // (LocationAtmosphereEffects: пыль, мошкара, дождь, листья, искры, пар), что
    // у мест (LocationWorldRenderer.Atmosphere), с теми же настройками, видом и
    // поведением. Частицы лежат в плоскости кадра (как на плоской картинке
    // места), а не в объёмном мире арены: система стоит перпендикулярно камере,
    // и вся логика — прежняя двумерная. Рисуются в нижнем кадре арены (под
    // сеткой доски и слоями стоящих объектов); бойцы — элементы UI над кадром.
    // Размер, скорость и плотность пересчитываются масштабом рисунков арены
    // (единиц мира MapLab на единицу мира мест): «Размер 0,12» выглядит на
    // арене так же, как на месте. Арена по цвету всегда дневная; час суток для
    // слоёв «Ночью» и «В часы» — ArenaFrameSettings.Hour.
    public sealed partial class MapLabArena
    {
        private MapLabArenaParticles particles;
        private readonly List<Vector2> headSources = new List<Vector2>();

        // Час боя для частиц (NaN — из настроек «Генератора арен»).
        public float AtmosphereHour = float.NaN;

        // Доля плотности частиц по уровню качества (высокое — 1, среднее — .7, низкое — .4; ставит связка обработки кадра).
        public float ParticleDensityScale = 1f;

        // Частиц сейчас в слоях атмосферы (проверки, окно).
        public int AtmosphereParticleCount => particles != null ? particles.Count : 0;

        // Единиц мира MapLab на единицу мира мест (размер рисунков и частиц арены).
        public float AssetUnitScale => Mathf.Max(.01f, windScale);

        public float CurrentAtmosphereHour => float.IsNaN(AtmosphereHour) ? ArenaGeneratorAsset.Load().Frame.Hour : AtmosphereHour;

        private LocationAtmosphereEffects AtmosphereSettings
        {
            get
            {
                LocationPostEffects post = Spec.Frame;
                return post != null && post.Enabled ? post.Atmosphere : null;
            }
        }

        // Хоть один слой сейчас виден: кадр нужно перерисовывать со временем.
        public bool ParticlesActive
        {
            get
            {
                LocationAtmosphereEffects settings = AtmosphereSettings;
                if (settings == null) return false;
                float hour = CurrentAtmosphereHour, day = MapLabArenaParticles.DayFactorAt(hour);
                return Live(settings.Dust, hour, day) || Live(settings.Midges, hour, day) || Live(settings.Rain, hour, day) ||
                       Live(settings.Leaves, hour, day) || Live(settings.Sparks, hour, day, FireSources().Count > 0) || Live(settings.Breath, hour, day, headSources.Count > 0);
            }
        }

        // Слой включён и виден (или ещё не истаял): без перечислений и выделений памяти.
        private bool Live(LocationParticleLayer layer, float hour, float day, bool hasSource = true) =>
            layer != null && layer.Enabled && (hasSource && layer.Visibility(hour, day) > .001f || particles != null && particles.Count > 0);

        // Пару изо рта нужны головы бойцов (кадр поля, доли высоты, Y вниз).
        public bool WantsHeads
        {
            get
            {
                LocationAtmosphereEffects settings = AtmosphereSettings;
                return settings != null && settings.Breath != null && settings.Breath.Enabled;
            }
        }

        public void SetHeads(IReadOnlyList<Vector2> nominalHeads)
        {
            headSources.Clear();
            if (nominalHeads != null)
                for (int i = 0; i < nominalHeads.Count; i++)
                    headSources.Add(nominalHeads[i]);
        }

        // Огни боя — клетки с огнём (поверхность «Огонь»): центры в долях
        // кадра, Y вниз. Над ними искры и марево.
        private readonly List<Vector2> fireSources = new List<Vector2>();

        private List<Vector2> FireSources()
        {
            fireSources.Clear();
            ulong fire = MapLabArenaSurfaces.Bit(MapLabArenaSurface.Fire);
            foreach ((Vector2 center, ulong layers) in Spec.Surfaces)
                if ((layers & fire) != 0UL)
                    fireSources.Add(center);
            return fireSources;
        }

        // Огни боя для марева над огнём: точки в кадре, 0..1, Y вверх (вьюпорт камеры арены).
        public void CollectFireViewport(List<Vector2> into)
        {
            if (into == null) return;
            into.Clear();
            if (!Ready) return;
            foreach (Vector2 fire in FireSources())
                into.Add(new Vector2(fire.x / Spec.Aspect, 1f - fire.y + .02f));
        }

        // Перед кадром камеры: выпустить и сдвинуть частицы (из Render).
        private void UpdateParticles(Vector3 focus, Quaternion rotation, float halfWidth, float halfHeight, float forcedStep = -1f)
        {
            particleFocus = focus; particleRotation = rotation; particleHalf = new Vector2(halfWidth, halfHeight);
            LocationAtmosphereEffects settings = AtmosphereSettings;
            if (settings == null && particles == null) return;
            particles ??= new MapLabArenaParticles(Layer);
            float hour = CurrentAtmosphereHour;
            particles.Update(settings, hour, MapLabArenaParticles.DayFactorAt(hour), Spec.Frame?.Sprites, focus, rotation, halfWidth, halfHeight,
                Mathf.Max(.01f, windScale), Spec.Aspect, Spec.HexWorld, FireSources(), headSources, forcedStep, ParticleDensityScale);
        }

        // Прогнать частицы вперёд на seconds (проверки, снимки): шаги по 0,1 с, камера — как в последнем кадре.
        public void AdvanceParticles(float seconds)
        {
            for (float t = 0f; t < seconds; t += .1f)
                UpdateParticles(particleFocus, particleRotation, particleHalf.x, particleHalf.y, .1f);
        }

        private Vector3 particleFocus;
        private Quaternion particleRotation = Quaternion.identity;
        private Vector2 particleHalf;

        // Центры роёв мошкары в долях кадра 0..1 (проверки).
        public void CollectSwarmShares(List<Vector2> into) => particles?.CollectSwarmShares(into);

        private void DisposeParticles()
        {
            particles?.Dispose();
            particles = null;
        }
    }

    // Частицы атмосферы арены: по одной системе Unity на слой, выпуск и
    // сдвиг по часам арены (кадр арены рисуется по запросу, система стоит на
    // паузе). Система в локальных осях плоскости кадра: x вправо, y вверх.
    internal sealed class MapLabArenaParticles
    {
        private const string ShaderPath = "MapLabArenaParticle";
        // Не больше стольких частиц у слоя: арена и так тяжёлая.
        private const int MaxPerLayer = 1500;
        // За шаг не больше (окно в фоне, пауза): не выпускать пачку разом.
        private const float MaxStep = .2f;
        // Первый вход слоя: прогрев (видно сразу, не нарастает секундами).
        private const float PrewarmStep = .1f;
        private const float PrewarmMax = 4f;

        private enum Kind { Dust, Midges, Rain, Leaves, Sparks, Breath }

        private sealed class View
        {
            public Kind Kind;
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
            public Material Material;
            public float Carry;
            public int Signature;
            public bool Primed;
        }

        private readonly View[] views = new View[6];
        private readonly GameObject root;
        private readonly System.Random random = new System.Random(7);
        private readonly Vector2[] swarmCenters = new Vector2[12];
        private int activeGroups;
        private float clock = float.NaN;
        private float seconds;
        private Shader shader;

        // Рабочее состояние кадра (задаётся перед Emit).
        private Vector3 focus;
        private Quaternion rotation;
        private float halfWidth, halfHeight, scale = 1f, aspect = 16f / 9f, hexWorld = 3f;
        private IReadOnlyList<Vector2> fires, heads;
        private float hour, day;
        private float densityScale = 1f;

        public MapLabArenaParticles(int layer)
        {
            root = new GameObject("MapLab Arena Atmosphere") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
        }

        public int Count
        {
            get
            {
                int count = 0;
                foreach (View view in views)
                    if (view?.System != null) count += view.System.particleCount;
                return count;
            }
        }

        // Яркость суток по часу (для «Днём» и «Ночью»): день 7–19, плавно.
        public static float DayFactorAt(float hour) =>
            Mathf.Clamp01(1.2f * Mathf.Cos((Mathf.Repeat(hour, 24f) - 13f) / 12f * Mathf.PI) + .4f);

        public void Update(LocationAtmosphereEffects settings, float hour, float dayFactor, LocationSpriteEffects sprites, Vector3 focus, Quaternion rotation,
            float halfWidth, float halfHeight, float scale, float aspect, float hexWorld, IReadOnlyList<Vector2> fires, IReadOnlyList<Vector2> heads, float forcedStep = -1f, float densityScale = 1f)
        {
            float now = Time.realtimeSinceStartup;
            float step = forcedStep >= 0f ? forcedStep : float.IsNaN(clock) ? 0f : Mathf.Clamp(now - clock, 0f, MaxStep);
            clock = now;
            seconds += step;
            this.hour = hour; day = dayFactor;
            this.focus = focus; this.rotation = rotation;
            this.halfWidth = halfWidth; this.halfHeight = halfHeight;
            this.scale = scale; this.aspect = aspect; this.hexWorld = hexWorld;
            this.fires = fires; this.heads = heads;
            this.densityScale = Mathf.Clamp01(densityScale);
            if (settings == null)
            {
                ClearAll();
                clock = float.NaN;
                return;
            }
            settings.Repair();
            root.transform.SetPositionAndRotation(focus, rotation);
            Vector2 wind = Wind(sprites);
            for (int i = 0; i < views.Length; i++)
            {
                Kind kind = (Kind)i;
                LocationParticleLayer layer = Layer(settings, kind);
                View view = views[i];
                if (layer == null || !layer.Enabled)
                {
                    if (view?.System != null)
                    {
                        if (view.System.particleCount > 0) view.System.Clear();
                        view.Renderer.enabled = false;
                        view.Primed = false;
                    }
                    continue;
                }
                float visible = layer.Visibility(hour, dayFactor) * densityScale;
                if (visible <= .001f && (view == null || view.System.particleCount == 0))
                {
                    if (view != null) { view.Renderer.enabled = false; view.Primed = false; }
                    continue;
                }
                view ??= views[i] = Create(kind);
                if (view == null) continue;
                view.Renderer.enabled = true;
                Configure(view, layer, wind);
                if (!view.Primed)
                {
                    view.Primed = true;
                    Prewarm(view, layer, visible);
                }
                else if (step > 0f)
                {
                    Emit(view, layer, step, visible);
                    view.System.Simulate(step, false, false, false);
                }
            }
        }

        // Первый вход слоя: частицы уже в полёте.
        private void Prewarm(View view, LocationParticleLayer layer, float visible)
        {
            float total = Mathf.Min(PrewarmMax, Mathf.Max(.1f, layer.Lifetime));
            float saved = seconds;
            for (float t = 0f; t < total; t += PrewarmStep)
            {
                seconds += PrewarmStep;
                Emit(view, layer, PrewarmStep, visible);
                view.System.Simulate(PrewarmStep, false, false, false);
            }
            seconds = saved + total;
        }

        private static LocationParticleLayer Layer(LocationAtmosphereEffects settings, Kind kind)
        {
            switch (kind)
            {
                case Kind.Dust: return settings.Dust;
                case Kind.Midges: return settings.Midges;
                case Kind.Rain: return settings.Rain;
                case Kind.Leaves: return settings.Leaves;
                case Kind.Sparks: return settings.Sparks;
                default: return settings.Breath;
            }
        }

        private static string Name(Kind kind)
        {
            switch (kind)
            {
                case Kind.Dust: return "Пыль в лучах";
                case Kind.Midges: return "Мошкара";
                case Kind.Rain: return "Дождь";
                case Kind.Leaves: return "Листья";
                case Kind.Sparks: return "Искры";
                default: return "Пар изо рта";
            }
        }

        private void ClearAll()
        {
            foreach (View view in views)
            {
                if (view?.System == null) continue;
                if (view.System.particleCount > 0) view.System.Clear();
                view.Renderer.enabled = false;
                view.Primed = false;
            }
        }

        private View Create(Kind kind)
        {
            if (shader == null) shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported) return null;
            GameObject target = new GameObject(Name(kind)) { hideFlags = HideFlags.HideAndDontSave, layer = root.layer };
            target.transform.SetParent(root.transform, false);
            ParticleSystem system = target.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            // Локальные оси плоскости кадра: двигаются, как на плоской картинке места.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = MaxPerLayer;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = target.GetComponent<ParticleSystemRenderer>();
            Material material = new Material(shader) { name = "Атмосфера арены · " + Name(kind), hideFlags = HideFlags.HideAndDontSave };
            renderer.sharedMaterial = material;
            renderer.sortingOrder = kind == Kind.Sparks || kind == Kind.Breath ? 21000 : 30000;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 10f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Система стоит на паузе: её двигает арена (Simulate), выпуск — Emit.
            system.Simulate(0f, false, true, false);
            return new View { Kind = kind, System = system, Renderer = renderer, Material = material };
        }

        // Ветер рисунков в единицах мира мест в секунду (как у мест).
        private static Vector2 Wind(LocationSpriteEffects s)
        {
            if (s == null || !s.Wind) return Vector2.zero;
            return new Vector2(Mathf.Clamp(s.WindDirection, -1f, 1f) * Mathf.Max(0f, s.WindStrength) * 10f * (.6f + Mathf.Clamp01(s.WindGusts) * .4f), 0f);
        }

        private static int Hash(LocationParticleLayer l, Vector2 wind, float scale)
        {
            int h = 17;
            void Add(float v) => h = h * 31 + v.GetHashCode();
            Add(l.Gravity); Add(l.Wander); Add(l.WanderFrequency); Add(l.WindShare); Add(l.Stretch);
            Add(wind.x); Add(wind.y); Add(scale);
            h = h * 31 + (l.Texture != null ? l.Texture.GetHashCode() : 0);
            return h;
        }

        // Модули системы — по настройкам слоя (только когда настройки сменились).
        private void Configure(View view, LocationParticleLayer layer, Vector2 wind)
        {
            int signature = Hash(layer, wind, scale);
            if (signature == view.Signature) return;
            view.Signature = signature;
            ParticleSystem system = view.System;
            Kind kind = view.Kind;
            // Тяга — ускорение вдоль оси y кадра (гравитация Unity — мировая, а плоскость кадра наклонена).
            ParticleSystem.MainModule main = system.main;
            main.gravityModifier = 0f;
            ParticleSystem.ForceOverLifetimeModule force = system.forceOverLifetime;
            force.enabled = Mathf.Abs(layer.Gravity) > 1e-5f;
            force.space = ParticleSystemSimulationSpace.Local;
            force.x = new ParticleSystem.MinMaxCurve(0f);
            force.y = new ParticleSystem.MinMaxCurve(-9.81f * layer.Gravity * scale);
            force.z = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = layer.Wander > .0001f;
            noise.strength = layer.Wander * scale;
            noise.frequency = Mathf.Max(.01f, layer.WanderFrequency) / scale;
            noise.scrollSpeed = layer.WanderFrequency * .5f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            float fadeIn = kind == Kind.Rain || kind == Kind.Sparks || kind == Kind.Breath ? .05f : .2f;
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, .7f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = kind == Kind.Sparks || kind == Kind.Breath;
            if (kind == Kind.Sparks) size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .2f));
            if (kind == Kind.Breath) size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .5f, 1f, 1.3f));

            ParticleSystem.RotationOverLifetimeModule rotationModule = system.rotationOverLifetime;
            rotationModule.enabled = kind == Kind.Leaves;
            if (rotationModule.enabled) rotationModule.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            Vector2 drift = wind * layer.WindShare * scale;
            velocity.enabled = drift.sqrMagnitude > 1e-6f;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystemRenderer renderer = view.Renderer;
            bool streak = kind == Kind.Rain;
            renderer.renderMode = streak ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = streak ? Mathf.Max(1f, layer.Stretch) : 1f;
            renderer.velocityScale = 0f;
            float shapeId = layer.Texture != null ? 4f : kind == Kind.Rain ? 1f : kind == Kind.Leaves ? 2f : kind == Kind.Breath ? 3f : 0f;
            view.Material.SetFloat("_KsShape", shapeId);
            view.Material.SetTexture("_MainTex", layer.Texture != null ? layer.Texture : Texture2D.whiteTexture);
            bool additive = kind == Kind.Sparks;
            view.Material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            view.Material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        }

        private float Random01() => (float)random.NextDouble();
        private float RandomRange(float a, float b) => a + (b - a) * Random01();

        // Кадр поля (доли высоты, Y вниз) → локальные оси плоскости кадра.
        private Vector2 ToPlane(Vector2 nominal) =>
            new Vector2((nominal.x / aspect - .5f) * 2f * halfWidth, (.5f - nominal.y) * 2f * halfHeight);

        private void Emit(View view, LocationParticleLayer layer, float step, float visible)
        {
            if (visible <= .001f) return;
            switch (view.Kind)
            {
                case Kind.Sparks:
                    if (fires != null)
                        for (int i = 0; i < fires.Count; i++)
                            EmitAt(view, layer, ToPlane(fires[i]) + new Vector2(0f, hexWorld * .35f), .12f * scale, layer.Density * step * visible);
                    break;
                case Kind.Breath:
                    if (heads != null)
                        for (int i = 0; i < heads.Count; i++)
                            EmitAt(view, layer, ToPlane(heads[i]), .03f * scale, layer.Density * step * visible);
                    break;
                case Kind.Midges:
                    UpdateSwarms(layer);
                    int groups = Mathf.Clamp(layer.Groups, 1, 12);
                    for (int i = 0; i < groups; i++)
                        EmitAt(view, layer, swarmCenters[i], .6f * scale, layer.Density * step * visible);
                    break;
                default:
                    float area = 4f * halfWidth * halfHeight / (scale * scale);
                    EmitArea(view, layer, layer.Density * area / 100f * step * visible);
                    break;
            }
        }

        private void EmitArea(View view, LocationParticleLayer layer, float count)
        {
            view.Carry += count;
            int whole = Mathf.FloorToInt(view.Carry);
            view.Carry -= whole;
            whole = Mathf.Min(whole, MaxPerLayer - view.System.particleCount);
            for (int i = 0; i < whole; i++)
                EmitOne(view, layer, new Vector2(RandomRange(-halfWidth, halfWidth), RandomRange(-halfHeight, halfHeight)));
        }

        private void EmitAt(View view, LocationParticleLayer layer, Vector2 center, float radius, float count)
        {
            view.Carry += count;
            int whole = Mathf.FloorToInt(view.Carry);
            view.Carry -= whole;
            whole = Mathf.Min(whole, MaxPerLayer - view.System.particleCount);
            for (int i = 0; i < whole; i++)
            {
                float angle = Random01() * Mathf.PI * 2f, distance = Mathf.Sqrt(Random01()) * radius;
                EmitOne(view, layer, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
            }
        }

        private void EmitOne(View view, LocationParticleLayer layer, Vector2 position)
        {
            float angle = (layer.Angle + RandomRange(-.5f, .5f) * Mathf.Clamp(layer.Spread, 0f, 360f)) * Mathf.Deg2Rad;
            float speed = Mathf.Max(0f, layer.Speed) * RandomRange(.7f, 1.3f) * scale;
            float size = Mathf.Max(.002f, layer.Size) * (1f + RandomRange(-1f, 1f) * Mathf.Clamp01(layer.SizeJitter)) * scale;
            Color color = Color.Lerp(layer.Color, layer.ColorB, Random01());
            color.a = Mathf.Clamp01(layer.Opacity);
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, 0f),
                velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed,
                startSize = size,
                startLifetime = Mathf.Max(.05f, layer.Lifetime) * RandomRange(.75f, 1.25f),
                startColor = color,
                rotation = view.Kind == Kind.Leaves ? RandomRange(0f, 360f) : 0f,
                applyShapeToPosition = false
            };
            view.System.Emit(emit, 1);
        }

        // Рои мошкары медленно плавают по кадру (устойчиво по времени).
        private void UpdateSwarms(LocationParticleLayer layer)
        {
            int groups = Mathf.Clamp(layer.Groups, 1, 12);
            activeGroups = groups;
            for (int i = 0; i < groups; i++)
            {
                // Шум Перлина сам по себе кучкуется у 0,5 (все рои слипались в полосу): место роя — устойчивая
                // равномерная раскладка по кадру (дробные части кратных чисел с разным шагом по осям), шум только покачивает.
                float seed = i * 12.9898f;
                float bx = Mathf.Repeat(.1f + i * .4f, 1f), by = Mathf.Repeat(.2f + i * .7f, 1f);
                float x = Mathf.Clamp01(.08f + .84f * bx + (Mathf.PerlinNoise(seed, seconds * .05f) - .5f) * .5f);
                float y = Mathf.Clamp01(.08f + .84f * by + (Mathf.PerlinNoise(seed + 5.3f, seconds * .05f + 3.7f) - .5f) * .5f);
                swarmCenters[i] = new Vector2(Mathf.Lerp(-halfWidth, halfWidth, x), Mathf.Lerp(-halfHeight, halfHeight, y));
            }
        }

        public void CollectSwarmShares(List<Vector2> into)
        {
            into.Clear();
            if (halfWidth <= 0f || halfHeight <= 0f) return;
            for (int i = 0; i < activeGroups; i++)
                into.Add(new Vector2(swarmCenters[i].x / (2f * halfWidth) + .5f, swarmCenters[i].y / (2f * halfHeight) + .5f));
        }

        public void Dispose()
        {
            foreach (View view in views)
                if (view?.Material != null) Object.DestroyImmediate(view.Material);
            if (root != null) Object.DestroyImmediate(root);
            for (int i = 0; i < views.Length; i++) views[i] = null;
        }
    }
}
