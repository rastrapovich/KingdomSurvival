using System;
using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12Р: цвет экземпляра — сдвиг тона (градусы), насыщенность и яркость
    // (множители) и подкраска (умножение). По умолчанию — рисунок как есть.
    [Serializable]
    public sealed class LocationColorAdjust
    {
        public float Hue;
        public float Saturation = 1;
        public float Brightness = 1;
        public Color Tint = Color.white;

        public bool HasHsv => Mathf.Abs(Mathf.DeltaAngle(0, Hue)) > .01f || Mathf.Abs(Saturation - 1) > .001f || Mathf.Abs(Brightness - 1) > .001f;
        public bool IsIdentity => !HasHsv && Tint == Color.white;

        // Для шейдера: тон в оборотах, насыщенность, яркость.
        public Vector4 ShaderHsv => new Vector4(Mathf.DeltaAngle(0, Hue) / 360f, Mathf.Max(0, Saturation), Mathf.Max(0, Brightness), 0);

        public LocationColorAdjust Clone() => new LocationColorAdjust { Hue = Hue, Saturation = Saturation, Brightness = Brightness, Tint = Tint };
    }

    // Как слой рисуется. Объекты — каждый экземпляр как предмет места:
    // сортировка с людьми, нормали, тени, проходимость. Ковёр — одной пачкой,
    // как неподвижные частицы, слоем под (или над) всеми: тысячи штук почти
    // бесплатно, но без сортировки с людьми, теней и проходимости.
    public enum LocationScatterMode { Objects, Carpet }
    public enum LocationScatterFlip { Never, Random, Always }
    public enum LocationScatterViewMode { Fixed, Random }
    public enum LocationScatterTool { Paint, Erase, Recolor }

    // Ассет в кисти: вес (как часто выпадает) и свой множитель размера.
    [Serializable]
    public sealed class LocationScatterEntry
    {
        public string AssetId = "";
        public float Weight = 1;
        public float ScaleMultiplier = 1;
        public bool Enabled = true;
        // Реакция на проход: гнётся ли этот ассет, если она включена у слоя
        // (камни в том же слое не гнутся).
        public bool Bends = true;
        // Свои множители реакции ассета: сила прогиба и радиус касания.
        public float BendScale = 1;
        public float RadiusScale = 1;
    }

    public enum LocationScatterReactionPreset { SmallGrass, TallGrass, SoftBush }

    // Реакция растительности на проход: временный визуальный прогиб от ног
    // фигур и плавное восстановление. Не меняет проходимость, гексы, маршрут
    // и состояние мира; в базу пишутся только настройки, не прогиб.
    // Доли — от высоты растения над опорой; радиус — в размерах клетки боя
    // (рост фигур тоже от неё), поэтому камера и масштаб его не меняют.
    [Serializable]
    public sealed class LocationScatterReaction
    {
        public const float MinRadius = .05f, MaxRadius = 2f;
        public const float MaxBend = .5f, MaxPress = .25f;
        public const float MinTime = .02f, MaxTime = 5f;
        public const float MaxPinned = .9f;

        public bool Enabled;

        // --- Кто задевает -------------------------------------------------
        // Командир и бойцы отряда, свита, противники в исследовании, фигуры
        // боя на месте (обе стороны).
        public bool ByParty = true;
        public bool ByRetinue = true;
        public bool ByEnemies = true;
        public bool ByBattle = true;

        // --- Зона касания -------------------------------------------------
        // Радиус воздействия, клеток боя.
        public float Radius = .45f;
        // Полная сила у самых ног: доля радиуса без спада (0 — спад от центра).
        public float Core;
        // Мягкость края: 1 — плавно с самого центра, больше — дольше держит
        // силу и резче спадает к краю.
        public float EdgeSoftness = 2;
        // Вытянутость зоны вглубь картинки: 1 — как гексы на земле, меньше —
        // зона плоская (задевает только сбоку), больше — длиннее вглубь.
        public float DepthStretch = 1;
        // Зона сдвинута вперёд по ходу (доля радиуса): трава расступается заранее.
        public float Lead;

        // --- Форма прогиба ------------------------------------------------
        // Наибольшее отклонение верха, доля высоты.
        public float Bend = .1f;
        // Приминание: опускание верха при полном контакте, доля высоты.
        public float Press = .03f;
        // Закреплённая нижняя часть (доля высоты над опорой) — не сдвигается.
        public float Pinned = .25f;
        // Изгиб по высоте: 1 — прямой наклон (скос), 2 — дуга, больше —
        // гнётся только верхушка.
        public float Curve = 2;
        // Наклон вглубь картинки (к зрителю / от зрителя) относительно бокового.
        public float DepthLean = 1;
        // Укорочение дуги: верх, уходя вбок, опускается (0 — не опускается).
        public float ArcDrop = 1;
        // Направление: 0 — от ног (расступается в стороны), 1 — по ходу фигуры.
        public float Forward;
        // Предел суммы нескольких фигур (доля полного прогиба).
        public float SumLimit = 1;

        // --- Время --------------------------------------------------------
        // Время, за которое растение уступает и возвращается (с).
        public float ReactTime = .12f;
        public float RecoverTime = .6f;
        // Задержка перед возвратом после ухода (с).
        public float Hold;
        // Покачивание при отпускании: 0 — плавно без перехлёста, 1 — сильно.
        public float Spring;
        // Частота покачивания, колебаний в секунду.
        public float SpringFrequency = 2.5f;

        // --- Движение -----------------------------------------------------
        // Влияние скорости: 0 — стоящий гнёт так же, как идущий; 1 — только идущий.
        public float SpeedInfluence = .3f;
        // Скорость полного касания, доля обычного шага: меньше — полная сила
        // уже при медленном шаге.
        public float SpeedReference = 1;
        // Сглаживание скорости (с): насколько плавно затухает доля движения
        // после остановки.
        public float SpeedSmoothing = .12f;

        // --- Разброс по пучкам --------------------------------------------
        // Упругость: каждый пучок гнётся сильнее или слабее на ± эту долю.
        public float BendJitter;
        // Время: реакция и возврат у каждого пучка дольше или короче на ± долю.
        public float TimeJitter;

        // Отладка: контур воздействия у фигур.
        public bool ShowRadius;

        public float SafeRadius => Mathf.Clamp(Radius, MinRadius, MaxRadius);
        public float SafeCore => Mathf.Clamp(Core, 0, .9f);
        public float SafeEdgeSoftness => Mathf.Clamp(EdgeSoftness, .5f, 6);
        public float SafeDepthStretch => Mathf.Clamp(DepthStretch, .2f, 3);
        public float SafeLead => Mathf.Clamp(Lead, 0, 1);
        public float SafeBend => Mathf.Clamp(Bend, 0, MaxBend);
        public float SafePress => Mathf.Clamp(Press, 0, MaxPress);
        public float SafePinned => Mathf.Clamp(Pinned, 0, MaxPinned);
        public float SafeCurve => Mathf.Clamp(Curve, 1, 4);
        public float SafeDepthLean => Mathf.Clamp(DepthLean, 0, 2);
        public float SafeArcDrop => Mathf.Clamp(ArcDrop, 0, 2);
        public float SafeForward => Mathf.Clamp01(Forward);
        public float SafeSumLimit => Mathf.Clamp(SumLimit, .1f, 1);
        public float SafeReactTime => Mathf.Clamp(ReactTime, MinTime, MaxTime);
        public float SafeRecoverTime => Mathf.Clamp(RecoverTime, MinTime, MaxTime);
        public float SafeHold => Mathf.Clamp(Hold, 0, 3);
        public float SafeSpring => Mathf.Clamp01(Spring);
        public float SafeSpringFrequency => Mathf.Clamp(SpringFrequency, .3f, 8);
        public float SafeSpeedInfluence => Mathf.Clamp01(SpeedInfluence);
        public float SafeSpeedReference => Mathf.Clamp(SpeedReference, .1f, 3);
        public float SafeSpeedSmoothing => Mathf.Clamp(SpeedSmoothing, .01f, 2);
        public float SafeBendJitter => Mathf.Clamp(BendJitter, 0, .9f);
        public float SafeTimeJitter => Mathf.Clamp(TimeJitter, 0, .9f);

        // Стартовые настройки эффекта, не ботаническая классификация.
        // «Кто задевает», отладка и включение не меняются.
        public void Apply(LocationScatterReactionPreset preset)
        {
            Core = 0; EdgeSoftness = 2; DepthStretch = 1; Lead = 0;
            Curve = 2; DepthLean = 1; ArcDrop = 1; Forward = 0; SumLimit = 1;
            Hold = 0; Spring = 0; SpringFrequency = 2.5f;
            SpeedReference = 1; SpeedSmoothing = .12f; BendJitter = 0; TimeJitter = 0;
            switch (preset)
            {
                case LocationScatterReactionPreset.TallGrass:
                    Radius = .55f; Bend = .2f; Press = .04f; ReactTime = .15f; RecoverTime = .9f; Pinned = .2f; SpeedInfluence = .35f;
                    Curve = 2.2f; Forward = .15f; Lead = .1f; BendJitter = .2f; TimeJitter = .15f;
                    break;
                case LocationScatterReactionPreset.SoftBush:
                    Radius = .7f; Bend = .12f; Press = .02f; ReactTime = .22f; RecoverTime = 1.2f; Pinned = .35f; SpeedInfluence = .25f;
                    Curve = 1.6f; Hold = .1f; Spring = .2f; SpringFrequency = 1.8f; BendJitter = .15f; TimeJitter = .2f;
                    break;
                default:
                    Radius = .45f; Bend = .1f; Press = .03f; ReactTime = .12f; RecoverTime = .6f; Pinned = .25f; SpeedInfluence = .3f;
                    BendJitter = .15f; TimeJitter = .1f;
                    break;
            }
        }
    }

    // Настройки кисти слоя. Пиксели — пиксели рисунка места. Пары min/max —
    // равномерный разброс; одинаковые значения — без разброса.
    [Serializable]
    public sealed class LocationScatterBrush
    {
        public float Radius = 90;
        // Итоговая плотность: штук на квадрат 100×100 пикселей рисунка места.
        // Повторный мазок по заполненному месту не добавляет сверх неё.
        public float Density = 3;
        // Наименьшее расстояние между опорами (пиксели), в том числе до уже стоящих.
        public float MinDistance = 16;
        // 0 — ровно до края круга, 1 — к краю редеет до нуля.
        public float Falloff = .4f;
        // Нажим: какую долю недостающего до плотности добавляет один отпечаток
        // кисти. 1 — сразу до плотности; меньше — мазок набирает постепенно.
        public float Flow = 1;
        // Поверх уже стоящих: мазок не считает прежние экземпляры слоя (ни для
        // плотности, ни для наименьшего расстояния) — новый «слой» той же кисти.
        public bool PaintOver;

        public Vector2 Scale = new Vector2(.85f, 1.15f);
        // Растяжение / сжатие по горизонтали (множитель ширины).
        public Vector2 Stretch = new Vector2(1, 1);
        // Поворот вокруг опоры, градусы против часовой.
        public Vector2 Rotation = new Vector2(0, 0);
        public LocationScatterFlip Flip = LocationScatterFlip.Random;
        public LocationScatterViewMode ViewMode = LocationScatterViewMode.Random;
        public ArtAssetView View = ArtAssetView.Front;

        // Цвет: общий сдвиг тона и разброс ± (градусы), множители насыщенности
        // и яркости, подкраска — случайная между двумя цветами.
        public float HueShift;
        public float HueJitter = 15;
        public Vector2 Saturation = new Vector2(1, 1);
        public Vector2 Brightness = new Vector2(.9f, 1.1f);
        public Color TintA = Color.white;
        public Color TintB = Color.white;

        // Где можно: только на проходимом, не на основаниях предметов,
        // только на выбранных типах местности (пусто — на любых).
        public bool OnlyPassable;
        public bool AvoidObjects = true;
        public List<WorldMapGameplayTerrainType> Terrain = new List<WorldMapGameplayTerrainType>();

        // Ластик и перекраска — только ассеты этой кисти.
        public bool OnlyBrushAssets;
    }

    // Один экземпляр слоя. Компактно: слой хранит сотни и тысячи штук.
    [Serializable]
    public sealed class LocationScatterInstance
    {
        public int Key;
        public string AssetId = "";
        public ArtAssetView View;
        // Опора — доля рисунка места (Y вниз), как у предметов.
        public Vector2 Position;
        public float Scale = 1;
        public float Stretch = 1;
        public float Rotation;
        public bool FlipX;
        public float Hue;
        public float Saturation = 1;
        public float Brightness = 1;
        public Color Tint = Color.white;

        public LocationColorAdjust ColorAdjust => new LocationColorAdjust { Hue = Hue, Saturation = Saturation, Brightness = Brightness, Tint = Tint };
    }

    // ПР-12Р: слой раскидки места — трава, камни, мох, цветы. Свой набор
    // ассетов с весами, своя кисть и экземпляры. Ассеты — по ID Базы ассетов.
    [Serializable]
    public sealed class LocationScatterLayer
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Трава";
        public bool Hidden;
        public bool Locked;
        public LocationScatterMode Mode = LocationScatterMode.Objects;
        // Объекты: «Детали земли» — всегда под людьми, «Объекты и персонажи» —
        // по глубине с людьми, «Передний план» — всегда над. Ковёр по глубине
        // с людьми не сортируется: «Объекты и персонажи» у него — как детали земли.
        public LocationVisualBand Band = LocationVisualBand.World;
        public int OrderOffset;
        // Объекты: тень от солнца (силуэт) и от огня (по контуру рисунка);
        // проходимость — как у ассета (иначе не мешает никогда).
        public bool ProjectsShadow;
        // Тень от огня: 0 — прежний слой (как тень от солнца), 1 — да, 2 — нет.
        public int FireShadowState;
        public bool ProjectsFireShadow
        {
            get => FireShadowState == 0 ? ProjectsShadow : FireShadowState == 1;
            set => FireShadowState = value ? 1 : 2;
        }
        public bool BlocksMovement;
        public List<LocationScatterEntry> Assets = new List<LocationScatterEntry>();
        public LocationScatterBrush Brush = new LocationScatterBrush();
        public LocationScatterReaction Reaction = new LocationScatterReaction();
        public List<LocationScatterInstance> Instances = new List<LocationScatterInstance>();
        public int NextKey = 1;

        public string InstanceId(LocationScatterInstance instance) => Id + "#" + instance.Key;

        // Гнётся ли экземпляр этого ассета от прохода. Ассета уже нет в
        // кисти — как у слоя.
        public bool Bends(string assetId)
        {
            if (Reaction == null || !Reaction.Enabled || Hidden) return false;
            LocationScatterEntry entry = Assets?.Find(item => item != null && item.AssetId == assetId);
            return entry == null || entry.Bends;
        }

        // Порядок ковра: детали земли или передний план.
        public LocationVisualBand CarpetBand => Band == LocationVisualBand.Foreground ? LocationVisualBand.Foreground
            : Band == LocationVisualBand.Ground ? LocationVisualBand.Ground : LocationVisualBand.GroundDetail;

        // Экземпляр как предмет места: тот же разрешатель, рендерер и
        // проходимость, что у поставленных вручную. Слой задаёт слой рисунка
        // и тени; проходимость — как у ассета или «не мешает».
        public LocationVisualObject ToObject(LocationScatterInstance instance)
        {
            LocationVisualObject item = new LocationVisualObject
            {
                Id = InstanceId(instance), Name = Name + " " + instance.Key, AssetId = instance.AssetId, View = instance.View,
                Position = instance.Position, Scale = instance.Scale, Stretch = instance.Stretch, Rotation = instance.Rotation,
                FlipX = instance.FlipX, ColorAdjust = instance.ColorAdjust, Band = Band, OrderOffset = OrderOffset,
                ProjectsShadow = ProjectsShadow && Mode == LocationScatterMode.Objects,
                ProjectsFireShadow = ProjectsFireShadow && Mode == LocationScatterMode.Objects, CastsShadow = false, AssetFootprint = true,
                Overrides = LocationAssetOverride.Layer | LocationAssetOverride.Shadows
            };
            if (!BlocksMovement)
            {
                item.Overrides |= LocationAssetOverride.Passability;
                item.BlocksMovement = false;
            }
            return item;
        }
    }

    // Быстрый поиск соседей для наименьшего расстояния (пиксели рисунка).
    public sealed class LocationScatterIndex
    {
        private readonly float cell;
        private readonly Dictionary<long, List<Vector2>> cells = new Dictionary<long, List<Vector2>>();

        public LocationScatterIndex(float cellSize) => cell = Mathf.Max(4, cellSize);

        public static LocationScatterIndex Of(LocationScatterLayer layer, LocalLocationDefinition location)
        {
            LocationScatterIndex index = new LocationScatterIndex(Mathf.Max(8, layer.Brush.MinDistance));
            foreach (LocationScatterInstance instance in layer.Instances)
                index.Add(LocationVisualGeometry.ToPixel(location, instance.Position));
            return index;
        }

        private long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        public void Add(Vector2 point)
        {
            long key = Key(Mathf.FloorToInt(point.x / cell), Mathf.FloorToInt(point.y / cell));
            if (!cells.TryGetValue(key, out List<Vector2> list)) cells[key] = list = new List<Vector2>();
            list.Add(point);
        }

        public void Remove(Vector2 point)
        {
            long key = Key(Mathf.FloorToInt(point.x / cell), Mathf.FloorToInt(point.y / cell));
            if (!cells.TryGetValue(key, out List<Vector2> list)) return;
            int found = list.FindIndex(item => (item - point).sqrMagnitude < 1e-4f);
            if (found >= 0) list.RemoveAt(found);
        }

        public bool AnyWithin(Vector2 point, float distance)
        {
            if (distance <= 0) return false;
            int reach = Mathf.CeilToInt(distance / cell);
            int cx = Mathf.FloorToInt(point.x / cell), cy = Mathf.FloorToInt(point.y / cell);
            float squared = distance * distance;
            for (int x = cx - reach; x <= cx + reach; x++)
                for (int y = cy - reach; y <= cy + reach; y++)
                {
                    if (!cells.TryGetValue(Key(x, y), out List<Vector2> list)) continue;
                    foreach (Vector2 other in list)
                        if ((other - point).sqrMagnitude < squared) return true;
                }
            return false;
        }
    }

    // ПР-12Р: кисть раскидки. Мазок досыпает экземпляры в круг до заданной
    // плотности (с учётом уже стоящих), соблюдая наименьшее расстояние и
    // маску «где можно»; ластик убирает, перекраска заново разыгрывает облик
    // по текущей кисти. Случайность — внешняя (System.Random), поэтому мазок
    // воспроизводим в проверках.
    public static class LocationScatterPainter
    {
        public const float DensityArea = 100 * 100;

        // Маска «где можно» по кисти: местность места и основания предметов.
        public static Func<Vector2, bool> Mask(LocationScatterBrush brush, LocalLocationDefinition location, LocationVisualDefinition visual)
        {
            WorldMapTerrainLayer terrain = location.CreateTerrainLayer();
            WorldMapMovementRules rules = location.MovementRules;
            HashSet<WorldMapGameplayTerrainType> allowed = brush.Terrain != null && brush.Terrain.Count > 0
                ? new HashSet<WorldMapGameplayTerrainType>(brush.Terrain) : null;
            List<Rect> blocked = brush.AvoidObjects && visual != null ? ObjectFootprints(visual, location) : null;
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            return pixel =>
            {
                if (pixel.x < 0 || pixel.y < 0 || pixel.x > canvas.x || pixel.y > canvas.y) return false;
                if (allowed != null || brush.OnlyPassable)
                {
                    WorldMapGameplayTerrainType type = terrain.IsEmpty ? WorldMapGameplayTerrainType.OpenGround : terrain.GetAtPixel(pixel.x, pixel.y);
                    if (allowed != null && !allowed.Contains(type)) return false;
                    if (brush.OnlyPassable && !rules.IsTraversable(type)) return false;
                }
                if (blocked != null)
                    foreach (Rect rect in blocked)
                        if (rect.Contains(pixel)) return false;
                return true;
            };
        }

        // Основания поставленных вручную предметов, которые не пропускают.
        private static List<Rect> ObjectFootprints(LocationVisualDefinition visual, LocalLocationDefinition location)
        {
            List<Rect> result = new List<Rect>();
            foreach (LocationVisualObject item in visual.Objects)
            {
                if (item == null || item.Hidden || item.LightOnly) continue;
                LocationResolvedVisual resolved = LocationVisualResolver.Resolve(item);
                if (resolved.BlocksMovement) result.AddRange(LocationVisualGeometry.FootprintRects(location, item, resolved));
            }
            return result;
        }

        // Мазок в круге: возвращает добавленные экземпляры. countOnly — для
        // плотности считаются только эти экземпляры (рисование поверх: только
        // поставленные этим мазком); null — все экземпляры слоя.
        public static List<LocationScatterInstance> Stamp(LocationScatterLayer layer, LocalLocationDefinition location, Vector2 center,
            System.Random random, Func<Vector2, bool> allowed, LocationScatterIndex index, ArtAssetDatabaseAsset catalog = null,
            ICollection<int> countOnly = null)
        {
            List<LocationScatterInstance> added = new List<LocationScatterInstance>();
            LocationScatterBrush brush = layer.Brush;
            float radius = Mathf.Max(1, brush.Radius);
            float falloff = Mathf.Clamp01(brush.Falloff);
            // Ожидаемое число в круге с учётом спада к краю (среднее по площади 1 − 2/3·спад).
            float target = Mathf.Max(0, brush.Density) / DensityArea * Mathf.PI * radius * radius * (1 - falloff * 2 / 3f);
            int existing = 0;
            foreach (LocationScatterInstance instance in layer.Instances)
                if ((countOnly == null || countOnly.Contains(instance.Key)) &&
                    (LocationVisualGeometry.ToPixel(location, instance.Position) - center).sqrMagnitude <= radius * radius) existing++;
            // Нажим: отпечаток добавляет долю недостающего; дробная часть — случаем.
            float flow = Mathf.Clamp(brush.Flow, .01f, 1);
            int need = Mathf.FloorToInt((target - existing) * flow + (float)random.NextDouble());
            if (need <= 0) return added;
            int attempts = need * 6 + 8;
            for (int i = 0; i < attempts && added.Count < need; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                float distance = radius * Mathf.Sqrt((float)random.NextDouble());
                if (falloff > 0 && random.NextDouble() > 1 - falloff * distance / radius) continue;
                Vector2 pixel = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * distance;
                LocationScatterInstance instance = TryPlace(layer, location, pixel, random, allowed, index, catalog);
                if (instance != null) added.Add(instance);
            }
            return added;
        }

        // Залить весь рисунок места до плотности кисти; over — ещё один слой
        // плотности поверх уже стоящих (index тогда пустой).
        public static List<LocationScatterInstance> Fill(LocationScatterLayer layer, LocalLocationDefinition location, System.Random random,
            Func<Vector2, bool> allowed, LocationScatterIndex index, ArtAssetDatabaseAsset catalog = null, int limit = 20000, bool over = false)
        {
            List<LocationScatterInstance> added = new List<LocationScatterInstance>();
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            int need = Mathf.Min(limit, Mathf.FloorToInt(Mathf.Max(0, layer.Brush.Density) / DensityArea * canvas.x * canvas.y) - (over ? 0 : layer.Instances.Count));
            int attempts = need * 6 + 8;
            for (int i = 0; i < attempts && added.Count < need; i++)
            {
                Vector2 pixel = new Vector2((float)random.NextDouble() * canvas.x, (float)random.NextDouble() * canvas.y);
                LocationScatterInstance instance = TryPlace(layer, location, pixel, random, allowed, index, catalog);
                if (instance != null) added.Add(instance);
            }
            return added;
        }

        private static LocationScatterInstance TryPlace(LocationScatterLayer layer, LocalLocationDefinition location, Vector2 pixel, System.Random random,
            Func<Vector2, bool> allowed, LocationScatterIndex index, ArtAssetDatabaseAsset catalog)
        {
            if (allowed != null && !allowed(pixel)) return null;
            if (index != null && index.AnyWithin(pixel, layer.Brush.MinDistance)) return null;
            LocationScatterEntry entry = PickEntry(layer, random, catalog);
            if (entry == null) return null;
            LocationScatterInstance instance = new LocationScatterInstance
            {
                Key = layer.NextKey++, AssetId = entry.AssetId, Position = LocationVisualGeometry.ToNormalized(location, pixel)
            };
            Randomize(instance, layer.Brush, entry, random, catalog);
            layer.Instances.Add(instance);
            index?.Add(pixel);
            return instance;
        }

        // Ассет по весу среди включённых, которые есть в каталоге.
        public static LocationScatterEntry PickEntry(LocationScatterLayer layer, System.Random random, ArtAssetDatabaseAsset catalog = null)
        {
            float total = 0;
            foreach (LocationScatterEntry entry in layer.Assets)
                if (Usable(entry, catalog)) total += entry.Weight;
            if (total <= 0) return null;
            float roll = (float)random.NextDouble() * total;
            LocationScatterEntry last = null;
            foreach (LocationScatterEntry entry in layer.Assets)
            {
                if (!Usable(entry, catalog)) continue;
                last = entry;
                roll -= entry.Weight;
                if (roll <= 0) return entry;
            }
            return last;
        }

        private static bool Usable(LocationScatterEntry entry, ArtAssetDatabaseAsset catalog) =>
            entry != null && entry.Enabled && entry.Weight > 0 && !string.IsNullOrEmpty(entry.AssetId) && Find(entry.AssetId, catalog) != null;

        private static ArtAssetDefinition Find(string id, ArtAssetDatabaseAsset catalog) =>
            catalog != null ? catalog.Find(id) : ArtAssetDatabaseAsset.FindCurrent(id);

        // Облик экземпляра по кисти: размер, растяжение, поворот, отражение,
        // ракурс, цвет.
        public static void Randomize(LocationScatterInstance instance, LocationScatterBrush brush, LocationScatterEntry entry, System.Random random,
            ArtAssetDatabaseAsset catalog = null)
        {
            float Range(Vector2 range) => Mathf.Lerp(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y), (float)random.NextDouble());
            instance.Scale = Mathf.Max(.01f, Range(brush.Scale) * (entry != null ? Mathf.Max(.01f, entry.ScaleMultiplier) : 1));
            instance.Stretch = Mathf.Max(.05f, Range(brush.Stretch));
            instance.Rotation = Range(brush.Rotation);
            instance.FlipX = brush.Flip == LocationScatterFlip.Always || (brush.Flip == LocationScatterFlip.Random && random.NextDouble() < .5);
            instance.Hue = brush.HueShift + (float)(random.NextDouble() * 2 - 1) * Mathf.Abs(brush.HueJitter);
            instance.Saturation = Mathf.Max(0, Range(brush.Saturation));
            instance.Brightness = Mathf.Max(0, Range(brush.Brightness));
            instance.Tint = Color.Lerp(brush.TintA, brush.TintB, (float)random.NextDouble());
            instance.View = brush.View;
            if (brush.ViewMode == LocationScatterViewMode.Random)
            {
                ArtAssetDefinition asset = Find(instance.AssetId, catalog);
                List<ArtAssetView> views = new List<ArtAssetView>();
                if (asset != null)
                    foreach (ArtAssetView view in ArtAssetLabels.Views)
                        if (asset.HasView(view)) views.Add(view);
                if (views.Count > 0) instance.View = views[random.Next(views.Count)];
            }
        }

        // Ластик: убрать экземпляры в круге; возвращает их ключи.
        public static List<int> Erase(LocationScatterLayer layer, LocalLocationDefinition location, Vector2 center, LocationScatterIndex index = null)
        {
            List<int> removed = new List<int>();
            float radius = Mathf.Max(1, layer.Brush.Radius);
            HashSet<string> brushAssets = BrushAssets(layer);
            for (int i = layer.Instances.Count - 1; i >= 0; i--)
            {
                LocationScatterInstance instance = layer.Instances[i];
                Vector2 pixel = LocationVisualGeometry.ToPixel(location, instance.Position);
                if ((pixel - center).sqrMagnitude > radius * radius) continue;
                if (brushAssets != null && !brushAssets.Contains(instance.AssetId)) continue;
                removed.Add(instance.Key);
                layer.Instances.RemoveAt(i);
                index?.Remove(pixel);
            }
            return removed;
        }

        // Перекраска: облик экземпляров в круге (или всего слоя — center null)
        // заново по кисти; положение и ассет остаются. skip — уже перекрашенные
        // этим мазком. Возвращает ключи.
        public static List<int> Recolor(LocationScatterLayer layer, LocalLocationDefinition location, Vector2? center, System.Random random,
            ArtAssetDatabaseAsset catalog = null, ICollection<int> skip = null)
        {
            List<int> changed = new List<int>();
            float radius = Mathf.Max(1, layer.Brush.Radius);
            HashSet<string> brushAssets = BrushAssets(layer);
            foreach (LocationScatterInstance instance in layer.Instances)
            {
                if (skip != null && skip.Contains(instance.Key)) continue;
                if (center.HasValue && (LocationVisualGeometry.ToPixel(location, instance.Position) - center.Value).sqrMagnitude > radius * radius) continue;
                if (brushAssets != null && !brushAssets.Contains(instance.AssetId)) continue;
                LocationScatterEntry entry = layer.Assets.Find(item => item != null && item.AssetId == instance.AssetId);
                Randomize(instance, layer.Brush, entry, random, catalog);
                changed.Add(instance.Key);
            }
            return changed;
        }

        private static HashSet<string> BrushAssets(LocationScatterLayer layer)
        {
            if (!layer.Brush.OnlyBrushAssets) return null;
            HashSet<string> result = new HashSet<string>();
            foreach (LocationScatterEntry entry in layer.Assets)
                if (entry != null && entry.Enabled && !string.IsNullOrEmpty(entry.AssetId)) result.Add(entry.AssetId);
            return result;
        }
    }
}
