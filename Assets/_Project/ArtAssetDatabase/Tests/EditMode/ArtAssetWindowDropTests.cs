using System.Collections;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace KingdomSurvival.ArtAssets.Tests
{
    // ПР-12Н: PNG из «Проводника», брошенный в окно, создаёт ассет. Событие
    // перетаскивания идёт тем же путём UI Toolkit, что и настоящее: окно
    // получает DragPerformEvent с путями файлов.
    public sealed class ArtAssetWindowDropTests
    {
        private string source;
        private ArtAssetDatabaseAsset catalog;

        [SetUp]
        public void SetUp()
        {
            source = Path.Combine(Path.GetTempPath(), "ks_art_drop_" + System.Guid.NewGuid().ToString("N").Substring(0, 8)).Replace('\\', '/');
            Directory.CreateDirectory(source);
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            ArtAssetDatabaseAsset.Override = catalog;
            ArtAssetImportSummaryWindow.AutoConfirm = true;
            ArtAssetViewState.DisableSave = true;
        }

        [TearDown]
        public void TearDown()
        {
            ArtAssetImportSummaryWindow.AutoConfirm = false;
            ArtAssetViewState.DisableSave = false;
            ArtAssetDatabaseAsset.Override = null;
            foreach (ArtAssetDefinition asset in catalog.assets)
                if (AssetDatabase.IsValidFolder(ArtAssetImporter.AssetFolder(asset))) AssetDatabase.DeleteAsset(ArtAssetImporter.AssetFolder(asset));
            Object.DestroyImmediate(catalog);
            DragAndDrop.PrepareStartDrag();
            if (Directory.Exists(source)) Directory.Delete(source, true);
        }

        private string Png(string name, Color color)
        {
            string path = Path.Combine(source, name).Replace('\\', '/');
            Texture2D texture = new Texture2D(32, 48, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(color, 32 * 48).ToArray());
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return path;
        }

        private static void Drop(VisualElement target, params string[] paths)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.paths = paths;
            DragAndDrop.objectReferences = new Object[0];
            Vector2 point = target.worldBound.center;
            using (DragUpdatedEvent updated = DragUpdatedEvent.GetPooled(new Event { type = EventType.DragUpdated, mousePosition = point }))
            {
                updated.target = target;
                target.SendEvent(updated);
            }
            using (DragPerformEvent perform = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = point }))
            {
                perform.target = target;
                target.SendEvent(perform);
            }
        }

        [UnityTest]
        public IEnumerator DroppingLoosePngIntoTheWindow_CreatesAnAssetWithItsNormal()
        {
            string color = Png("сарай.png", new Color(.6f, .4f, .2f, 1));
            string normal = Png("сарай_normal.png", new Color(.5f, .5f, 1, 1));
            ArtAssetDatabaseWindow window = EditorWindow.GetWindow<ArtAssetDatabaseWindow>();
            window.position = new Rect(50, 50, 1300, 800);
            window.CreateGUI();
            window.ShowMode(ArtAssetCenterMode.Canvas);
            yield return null;
            IMGUIContainer center = window.rootVisualElement.Q<IMGUIContainer>();
            Drop(center, color, normal);
            // Импорт идёт после завершения перетаскивания (delayCall).
            for (double until = EditorApplication.timeSinceStartup + 10; catalog.assets.Count == 0 && EditorApplication.timeSinceStartup < until;) yield return null;

            Assert.That(catalog.assets.Count, Is.EqualTo(1), "PNG без ракурса в имени создаёт ассет.");
            ArtAssetDefinition shed = catalog.assets[0];
            Assert.That(shed.Name, Is.EqualTo("сарай"));
            Assert.That(shed.HasView(ArtAssetView.Front), Is.True);
            Assert.That(shed.HasNormal(ArtAssetView.Front), Is.True, "Пара *_normal стала нормалью ракурса.");
            Assert.That(ArtAssetPicker.DraggedAssetId(), Is.Null);

            // В открытой карточке файл с ракурсом в имени добавляется в этот же ассет.
            window.SelectAsset(shed.Id, true);
            yield return null;
            string back = Png("barn_Back.png", Color.gray);
            Drop(center, back);
            for (double until = EditorApplication.timeSinceStartup + 10; !catalog.assets[0].HasView(ArtAssetView.Back) && EditorApplication.timeSinceStartup < until;) yield return null;
            Assert.That(catalog.assets.Count, Is.EqualTo(1), "Брошенное в карточку не создаёт новый ассет.");
            Assert.That(catalog.assets[0].Id, Is.EqualTo(shed.Id));
            Assert.That(catalog.assets[0].HasView(ArtAssetView.Back), Is.True, "Ракурс «Сзади» добавлен в открытый ассет.");
            Assert.That(catalog.assets[0].HasView(ArtAssetView.Front), Is.True, "«Спереди» не тронут.");
            window.Close();
        }
    }
}
