using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Реакция растительности на проход: трава и кусты слоёв раскидки с
    // включённой «Реакцией на проход» плавно отклоняются от ног фигур и
    // возвращаются после ухода. Только картинка: проходимость, гексы, маршрут,
    // бой и состояние мира не меняются, в базу ничего не пишется.
    //
    // Источники — опоры ног фигур исследования и живых фигур боя на месте
    // (фактически показанные точки, с промежуточным движением), не центр
    // рисунка; какие из них задевают слой — настройки слоя. Расстояние — в
    // плоскости земли (вертикаль разжата на BattlefieldFrame.VerticalScale,
    // как у гексов). Ноги за шаг — отрезок от прошлой точки: быстрый шаг не
    // перескакивает пучок; появление, смена места и скачок дальше нескольких
    // клеток — без отрезка.
    // Растения — в сеточном индексе; каждый шаг обрабатываются только
    // активные: рядом с источниками и ещё восстанавливающиеся.
    // Ковёр гнётся на CPU (LocationScatterCarpet.SetBend), объекты — в
    // шейдере по блоку свойств (_KsBend, _KsBendShape) тем же законом.
    public sealed partial class LocationWorldRenderer
    {
        private enum SourceKind { Party, Retinue, Enemy, Battle }

        private sealed class Plant
        {
            public ScatterView View;
            public LocationScatterCarpet Carpet;
            public int CarpetVersion;
            public LocationScatterCarpet.BendingPlant CarpetPlant;
            public Placed Object;
            public Vector2 Ground;
            public float Height;
            public Vector2 Bend, Velocity;
            public float HoldLeft;
            public bool Active, Touched;
            public Vector2 Side;
            // Множители ассета (сила, радиус) и устойчивый разброс пучка −1..1.
            public float AssetBend = 1, AssetRadius = 1;
            public float BendJitter, TimeJitter;
            // Объект: опора по Y и 1/высота в пространстве каждого рисунка.
            public Vector4[] Shapes;
        }

        private sealed class BendSource
        {
            public SourceKind Kind;
            public Vector2 From, To;
            public Vector2 Direction;
            public float Speed;
            public bool Seen;
        }

        private static readonly int BendId = Shader.PropertyToID("_KsBend");
        private static readonly int BendShapeId = Shader.PropertyToID("_KsBendShape");
        // Скачок дальше стольких клеток за шаг — перенос, а не ходьба.
        private const float TeleportHexes = 3;

        private readonly List<Plant> plants = new List<Plant>();
        private readonly List<Plant> activePlants = new List<Plant>();
        private readonly Dictionary<long, List<Plant>> plantCells = new Dictionary<long, List<Plant>>();
        private readonly Dictionary<string, BendSource> bendSources = new Dictionary<string, BendSource>(StringComparer.Ordinal);
        private readonly List<string> goneSources = new List<string>();
        private readonly List<LocationScatterCarpet> bentCarpets = new List<LocationScatterCarpet>();
        private readonly MaterialPropertyBlock bendBlock = new MaterialPropertyBlock();
        private readonly System.Diagnostics.Stopwatch vegetationWatch = new System.Diagnostics.Stopwatch();
        private bool plantsDirty = true, vegetationOn;
        private float plantCell = 64, assetRadiusMax = 1;
        private double lastVegetationSeconds = double.NaN;
        private Transform radiusLayer;
        private readonly Dictionary<string, LineRenderer> radiusRings = new Dictionary<string, LineRenderer>(StringComparer.Ordinal);

        // Включается там, где фигуры ходят: игра и тестовый запуск места.
        // В окне базы трава от стоящих меток не шевелится.
        public bool VegetationReacts
        {
            get => vegetationOn;
            set
            {
                if (vegetationOn == value) return;
                vegetationOn = value;
                if (!value) ResetVegetation();
            }
        }

        // Для проверок и замеров.
        public int BendingPlantCount { get { if (plantsDirty) RebuildPlants(); return plants.Count; } }
        public int ActivePlantCount => activePlants.Count;
        public double LastVegetationMilliseconds { get; private set; }

        private void MarkPlantsDirty() => plantsDirty = true;

        // Шаг по времени места (SetTime): dt — по часам кадра, не больше 0,1 с.
        private void StepVegetationByClock(float seconds)
        {
            double previous = lastVegetationSeconds;
            lastVegetationSeconds = seconds;
            if (!vegetationOn || double.IsNaN(previous)) return;
            float delta = (float)(seconds - previous);
            if (delta > 0) StepVegetation(Mathf.Min(delta, .1f));
        }

        // Один шаг реакции: источники, новые соседи, прогиб активных.
        public void StepVegetation(float deltaTime)
        {
            if (!vegetationOn || !(deltaTime > 0)) return;
            vegetationWatch.Restart();
            if (plantsDirty) RebuildPlants();
            UpdateBendSources(deltaTime);
            UpdateRadiusRings();
            if (plants.Count > 0)
            {
                float reach = PlantReach();
                foreach (BendSource source in bendSources.Values) Touch(source, reach);
                float walk = Mathf.Max(1, BattlefieldDatabaseAsset.SharedWalkHexesPerSecond * HexSizePixels * 1.5f);
                for (int i = activePlants.Count - 1; i >= 0; i--)
                {
                    Plant plant = activePlants[i];
                    if (!StepPlant(plant, deltaTime, walk))
                    {
                        plant.Active = false;
                        activePlants[i] = activePlants[activePlants.Count - 1];
                        activePlants.RemoveAt(activePlants.Count - 1);
                    }
                }
                foreach (LocationScatterCarpet carpet in bentCarpets) carpet.FlushBends();
            }
            LastVegetationMilliseconds = vegetationWatch.Elapsed.TotalMilliseconds;
        }

        // Все растения — в исходную форму, источники забыты (выключение,
        // смена места, пересборка слоёв).
        public void ResetVegetation()
        {
            foreach (Plant plant in activePlants)
            {
                plant.Bend = plant.Velocity = Vector2.zero;
                plant.HoldLeft = 0;
                plant.Touched = false;
                plant.Active = false;
                Apply(plant);
            }
            activePlants.Clear();
            foreach (LocationScatterCarpet carpet in bentCarpets) carpet.FlushBends();
            bendSources.Clear();
            lastVegetationSeconds = double.NaN;
            UpdateRadiusRings();
        }

        // Опоры ног: фигуры исследования (если показаны) и живые фигуры боя.
        // Скорость сглаживается самым долгим «Сглаживанием скорости» слоёв.
        private void UpdateBendSources(float deltaTime)
        {
            float smoothing = .01f;
            foreach (ScatterView view in scatter.Values)
            {
                LocationScatterReaction reaction = view.Layer?.Reaction;
                if (reaction != null && reaction.Enabled) smoothing = Mathf.Max(smoothing, reaction.SafeSpeedSmoothing);
            }
            foreach (BendSource source in bendSources.Values) source.Seen = false;
            if (actorLayer != null && actorLayer.gameObject.activeInHierarchy)
                foreach (Actor actor in actors.Values)
                    MoveSource(actor.Id, actor.Kind == ActorKind.Enemy ? SourceKind.Enemy : actor.Kind == ActorKind.Retinue ? SourceKind.Retinue : SourceKind.Party,
                        actor.Pixel, deltaTime, smoothing);
            foreach (KeyValuePair<string, BattleFigure> entry in battleFigures)
                if (!entry.Value.Corpse) MoveSource(entry.Value.SourceId ??= "бой:" + entry.Key, SourceKind.Battle, entry.Value.Ground, deltaTime, smoothing);
            goneSources.Clear();
            foreach (KeyValuePair<string, BendSource> entry in bendSources)
                if (!entry.Value.Seen) goneSources.Add(entry.Key);
            foreach (string id in goneSources) bendSources.Remove(id);
        }

        private void MoveSource(string id, SourceKind kind, Vector2 pixel, float deltaTime, float smoothing)
        {
            Vector2 ground = LocationGrassBend.ToGround(pixel, BattlefieldFrame.VerticalScale);
            if (!bendSources.TryGetValue(id, out BendSource source))
            {
                bendSources[id] = new BendSource { Kind = kind, From = ground, To = ground, Seen = true };
                return;
            }
            source.Kind = kind;
            source.Seen = true;
            source.From = source.To;
            source.To = ground;
            Vector2 step = source.To - source.From;
            float distance = step.magnitude;
            if (distance > TeleportHexes * HexSizePixels)
            {
                source.From = source.To;
                source.Speed = 0;
                return;
            }
            if (distance > 1e-3f) source.Direction = step / distance;
            // Скорость сглажена: после остановки её доля затухает, а не обрывается.
            source.Speed += (distance / deltaTime - source.Speed) * (1 - Mathf.Exp(-deltaTime / smoothing));
        }

        private static bool Touches(LocationScatterReaction reaction, SourceKind kind)
        {
            switch (kind)
            {
                case SourceKind.Retinue: return reaction.ByRetinue;
                case SourceKind.Enemy: return reaction.ByEnemies;
                case SourceKind.Battle: return reaction.ByBattle;
                default: return reaction.ByParty;
            }
        }

        // Наибольшая дальность касания слоёв (пиксели плоскости земли) —
        // для поиска соседей; читается на ходу, как и сами настройки.
        private float PlantReach()
        {
            float reach = 0;
            foreach (ScatterView view in scatter.Values)
            {
                LocationScatterReaction reaction = view.Layer?.Reaction;
                if (view.Hidden || reaction == null || !reaction.Enabled) continue;
                reach = Mathf.Max(reach, reaction.SafeRadius * (1 + reaction.SafeLead) * Mathf.Max(1, reaction.SafeDepthStretch));
            }
            return reach * HexSizePixels * assetRadiusMax;
        }

        // Растения у отрезка ног за шаг — в активный набор.
        private void Touch(BendSource source, float reach)
        {
            Vector2 min = Vector2.Min(source.From, source.To) - Vector2.one * reach;
            Vector2 max = Vector2.Max(source.From, source.To) + Vector2.one * reach;
            int x0 = Mathf.FloorToInt(min.x / plantCell), x1 = Mathf.FloorToInt(max.x / plantCell);
            int y0 = Mathf.FloorToInt(min.y / plantCell), y1 = Mathf.FloorToInt(max.y / plantCell);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!plantCells.TryGetValue(CellKey(x, y), out List<Plant> cell)) continue;
                    foreach (Plant plant in cell)
                    {
                        if (plant.Active) continue;
                        plant.Active = true;
                        activePlants.Add(plant);
                    }
                }
        }

        // false — растение вернулось в исходную форму и выходит из набора.
        private bool StepPlant(Plant plant, float deltaTime, float walk)
        {
            LocationScatterReaction reaction = plant.View.Layer.Reaction;
            float radius = reaction.SafeRadius * HexSizePixels * plant.AssetRadius;
            LocationGrassBend.Zone zone = new LocationGrassBend.Zone
            {
                Core = reaction.SafeCore, Softness = reaction.SafeEdgeSoftness, DepthStretch = reaction.SafeDepthStretch,
                Lead = reaction.SafeLead, Forward = reaction.SafeForward
            };
            float fullSpeed = walk * reaction.SafeSpeedReference;
            Vector2 fallback = plant.Bend.sqrMagnitude > 1e-6f ? plant.Bend : plant.Side;
            Vector2 target = Vector2.zero;
            foreach (BendSource source in bendSources.Values)
            {
                if (!Touches(reaction, source.Kind)) continue;
                float speed = source.Speed / fullSpeed;
                Vector2 moving = speed > .05f ? source.Direction : Vector2.zero;
                target += LocationGrassBend.Push(plant.Ground, source.From, source.To, radius,
                    LocationGrassBend.Strength(reaction.SafeSpeedInfluence, speed), moving, fallback, zone);
            }
            target = LocationGrassBend.Limit(target, reaction.SafeSumLimit);
            float time = Mathf.Max(.1f, 1 + reaction.SafeTimeJitter * plant.TimeJitter);
            bool touching = target.sqrMagnitude > 1e-8f;
            // Задержка отсчитывается с ухода последней фигуры.
            if (touching) { plant.Touched = true; plant.HoldLeft = 0; }
            else if (plant.Touched) { plant.Touched = false; plant.HoldLeft = reaction.SafeHold * time; }
            if (!touching && plant.HoldLeft > 0)
            {
                // Задержка перед возвратом: держит прогиб, как был.
                plant.HoldLeft -= deltaTime;
                plant.Velocity = Vector2.zero;
                return true;
            }
            Vector2 next = plant.Bend;
            if (reaction.SafeSpring > 0 && target.sqrMagnitude < plant.Bend.sqrMagnitude)
                LocationGrassBend.SpringStep(ref next, ref plant.Velocity, target, deltaTime, reaction.SafeSpringFrequency / time, reaction.SafeSpring);
            else
            {
                next = LocationGrassBend.Approach(plant.Bend, target, deltaTime, reaction.SafeReactTime * time, reaction.SafeRecoverTime * time);
                plant.Velocity = (next - plant.Bend) / deltaTime;
            }
            bool rest = !touching && next.sqrMagnitude < 1e-5f && plant.Velocity.sqrMagnitude < 1e-3f;
            if (rest)
            {
                next = Vector2.zero;
                plant.Velocity = Vector2.zero;
            }
            if (next != plant.Bend)
            {
                plant.Bend = next;
                Apply(plant);
            }
            return !rest;
        }

        private void Apply(Plant plant)
        {
            LocationScatterReaction reaction = plant.View.Layer.Reaction;
            float bend = Mathf.Clamp(reaction.SafeBend * plant.AssetBend * (1 + reaction.SafeBendJitter * plant.BendJitter), 0, LocationScatterReaction.MaxBend * 2);
            LocationGrassBend.TopOffset(plant.Bend, plant.Height, bend, reaction.SafePress, BattlefieldFrame.VerticalScale,
                out Vector2 offset, out float drop, reaction.SafeDepthLean, reaction.SafeArcDrop);
            if (plant.CarpetPlant != null)
            {
                if (plant.Carpet != null && plant.Carpet.Version == plant.CarpetVersion)
                    plant.Carpet.SetBend(plant.CarpetPlant, offset, drop, reaction.SafePinned, reaction.SafeCurve);
                return;
            }
            Placed entry = plant.Object;
            if (entry?.Images == null || plant.Shapes == null) return;
            Vector3 world = Root.transform.TransformVector(offset);
            float worldDrop = drop * Root.transform.lossyScale.y;
            for (int i = 0; i < entry.Images.Count && i < plant.Shapes.Length; i++)
            {
                SpriteRenderer image = entry.Images[i];
                if (image == null) continue;
                image.GetPropertyBlock(bendBlock);
                bendBlock.SetVector(BendId, new Vector4(world.x, world.y, worldDrop, 0));
                Vector4 shape = plant.Shapes[i];
                shape.z = reaction.SafePinned;
                shape.w = reaction.SafeCurve;
                bendBlock.SetVector(BendShapeId, shape);
                image.SetPropertyBlock(bendBlock);
            }
        }

        // Реестр гнущихся растений по слоям раскидки; прежний прогиб снимается.
        private void RebuildPlants()
        {
            foreach (Plant plant in activePlants)
            {
                plant.Bend = Vector2.zero;
                Apply(plant);
            }
            foreach (LocationScatterCarpet carpet in bentCarpets) carpet.FlushBends();
            plants.Clear();
            activePlants.Clear();
            plantCells.Clear();
            bentCarpets.Clear();
            plantsDirty = false;
            assetRadiusMax = 1;
            float reach = 0;
            foreach (ScatterView view in scatter.Values)
            {
                LocationScatterLayer layer = view.Layer;
                if (view.Hidden || layer?.Reaction == null || !layer.Reaction.Enabled) continue;
                reach = Mathf.Max(reach, layer.Reaction.SafeRadius * HexSizePixels);
                if (view.Carpet != null)
                {
                    if (view.Carpet.BendingCount > 0) bentCarpets.Add(view.Carpet);
                    foreach (LocationScatterCarpet.BendingPlant item in view.Carpet.BendingPlants)
                    {
                        LocationScatterInstance instance = layer.Instances.Find(entry => entry != null && entry.Key == item.Key);
                        plants.Add(Configure(new Plant
                        {
                            View = view, Carpet = view.Carpet, CarpetVersion = view.Carpet.Version, CarpetPlant = item,
                            Ground = LocationGrassBend.ToGround(item.Pixel, BattlefieldFrame.VerticalScale), Height = item.Height
                        }, layer, item.Key, instance?.AssetId));
                    }
                    continue;
                }
                foreach (KeyValuePair<int, Placed> pair in view.Objects)
                {
                    Plant plant = ObjectPlant(view, pair.Key, pair.Value);
                    if (plant != null) plants.Add(Configure(plant, layer, pair.Key, pair.Value.Data.AssetId));
                }
            }
            plantCell = Mathf.Max(32, reach);
            // Активный набор не растёт в шаге — без выделения памяти по ходу.
            if (activePlants.Capacity < plants.Count) activePlants.Capacity = plants.Count;
            foreach (Plant plant in plants)
            {
                long key = CellKey(Mathf.FloorToInt(plant.Ground.x / plantCell), Mathf.FloorToInt(plant.Ground.y / plantCell));
                if (!plantCells.TryGetValue(key, out List<Plant> cell)) plantCells[key] = cell = new List<Plant>();
                cell.Add(plant);
            }
        }

        // Множители ассета кисти и устойчивый разброс пучка (по слою и ключу).
        private Plant Configure(Plant plant, LocationScatterLayer layer, int key, string assetId)
        {
            LocationScatterEntry entry = assetId != null ? layer.Assets.Find(item => item != null && item.AssetId == assetId) : null;
            plant.AssetBend = entry != null ? Mathf.Clamp(entry.BendScale, 0, LocationScatterReaction.MaxAssetScale) : 1;
            plant.AssetRadius = entry != null ? Mathf.Clamp(entry.RadiusScale, LocationScatterReaction.MinAssetRadius, LocationScatterReaction.MaxAssetScale) : 1;
            assetRadiusMax = Mathf.Max(assetRadiusMax, plant.AssetRadius);
            uint hash = (uint)key * 2654435761u ^ (uint)layer.Id.GetHashCode();
            plant.BendJitter = (hash & 0xffff) / 32767.5f - 1;
            plant.TimeJitter = ((hash >> 16) & 0xffff) / 32767.5f - 1;
            plant.Side = (key & 1) == 0 ? Vector2.right : Vector2.left;
            return plant;
        }

        // Объект: высота растения над опорой — по самой высокой части; для
        // каждого рисунка — опора и высота в его собственном пространстве
        // (с поворотом, растяжением и отражением рисунка).
        private Plant ObjectPlant(ScatterView view, int key, Placed entry)
        {
            if (!entry.Bends || entry.Images.Count == 0) return null;
            float height = 0;
            foreach (LocationResolvedPart part in entry.Parts)
                if (part != null) height = Mathf.Max(height, part.Height * (1 - Mathf.Clamp01(part.Pivot.y)));
            if (height <= 1e-4f) return null;
            Vector4[] shapes = new Vector4[entry.Images.Count];
            Vector3 anchor = entry.Anchor.position;
            for (int i = 0; i < entry.Images.Count; i++)
            {
                Transform image = entry.Images[i] != null ? entry.Images[i].transform : null;
                if (image == null) continue;
                float root = image.InverseTransformPoint(anchor).y;
                float top = image.InverseTransformPoint(anchor + image.up * height * Root.transform.lossyScale.y).y;
                shapes[i] = new Vector4(root, Mathf.Abs(top - root) > 1e-6f ? 1 / (top - root) : 0, 0, 2);
            }
            return new Plant
            {
                View = view, Object = entry, Height = height, Shapes = shapes,
                Ground = LocationGrassBend.ToGround(LocationVisualGeometry.ToPixel(Location, entry.Data.Position), BattlefieldFrame.VerticalScale)
            };
        }

        private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        // Отладка: контур воздействия у каждого источника (эллипс на земле).
        private void UpdateRadiusRings()
        {
            float radius = 0, depth = 1;
            if (vegetationOn)
                foreach (ScatterView view in scatter.Values)
                {
                    LocationScatterReaction reaction = view.Layer?.Reaction;
                    if (view.Hidden || reaction == null || !reaction.Enabled || !reaction.ShowRadius) continue;
                    if (reaction.SafeRadius * HexSizePixels <= radius) continue;
                    radius = reaction.SafeRadius * HexSizePixels;
                    depth = reaction.SafeDepthStretch;
                }
            if (radius <= 0)
            {
                if (radiusRings.Count == 0) return;
                foreach (LineRenderer ring in radiusRings.Values)
                    if (ring != null) Destroy(ring.gameObject);
                radiusRings.Clear();
                return;
            }
            if (radiusLayer == null) radiusLayer = Child("Реакция травы · радиус").transform;
            goneSources.Clear();
            foreach (string id in radiusRings.Keys)
                if (!bendSources.ContainsKey(id)) goneSources.Add(id);
            foreach (string id in goneSources)
            {
                if (radiusRings[id] != null) Destroy(radiusRings[id].gameObject);
                radiusRings.Remove(id);
            }
            const int points = 40;
            foreach (KeyValuePair<string, BendSource> entry in bendSources)
            {
                if (!radiusRings.TryGetValue(entry.Key, out LineRenderer ring) || ring == null)
                {
                    GameObject target = new GameObject(entry.Key);
                    target.transform.SetParent(radiusLayer, false);
                    ring = target.AddComponent<LineRenderer>();
                    ring.sharedMaterial = unlit;
                    ring.useWorldSpace = false;
                    ring.loop = true;
                    ring.positionCount = points;
                    ring.widthMultiplier = 2f / LocationVisualGeometry.PixelsPerUnit;
                    ring.startColor = ring.endColor = new Color(1, .85f, .3f, .8f);
                    ring.sortingOrder = LocationVisualGeometry.SortOrder(LocationVisualBand.Foreground, 0, 900);
                    radiusRings[entry.Key] = ring;
                }
                Vector2 center = new Vector2(entry.Value.To.x, entry.Value.To.y * BattlefieldFrame.VerticalScale);
                for (int i = 0; i < points; i++)
                {
                    float angle = i * Mathf.PI * 2 / points;
                    Vector2 pixel = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * BattlefieldFrame.VerticalScale * depth);
                    ring.SetPosition(i, LocationVisualGeometry.PixelToWorld(Location, pixel));
                }
            }
        }
    }
}
