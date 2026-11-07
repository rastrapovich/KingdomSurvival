using KingdomSurvival.MapLab;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // 12У-D: окно «Генератор карты» строит регион тем же генератором, что и
    // игра (WorldMapGenerator по настройкам ассета генератора), и рисует его
    // MapLab. Основные настройки окна переводятся в настройки плана: тип
    // региона, размер, рельеф, вода, лесистость, освоенность, насыщенность
    // открытиями, связанность маршрутов. Ассет генератора не меняется.
    [InitializeOnLoad]
    public static class MapLabPlanHook
    {
        static MapLabPlanHook()
        {
            MapLabPlanSource.Build = Build;
        }

        public static int HexesFor(MapLabWorldSize size)
        {
            switch (size)
            {
                case MapLabWorldSize.Small: return 40;
                case MapLabWorldSize.Large: return 80;
                case MapLabWorldSize.Huge: return 100;
                default: return 60;
            }
        }

        public static WorldMapRegionKind KindFor(int planRegion)
        {
            switch (planRegion)
            {
                case 1: return WorldMapRegionKind.RiverValley;
                case 2: return WorldMapRegionKind.ForestLake;
                case 3: return WorldMapRegionKind.HillAndLedge;
                case 4: return WorldMapRegionKind.SettledLand;
                default: return WorldMapRegionKind.BySeed;
            }
        }

        public static WorldMapGenerationSettings PlanSettings(MapLabSettings s)
        {
            WorldMapGeneratorAsset asset = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
            WorldMapGenerationSettings p = asset != null ? asset.Settings.Clone() : WorldMapGenerationSettings.CreateDefault();
            MapLabAuthorProfile a = s.Author;
            p.HexesAcross = HexesFor(a.WorldSize);
            p.RegionKind = KindFor(a.PlanRegion);
            float relief = MapLabAuthor.Factor(a.Relief, 0.4f, 1.8f);
            p.HillsShare *= relief;
            p.MountainsShare *= relief;
            p.CliffsShare *= MapLabAuthor.Factor(a.Relief, 0.5f, 1.5f);
            p.LakesShare *= MapLabAuthor.Factor(a.Water, 0.35f, 2f);
            p.RiverCount = Mathf.RoundToInt(p.RiverCount * MapLabAuthor.Factor(a.Water, 0.34f, 1.67f));
            p.ForestShare *= MapLabAuthor.Factor(a.Forest, 0.3f, 1.9f);
            p.VillageCount = Mathf.Max(1, Mathf.RoundToInt(p.VillageCount * MapLabAuthor.Factor(a.Settlement, 0.4f, 1.6f)));
            p.FieldDensity *= MapLabAuthor.Factor(a.Settlement, 0.5f, 1.5f);
            p.Discoveries = (WorldMapDiscoveryRate)(int)a.Discoveries;
            p.RouteConnectivity = a.Connectivity;
            p.Sanitize();
            return p;
        }

        public static MapLabPlanReport Build(MapLabSettings s)
        {
            WorldMapGenerationSettings p = PlanSettings(s);
            GeneratedWorldMap plan = WorldMapGenerator.Generate(p, s.General.Seed);
            MapLabRegionPlan.Apply(plan, s);
            if (s.WorldSide > 600f && s.General.Quality != MapLabQuality.Samples513)
                s.General.Quality = MapLabQuality.Samples513;
            return Report(plan);
        }

        public static MapLabPlanReport Report(GeneratedWorldMap plan)
        {
            MapLabPlanReport r = new MapLabPlanReport { Key = $"{plan.Passport.Kind}:{plan.Seed}:{plan.HexesAcross}" };
            GeneratedRegionPassport passport = plan.Passport;
            r.Passport.Add(passport.Character);
            r.Passport.Add(passport.Settledness);
            r.Passport.AddRange(passport.Links);
            r.Passport.AddRange(passport.Content);
            r.Passport.Add("Не используется: " + string.Join(", ", passport.Excluded) + ".");
            r.Passport.Add($"Достижимость обязательного: {plan.Report.RequiredReachable:P0}; попыток восстановления {plan.Report.RepairAttempts}.");
            r.Passport.Add("Отпечаток: " + passport.Fingerprint);
            foreach (GeneratedRegionIssue issue in plan.Report.Issues)
                r.Issues.Add((issue.Error, $"{issue.Code}: {issue.Message}"));
            r.Diagram = MapLabPlanDiagram.Render(plan, 900);
            return r;
        }
    }
}
