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
                info.ArtBuildingSpritesShown = view.ArtPlan != null && s.Art.BuildingSpritesAt(rig.Pitch) && view.ArtPlan.Replaced.Contains(MapLabObjectKind.House);
            }
            Describe(s, out info.SettingsAsset, out info.SettingsGuid);
            Describe(s.PainterlyStyle.Palette, out info.PaletteAsset, out info.PaletteGuid);
            Describe(s.Relief.ExternalHeightmap, out info.HeightmapAsset, out info.HeightmapGuid);
            MapLabArtSet set = view.ResolvedArtSet;
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

        public static void Write(MapLabView view, string pngPath, int width, int height)
        {
            string json = JsonUtility.ToJson(From(view, width, height), true);
            File.WriteAllText(Path.ChangeExtension(pngPath, ".json"), json);
        }
    }
}
