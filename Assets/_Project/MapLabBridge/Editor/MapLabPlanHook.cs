using System.Collections.Generic;
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
                // Те же размеры, что на экране игры «Новая игра».
                case MapLabWorldSize.Small: return CampaignStartOptions.SmallMapHexesAcross;
                case MapLabWorldSize.Large: return CampaignStartOptions.LargeMapHexesAcross;
                case MapLabWorldSize.Huge: return CampaignStartOptions.HugeMapHexesAcross;
                default: return CampaignStartOptions.MediumMapHexesAcross;
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
            // Деревни и опасные группы — по тем же правилам, что у новой партии
            // игры (CampaignMapOptions): на 100 клетках — как в ассете.
            double area = System.Math.Max(1.0, System.Math.Pow(p.HexesAcross / 100.0, 1.5));
            p.VillageCount = Mathf.Max(2, Mathf.RoundToInt((float)(p.VillageCount * area) * MapLabAuthor.Factor(a.Settlement, 0.5f, 1.6f)));
            double areaLinear = System.Math.Max(1.0, p.HexesAcross / 100.0);
            if (p.Groups != null)
                foreach (WorldMapGroupRule g in p.Groups)
                    if (g != null && g.Role == WorldMapGroupRule.Band)
                        g.Count = Mathf.Max(1, Mathf.RoundToInt((float)(g.Count * areaLinear)));
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
            MapLabComposition composition = MapLabRegionPlan.Apply(plan, s);
            // Здания услуг — как в игре: профили поселений назначаются той же
            // настройкой партии, роли — по действиям профиля.
            foreach (KeyValuePair<string, List<string>> pair in PreviewServices(plan))
                foreach (MapLabComposition.Settlement st in composition.Settlements)
                    if (st.Id == pair.Key)
                        st.Services.AddRange(pair.Value);
            if (s.WorldSide > 600f && s.General.Quality != MapLabQuality.Samples513)
                s.General.Quality = MapLabQuality.Samples513;
            return Report(plan);
        }

        // Услуги деревень плана, как в новой партии: временная партия по плану
        // (профили поселений из Базы поселений), роли — по типам действий.
        public static Dictionary<string, List<string>> PreviewServices(GeneratedWorldMap plan)
        {
            Dictionary<string, List<string>> services = new Dictionary<string, List<string>>();
            try
            {
                KingdomSurvival.SettlementDatabase.SettlementDatabaseRuntime.ApplyFromResources();
                GameState state = new CampaignSetup { WorldSeed = plan.Seed, MapHexesAcross = plan.HexesAcross }.CreateCampaign(null, null, plan);
                string[] order = { "tavern", "merchant", "board", "healer", "elder" };
                foreach (SettlementInstanceData instance in state.Settlements.Instances)
                {
                    SettlementProfileDefinition profile = SettlementCatalog.Current.FindProfile(instance.ProfileId);
                    if (profile?.Actions == null)
                        continue;
                    List<string> roles = new List<string>();
                    foreach (string role in order)
                        foreach (SettlementActionDefinition action in profile.Actions)
                            if (action != null && RoleOf(action.Type) == role)
                            {
                                roles.Add(role);
                                break;
                            }
                    if (roles.Count > 0)
                        services[instance.LocationId] = roles;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Генератор карты: услуги деревень для предпросмотра не назначены — " + exception.Message);
            }
            finally
            {
                WorldMapNavigation.ConfigureDefaultTerrain();
            }
            return services;
        }

        private static string RoleOf(SettlementActionType type)
        {
            switch (type)
            {
                case SettlementActionType.Tavern: return "tavern";
                case SettlementActionType.Trade: return "merchant";
                case SettlementActionType.NoticeBoard: return "board";
                case SettlementActionType.Healer: return "healer";
                case SettlementActionType.Dialogue: return "elder";
                default: return null;
            }
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
