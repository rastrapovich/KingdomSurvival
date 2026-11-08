using System.Collections.Generic;
using KingdomSurvival.FreePlay;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // Гайд исследования (12У-H): диагностические слои плана для вкладки
    // «Проверка» — проходимость, время пути от старта, заметность мест — и
    // раздел «Истории»: куда на этой карте встаёт роль пилота и почему.
    // Слои — инструмент автора, игроку не показываются.
    public static class MapLabPlanLayers
    {
        private static WorldMapMovementRules Rules()
        {
            WorldMapMovementSettingsAsset asset = AssetDatabase.LoadAssetAtPath<WorldMapMovementSettingsAsset>(WorldMapMovementSettingsAsset.AssetPath);
            return asset != null ? asset.ToRules() : WorldMapMovementRules.CreateDefault();
        }

        private delegate Color CellColor(int cell);

        private static Texture2D Paint(GeneratedWorldMap plan, int width, CellColor color, string name)
        {
            int height = Mathf.Max(8, Mathf.RoundToInt(width * plan.CanvasHeight / Mathf.Max(1f, plan.CanvasWidth)));
            WorldMapHexGrid grid = plan.CreateGrid();
            Color[] px = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int cell = grid.IndexOf(grid.CellAtPercent((x + 0.5f) / width * 100f, (y + 0.5f) / height * 100f));
                    px[(height - 1 - y) * width + x] = cell >= 0 && cell < grid.CellCount ? color(cell) : Color.gray;
                }
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // Проходимость: зелёный — проходимо (темнее — дороже), синий — вода,
        // тёмный — скалы и прочее непроходимое, светлый — дороги и тропы.
        public static Texture2D Passability(GeneratedWorldMap plan, int width)
        {
            WorldMapTerrainLayer layer = plan.CreateTerrainLayer();
            WorldMapMovementRules rules = Rules();
            return Paint(plan, width, cell =>
            {
                WorldMapGameplayTerrainType t = layer.GetAtIndex(cell);
                if (t == WorldMapGameplayTerrainType.Water)
                    return new Color(0.25f, 0.45f, 0.8f);
                if (!rules.IsTraversable(t))
                    return new Color(0.2f, 0.18f, 0.2f);
                if (t == WorldMapGameplayTerrainType.Road || t == WorldMapGameplayTerrainType.Trail)
                    return new Color(0.92f, 0.86f, 0.6f);
                float cost = Mathf.Clamp01(((float)rules.PathCost(t) - 0.7f) / 2.5f);
                return Color.Lerp(new Color(0.55f, 0.85f, 0.45f), new Color(0.25f, 0.42f, 0.2f), cost);
            }, "MapLabPlanPassability");
        }

        // Время пути от старта (часы по правилам движения): от зелёного к
        // красному; серое — недостижимо.
        public static Texture2D TravelTime(GeneratedWorldMap plan, int width, out double maxHours)
        {
            WorldMapHexGrid grid = plan.CreateGrid();
            WorldMapTerrainLayer layer = plan.CreateTerrainLayer();
            WorldMapMovementRules rules = Rules();
            int count = grid.CellCount;
            double[] hours = new double[count];
            for (int i = 0; i < count; i++)
                hours[i] = double.MaxValue;
            int start = grid.IndexOf(grid.CellAtPercent(plan.StartXPercent, plan.StartYPercent));
            SortedSet<(double H, int Cell)> open = new SortedSet<(double, int)>();
            if (start >= 0 && rules.IsTraversable(layer.GetAtIndex(start)))
            {
                hours[start] = 0;
                open.Add((0, start));
            }
            while (open.Count > 0)
            {
                (double h, int c) = open.Min;
                open.Remove(open.Min);
                if (h > hours[c])
                    continue;
                WorldMapHexCell cell = grid.CellAt(c);
                for (int d = 0; d < 6; d++)
                {
                    WorldMapHexCell n = cell.Neighbor(d);
                    if (!grid.IsInside(n))
                        continue;
                    int ni = grid.IndexOf(n);
                    WorldMapGameplayTerrainType t = layer.GetAtIndex(ni);
                    if (!rules.IsTraversable(t))
                        continue;
                    double nh = h + rules.HoursPerHex(t);
                    if (nh < hours[ni])
                    {
                        open.Remove((hours[ni], ni));
                        hours[ni] = nh;
                        open.Add((nh, ni));
                    }
                }
            }
            double max = 0;
            foreach (double h in hours)
                if (h < double.MaxValue)
                    max = System.Math.Max(max, h);
            maxHours = max;
            return Paint(plan, width, cell =>
            {
                if (hours[cell] == double.MaxValue)
                    return new Color(0.45f, 0.45f, 0.48f);
                float t = max > 0 ? (float)(hours[cell] / max) : 0f;
                return t < 0.5f ? Color.Lerp(new Color(0.3f, 0.75f, 0.35f), new Color(0.95f, 0.85f, 0.3f), t * 2f) : Color.Lerp(new Color(0.95f, 0.85f, 0.3f), new Color(0.85f, 0.25f, 0.2f), (t - 0.5f) * 2f);
            }, "MapLabPlanTravelTime");
        }

        // Заметность: серая местность, у каждого места — пятно с радиусом по
        // числу оснований обнаружить (цвет — как на схеме), скрытые — с тёмным
        // ободом.
        public static Texture2D Discoverability(GeneratedWorldMap plan, int width)
        {
            WorldMapTerrainLayer layer = plan.CreateTerrainLayer();
            WorldMapHexGrid grid = plan.CreateGrid();
            float[] glow = new float[grid.CellCount];
            Color[] tint = new Color[grid.CellCount];
            if (plan.Landmarks != null)
                foreach (GeneratedMapSite site in plan.Landmarks)
                {
                    int n = site.Signals?.Count ?? 0;
                    Color c = n <= 1 ? new Color(0.85f, 0.2f, 0.15f) : n == 2 ? new Color(0.95f, 0.75f, 0.15f) : new Color(0.2f, 0.7f, 0.25f);
                    float radius = 1f + n;
                    WorldMapHexCell center = grid.CellAt(site.Cell);
                    for (int i = 0; i < grid.CellCount; i++)
                    {
                        int dist = grid.CellAt(i).DistanceTo(center);
                        if (dist > radius)
                            continue;
                        float g = 1f - dist / (radius + 1f);
                        if (g > glow[i])
                        {
                            glow[i] = g;
                            tint[i] = site.Hidden && dist == 0 ? Color.black : c;
                        }
                    }
                }
            return Paint(plan, width, cell =>
            {
                WorldMapGameplayTerrainType t = layer.GetAtIndex(cell);
                Color baseColor = t == WorldMapGameplayTerrainType.Water ? new Color(0.62f, 0.68f, 0.75f) : new Color(0.72f, 0.72f, 0.7f);
                return Color.Lerp(baseColor, tint[cell], glow[cell] * 0.85f);
            }, "MapLabPlanDiscoverability");
        }

        // Истории: роль пилота «Тайник у старого русла» на этом плане —
        // опора у ближайшей к старту деревни (в игре — торговое село).
        public static List<string> Stories(GeneratedWorldMap plan)
        {
            List<string> lines = new List<string>();
            GeneratedMapSite anchor = null;
            double best = double.MaxValue;
            if (plan.Villages != null)
                foreach (GeneratedMapSite v in plan.Villages)
                {
                    if (v == null || v.IsStart)
                        continue;
                    double d = (v.XPercent - plan.StartXPercent) * (v.XPercent - plan.StartXPercent) + (v.YPercent - plan.StartYPercent) * (v.YPercent - plan.StartYPercent);
                    if (d < best)
                    {
                        best = d;
                        anchor = v;
                    }
                }
            if (anchor == null)
            {
                lines.Add("«Тайник у старого русла»: нет деревни-опоры, кроме стартовой — роль не привязывается (история пропущена честно).");
                return lines;
            }
            try
            {
                WorldMapNavigation.ConfigureFromDefinition(plan.ToDefinitionData());
                StoryRoleBindingData b = WorldMapStoryBinder.Choose(plan, plan.Seed, FreePlayCacheStory.CacheRole, anchor.XPercent, anchor.YPercent);
                lines.Add(b.Bound
                    ? $"«Тайник у старого русла»: опора — деревня ({anchor.XPercent:0.#}%, {anchor.YPercent:0.#}%); тайник ({b.XPercent:0.#}%, {b.YPercent:0.#}%), путь {b.TravelHours:0.#} ч. {b.Reason}"
                    : $"«Тайник у старого русла»: не привязан — {b.Reason}");
            }
            finally
            {
                WorldMapNavigation.ConfigureDefaultTerrain();
            }
            lines.Add("Опора в игре — торговое село партии; здесь — ближайшая к старту деревня (профили поселений назначаются при старте партии).");
            return lines;
        }
    }
}
