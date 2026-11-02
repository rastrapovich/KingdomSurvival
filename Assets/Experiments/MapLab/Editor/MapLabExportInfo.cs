using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Параметры воспроизведения рядом с PNG: одного seed недостаточно.
    [Serializable]
    public sealed class MapLabExportInfo
    {
        public int AlgorithmVersion;
        public string Preset;
        public int Seed;
        public float MapSize;
        public int Resolution;
        public int PaintResolution;
        public float CameraPitchDownDegrees;
        public float Zoom;
        public int ImageWidth;
        public int ImageHeight;
        public string SettingsAsset;
        public string SettingsGuid;
        public string PaletteAsset;
        public string PaletteGuid;
        public string HeightmapAsset;
        public string HeightmapGuid;
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
            MapLabExportInfo info = new MapLabExportInfo
            {
                AlgorithmVersion = MapLabData.AlgorithmVersion,
                Preset = s.General.Preset.ToString(),
                Seed = s.General.Seed,
                MapSize = s.General.MapSize,
                Resolution = s.Resolution,
                PaintResolution = s.PaintResolution,
                CameraPitchDownDegrees = s.Camera.CameraPitchDownDegrees,
                Zoom = s.Camera.Zoom,
                ImageWidth = width,
                ImageHeight = height,
                UnityVersion = Application.unityVersion,
                Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                SettingsJson = EditorJsonUtility.ToJson(s, false),
            };
            Describe(s, out info.SettingsAsset, out info.SettingsGuid);
            Describe(s.PainterlyStyle.Palette, out info.PaletteAsset, out info.PaletteGuid);
            Describe(s.Relief.ExternalHeightmap, out info.HeightmapAsset, out info.HeightmapGuid);
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
