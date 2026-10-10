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
        // Кисть основания в карточке: мазок закрашивает землю, «По силуэту
        // рисунка» — непрозрачную часть рисунка основы, «Стереть кисть» —
        // снова прямоугольник.
        [Test]
        public void FootprintBrush_PaintsMask_AndFillsBySilhouette()
        {
            ArtAssetViewState.DisableSave = true;
            ArtAssetDatabaseAsset catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            // Рисунок: нижняя половина непрозрачна, верхняя — пусто.
            Texture2D texture = new Texture2D(40, 40, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[40 * 40];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = i / 40 < 20 ? Color.gray : Color.clear;
            texture.SetPixels(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 40, 40), new Vector2(.5f, 0), 100);
            ArtAssetDefinition asset = new ArtAssetDefinition { Id = "zz_brush", Name = "Камень", PixelsPerUnit = 40 };
            asset.MainPart.View(ArtAssetView.Front).Sprite = sprite;
            asset.Settings(ArtAssetView.Front).Pivot = new Vector2(.5f, 0);
            catalog.assets.Add(asset);
            ArtAssetDatabaseAsset.Override = catalog;
            ArtAssetDatabaseWindow window = EditorWindow.GetWindow<ArtAssetDatabaseWindow>();
            try
            {
                window.CreateGUI();
                window.SelectAsset(asset.Id, true);
                ArtAssetFootprintMask mask = asset.Settings(ArtAssetView.Front).FootprintMask;
                System.Reflection.MethodInfo paint = typeof(ArtAssetDatabaseWindow).GetMethod("PaintFootprint",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                paint.Invoke(window, new object[] { asset, new Vector2(0, .2f) });
                Assert.That(mask.Contains(new Vector2(0, .2f)), Is.True, "Мазок закрасил землю под кистью.");
                Assert.That(mask.Contains(new Vector2(0, .9f)), Is.False);
                Assert.That(asset.Settings(ArtAssetView.Front).UsesFootprintMask, Is.True);

                window.SelectAsset(asset.Id, true);
                void Click(string text)
                {
                    Button button = window.rootVisualElement.Query<Button>().ToList().First(item => item.text == text);
                    using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                    {
                        submit.target = button;
                        button.SendEvent(submit);
                    }
                }
                Click("По силуэту рисунка");
                Assert.That(mask.Contains(new Vector2(0, .25f)) && mask.Contains(new Vector2(-.4f, .1f)), Is.True, "Непрозрачная нижняя половина закрашена.");
                Assert.That(mask.Contains(new Vector2(0, .75f)), Is.False, "Прозрачный верх — нет.");
                Assert.That(mask.Contains(new Vector2(.7f, .1f)), Is.False, "За краем рисунка — нет.");
                Click("Стереть кисть — снова прямоугольник");
                Assert.That(asset.Settings(ArtAssetView.Front).UsesFootprintMask, Is.False);
            }
            finally
            {
                window.Close();
                ArtAssetDatabaseAsset.Override = null;
                ArtAssetViewState.DisableSave = false;
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

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
