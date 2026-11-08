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
        public const float GameOverviewTexels = 1.5f, GameShadowTexels = 4f;

        static MapLabPlanHook()
        {
            MapLabPlanSource.Build = Build;
            MapLabPlanSource.ApplyToGame = ApplyToGame;
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
            // Макрорегуляторы окна отсчитываются от исходных значений
            // генератора, а не от ассета: «Применить к новой партии» пишет их в
            // ассет, и повторное применение не накапливает множители.
            WorldMapGenerationSettings d = WorldMapGenerationSettings.CreateDefault();
            MapLabAuthorProfile a = s.Author;
            p.HexesAcross = HexesFor(a.WorldSize);
            p.RegionKind = KindFor(a.PlanRegion);
            float relief = MapLabAuthor.Factor(a.Relief, 0.4f, 1.8f);
            p.HillsShare = d.HillsShare * relief;
            p.MountainsShare = d.MountainsShare * relief;
            p.CliffsShare = d.CliffsShare * MapLabAuthor.Factor(a.Relief, 0.5f, 1.5f);
            p.LakesShare = d.LakesShare * MapLabAuthor.Factor(a.Water, 0.35f, 2f);
            p.RiverCount = Mathf.RoundToInt(d.RiverCount * MapLabAuthor.Factor(a.Water, 0.34f, 1.67f));
            p.ForestShare = d.ForestShare * MapLabAuthor.Factor(a.Forest, 0.3f, 1.9f);
            p.VillageCount = Mathf.Max(1, Mathf.RoundToInt(d.VillageCount * MapLabAuthor.Factor(a.Settlement, 0.4f, 1.6f) * (p.HexesAcross / 100f) * (p.HexesAcross / 100f) * 1.6f + 1f));
            p.FieldDensity = d.FieldDensity * MapLabAuthor.Factor(a.Settlement, 0.5f, 1.5f);
            p.Discoveries = (WorldMapDiscoveryRate)(int)a.Discoveries;
            p.RouteConnectivity = a.Connectivity;
            p.Sanitize();
            return p;
        }

        // Настройки окна — в новые партии: генератор игры (структура мира) и
        // профиль рисунка MapLab игры (внешний вид, арт). Текущая партия не
        // меняется: её карта хранится в сохранении.
        public static string ApplyToGame(MapLabSettings s)
        {
            WorldMapGeneratorAsset asset = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
            if (asset == null)
                return "Нет ассета генератора игры: " + WorldMapGeneratorAsset.AssetPath;
            WorldMapGenerationSettings p = PlanSettings(s);
            Undo.RecordObject(asset, "Генератор карты: применить к новой партии");
            WorldMapGenerationSettings target = asset.Settings;
            target.HexesAcross = p.HexesAcross;
            target.RegionKind = p.RegionKind;
            target.HillsShare = p.HillsShare;
            target.MountainsShare = p.MountainsShare;
            target.CliffsShare = p.CliffsShare;
            target.LakesShare = p.LakesShare;
            target.RiverCount = p.RiverCount;
            target.ForestShare = p.ForestShare;
            target.VillageCount = p.VillageCount;
            target.FieldDensity = p.FieldDensity;
            target.Discoveries = p.Discoveries;
            target.RouteConnectivity = p.RouteConnectivity;
            EditorUtility.SetDirty(asset);
            MapLabGameAssets game = MapLabGameSetup.Prepare();
            if (game.Profile != null && game.Profile != s)
            {
                string name = game.Profile.name;
                Undo.RecordObject(game.Profile, "Генератор карты: профиль рисунка игры");
                EditorUtility.CopySerialized(s, game.Profile);
                game.Profile.name = name;
                game.Profile.RegionPlan = null;
                // Загрузка партии быстрее: обзор и тени — облегчённой плотности
                // (подробные участки покраски догружаются при приближении).
                game.Profile.Stage05.OverviewTexels = Mathf.Min(game.Profile.Stage05.OverviewTexels, GameOverviewTexels);
                game.Profile.Stage03.ShadowTexelsPerUnit = Mathf.Min(game.Profile.Stage03.ShadowTexelsPerUnit, GameShadowTexels);
                EditorUtility.SetDirty(game.Profile);
            }
            AssetDatabase.SaveAssets();
            MapLabGameLive.Clear();
            return $"Применено к новым партиям: {p.HexesAcross} клеток по ширине, тип «{WorldMapRegionKinds.Title(p.RegionKind)}», деревень до {p.VillageCount}; внешний вид — профиль рисунка игры. Текущая партия не меняется.";
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
            r.Layers.Add(("Проходимость", "Зелёный — проходимо (темнее — дороже), светлый — дороги и тропы, синий — вода, тёмный — непроходимо.", MapLabPlanLayers.Passability(plan, 900)));
            Texture2D travel = MapLabPlanLayers.TravelTime(plan, 900, out double maxHours);
            r.Layers.Add(("Время пути", $"От старта: зелёный — близко, красный — до {maxHours:0} ч пути; серое — недостижимо.", travel));
            r.Layers.Add(("Заметность", "Пятно у места — по числу оснований обнаружить (красный 0–1, жёлтый 2, зелёный 3+), чёрная точка — скрытое место.", MapLabPlanLayers.Discoverability(plan, 900)));
            r.Stories.AddRange(MapLabPlanLayers.Stories(plan));
            return r;
        }
    }
}
