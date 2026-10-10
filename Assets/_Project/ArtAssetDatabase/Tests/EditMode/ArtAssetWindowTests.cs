using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ArtAssets.Tests
{
    public sealed class ArtAssetWindowTests
    {
        [Test]
        public void WindowOpensFromMenu_WithRussianControls()
        {
            ArtAssetViewState.DisableSave = true;
            Assert.That(EditorApplication.ExecuteMenuItem("Kingdom Survival/База ассетов"), Is.True, "Окно в общем меню Kingdom Survival.");
            ArtAssetDatabaseWindow window = EditorWindow.GetWindow<ArtAssetDatabaseWindow>();
            window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<IMGUIContainer>(), Is.Not.Null, "Холст / галерея / карточка.");
            List<string> texts = window.rootVisualElement.Query<TextElement>().ToList().Select(item => item.text).ToList();
            foreach (string expected in new[] { "Загрузить папку…", "Загрузить файлы…", "+ Пустой ассет", "Холст", "Галерея", "Карточка", "Проверить", "Сохранить" })
                Assert.That(texts, Has.Member(expected));
            Assert.That(texts.Any(text => text.StartsWith("Все (")), Is.True, "Категории с числом записей.");
            window.ShowMode(ArtAssetCenterMode.Gallery);
            window.ShowMode(ArtAssetCenterMode.Card);
            window.ShowMode(ArtAssetCenterMode.Canvas);
            window.Close();
            ArtAssetViewState.DisableSave = false;
        }

        // Визуальная проверка без batchmode: снимки окон Базы ассетов и Базы
        // локаций с техническим местом (Logs/ArtAssets).
        public static void CaptureEditor()
        {
            ArtAssetDatabaseWindow window = ArtAssetDatabaseWindow.Open(ArtAssetTechnicalSet.HouseId);
            window.position = new Rect(60, 60, 1600, 920);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/ArtAssets"));
            Directory.CreateDirectory(folder);
            List<(double at, Action action)> steps = new List<(double, Action)>();
            double time = EditorApplication.timeSinceStartup + 8;
            void Step(Action action) { steps.Add((time, action)); time += 3; }
            void Capture(EditorWindow target, string name)
            {
                target.Repaint();
                int width = Mathf.RoundToInt(target.position.width), height = Mathf.RoundToInt(target.position.height);
                Color[] pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(target.position.position, width, height);
                Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.SetPixels(pixels); image.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            Step(() => window.ShowMode(ArtAssetCenterMode.Canvas));
            Step(() => Capture(window, "window-canvas"));
            Step(() => window.ShowMode(ArtAssetCenterMode.Gallery));
            Step(() => Capture(window, "window-gallery"));
            Step(() => window.ShowMode(ArtAssetCenterMode.Card));
            Step(() => Capture(window, "window-card"));
            Step(() => window.ShowMode(ArtAssetCenterMode.Card, ArtAssetCardDisplay.Lit));
            Step(() => Capture(window, "window-card-lit"));
            Step(() =>
            {
                window.Close();
                ArtAssetUsage house = ArtAssetUsages.Find(ArtAssetTechnicalSet.HouseId).FirstOrDefault();
                ArtAssetUsages.OpenLocation("technical_asset_demo", house?.ElementId);
            });
            Step(() =>
            {
                EditorWindow locations = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(item => item.titleContent.text == "База локаций");
                if (locations != null) { locations.position = new Rect(60, 60, 1600, 920); locations.Repaint(); }
            });
            Step(() =>
            {
                EditorWindow locations = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(item => item.titleContent.text == "База локаций");
                if (locations != null) { Capture(locations, "window-locations"); locations.Close(); }
                EditorApplication.Exit(0);
            });
            int index = 0;
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if (index >= steps.Count || EditorApplication.timeSinceStartup < steps[index].at) return;
                Action action = steps[index++].action;
                try { action(); }
                catch (Exception exception) { Debug.LogException(exception); }
            };
            EditorApplication.update += callback;
        }
    }
}
