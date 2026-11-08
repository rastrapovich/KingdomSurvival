using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Этап 05: ракурс постройки — нарисованный угол конька, направление входа и
    // основание. Берётся из регистрации набора этапа 05 (углы основания PNG);
    // без набора — приближённые значения тех же рисунков.
    public struct MapLabHouseView
    {
        public string Tag;
        public float Yaw;          // угол конька, градусов (от +X к +Z)
        public Vector2 Entrance;   // направление входа в мире
        public Vector2 Footprint;  // длина вдоль конька × ширина
        public string EntryId;
    }

    public static class MapLabHouseViews
    {
        public static readonly string[] Tags = { "FrontLeft", "FrontRight", "BackLeft", "BackRight" };

        public static List<MapLabHouseView> For(MapLabArtSet set, string group, bool barn)
        {
            List<MapLabHouseView> views = new List<MapLabHouseView>();
            if (set != null)
            {
                foreach (MapLabArtEntry e in set.Entries)
                {
                    if (e == null || !e.HasTexture || e.Category != MapLabArtCategory.Building || e.VariantGroup != group || string.IsNullOrEmpty(e.ViewTag))
                        continue;
                    views.Add(new MapLabHouseView
                    {
                        Tag = e.ViewTag,
                        Yaw = e.DrawnYaw,
                        Entrance = e.BaseRegistered ? e.EntranceDirection : Fallback(e.ViewTag, barn).Entrance,
                        Footprint = e.BaseRegistered ? e.Footprint : Fallback(e.ViewTag, barn).Footprint,
                        EntryId = e.Id,
                    });
                }
            }
            if (views.Count == 0)
            {
                foreach (string tag in Tags)
                    if (!barn || tag.StartsWith("Front"))
                        views.Add(Fallback(tag, barn));
            }
            views.Sort((a, b) => System.Array.IndexOf(Tags, a.Tag).CompareTo(System.Array.IndexOf(Tags, b.Tag)));
            return views;
        }

        // Приближение по рисункам (конёк ±12°, вход — по фронтону ракурса).
        public static MapLabHouseView Fallback(string tag, bool barn)
        {
            bool left = tag.EndsWith("Left"), front = tag.StartsWith("Front");
            float yaw = left ? 12f : -12f;
            Vector2 ridge = new Vector2(Mathf.Cos(yaw * Mathf.Deg2Rad), Mathf.Sin(yaw * Mathf.Deg2Rad));
            // …Left: видимый фронтон слева (−ridge для конька вправо-вверх), …Right — справа.
            Vector2 visible = left ? -ridge : ridge;
            return new MapLabHouseView
            {
                Tag = tag,
                Yaw = yaw,
                Entrance = front ? visible : -visible,
                Footprint = barn ? new Vector2(7f, 3.4f) : new Vector2(5.2f, 3.6f),
                EntryId = null,
            };
        }

        public static int Index(string tag) => System.Array.IndexOf(Tags, tag);
    }

    public static partial class MapLabPlacer
    {
        // Деревня этапа 05: дорога, небольшое расширение у колодца, 5–9 разных
        // дворов. Ракурс дома выбирается по ориентированному участку и подходу:
        // вход смотрит к воротам/дороге (рисунок не крутится — выбирается один
        // из четырёх). Сарай, дровяник, поленница и сено — в глубине или сбоку
        // двора, колодец — в общей точке, корыто — у рабочего прохода; не у
        // каждого дома. У подхода к мосту — свободно. Ограды — граф.
        private static void PlaceVillage05(MapLabData data, MapLabSettings settings)
        {
            data.FenceGraph = new MapLabFenceGraph();
            List<MapLabComposition.Settlement> list = data.Composition.Settlements;
            for (int i = 0; i < list.Count; i++)
                PlaceVillage05At(data, settings, list[i], i);
        }

        // 12У-E: одна деревня плана. Первая — прежний поток случайности (карты с
        // одной деревней не меняются), следующие — свой поток по номеру.
        private static void PlaceVillage05At(MapLabData data, MapLabSettings settings, MapLabComposition.Settlement st, int index)
        {
            MapLabStage05Settings s5 = settings.Stage05;
            MapLabSettlementSettings s = settings.Settlement;
            MapLabPolyline main = null;
            float best = float.MaxValue;
            int bestIndex = 0;
            foreach (MapLabPolyline road in data.Roads)
            {
                if (road.Kind != MapLabRoadKind.Road || road.Points.Count < 3)
                    continue;
                float d = MapLabGeometry.Nearest(road.Points, st.Center, out int segment, out _, out _, out _);
                if (d < best)
                {
                    best = d;
                    main = road;
                    bestIndex = Mathf.Clamp(segment, 1, road.Points.Count - 2);
                }
            }
            if (main == null)
                return;
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, index == 0 ? "village05" : "village05-" + index, MapLabData.AlgorithmVersion);
            string family = st.Family ?? string.Empty;
            // 12У-E: поселение у переправы — площадь там, где подходы сходятся
            // к мосту (не на самом подходе: 12 ед. у моста свободны).
            if (family == "crossing" && data.Bridges.Count > 0)
            {
                Vector2 end = Vector2.zero;
                float endDistance = float.MaxValue;
                foreach (MapLabBridge b in data.Bridges)
                {
                    foreach (Vector2 e in new[] { b.EndA, b.EndB })
                    {
                        float d = Vector2.Distance(e, st.Center);
                        if (d < endDistance)
                        {
                            endDistance = d;
                            end = e;
                        }
                    }
                }
                if (endDistance < st.Radius * 1.6f)
                {
                    Vector2 want = end + (st.Center - end).normalized * 16f;
                    float near = float.MaxValue;
                    for (int i = 1; i < main.Points.Count - 1; i++)
                    {
                        float d = Vector2.Distance(main.Points[i], want);
                        if (d < near)
                        {
                            near = d;
                            bestIndex = i;
                        }
                    }
                }
            }
            float scale = s.BuildingScale;
            List<MapLabHouseView>[] houseViews =
            {
                MapLabHouseViews.For(s5.Art, settings.Art.HouseGroupsByVariant.Length > 0 ? settings.Art.HouseGroupsByVariant[0] : "House_Thatch_A", false),
                MapLabHouseViews.For(s5.Art, settings.Art.HouseGroupsByVariant.Length > 2 ? settings.Art.HouseGroupsByVariant[2] : "House_Shingle_A", false),
            };
            List<MapLabHouseView> barnViews = MapLabHouseViews.For(s5.Art, settings.Art.BarnGroup, true);

            // Площадка у колодца: короткая широкая вставка полотна дороги.
            Vector2 plaza = main.Points[bestIndex];
            Vector2 plazaTangent = (main.Points[bestIndex + 1] - main.Points[bestIndex - 1]).normalized;
            Vector2 plazaNormal = MapLabGeometry.LeftNormal(plazaTangent);
            int plazaSide = rng.Chance(0.5f) ? 1 : -1;
            if (s5.WellWidening > 0.1f)
            {
                MapLabPolyline widening = new MapLabPolyline { Kind = MapLabRoadKind.Road, Width = main.Width + s5.WellWidening, Widening = true };
                Vector2 shift = plazaNormal * plazaSide * s5.WellWidening * 0.45f;
                widening.Points.Add(plaza - plazaTangent * (s5.WellWidening * 0.9f + 1f) + shift * 0.4f);
                widening.Points.Add(plaza + shift);
                widening.Points.Add(plaza + plazaTangent * (s5.WellWidening * 0.9f + 1f) + shift * 0.4f);
                data.Roads.Add(widening);
            }
            Vector2 wellPoint = plaza + plazaNormal * plazaSide * (main.Width * 0.5f + s5.WellWidening * 0.7f);
            Vector3 plazaInfo = new Vector3(plaza.x, plaza.y, s5.WellWidening + main.Width * 0.5f);
            if (index == 0)
                data.Plaza = plazaInfo;
            data.Plazas.Add(plazaInfo);
            data.Props.Add(new MapLabProp { Role = "village_well", Position = Ground(data, wellPoint), Scale = 1f, Yard = 0 });

            // 12У-E (мастер-бриф §9.2–9.4): размер меняет структуру, а не только
            // число домов; семейство — уличный скелет.
            //   вдоль дороги — главная улица; крупная — ещё два переулка;
            //   у переправы — дворы ближе к подходам; крупная — один переулок;
            //   промысловая слобода — переулок от площади к промыслу;
            //   рассеянные дворы — 2–3 малые группы на коротких проездах.
            List<Vector2> groups = new List<Vector2>();
            int lanes = 0;
            float laneWidth = Mathf.Max(1.4f, main.Width * 0.62f);
            if (family == "craft")
            {
                if (AddLane(data, ref rng, plaza + plazaNormal * plazaSide * main.Width * 0.5f, plazaNormal * plazaSide, st.Radius * 0.9f, laneWidth) != null)
                    lanes++;
            }
            else if (family == "scattered")
            {
                int count = rng.Range(2, 4);
                float start = rng.Range(0f, 360f);
                for (int g = 0; g < count; g++)
                {
                    float angle = (start + g * 360f / count + rng.Range(-25f, 25f)) * Mathf.Deg2Rad;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    if (Mathf.Abs(Vector2.Dot(dir, plazaTangent)) > 0.85f)
                        dir = (dir + plazaNormal * Mathf.Sign(Vector2.Dot(dir, plazaNormal) + 0.01f)).normalized;
                    MapLabPolyline lane = AddLane(data, ref rng, plaza + dir * main.Width * 0.5f, dir, st.Radius * rng.Range(0.7f, 1.05f), laneWidth * 0.85f);
                    if (lane != null)
                    {
                        lanes++;
                        groups.Add(lane.Points[lane.Points.Count - 1]);
                    }
                }
            }
            else if (st.Size >= 3)
            {
                int want = family == "crossing" ? 1 : 2;
                for (int l = 0; l < want; l++)
                {
                    int sign = l == 0 ? 1 : -1;
                    int at = Mathf.Clamp(bestIndex + sign * Mathf.Max(3, (int)(st.Radius * 0.35f / 2.5f)), 1, main.Points.Count - 2);
                    Vector2 tangent = (main.Points[at + 1] - main.Points[at - 1]).normalized;
                    Vector2 side = MapLabGeometry.LeftNormal(tangent) * (l == 0 ? plazaSide : -plazaSide);
                    if (AddLane(data, ref rng, main.Points[at] + side * main.Width * 0.5f, side, st.Radius * 0.75f, laneWidth) != null)
                        lanes++;
                }
            }
            Vector2Int range = YardRange(s5, st.Size, lanes);
            int target = rng.Range(range.x, range.y + 1);
            // R03: ограда — не атрибут каждого дома. Небольшой бюджет на деревню
            // (около одной на пять дворов, 1–3) и выбор на уровне плана: какие по
            // счёту дворы станут просторными огороженными хозяйствами.
            int budget = settings.Settlement.Fences ? Mathf.Clamp(Mathf.RoundToInt(target * s5.FencedYardShare), 1, 3) : 0;
            List<int> order = new List<int>();
            for (int i = 0; i < target; i++)
                order.Add(i);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }
            HashSet<int> spacious = new HashSet<int>(order.GetRange(0, Mathf.Min(budget, order.Count)));
            List<MapLabYardRect> yards = new List<MapLabYardRect>();
            List<(Vector2 a, Vector2 b, float gate, bool fenced)> outlines = new List<(Vector2, Vector2, float, bool)>();
            List<MapLabPlacement> placed = new List<MapLabPlacement>();
            int objectsBefore = data.Objects.Count;
            // Кандидаты — точки всех дорог поселения (не только главной) через
            // 2.5 ед., ближние к площадке — раньше; пересечения отсекает TryYard05.
            List<(Vector2 p, Vector2 t, MapLabPolyline road, float d)> spots = new List<(Vector2, Vector2, MapLabPolyline, float)>();
            foreach (MapLabPolyline road in data.Roads)
            {
                if (road.Kind != MapLabRoadKind.Road || road.Widening || road.Points.Count < 2)
                    continue;
                List<Vector2> pts = MapLabGeometry.Resample(road.Points, 2.5f);
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    float dc = Vector2.Distance(pts[i], st.Center);
                    if (dc > st.Radius * (groups.Count > 0 ? 1.3f : 1.15f) || Vector2.Distance(pts[i], plaza) < s5.WellWidening + 7f)
                        continue;
                    // Рассеянные дворы — только у своих групп; между ними остаётся природа.
                    if (groups.Count > 0)
                    {
                        bool inGroup = false;
                        foreach (Vector2 g in groups)
                            inGroup |= Vector2.Distance(pts[i], g) < 13f;
                        if (!inGroup)
                            continue;
                    }
                    spots.Add((pts[i], (pts[i + 1] - pts[i - 1]).normalized, road, Vector2.Distance(pts[i], plaza) + rng.Range(0f, 6f)));
                }
            }
            spots.Sort((x, y) => x.d.CompareTo(y.d));
            foreach (var spot in spots)
            {
                if (yards.Count >= target)
                    break;
                Vector2 normal = MapLabGeometry.LeftNormal(spot.t);
                int first = rng.Chance(0.5f) ? 1 : -1;
                for (int k = 0; k < 2 && yards.Count < target; k++)
                {
                    int side = k == 0 ? first : -first;
                    if (rng.Chance(0.25f))
                        continue;
                    TryYard05(data, settings, ref rng, spot.road, spot.p, spot.t, normal * side, houseViews, barnViews, yards, outlines, placed, spacious.Contains(yards.Count));
                }
            }
            data.YardRects.AddRange(yards);
            AssignServiceBuildings(data, st, plaza, objectsBefore);
            BuildFenceGraph(data, settings, outlines, placed);
        }

        // Дворов по размеру поселения: среднее — диапазон профиля, малое — 3–6,
        // крупное — 10–12 на одной улице и 12–18, когда есть переулки.
        private static Vector2Int YardRange(MapLabStage05Settings s5, int size, int lanes = 0)
        {
            int lo = Mathf.Min(s5.YardsMin, s5.YardsMax), hi = Mathf.Max(s5.YardsMin, s5.YardsMax);
            if (size <= 1)
                return new Vector2Int(3, Mathf.Clamp(Mathf.Min(hi, 6), 3, 6));
            if (size >= 3)
                return lanes > 0 ? new Vector2Int(12, Mathf.Max(hi, 18)) : new Vector2Int(Mathf.Max(lo, Mathf.Min(hi, 10)), Mathf.Max(hi, 12));
            return new Vector2Int(lo, hi);
        }

        // Переулок: короткая улица от главной дороги, слегка изогнутая; идёт,
        // пока земля пригодна (не вода, не круча, не у моста). Короче 10 ед. —
        // не строится.
        private static MapLabPolyline AddLane(MapLabData data, ref MapLabRandom rng, Vector2 start, Vector2 dir, float length, float width)
        {
            MapLabPolyline lane = new MapLabPolyline { Kind = MapLabRoadKind.Road, Width = width };
            Vector2 p = start;
            Vector2 d = dir.normalized;
            lane.Points.Add(p - d * 1.5f);
            float walked = 0f;
            while (walked < length)
            {
                float turn = rng.Range(-8f, 8f) * Mathf.Deg2Rad;
                d = new Vector2(d.x * Mathf.Cos(turn) - d.y * Mathf.Sin(turn), d.x * Mathf.Sin(turn) + d.y * Mathf.Cos(turn)).normalized;
                Vector2 q = p + d * 3f;
                if (!BuildableSpot(data, q, width * 0.6f, 15f))
                    break;
                bool nearBridge = false;
                foreach (MapLabBridge b in data.Bridges)
                    nearBridge |= Vector2.Distance(q, b.EndA) < 12f || Vector2.Distance(q, b.EndB) < 12f;
                if (nearBridge)
                    break;
                p = q;
                lane.Points.Add(p);
                walked += 3f;
            }
            if (walked < 10f)
                return null;
            data.Roads.Add(lane);
            return lane;
        }

        private static Vector3 Ground(MapLabData data, Vector2 p) => new Vector3(p.x, data.SampleHeight(p.x, p.y), p.y);

        // 12У-G: рисунки отдельных зданий услуг (набор выбранных мест, ракурс FrontRight).
        public static string ServiceArt(string role)
        {
            switch (role)
            {
                case "tavern": return "Tavern_A_FrontRight";
                case "merchant": return "MerchantShop_A_FrontRight";
                case "board": return "NoticePost_A_FrontRight";
                case "healer": return "HealerHouse_A_FrontRight";
                case "elder": return "ElderHouse_A_FrontRight";
                case "smithy": return "Smithy_A_FrontRight";
                default: return null;
            }
        }

        // Ближайшие к площади дома деревни становятся зданиями её услуг
        // (по порядку списка). Домов меньше, чем услуг, — остальные услуги
        // остаются в общем экране поселения.
        private static void AssignServiceBuildings(MapLabData data, MapLabComposition.Settlement st, Vector2 plaza, int objectsBefore)
        {
            if (st.Services.Count == 0)
                return;
            List<int> houses = new List<int>();
            for (int i = objectsBefore; i < data.Objects.Count; i++)
                if (data.Objects[i].Kind == MapLabObjectKind.House)
                    houses.Add(i);
            houses.Sort((a, b) =>
            {
                float da = Vector2.Distance(new Vector2(data.Objects[a].Position.x, data.Objects[a].Position.z), plaza);
                float db = Vector2.Distance(new Vector2(data.Objects[b].Position.x, data.Objects[b].Position.z), plaza);
                return da != db ? da.CompareTo(db) : a.CompareTo(b);
            });
            // У рисунков зданий услуг один ракурс — «спереди справа», вход
            // смотрит туда же. Двор выбран генератором так, что вход избы
            // смотрит на улицу; поэтому под здание услуги берётся ближняя к
            // площади изба с тем же ракурсом (вход к улице), а если такой
            // поблизости нет — просто ближняя.
            int frontRight = System.Array.IndexOf(MapLabHouseViews.Tags, "FrontRight");
            int window = Mathf.Max(st.Services.Count * 3, 6);
            HashSet<int> used = new HashSet<int>();
            for (int k = 0; k < st.Services.Count && used.Count < houses.Count; k++)
            {
                string art = ServiceArt(st.Services[k]);
                if (art == null)
                    continue;
                int pick = -1;
                for (int c = 0; c < houses.Count && c < window; c++)
                    if (!used.Contains(houses[c]) && data.Objects[houses[c]].View == frontRight)
                    {
                        pick = houses[c];
                        break;
                    }
                if (pick < 0)
                    foreach (int h in houses)
                        if (!used.Contains(h))
                        {
                            pick = h;
                            break;
                        }
                if (pick < 0)
                    break;
                used.Add(pick);
                MapLabPlacement o = data.Objects[pick];
                o.ArtId = art;
                o.SettlementId = st.Id;
                o.Role = st.Services[k];
                data.Objects[pick] = o;
            }
        }

        private static void TryYard05(MapLabData data, MapLabSettings settings, ref MapLabRandom rng, MapLabPolyline road, Vector2 roadPoint, Vector2 axisU, Vector2 backV,
            List<MapLabHouseView>[] houseViews, List<MapLabHouseView> barnViews, List<MapLabYardRect> yards, List<(Vector2 a, Vector2 b, float gate, bool fenced)> outlines, List<MapLabPlacement> placed, bool spacious)
        {
            MapLabStage05Settings s5 = settings.Stage05;
            float scale = settings.Settlement.BuildingScale;
            // Два типа домов чередуются (с редкими повторами), чтобы оба были в деревне.
            int group = (yards.Count + (rng.Chance(0.3f) ? 1 : 0)) % 2;
            List<MapLabHouseView> views = houseViews[group];
            // Подход: от дома к дороге, с небольшим сдвигом вдоль неё (дверь к воротам).
            Vector2 approach = (-backV + axisU * rng.Range(-0.45f, 0.45f)).normalized;
            MapLabHouseView view = views[0];
            float bestDot = float.MinValue;
            foreach (MapLabHouseView v in views)
            {
                float d = Vector2.Dot(v.Entrance, approach);
                if (d > bestDot)
                {
                    bestDot = d;
                    view = v;
                }
            }
            Vector2 foot = view.Footprint * scale;
            Vector2 ridge = new Vector2(Mathf.Cos(view.Yaw * Mathf.Deg2Rad), Mathf.Sin(view.Yaw * Mathf.Deg2Rad));
            Vector2 across = MapLabGeometry.LeftNormal(ridge);
            // Полуразмеры основания в осях двора.
            float hu = Mathf.Abs(Vector2.Dot(ridge, axisU)) * foot.x * 0.5f + Mathf.Abs(Vector2.Dot(across, axisU)) * foot.y * 0.5f;
            float hv = Mathf.Abs(Vector2.Dot(ridge, backV)) * foot.x * 0.5f + Mathf.Abs(Vector2.Dot(across, backV)) * foot.y * 0.5f;
            float front = road.Width * 0.5f + rng.Range(1.3f, 2.2f);
            float width = hu * 2f + rng.Range(3.2f, 5.2f) * scale;
            float depth = hv * 2f + rng.Range(6f, 9f) * scale;
            // Просторное хозяйство: длинные связные прогоны ограды вокруг двора с
            // амбаром, огородом и сеном (а не тесная клетка вокруг избы).
            if (spacious)
            {
                width += rng.Range(5f, 8f) * scale;
                depth += rng.Range(4f, 7f) * scale;
            }
            Vector2 frontCenter = roadPoint + backV * front + axisU * rng.Signed() * 0.8f;
            Vector2 center = frontCenter + backV * depth * 0.5f;
            MapLabYardRect rect = new MapLabYardRect { Center = center, AxisU = axisU, Half = new Vector2(width * 0.5f, depth * 0.5f) };
            // Отказ: вода, круча, мост рядом, пересечение с соседями (касание стороной — можно).
            Vector2 P(float u, float v) => frontCenter + axisU * u + backV * v;
            for (int k = 0; k < 9; k++)
            {
                Vector2 q = P((k % 3 - 1) * width * 0.5f, (k / 3) * depth * 0.5f);
                if (!BuildableSpot(data, q, 0.6f, 15f) || MinRoadDistance(data, q) < 0.5f)
                    return;
                for (int b = 0; b < data.Bridges.Count; b++)
                    if (Vector2.Distance(q, data.Bridges[b].EndA) < 12f || Vector2.Distance(q, data.Bridges[b].EndB) < 12f)
                        return;
            }
            // Дворы (и их ограды) не заходят друг на друга нигде — точная
            // проверка повёрнутых прямоугольников; касание стороной допустимо.
            foreach (MapLabYardRect other in yards)
                if (rect.Overlaps(other, 0.15f))
                    return;
            // И с дворами соседних поселений.
            foreach (MapLabYardRect other in data.YardRects)
                if (rect.Overlaps(other, 0.15f))
                    return;
            if (data.Plaza.z > 0f && Vector2.Distance(center, new Vector2(data.Plaza.x, data.Plaza.y)) < data.Plaza.z + Mathf.Max(width, depth) * 0.5f)
                return;
            int yardId = yards.Count + 1;
            // Дом — у передней стороны двора, со стороны, противоположной двери вдоль дороги.
            float doorAlong = Mathf.Sign(Vector2.Dot(view.Entrance, axisU));
            float houseU = Mathf.Clamp(-doorAlong * (width * 0.5f - hu - 1f) * rng.Range(0.2f, 0.8f), -(width * 0.5f - hu - 0.6f), width * 0.5f - hu - 0.6f);
            Vector2 houseCenter = P(houseU, hv + rng.Range(1.4f, 2.2f));
            if (MinRoadDistance(data, houseCenter) < hv + 0.8f || !BuildableSpot(data, houseCenter, Mathf.Max(hu, hv), 14f))
                return;
            yards.Add(rect);
            MapLabPlacement house = new MapLabPlacement
            {
                Kind = MapLabObjectKind.House,
                Position = Ground(data, houseCenter),
                Yaw = view.Yaw,
                Scale = 1f,
                Size = foot,
                Variant = group == 0 ? 0 : 2,
                Brightness = rng.Range(0.94f, 1.05f),
                Tone = rng.Value(),
                View = MapLabHouseViews.Index(view.Tag),
                Entrance = view.Entrance,
                Yard = yardId,
            };
            data.Objects.Add(house);
            placed.Add(house);
            // Ворота — на передней стороне, где подход от двери выходит к дороге.
            Vector2 door = houseCenter + view.Entrance * (foot.x * 0.5f + 0.4f);
            float gateU = Mathf.Clamp(Vector2.Dot(door - frontCenter, axisU), -width * 0.5f + s5.GateWidth * 0.5f + 0.7f, width * 0.5f - s5.GateWidth * 0.5f - 0.7f);
            bool fenced = settings.Settlement.Fences && spacious;
            outlines.Add((P(-width * 0.5f, 0f), P(width * 0.5f, 0f), gateU + width * 0.5f, fenced));
            outlines.Add((P(width * 0.5f, 0f), P(width * 0.5f, depth), -1f, fenced));
            outlines.Add((P(width * 0.5f, depth), P(-width * 0.5f, depth), -1f, fenced));
            outlines.Add((P(-width * 0.5f, depth), P(-width * 0.5f, 0f), -1f, fenced));
            // Огород в глубине двора (покраска), тропинка ворота → дверь и ворота → дорога.
            Vector2 garden = P(rng.Range(-0.2f, 0.2f) * width, depth * 0.72f);
            data.Yards.Add(new Vector4(garden.x, garden.y, Mathf.Min(depth * 0.22f, width * 0.32f), Mathf.Atan2(backV.y, backV.x)));
            Vector2 gate = P(gateU, 0f);
            MapLabPolyline toDoor = new MapLabPolyline { Width = 0.9f, Kind = MapLabRoadKind.Path };
            toDoor.Points.Add(gate - backV * 0.4f);
            toDoor.Points.Add(Vector2.Lerp(gate, door, 0.55f) + axisU * rng.Signed() * 0.3f);
            toDoor.Points.Add(door);
            data.YardPaths.Add(toDoor);
            // Тропинка ворота → ближайшая дорога: доходит до полотна и чуть заходит
            // в него (объединённое поле кромки сливает их без шва).
            if (NearestRoad(data, gate, out Vector2 onRoad, out float roadHalf) && (onRoad - gate).magnitude < 30f)
            {
                Vector2 toRoad = onRoad - gate;
                float len = toRoad.magnitude;
                if (len > roadHalf + 0.3f)
                {
                    Vector2 dir = toRoad / len;
                    MapLabPolyline toStreet = new MapLabPolyline { Width = 0.9f, Kind = MapLabRoadKind.Path };
                    toStreet.Points.Add(gate - backV * 0.4f);
                    toStreet.Points.Add(Vector2.Lerp(gate, onRoad, 0.5f) + new Vector2(-dir.y, dir.x) * ((Mathf.FloorToInt(gate.x * 7f + gate.y * 3f) & 1) == 0 ? 1f : -1f) * Mathf.Min(0.5f, len * 0.08f));
                    toStreet.Points.Add(onRoad - dir * Mathf.Max(0f, roadHalf - 0.5f));
                    data.YardPaths.Add(toStreet);
                }
            }
            // Сарай — в глубине, у одного из задних углов; вход — к дому.
            float side = rng.Chance(0.5f) ? 1f : -1f;
            if (barnViews.Count > 0 && rng.Chance(spacious ? 0.9f : 0.3f))
            {
                Vector2 toHouse = -backV;
                MapLabHouseView barnView = barnViews[0];
                float bd = float.MinValue;
                foreach (MapLabHouseView v in barnViews)
                    if (Vector2.Dot(v.Entrance, toHouse + axisU * -side * 0.6f) > bd)
                    {
                        bd = Vector2.Dot(v.Entrance, toHouse + axisU * -side * 0.6f);
                        barnView = v;
                    }
                Vector2 bf = barnView.Footprint * scale * 0.85f;
                Vector2 br = new Vector2(Mathf.Cos(barnView.Yaw * Mathf.Deg2Rad), Mathf.Sin(barnView.Yaw * Mathf.Deg2Rad));
                float bhu = Mathf.Abs(Vector2.Dot(br, axisU)) * bf.x * 0.5f + Mathf.Abs(Vector2.Dot(MapLabGeometry.LeftNormal(br), axisU)) * bf.y * 0.5f;
                float bhv = Mathf.Abs(Vector2.Dot(br, backV)) * bf.x * 0.5f + Mathf.Abs(Vector2.Dot(MapLabGeometry.LeftNormal(br), backV)) * bf.y * 0.5f;
                Vector2 barnCenter = P(side * Mathf.Max(0f, width * 0.5f - bhu - 0.7f), depth - bhv - 0.8f);
                if (bhu * 2f < width - 1f && BuildableSpot(data, barnCenter, 1.4f, 16f) && !Overlaps(placed, barnCenter, Mathf.Max(bhu, bhv)))
                {
                    MapLabPlacement barn = new MapLabPlacement
                    {
                        Kind = MapLabObjectKind.Barn,
                        Position = Ground(data, barnCenter),
                        Yaw = barnView.Yaw,
                        Scale = 1f,
                        Size = bf,
                        Variant = 0,
                        Brightness = rng.Range(0.9f, 1.02f),
                        View = MapLabHouseViews.Index(barnView.Tag),
                        Entrance = barnView.Entrance,
                        Yard = yardId,
                    };
                    data.Objects.Add(barn);
                    placed.Add(barn);
                    // Корыто — у рабочего прохода между домом и сараем.
                    if (rng.Chance(0.45f))
                        AddProp(data, placed, "village_trough", Vector2.Lerp(houseCenter, barnCenter, 0.5f) + axisU * rng.Signed() * 0.8f, yardId, 0.5f);
                }
            }
            // Дровяник и поленница — сбоку или в глубине; сено — в глубине двора.
            if (rng.Chance(0.35f))
                AddProp(data, placed, "village_woodshed", P(-side * (width * 0.5f - 1.6f), depth * rng.Range(0.55f, 0.8f)), yardId, 1.3f);
            if (rng.Chance(0.4f))
                AddProp(data, placed, "woodpile", houseCenter - view.Entrance * (foot.x * 0.5f + 0.7f) + backV * 0.4f, yardId, 0.6f);
            if (rng.Chance(spacious ? 0.75f : 0.2f))
                AddProp(data, placed, rng.Chance(0.5f) ? "haystack" : "hay_sheaf", P(rng.Range(-0.35f, 0.35f) * width, depth - 1.3f), yardId, 1f);
        }

        private static bool Overlaps(List<MapLabPlacement> placed, Vector2 p, float radius)
        {
            foreach (MapLabPlacement o in placed)
            {
                float r = Mathf.Max(o.Size.x, o.Size.y) * 0.5f;
                if (Vector2.Distance(new Vector2(o.Position.x, o.Position.z), p) < r + radius)
                    return true;
            }
            return false;
        }

        private static void AddProp(MapLabData data, List<MapLabPlacement> placed, string role, Vector2 p, int yard, float radius)
        {
            if (!Buildable(data, p) || Overlaps(placed, p, radius) || MinRoadDistance(data, p) < radius + 0.4f)
                return;
            foreach (MapLabProp other in data.Props)
                if (Vector2.Distance(new Vector2(other.Position.x, other.Position.z), p) < radius + 0.8f)
                    return;
            data.Props.Add(new MapLabProp { Role = role, Position = Ground(data, p), Scale = 1f, Yard = yard });
        }

        // ---------- Граф оград ----------

        // Узел — один столб, ребро — одно заполнение. Общие границы соседних дворов
        // не строятся дважды (наложение вычитается), ворота — явный проём между
        // двумя узлами, короткие остатки сливаются или распределяются, длинные
        // пролёты делятся промежуточными узлами (и чаще на склоне — жерди не
        // «висят»); рёбра не пересекают дома и дороги.
        public static void BuildFenceGraph(MapLabData data, MapLabSettings settings, List<(Vector2 a, Vector2 b, float gate, bool fenced)> outlines, List<MapLabPlacement> buildings)
        {
            MapLabStage05Settings s5 = settings.Stage05;
            MapLabFenceGraph graph = data.FenceGraph ?? (data.FenceGraph = new MapLabFenceGraph());
            List<(Vector2 a, Vector2 b, bool gate, int style, int yard)> accepted = new List<(Vector2, Vector2, bool, int, int)>();
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "fence05", MapLabData.AlgorithmVersion);
            int yard = 0;
            int style = 0;
            for (int o = 0; o < outlines.Count; o++)
            {
                if (o % 4 == 0)
                {
                    yard++;
                    style = rng.Range(0, 3);
                }
                var line = outlines[o];
                if (!line.fenced)
                    continue;
                List<(float t0, float t1, bool gate)> parts = new List<(float, float, bool)>();
                float length = Vector2.Distance(line.a, line.b);
                if (line.gate >= 0f)
                {
                    float g0 = (line.gate - s5.GateWidth * 0.5f) / length, g1 = (line.gate + s5.GateWidth * 0.5f) / length;
                    parts.Add((0f, g0, false));
                    parts.Add((g0, g1, true));
                    parts.Add((g1, 1f, false));
                }
                else
                    parts.Add((0f, 1f, false));
                foreach (var part in parts)
                {
                    Vector2 a = Vector2.Lerp(line.a, line.b, part.t0), b = Vector2.Lerp(line.a, line.b, part.t1);
                    List<(float, float)> free = part.gate ? new List<(float, float)> { (0f, 1f) } : Subtract(a, b, accepted, ref graph.SharedRemoved);
                    foreach ((float u0, float u1) in free)
                    {
                        Vector2 fa = Vector2.Lerp(a, b, u0), fb = Vector2.Lerp(a, b, u1);
                        foreach ((Vector2 ca, Vector2 cb) in Clip(data, fa, fb, buildings, part.gate, ref graph.Clipped))
                            accepted.Add((ca, cb, part.gate, style, yard));
                    }
                }
            }
            // Узлы с притяжением (концы соседних отрезков — один столб).
            int Node(Vector2 p)
            {
                for (int i = 0; i < graph.Nodes.Count; i++)
                    if ((graph.Nodes[i] - p).sqrMagnitude < 0.35f * 0.35f)
                        return i;
                graph.Nodes.Add(p);
                return graph.Nodes.Count - 1;
            }
            foreach (var seg in accepted)
            {
                float length = Vector2.Distance(seg.a, seg.b);
                if (!seg.gate && length < s5.FenceMinSpan)
                {
                    // Короткий остаток не строится отдельным пролётом: его концы
                    // сливаются в один узел (столб соседнего пролёта).
                    int na = Node(seg.a);
                    graph.Nodes[na] = (graph.Nodes[na] + seg.b) * 0.5f;
                    graph.Merged++;
                    continue;
                }
                int steps = seg.gate ? 1 : Mathf.Max(1, Mathf.CeilToInt(length / s5.FenceSpan));
                // На склоне — чаще: перепад между столбами не больше ~0.45 ед.
                if (!seg.gate)
                {
                    float rise = Mathf.Abs(data.SampleHeight(seg.a.x, seg.a.y) - data.SampleHeight(seg.b.x, seg.b.y));
                    steps = Mathf.Max(steps, Mathf.CeilToInt(rise / 0.45f));
                    steps = Mathf.Min(steps, Mathf.Max(1, Mathf.FloorToInt(length / s5.FenceMinSpan)));
                }
                int prev = Node(seg.a);
                for (int k = 1; k <= steps; k++)
                {
                    int next = Node(Vector2.Lerp(seg.a, seg.b, k / (float)steps));
                    if (next != prev && !HasEdge(graph, prev, next))
                        graph.Edges.Add(new MapLabFenceEdge { A = prev, B = next, Gate = seg.gate, Style = seg.style, Yard = seg.yard });
                    prev = next;
                }
            }
        }

        private static bool HasEdge(MapLabFenceGraph g, int a, int b)
        {
            foreach (MapLabFenceEdge e in g.Edges)
                if ((e.A == a && e.B == b) || (e.A == b && e.B == a))
                    return true;
            return false;
        }

        // Части отрезка a–b, не лежащие на уже принятых (общая граница дворов).
        private static List<(float, float)> Subtract(Vector2 a, Vector2 b, List<(Vector2 a, Vector2 b, bool gate, int style, int yard)> accepted, ref int removed)
        {
            List<(float, float)> free = new List<(float, float)> { (0f, 1f) };
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f)
                return new List<(float, float)>();
            Vector2 dir = d / len;
            foreach (var other in accepted)
            {
                Vector2 od = other.b - other.a;
                float ol = od.magnitude;
                if (ol < 1e-4f || Mathf.Abs(Vector2.Dot(od / ol, dir)) < 0.985f)
                    continue;
                // Расстояние между параллельными прямыми.
                Vector2 rel = other.a - a;
                if (Mathf.Abs(rel.x * dir.y - rel.y * dir.x) > 0.6f)
                    continue;
                float t0 = Vector2.Dot(other.a - a, dir) / len, t1 = Vector2.Dot(other.b - a, dir) / len;
                if (t0 > t1) { float t = t0; t0 = t1; t1 = t; }
                List<(float, float)> next = new List<(float, float)>();
                foreach ((float u0, float u1) in free)
                {
                    if (t1 <= u0 || t0 >= u1)
                    {
                        next.Add((u0, u1));
                        continue;
                    }
                    removed++;
                    if (t0 > u0) next.Add((u0, t0));
                    if (t1 < u1) next.Add((t1, u1));
                }
                free = next;
            }
            return free;
        }

        // Части отрезка вне дорог, домов, воды и кручи (по шагу 0.25 ед.).
        private static List<(Vector2, Vector2)> Clip(MapLabData data, Vector2 a, Vector2 b, List<MapLabPlacement> buildings, bool gate, ref int clipped)
        {
            List<(Vector2, Vector2)> runs = new List<(Vector2, Vector2)>();
            float length = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.25f));
            int start = -1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                bool ok = Buildable(data, p) && MinRoadDistance(data, p) > 0.45f && !InsideBuilding(buildings, p, 0.35f);
                if (ok && start < 0)
                    start = i;
                if ((!ok || i == steps) && start >= 0)
                {
                    int end = ok ? i : i - 1;
                    if (end > start)
                        runs.Add((Vector2.Lerp(a, b, start / (float)steps), Vector2.Lerp(a, b, end / (float)steps)));
                    start = -1;
                }
                if (!ok)
                    clipped++;
            }
            // Ворота не режутся на куски: либо целиком, либо нет.
            if (gate && (runs.Count != 1 || Vector2.Distance(runs[0].Item1, runs[0].Item2) < length * 0.95f))
                return new List<(Vector2, Vector2)>();
            return runs;
        }

        public static bool InsideBuilding(List<MapLabPlacement> buildings, Vector2 p, float margin)
        {
            foreach (MapLabPlacement o in buildings)
            {
                if (o.Kind != MapLabObjectKind.House && o.Kind != MapLabObjectKind.Barn)
                    continue;
                float yaw = o.Yaw * Mathf.Deg2Rad;
                Vector2 u = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
                Vector2 v = MapLabGeometry.LeftNormal(u);
                Vector2 d = p - new Vector2(o.Position.x, o.Position.z);
                if (Mathf.Abs(Vector2.Dot(d, u)) < o.Size.x * 0.5f + margin && Mathf.Abs(Vector2.Dot(d, v)) < o.Size.y * 0.5f + margin)
                    return true;
            }
            return false;
        }

        // Ближайшая точка осевой линии дорог и троп (без вставок у колодца) и полуширина полотна.
        private static bool NearestRoad(MapLabData data, Vector2 p, out Vector2 point, out float halfWidth)
        {
            point = p;
            halfWidth = 0f;
            float best = float.MaxValue;
            foreach (MapLabPolyline road in data.Roads)
            {
                if (road.Widening)
                    continue;
                for (int i = 0; i + 1 < road.Points.Count; i++)
                {
                    float d = MapLabGeometry.DistanceToSegment(p, road.Points[i], road.Points[i + 1], out float t);
                    if (d < best)
                    {
                        best = d;
                        point = Vector2.Lerp(road.Points[i], road.Points[i + 1], t);
                        halfWidth = road.Width * 0.5f;
                    }
                }
            }
            return best < float.MaxValue;
        }
    }
}
