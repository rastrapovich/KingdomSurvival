using System;
using System.Collections.Generic;
using System.Linq;

// 12У-E: минимальный слой фракций в генераторе (пакет «Фракции на карте»):
// контроль деревень державами по связным группам дорог и постоянные группы
// мира с маршрутами по существующим дорогам и тропам. Свой поток случайности;
// карта без сторон и групп работает как прежде.
public static partial class WorldMapGenerator
{
    private sealed partial class Builder
    {
        private void BuildFactions()
        {
            result.Report.Issues.AddRange(WorldMapFactionCatalog.Validate(settings.Factions, settings.Groups));
            Random random = new Random(StreamSeed(seed, "factions"));
            List<GeneratedMapSite> villages = result.Villages;
            int n = villages.Count;
            int[][] roadDistance = new int[n][];
            for (int v = 0; v < n; v++)
                roadDistance[v] = RoadBfs(villages[v].Cell, out _);

            // Контроль: столицы — старт и затем самые удалённые по дорогам деревни;
            // каждая деревня — ближайшей по дорогам столице (связные группы).
            List<WorldMapFactionDefinition> polities = settings.Factions
                .Where(f => f != null && f.Enabled && f.Kind == WorldMapFactionDefinition.Polity && f.CanControlTerritory && string.IsNullOrEmpty(f.ParentId))
                .Take(settings.ControllingPolities).ToList();
            if (polities.Count > 0 && n > 0)
            {
                List<int> capitals = new List<int> { 0 };
                while (capitals.Count < Math.Min(polities.Count, n))
                {
                    int best = -1, bestValue = -1;
                    for (int v = 0; v < n; v++)
                    {
                        if (capitals.Contains(v))
                            continue;
                        int nearest = capitals.Min(c => Reach(roadDistance[c], villages[v].Cell, villages[c].Cell, villages[v].Cell));
                        if (nearest > bestValue)
                        {
                            bestValue = nearest;
                            best = v;
                        }
                    }
                    if (best < 0)
                        break;
                    capitals.Add(best);
                }
                for (int v = 0; v < n; v++)
                {
                    int owner = 0, ownerDistance = int.MaxValue;
                    for (int k = 0; k < capitals.Count; k++)
                    {
                        int d = Reach(roadDistance[capitals[k]], villages[v].Cell, villages[capitals[k]].Cell, villages[v].Cell);
                        if (d < ownerDistance)
                        {
                            ownerDistance = d;
                            owner = k;
                        }
                    }
                    villages[v].ControllerFactionId = polities[owner].Id;
                }
            }

            List<int> taken = result.Landmarks.Select(l => l.Cell).Concat(result.WorkSites.Select(w => w.Cell)).Concat(result.Features.Select(f => f.Cell)).ToList();
            foreach (WorldMapGroupRule rule in settings.Groups)
            {
                if (rule == null || !rule.Enabled || rule.Count <= 0)
                    continue;
                WorldMapFactionDefinition faction = WorldMapFactionCatalog.Find(settings.Factions, rule.FactionId);
                if (!string.IsNullOrEmpty(rule.FactionId) && faction == null)
                    continue;
                for (int k = 0; k < rule.Count; k++)
                {
                    GeneratedMapActor actor = new GeneratedMapActor
                    {
                        ActorId = rule.Id + "-" + (k + 1),
                        RuleId = rule.Id,
                        Role = rule.Role,
                        Name = rule.Name,
                        FactionId = rule.FactionId ?? string.Empty,
                        FigureSet = rule.FigureSet ?? string.Empty,
                        Description = rule.Description ?? string.Empty,
                        PhaseHours = (float)(random.NextDouble() * 24.0),
                    };
                    List<int> cells = null;
                    switch (rule.Role)
                    {
                        case WorldMapGroupRule.Patrol:
                            cells = PatrolRoute(actor, faction, random);
                            break;
                        case WorldMapGroupRule.Caravan:
                            cells = CaravanRoute(actor, roadDistance, random, k);
                            break;
                        case WorldMapGroupRule.Master:
                            cells = MasterRoute(actor, roadDistance, random);
                            break;
                        case WorldMapGroupRule.Band:
                            cells = BandRoute(actor, random, taken);
                            break;
                        default:
                            actor.BlockReason = "роль не поддержана";
                            break;
                    }
                    if (cells == null || cells.Count < 2)
                    {
                        actor.State = "Blocked";
                        if (string.IsNullOrEmpty(actor.BlockReason))
                            actor.BlockReason = "маршрут не найден";
                        result.Report.Issues.Add(new GeneratedRegionIssue
                        {
                            Code = "G01", Error = false, XPercent = actor.HomeXPercent, YPercent = actor.HomeYPercent,
                            Message = $"Группа «{actor.Name}» ({actor.ActorId}): {actor.BlockReason} — стоит на месте, движение не назначено."
                        });
                    }
                    else
                    {
                        actor.RouteCells = cells;
                        actor.Route = ToPercent(CellPoints(cells));
                    }
                    result.Actors.Add(actor);
                }
            }
        }

