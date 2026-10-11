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
    // рисунка. Расстояние — в плоскости земли (вертикаль разжата на
    // BattlefieldFrame.VerticalScale, как у гексов). Ноги за шаг — отрезок от
    // прошлой точки: быстрый шаг не перескакивает пучок; появление, смена
    // места и скачок дальше нескольких клеток — без отрезка.
    // Растения — в сеточном индексе; каждый шаг обрабатываются только
    // активные: рядом с источниками и ещё восстанавливающиеся.
    // Ковёр гнётся на CPU (LocationScatterCarpet.SetBend), объекты — в
    // шейдере по блоку свойств (_KsBend, _KsBendShape) тем же законом.
    public sealed partial class LocationWorldRenderer
    {
        private sealed class Plant
        {
            public ScatterView View;
            public LocationScatterCarpet Carpet;
            public int CarpetVersion;
            public LocationScatterCarpet.BendingPlant CarpetPlant;
            public Placed Object;
            public Vector2 Ground;
            public float Height;
            public Vector2 Bend;
            public bool Active;
            public Vector2 Side;
            // Объект: опора по Y и 1/высота в пространстве каждого рисунка.
            public Vector4[] Shapes;
        }

        private sealed class BendSource
        {
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
        private float plantCell = 64, plantReach;
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
                foreach (BendSource source in bendSources.Values) Touch(source);
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
                plant.Bend = Vector2.zero;
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
        private void UpdateBendSources(float deltaTime)
        {
            foreach (BendSource source in bendSources.Values) source.Seen = false;
            if (actorLayer != null && actorLayer.gameObject.activeInHierarchy)
                foreach (Actor actor in actors.Values) MoveSource(actor.Id, actor.Pixel, deltaTime);
            foreach (KeyValuePair<string, BattleFigure> entry in battleFigures)
                if (!entry.Value.Corpse) MoveSource("бой:" + entry.Key, entry.Value.Ground, deltaTime);
            goneSources.Clear();
            foreach (KeyValuePair<string, BendSource> entry in bendSources)
                if (!entry.Value.Seen) goneSources.Add(entry.Key);
            foreach (string id in goneSources) bendSources.Remove(id);
        }

        private void MoveSource(string id, Vector2 pixel, float deltaTime)
        {
            Vector2 ground = LocationGrassBend.ToGround(pixel, BattlefieldFrame.VerticalScale);
            if (!bendSources.TryGetValue(id, out BendSource source))
            {
                bendSources[id] = new BendSource { From = ground, To = ground, Seen = true };
                return;
            }
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
            source.Speed += (distance / deltaTime - source.Speed) * (1 - Mathf.Exp(-deltaTime / .12f));
        }

        // Растения у отрезка ног за шаг — в активный набор.
        private void Touch(BendSource source)
        {
            Vector2 min = Vector2.Min(source.From, source.To) - Vector2.one * plantReach;
            Vector2 max = Vector2.Max(source.From, source.To) + Vector2.one * plantReach;
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
            float radius = reaction.SafeRadius * HexSizePixels;
            Vector2 fallback = plant.Bend.sqrMagnitude > 1e-6f ? plant.Bend : plant.Side;
            Vector2 target = Vector2.zero;
            foreach (BendSource source in bendSources.Values)
            {
                float speed = source.Speed / walk;
                Vector2 moving = speed > .05f ? source.Direction : Vector2.zero;
                target += LocationGrassBend.Push(plant.Ground, source.From, source.To, radius,
                    LocationGrassBend.Strength(reaction.SafeSpeedInfluence, speed), moving, fallback);
            }
            target = LocationGrassBend.Limit(target);
            Vector2 next = LocationGrassBend.Approach(plant.Bend, target, deltaTime, reaction.SafeReactTime, reaction.SafeRecoverTime);
            bool rest = target.sqrMagnitude < 1e-8f && next.sqrMagnitude < 1e-5f;
            if (rest) next = Vector2.zero;
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
            LocationGrassBend.TopOffset(plant.Bend, plant.Height, reaction.SafeBend, reaction.SafePress, BattlefieldFrame.VerticalScale,
                out Vector2 offset, out float drop);
            if (plant.CarpetPlant != null)
            {
                if (plant.Carpet != null && plant.Carpet.Version == plant.CarpetVersion)
                    plant.Carpet.SetBend(plant.CarpetPlant, offset, drop, reaction.SafePinned);
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
            plantReach = 0;
            foreach (ScatterView view in scatter.Values)
            {
                LocationScatterLayer layer = view.Layer;
                if (view.Hidden || layer?.Reaction == null || !layer.Reaction.Enabled) continue;
                plantReach = Mathf.Max(plantReach, layer.Reaction.SafeRadius * HexSizePixels);
                if (view.Carpet != null)
                {
                    if (view.Carpet.BendingCount > 0) bentCarpets.Add(view.Carpet);
                    foreach (LocationScatterCarpet.BendingPlant item in view.Carpet.BendingPlants)
                        plants.Add(new Plant
                        {
                            View = view, Carpet = view.Carpet, CarpetVersion = view.Carpet.Version, CarpetPlant = item,
                            Ground = LocationGrassBend.ToGround(item.Pixel, BattlefieldFrame.VerticalScale), Height = item.Height, Side = SideOf(item.Key)
                        });
                    continue;
                }
                foreach (KeyValuePair<int, Placed> pair in view.Objects)
                {
                    Plant plant = ObjectPlant(view, pair.Key, pair.Value);
                    if (plant != null) plants.Add(plant);
                }
            }
            plantCell = Mathf.Max(32, plantReach);
            foreach (Plant plant in plants)
            {
                long key = CellKey(Mathf.FloorToInt(plant.Ground.x / plantCell), Mathf.FloorToInt(plant.Ground.y / plantCell));
                if (!plantCells.TryGetValue(key, out List<Plant> cell)) plantCells[key] = cell = new List<Plant>();
                cell.Add(plant);
            }
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
                shapes[i] = new Vector4(root, Mathf.Abs(top - root) > 1e-6f ? 1 / (top - root) : 0, 0, 0);
            }
            return new Plant
            {
                View = view, Object = entry, Height = height, Shapes = shapes, Side = SideOf(key),
                Ground = LocationGrassBend.ToGround(LocationVisualGeometry.ToPixel(Location, entry.Data.Position), BattlefieldFrame.VerticalScale)
            };
        }

        // Устойчивая сторона для ног ровно на опоре без движения.
        private static Vector2 SideOf(int key) => (key & 1) == 0 ? Vector2.right : Vector2.left;

        private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        // Отладка: контур воздействия у каждого источника (эллипс на земле).
        private void UpdateRadiusRings()
        {
            float radius = 0;
            if (vegetationOn)
                foreach (ScatterView view in scatter.Values)
                {
                    LocationScatterReaction reaction = view.Layer?.Reaction;
                    if (!view.Hidden && reaction != null && reaction.Enabled && reaction.ShowRadius)
                        radius = Mathf.Max(radius, reaction.SafeRadius * HexSizePixels);
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
                    Vector2 pixel = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * BattlefieldFrame.VerticalScale);
                    ring.SetPosition(i, LocationVisualGeometry.PixelToWorld(Location, pixel));
                }
            }
        }
    }
}
