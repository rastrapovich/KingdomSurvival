using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    public enum ArtAssetCenterMode { Canvas, Gallery, Card }
    public enum ArtAssetCanvasScale { GameSize, FitCells }
    public enum ArtAssetBackground { Light, Dark, Checker }
    public enum ArtAssetCardDisplay { Color, Normal, Lit }

    // ПР-12Н: настройки просмотра Базы ассетов — отдельно от игровых данных,
    // в UserSettings проекта (не в git). Раскладка холста — только порядок
    // показа; игровые размеры и места она не меняет.
    [Serializable]
    public sealed class ArtAssetViewState
    {
        private static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../UserSettings/KingdomSurvival.ArtAssetDatabase.json"));

        public ArtAssetCenterMode Mode = ArtAssetCenterMode.Canvas;
        public ArtAssetCanvasScale Scale = ArtAssetCanvasScale.GameSize;
        public ArtAssetBackground Background = ArtAssetBackground.Dark;
        public ArtAssetView View = ArtAssetView.Front;
        public ArtAssetCardDisplay CardDisplay = ArtAssetCardDisplay.Color;
        public float Zoom = 60;
        public Vector2 Pan;
        public float CardSize = 130;
        public float GalleryScroll;
        public string SelectedId = "";
        public int Category = -1;
        public bool Favorites, IncompleteViews, MissingNormals, Used, Unused;
        public List<string> Order = new List<string>();
        public bool AllViews;
        public float LightIntensity = 1.4f, LightHeight = 1.2f;
        public bool LightNormals = true, LightNight;
        public Vector2 LightPosition = new Vector2(.38f, .58f);

        public static ArtAssetViewState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonUtility.FromJson<ArtAssetViewState>(File.ReadAllText(FilePath)) ?? new ArtAssetViewState();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("База ассетов: настройки просмотра не прочитаны: " + exception.Message);
            }
            return new ArtAssetViewState();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("База ассетов: настройки просмотра не сохранены: " + exception.Message);
            }
        }
    }
}