        private static int Reach(int[] distance, int cell, int fromCell, int toCell) =>
            distance[cell] >= 0 ? distance[cell] : 100000 + Math.Abs(fromCell - toCell) % 1000;

        // Расстояние по дорогам и тропам от клетки (−1 — не связано дорогой).
        private int[] RoadBfs(int from, out int[] parent)
        {
            int[] distance = new int[count];
            parent = new int[count];
            for (int i = 0; i < count; i++)
            {
                distance[i] = -1;
                parent[i] = -1;
            }
            Queue<int> queue = new Queue<int>();
            distance[from] = 0;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    int next = neighbors[c * 6 + d];
                    if (next < 0 || distance[next] >= 0 || (terrain[next] != Road && terrain[next] != Trail))
                        continue;
                    distance[next] = distance[c] + 1;
                    parent[next] = c;
                    queue.Enqueue(next);
                }
            }
            return distance;
        }

        private List<int> RoadPath(int from, int to)
        {
            int[] distance = RoadBfs(from, out int[] parent);
            if (distance[to] < 0)
                return null;
            List<int> path = new List<int>();
            for (int c = to; c >= 0; c = parent[c])
                path.Add(c);
            path.Reverse();
            return path;
        }

        private void SetHome(GeneratedMapActor actor, string id, int cell)
        {
            actor.HomeId = id;
            grid.CellCenter(grid.CellAt(cell), out double x, out double y);
            actor.HomeXPercent = grid.PixelToPercentX(x);
            actor.HomeYPercent = grid.PixelToPercentY(y);
        }

        // Дозор: из деревни своей державы к ближайшей переправе или развилке в
        // 6–20 клетках по дороге и обратно.
        private List<int> PatrolRoute(GeneratedMapActor actor, WorldMapFactionDefinition faction, Random random)
        {
            WorldMapFactionDefinition top = faction != null ? WorldMapFactionCatalog.TopPolity(settings.Factions, faction.Id) : null;
            List<int> homes = new List<int>();
            for (int v = 0; v < result.Villages.Count; v++)
                if (top == null || result.Villages[v].ControllerFactionId == top.Id)
                    homes.Add(v);
            if (homes.Count == 0)
            {
                actor.BlockReason = top != null ? $"у державы «{top.Name}» нет деревень на карте" : "нет деревень";
                return null;
            }
            int home = homes[random.Next(homes.Count)];
            int homeCell = result.Villages[home].Cell;
            SetHome(actor, "village-" + home, homeCell);
            int[] distance = RoadBfs(homeCell, out _);
            HashSet<int> bridges = new HashSet<int>(bridgeSpans.SelectMany(s => new[] { s.Entry, s.Exit }));
            int target = -1, best = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (distance[i] < 6 || distance[i] > 20 || terrain[i] != Road)
                    continue;
                bool node = bridges.Contains(i) || RoadDegree(i) >= 3 || boundary[i];
                int score = (node ? 0 : 1000) + distance[i];
                if (score < best)
                {
                    best = score;
                    target = i;
                }
            }
            return target < 0 ? null : RoadPath(homeCell, target);
        }

        // Обоз: между двумя связанными дорогой деревнями, по возможности
        // далёкими (k-й обоз — k-я по дальности пара).
        private List<int> CaravanRoute(GeneratedMapActor actor, int[][] roadDistance, Random random, int k)
        {
            List<(int A, int B, int D)> pairs = new List<(int, int, int)>();
            for (int a = 0; a < result.Villages.Count; a++)
                for (int b = a + 1; b < result.Villages.Count; b++)
                {
                    int d = roadDistance[a][result.Villages[b].Cell];
                    if (d > 4)
                        pairs.Add((a, b, d));
                }
            if (pairs.Count == 0)
            {
                actor.BlockReason = "нет двух деревень, связанных дорогой";
                return null;
            }
            pairs.Sort((x, y) => y.D.CompareTo(x.D));
            (int from, int to, _) = pairs[Math.Min(k, pairs.Count - 1)];
            if (random.NextDouble() < 0.5)
                (from, to) = (to, from);
            SetHome(actor, "village-" + from, result.Villages[from].Cell);
            actor.WaitHours = 4f;
            return RoadPath(result.Villages[from].Cell, result.Villages[to].Cell);
        }

        // Мастер: обходит до трёх ближайших по дорогам деревень и возвращается.
        private List<int> MasterRoute(GeneratedMapActor actor, int[][] roadDistance, Random random)
        {
            int n = result.Villages.Count;
            if (n < 2)
            {
                actor.BlockReason = "одна деревня — ходить некуда";
                return null;
            }
            int current = random.Next(n);
            SetHome(actor, "village-" + current, result.Villages[current].Cell);
            List<int> route = new List<int> { result.Villages[current].Cell };
            HashSet<int> visited = new HashSet<int> { current };
            for (int stop = 0; stop < 3; stop++)
            {
                int next = -1, best = int.MaxValue;
                for (int v = 0; v < n; v++)
                {
                    int d = roadDistance[current][result.Villages[v].Cell];
                    if (!visited.Contains(v) && d > 0 && d < best)
                    {
                        best = d;
                        next = v;
                    }
                }
                if (next < 0)
                    break;
                List<int> leg = RoadPath(result.Villages[current].Cell, result.Villages[next].Cell);
                if (leg == null)
                    break;
                route.AddRange(leg.Skip(1));
                visited.Add(next);
                current = next;
            }
            if (route.Count < 2)
                actor.BlockReason = "соседние деревни не связаны дорогой";
            actor.WaitHours = 3f;
            return route;
        }

        // Ватага: постоянный лагерь на опушке вдали от деревень (деталь места
        // «лагерь») и путь от лагеря к ближайшей дороге. Лагерь и люди —
        // разные позиции: группа ходит, лагерь стоит.
        private List<int> BandRoute(GeneratedMapActor actor, Random random, List<int> taken)
        {
            bool[] reachable = Reachable();
            List<int> villageCells = result.Villages.Select(v => v.Cell).ToList();
            List<int> camps = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (!reachable[i] || NearEdge(i, 2) || protectedStart[i] || terrain[i] == Road || terrain[i] == Trail)
                    continue;
                if (!NearHolds("forest-edge", i) || (villageCells.Count > 0 && NearestDistance(i, villageCells) < 7) || IsNear(i, taken, 3))
                    continue;
                camps.Add(i);
            }
            if (camps.Count == 0)
            {
                actor.BlockReason = "нет укромной опушки вдали от деревень";
                return null;
            }
            int camp = camps[random.Next(camps.Count)];
            taken.Add(camp);
            SetHome(actor, "camp-" + actor.ActorId, camp);
            grid.CellCenter(grid.CellAt(camp), out double x, out double y);
            result.Features.Add(new GeneratedMapSite
            {
                Kind = "band-camp",
                Name = "Лагерь",
                Cell = camp,
                XPercent = grid.PixelToPercentX(x),
                YPercent = grid.PixelToPercentY(y),
                // Лагерь группы — шатёр, кострище и знамя её стороны (если есть).
                Visual = "Camp_Tent_A+Cold_Campfire_A" + (string.IsNullOrEmpty(WorldMapFactionCatalog.Find(settings.Factions, actor.FactionId)?.Banner) ? "" : "+" + WorldMapFactionCatalog.Find(settings.Factions, actor.FactionId).Banner),
                DrawMark = false,
                Description = "Шалаши у опушки, кострище присыпано землёй."
            });
            HashSet<int> roads = new HashSet<int>();
            for (int i = 0; i < count; i++)
                if (terrain[i] == Road)
                    roads.Add(i);
            List<int> path = FindPath(camp, roads, true);
            if (path.Count > 30)
                path = path.GetRange(0, 30);
            actor.WaitHours = 6f;
            return path;
        }
    }
}
