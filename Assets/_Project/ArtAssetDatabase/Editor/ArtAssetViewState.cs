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

        // Тесты не трогают настройки просмотра пользователя.
        public static bool DisableSave;

        public static ArtAssetViewState Load()
        {
            ArtAssetViewState state = null;
            try
            {
                if (!DisableSave && File.Exists(FilePath))
                    state = JsonUtility.FromJson<ArtAssetViewState>(File.ReadAllText(FilePath));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("База ассетов: настройки просмотра не прочитаны: " + exception.Message);
            }
            state ??= new ArtAssetViewState();
            state.Sanitize();
            return state;
        }

        // Повреждённые числа (NaN, бесконечность, ноль) не должны ломать холст.
        public void Sanitize()
        {
            static bool Bad(float value) => float.IsNaN(value) || float.IsInfinity(value);
            if (Bad(Zoom) || Zoom < 4 || Zoom > 400) Zoom = 60;
            if (Bad(Pan.x) || Bad(Pan.y)) Pan = Vector2.zero;
            if (Bad(CardSize) || CardSize < 80 || CardSize > 260) CardSize = 130;
            if (Bad(GalleryScroll) || GalleryScroll < 0) GalleryScroll = 0;
            if (Bad(LightIntensity)) LightIntensity = 1.4f;
            if (Bad(LightHeight) || LightHeight <= 0) LightHeight = 1.2f;
            if (Bad(LightPosition.x) || Bad(LightPosition.y)) LightPosition = new Vector2(.38f, .58f);
            Order ??= new List<string>();
            SelectedId ??= "";
        }

        public void Save()
        {
            if (DisableSave) return;
            Sanitize();
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
