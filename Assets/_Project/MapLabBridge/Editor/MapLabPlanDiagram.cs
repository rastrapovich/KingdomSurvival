using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // 12У-D: диагностическая схема плана для автора (гайд §19: районы, пути,
    // роли мест, основания обнаружения, нарушения). Только инструмент
    // разработки: игроку не показывается и тайн не раскрывает.
    //   районы — заливкой по характеру; реки — синим; дороги — тёмным, тропы —
    //   пунктиром; деревни — кругом цвета семейства; места — ромбом: красный —
    //   0–1 основание обнаружить, жёлтый — 2, зелёный — 3+; обвод — скрытое;
    //   хозяйственные площадки — коричневым квадратом; ошибки — красный крест,
    //   предупреждения — жёлтое кольцо.
    public static class MapLabPlanDiagram
    {
        private static Color DistrictColor(string kind)
        {
            switch (kind)
            {
                case GeneratedMapDistrict.Water: return new Color(0.55f, 0.72f, 0.86f);
                case GeneratedMapDistrict.Settled: return new Color(0.93f, 0.86f, 0.62f);
                case GeneratedMapDistrict.Upland: return new Color(0.74f, 0.68f, 0.6f);
                case GeneratedMapDistrict.DeepForest: return new Color(0.36f, 0.52f, 0.36f);
                case GeneratedMapDistrict.ForestEdge: return new Color(0.55f, 0.7f, 0.5f);
                case GeneratedMapDistrict.Waterside: return new Color(0.72f, 0.84f, 0.72f);
                default: return new Color(0.84f, 0.88f, 0.7f);
            }
        }

        public static Color FamilyColor(string family)
        {
            switch (family)
            {
                case GeneratedMapSite.FamilyAlongRoad: return new Color(0.85f, 0.35f, 0.15f);
                case GeneratedMapSite.FamilyCrossing: return new Color(0.2f, 0.35f, 0.85f);
                case GeneratedMapSite.FamilyCraft: return new Color(0.55f, 0.3f, 0.65f);
                case GeneratedMapSite.FamilyScattered: return new Color(0.9f, 0.7f, 0.1f);
                default: return Color.gray;
            }
        }

        public static Texture2D Render(GeneratedWorldMap plan, int width)
        {
            int height = Mathf.Max(8, Mathf.RoundToInt(width * plan.CanvasHeight / Mathf.Max(1f, plan.CanvasWidth)));
            Color[] px = new Color[width * height];
            WorldMapHexGrid grid = plan.CreateGrid();
            byte[] district = WorldMapRegionValidator.DecodeDistricts(plan);
            // Граница районов темнее — переходы читаются.
            int[] owner = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float xp = (x + 0.5f) / width * 100f, yp = (y + 0.5f) / height * 100f;
                    int cell = grid.IndexOf(grid.CellAtPercent(xp, yp));
                    int d = district != null && cell >= 0 && cell < district.Length ? district[cell] - 1 : -1;
                    owner[y * width + x] = d;
                    Color c = d >= 0 && d < plan.Districts.Count ? DistrictColor(plan.Districts[d].Kind) : new Color(0.8f, 0.8f, 0.8f);
                    px[(height - 1 - y) * width + x] = c;
                }
            }
            for (int y = 1; y < height; y++)
                for (int x = 1; x < width; x++)
                    if (owner[y * width + x] != owner[y * width + x - 1] || owner[y * width + x] != owner[(y - 1) * width + x])
                        px[(height - 1 - y) * width + x] *= 0.78f;

            void Dot(float xp, float yp, float r, Color c, bool ring = false, bool square = false, bool diamond = false)
            {
                float cx = xp / 100f * width, cy = (1f - yp / 100f) * height;
                int r0 = Mathf.CeilToInt(r + 1);
                for (int dy = -r0; dy <= r0; dy++)
                {
                    for (int dx = -r0; dx <= r0; dx++)
                    {
                        int x = Mathf.RoundToInt(cx) + dx, y = Mathf.RoundToInt(cy) + dy;
                        if (x < 0 || y < 0 || x >= width || y >= height)
                            continue;
                        float m = square ? Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) : diamond ? Mathf.Abs(dx) + Mathf.Abs(dy) : Mathf.Sqrt(dx * dx + dy * dy);
                        bool inside = ring ? Mathf.Abs(m - r) <= 0.8f : m <= r;
                        if (inside)
                            px[y * width + x] = c;
                    }
                }
            }

            void Line(List<MapPointData> points, float w, Color c, bool dashed)
            {
                if (points == null)
                    return;
                float run = 0f;
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    Vector2 a = new Vector2(points[i].XPercent / 100f * width, points[i].YPercent / 100f * height);
                    Vector2 b = new Vector2(points[i + 1].XPercent / 100f * width, points[i + 1].YPercent / 100f * height);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                    for (int s = 0; s <= steps; s++)
                    {
                        run += 1f;
                        if (dashed && run % 8f > 5f)
                            continue;
                        Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                        Dot(p.x / width * 100f, p.y / height * 100f, w, c);
                    }
                }
            }

            foreach (GeneratedMapLine river in plan.Rivers)
                Line(river.Points, 1.6f, new Color(0.2f, 0.42f, 0.75f), false);
            foreach (GeneratedMapLine road in plan.Roads)
            {
                bool trail = road.Kind == GeneratedMapLine.TrailKind;
                Line(road.Points, trail ? 0.6f : 1.1f, trail ? new Color(0.45f, 0.3f, 0.18f) : new Color(0.25f, 0.18f, 0.12f), trail);
            }
            // Группы мира: маршрут цветом стороны (рабочие цвета пакета фракций).
            if (plan.Actors != null)
            {
                foreach (GeneratedMapActor actor in plan.Actors)
                {
                    Color c = Color.magenta;
                    WorldMapFactionDefinition f = WorldMapFactionCatalog.DefaultFactions().Find(x => x.Id == actor.FactionId);
                    if (f != null)
                        ColorUtility.TryParseHtmlString(f.PrimaryColor, out c);
                    Line(actor.Route, 1.4f, c, true);
                }
            }
            foreach (MapPointData bridge in plan.Bridges)
                Dot(bridge.XPercent, bridge.YPercent, 2.2f, Color.black, square: true);
            foreach (GeneratedMapSite site in plan.WorkSites)
                Dot(site.XPercent, site.YPercent, 2.5f, new Color(0.5f, 0.32f, 0.15f), square: true);
            foreach (GeneratedMapSite site in plan.Landmarks)
            {
                int n = site.Signals?.Count ?? 0;
                Color c = n <= 1 ? new Color(0.85f, 0.1f, 0.1f) : n == 2 ? new Color(0.95f, 0.75f, 0.1f) : new Color(0.15f, 0.65f, 0.2f);
                if (site.Hidden)
                    Dot(site.XPercent, site.YPercent, 5.5f, Color.black, diamond: true);
                Dot(site.XPercent, site.YPercent, 4.2f, c, diamond: true);
            }
            for (int v = 0; v < plan.Villages.Count; v++)
            {
                GeneratedMapSite site = plan.Villages[v];
                Color ownerColor = Color.black;
                WorldMapFactionDefinition ctrl = WorldMapFactionCatalog.DefaultFactions().Find(x => x.Id == site.ControllerFactionId);
                if (ctrl != null)
                    ColorUtility.TryParseHtmlString(ctrl.PrimaryColor, out ownerColor);
                Dot(site.XPercent, site.YPercent, 7f + site.Size, ownerColor);
                Dot(site.XPercent, site.YPercent, 5.5f + site.Size, Color.black);
                Dot(site.XPercent, site.YPercent, 4.2f + site.Size, FamilyColor(site.Family));
                if (site.IsStart)
                    Dot(site.XPercent, site.YPercent, 9f + site.Size, Color.black, ring: true);
            }
            if (plan.Report?.Issues != null)
            {
                foreach (GeneratedRegionIssue issue in plan.Report.Issues)
                {
                    if (issue.Error)
                    {
                        for (int k = -6; k <= 6; k++)
                        {
                            Dot(issue.XPercent + k * 100f / width, issue.YPercent + k * 100f / height, 1f, Color.red);
                            Dot(issue.XPercent + k * 100f / width, issue.YPercent - k * 100f / height, 1f, Color.red);
                        }
                    }
                    else
                        Dot(issue.XPercent, issue.YPercent, 8f, new Color(1f, 0.85f, 0f), ring: true);
                }
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "MapLabPlanDiagram", hideFlags = HideFlags.DontSave };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
