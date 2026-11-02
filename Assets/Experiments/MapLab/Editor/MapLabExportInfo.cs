using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Параметры воспроизведения рядом с PNG: одного seed недостаточно.
    // Камера записывается по ФАКТИЧЕСКОМУ состоянию rig в момент экспорта
    // (после прокрутки колеса Settings.Camera.Zoom его уже не описывает).
    [Serializable]
    public sealed class MapLabExportInfo
    {
        public int AlgorithmVersion;
        public int GeneratorRevision;
        public string Preset;
        public int Seed;
        public float MapSize;
        public int Resolution;
        public int PaintResolution;
        public string CameraMode;
        public bool PitchByZoom;
        public float CameraPitchDownDegrees;
        public float CameraAzimuthDegrees;
        public float Zoom;
        public float OrthographicSize;
        public float FitSize;
        public string Projection;
        public Vector3 Target;
        public Vector3 CameraPosition;
        public float SettingsZoom;
        public int ImageWidth;
        public int ImageHeight;
        public string SettingsAsset;
        public string SettingsGuid;
        public string PaletteAsset;
        public string PaletteGuid;
        public string HeightmapAsset;
        public string HeightmapGuid;
        public string ArtVegetation;
        public string ArtBuildings;
        public bool ArtBuildingSpritesShown;
        public string ArtBoulders;
        public bool ArtCliffDecor;
        public bool ArtGroundTextures;
        public string ArtSetAsset;
        public string ArtSetPackage;
        public string ArtSetFingerprint;
        public int ArtSetImportVersion;
        public int ArtCards;
        public int ArtMissingTextures;
        public List<string> ArtSources = new List<string>();
        public bool MarkerShown;
        public string MarkerDirection;
        public string MarkerFrame;
        public float MarkerFacingDegrees;
        public Vector3 MarkerBase;
        public bool MarkerApproximateView;
        public string UnityVersion;
        public string Created;
        public float GenerateMs;
        public float PaintMs;
        public int Vertices;
        public int Triangles;
        public int Renderers;
        public int Materials;
        public int Trees;
        public int Houses;
        // Этап 03: язык карты, фактическая плотность запекания, слои и кадр домов.
        public string Look;
        public float PaintTexelsPerUnit;
        public int PaintTiles;
        public int PaintTileSize;
        public float PaintTileWorld;
        public long PaintStrokes;
        public float PaintFieldsMs;
        public bool LayerUndercoat;
        public bool LayerStrokes;
        public bool LayerInk;
        public bool LayerForestAccents;
        public bool LayerObjectShadows;
        public string DiagnosticMode;
        public int InkPolylines;
        public float InkLengthUnits;
        public int ShadowShapes;
        public int ShadowMaskResolution;
        public int HouseView;
        public bool ArtApproximateView;
        public int ForestTrees;
        public int ForestGroups;
        public int ForestAccentGroups;
        public int ForestAccentChains;
        public int ForestAccentTrees;
        public int Supersample;
        // Этап 04: активные эффекты прохода согласования и кадр героя.
        public bool Stage04Effects;
        public float ContactDensity;
        public float CrownLobes;
        public float Grain;
        public float Blots;
        public float JunctionLength;
        public float FieldInkShare;
        public int GrassTufts;
        public float ForestRhythm;
        public string HeroMarkerSet;
        public int HeroPitchSets;
        public float HeroSetPitch;
        public string SettingsJson;

        public static MapLabExportInfo From(MapLabView view, int width, int height)
        {
            MapLabSettings s = view.Settings;
            MapLabData data = view.Data;
            MapLabCameraRig rig = view.CameraRig;
            MapLabExportInfo info = new MapLabExportInfo
            {
                AlgorithmVersion = MapLabData.AlgorithmVersion,
                GeneratorRevision = MapLabData.Revision,
                Preset = s.General.Preset.ToString(),
                Seed = s.General.Seed,
                MapSize = s.General.MapSize,
                Resolution = s.Resolution,
                PaintResolution = s.PaintResolution,
                CameraMode = s.Camera.Mode.ToString(),
                PitchByZoom = s.Camera.PitchByZoom,
                SettingsZoom = s.Camera.Zoom,
                ImageWidth = width,
                ImageHeight = height,
                ArtVegetation = s.Art.Vegetation.ToString(),
                ArtBuildings = s.Art.Buildings.ToString(),
                ArtBoulders = s.Art.Boulders.ToString(),
                ArtCliffDecor = s.Art.CliffDecor,
                ArtGroundTextures = s.Art.GroundTextures,
                UnityVersion = Application.unityVersion,
                Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                SettingsJson = EditorJsonUtility.ToJson(s, false),
            };
            if (rig != null)
            {
                Camera cam = rig.Camera;
                info.CameraPitchDownDegrees = rig.Pitch;
                info.CameraAzimuthDegrees = rig.Azimuth;
                info.Zoom = rig.Zoom;
                info.FitSize = rig.FitSize;
                info.OrthographicSize = cam.orthographicSize;
                info.Projection = cam.orthographic ? "Orthographic" : "Perspective";
                info.Target = rig.Target;
                info.CameraPosition = rig.transform.position;
                info.ArtBuildingSpritesShown = view.ArtPlan != null && (s.IsStage03 ? view.HasArtHouses03 : s.Art.BuildingSpritesAt(rig.Pitch)) && view.ArtPlan.Replaced.Contains(MapLabObjectKind.House);
                info.ArtApproximateView = view.ArtSpritesApproximate;
            }
            info.Look = s.Stage03.Look.ToString();
            info.LayerUndercoat = s.Art.GroundTextures;
            info.LayerStrokes = s.Diagnostics.ShowStrokes;
            info.LayerInk = s.IsStage03 && s.Stage03.Ink;
            info.LayerForestAccents = s.IsStage03 && s.Stage03.ForestAccents;
            info.LayerObjectShadows = s.IsStage03 && s.Stage03.ObjectShadows;
            info.DiagnosticMode = s.Diagnostics.Mode.ToString();
            info.PaintTexelsPerUnit = view.PaintTexelsPerUnit;
            MapLabTiledPainter.Result tiles = view.LastTiledPaint;
            if (s.IsStage03 && tiles != null)
            {
                info.PaintTiles = tiles.Tiles.Count;
                info.PaintTileSize = tiles.Tiles.Count > 0 ? tiles.Tiles[0].Size : 0;
                info.PaintTileWorld = tiles.TileWorld;
                info.PaintStrokes = tiles.Strokes;
                info.PaintFieldsMs = tiles.FieldsMs;
                info.PaintResolution = tiles.TilesPerSide * (tiles.Tiles.Count > 0 ? tiles.Tiles[0].Core : 0);
            }
            if (s.IsStage04)
            {
                MapLabStage04Settings s4 = s.Stage04;
                info.Stage04Effects = true;
                info.ContactDensity = s4.ContactDensity;
                info.CrownLobes = s4.CrownLobes;
                info.Grain = s4.Grain;
                info.Blots = s4.Blots;
                info.JunctionLength = s4.JunctionLength;
                info.FieldInkShare = s4.FieldInkShare;
                info.GrassTufts = view.GrassTufts;
                info.ForestRhythm = s4.ForestRhythm;
            }
            MapLabMarkerSet heroSet = view.ActiveMarkerSet;
            if (heroSet != null)
            {
                Describe(heroSet, out info.HeroMarkerSet, out _);
                info.HeroPitchSets = heroSet.PitchSets != null ? heroSet.PitchSets.Count : 0;
                info.HeroSetPitch = view.Marker != null ? view.Marker.PitchSetPitch : heroSet.FullSetPitch;
            }
            info.InkPolylines = view.InkStats.Polylines;
            info.InkLengthUnits = view.InkStats.Length;
            info.ShadowShapes = view.ShadowShapes;
            info.ShadowMaskResolution = view.ShadowResolution;
            info.HouseView = s.IsStage03 ? view.HouseView : 0;
            MapLabArtBuilder.ForestStats forest = MapLabArtBuilder.LastForest;
            if (s.IsStage03 && forest != null)
            {
                info.ForestTrees = forest.Trees;
                info.ForestGroups = forest.Groups;
                info.ForestAccentGroups = forest.AccentGroups;
                info.ForestAccentChains = forest.Chains;
                info.ForestAccentTrees = forest.AccentTrees;
            }
            Describe(s, out info.SettingsAsset, out info.SettingsGuid);
            Describe(s.PainterlyStyle.Palette, out info.PaletteAsset, out info.PaletteGuid);
            Describe(s.Relief.ExternalHeightmap, out info.HeightmapAsset, out info.HeightmapGuid);
            MapLabArtSet set = view.ObjectArtSet;
            if (set != null)
            {
                Describe(set, out info.ArtSetAsset, out _);
                info.ArtSetPackage = set.PackageId;
                info.ArtSetFingerprint = set.Fingerprint;
                info.ArtSetImportVersion = set.ImportVersion;
                info.ArtMissingTextures = set.MissingTextures;
                if (view.ArtPlan != null)
                {
                    info.ArtCards = view.ArtPlan.Count;
                    foreach (MapLabArtEntry e in view.ArtPlan.Cards.Keys)
                        info.ArtSources.Add($"{e.Id} sha256={e.Sha256} pivot=({e.Pivot.x:0.###};{e.Pivot.y:0.###}) h={e.WorldHeight} cutoff={e.Cutoff}");
                    info.ArtSources.Sort(StringComparer.Ordinal);
                }
            }
            MapLabMarker marker = view.Marker;
            if (marker != null && marker.CurrentEntry != null && s.Art.ShowMarker)
            {
                info.MarkerShown = true;
                info.MarkerDirection = MapLabMarkerSet.DirectionNames[(int)marker.CurrentDirection];
                info.MarkerFrame = marker.CurrentEntry.Id + " sha256=" + marker.CurrentEntry.Sha256;
                info.MarkerFacingDegrees = marker.FacingDegrees;
                info.MarkerBase = marker.BasePoint;
                info.MarkerApproximateView = marker.Approximate;
            }
            if (data != null)
            {
                info.GenerateMs = data.Stats.GenerateMs;
                info.PaintMs = data.Stats.PaintMs;
                info.Vertices = data.Stats.Vertices;
                info.Triangles = data.Stats.Triangles;
                info.Renderers = data.Stats.Renderers;
                info.Materials = data.Stats.Materials;
                info.Trees = data.CountObjects(MapLabObjectKind.Deciduous) + data.CountObjects(MapLabObjectKind.Conifer) + data.CountObjects(MapLabObjectKind.DeciduousGroup);
                info.Houses = data.CountObjects(MapLabObjectKind.House);
            }
            return info;
        }

        private static void Describe(UnityEngine.Object obj, out string path, out string guid)
        {
            path = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
            guid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }

        public static void Write(MapLabView view, string pngPath, int width, int height, int supersample = 2)
        {
            MapLabExportInfo info = From(view, width, height);
            info.Supersample = supersample;
            string json = JsonUtility.ToJson(info, true);
            File.WriteAllText(Path.ChangeExtension(pngPath, ".json"), json);
        }
    }
}
