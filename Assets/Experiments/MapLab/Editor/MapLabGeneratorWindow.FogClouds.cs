using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // «Облака тумана войны» в окне «Генератор карты»: набор PNG с прозрачностью
    // и их цветокоррекция из ассета облика карты (MapLabMapLook). Игра
    // подхватывает замену сразу (и в Play Mode); числа раскладки облаков —
    // ассет настроек движения, раздел «Облака тумана войны».
    public sealed partial class MapLabGeneratorWindow
    {
        private bool fogCloudsOpen = true;

        // Ассет облика карты в Resources; нет — создаётся со значениями по умолчанию.
        private static MapLabMapLook LookAsset(bool create)
        {
            MapLabMapLook asset = AssetDatabase.LoadAssetAtPath<MapLabMapLook>(MapLabMapLook.AssetPath);
            if (asset != null || !create)
                return asset;
            string folder = Path.GetDirectoryName(MapLabMapLook.AssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                Directory.CreateDirectory(folder);
            asset = CreateInstance<MapLabMapLook>();
            AssetDatabase.CreateAsset(asset, MapLabMapLook.AssetPath);
            AssetDatabase.SaveAssets();
            MapLabMapLook.ResetCache();
            return asset;
        }

        // Рисунок записи каталога — для миниатюр и свойств ассета.
        private static Texture2D Shown(MapLabArtEntry e) => e != null ? e.Texture : null;

        // Вкладка «Карта»: группа «Облака тумана войны».
        private void DrawFogCloudsGroup()
        {
            fogCloudsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(fogCloudsOpen, "Облака тумана войны · игра, сразу");
            if (fogCloudsOpen)
            {
                MapLabMapLook asset = LookAsset(false);
                if (asset == null)
                {
                    EditorGUILayout.HelpBox("Ассета облика карты нет — игра работает со значениями по умолчанию без облаков. Создайте ассет, чтобы задать облака.", MessageType.Info);
                    if (GUILayout.Button("Создать ассет облика карты"))
                        Guard(() => LookAsset(true));
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    return;
                }
                DrawFogCloudList(asset);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // Рисунки можно заменить по одному (перетащить PNG в поле), добавить,
        // убрать или собрать из папки Art/MapFogClouds/classic.
        private void DrawFogCloudList(MapLabMapLook look)
        {
            look.FogClouds ??= new List<Texture2D>();
            int remove = -1;
            for (int i = 0; i < look.FogClouds.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    Texture2D t = (Texture2D)EditorGUILayout.ObjectField("Облако " + (i + 1), look.FogClouds[i], typeof(Texture2D), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(look, "Облака тумана");
                        look.FogClouds[i] = t;
                        EditorUtility.SetDirty(look);
                    }
                    if (GUILayout.Button(new GUIContent("×", "Убрать это облако из списка (файл остаётся)."), GUILayout.Width(22)))
                        remove = i;
                }
            }
            if (remove >= 0)
            {
                Undo.RecordObject(look, "Облака тумана: убрать");
                look.FogClouds.RemoveAt(remove);
                EditorUtility.SetDirty(look);
            }
            // Цвет облаков: как у ассетов карты — яркость, тон, насыщенность
            // и уровни (PNG не меняется; игра пересчитывает копии).
            EditorGUI.BeginChangeCheck();
            float cloudBrightness = EditorGUILayout.Slider(new GUIContent("Яркость облаков", "1 — без изменений"), look.FogCloudBrightness, 0.3f, 2f);
            float cloudHue = EditorGUILayout.Slider(new GUIContent("Цветовой тон облаков, °", "0 — без изменений"), look.FogCloudHue, -180f, 180f);
            float cloudSaturation = EditorGUILayout.Slider(new GUIContent("Насыщенность облаков", "1 — без изменений"), look.FogCloudSaturation, 0f, 2f);
            MapLabLevels cloudLevels = MapLabLevelsGUI.Draw(look.FogCloudLevels, "Уровни облаков");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(look, "Облака тумана: цвет");
                look.FogCloudBrightness = cloudBrightness;
                look.FogCloudHue = cloudHue;
                look.FogCloudSaturation = cloudSaturation;
                (look.FogCloudLevels ??= new MapLabLevels()).CopyFrom(cloudLevels);
                EditorUtility.SetDirty(look);
            }
            if (!look.FogCloudAdjustNeutral && GUILayout.Button(new GUIContent("Сбросить цвет облаков", "Яркость, тон, насыщенность и уровни облаков — к нейтральным.")))
            {
                Undo.RecordObject(look, "Облака тумана: сброс цвета");
                look.FogCloudBrightness = look.FogCloudSaturation = 1f;
                look.FogCloudHue = 0f;
                (look.FogCloudLevels ??= new MapLabLevels()).Reset();
                EditorUtility.SetDirty(look);
            }
            if (look.FogCloudCount == 0)
                EditorGUILayout.HelpBox("Нет облаков: в игре останется только маска тумана.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("+ Облако", "Пустое поле — перетащите в него PNG облака.")))
                    Guard(() =>
                    {
                        Undo.RecordObject(look, "Облака тумана: добавить");
                        look.FogClouds.Add(null);
                        EditorUtility.SetDirty(look);
                    });
                if (GUILayout.Button(new GUIContent("Собрать из папки", "Все PNG из " + MapLabMapLook.FogCloudsFolder + " (импорт: прозрачность, мип-карты, до 2048 px, высокое качество).")))
                    Guard(() => status = CollectFogClouds(look));
                if (GUILayout.Button("Открыть папку"))
                    Guard(() =>
                    {
                        EnsureFolder(MapLabMapLook.FogCloudsFolder);
                        EditorUtility.RevealInFinder(MapLabMapLook.FogCloudsFolder);
                    });
                if (GUILayout.Button("Сохранить"))
                    Guard(() =>
                    {
                        EditorUtility.SetDirty(look);
                        AssetDatabase.SaveAssetIfDirty(look);
                        status = "Облик карты сохранён.";
                    });
            }
        }

        // PNG папки облаков → список облаков (по имени файла).
        private static string CollectFogClouds(MapLabMapLook look)
        {
            string folder = MapLabMapLook.FogCloudsFolder;
            EnsureFolder(folder);
            List<Texture2D> found = new List<Texture2D>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) ||
                    Path.GetDirectoryName(path)?.Replace('\\', '/') != folder)
                    continue;
                ConfigureFogCloudImporter(path);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                    found.Add(texture);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            Undo.RecordObject(look, "Облака тумана из папки");
            look.FogClouds = found;
            EditorUtility.SetDirty(look);
            AssetDatabase.SaveAssetIfDirty(look);
            return "Облака тумана: " + found.Count + " из " + folder + ".";
        }

        // Импорт облака: прозрачность, мип-карты (карта отдаляется), без
        // повторения на краях, полный размер до 2048 px, сжатие высокого качества.
        private static void ConfigureFogCloudImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return;
            bool changed = importer.textureType != TextureImporterType.Default ||
                           !importer.alphaIsTransparency || !importer.mipmapEnabled ||
                           importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 2048 ||
                           importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                           importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 4;
            if (!changed)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            // Чёткость: облако на экране бывает шире 1000 px — полный размер
            // рисунка (2048), сжатие высокого качества (BC7, без «грязи» на
            // туши), трилинейная выборка между мип-уровнями при отдалении.
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        // Пакетная сборка облаков из папки classic (Unity -batchmode -executeMethod).
        public static void CollectFogCloudsBatch()
        {
            MapLabMapLook asset = LookAsset(true);
            if (AssetDatabase.IsValidFolder(MapLabMapLook.FogCloudsFolder))
                Debug.Log("MAP_FOG_CLOUDS: " + CollectFogClouds(asset));
            AssetDatabase.SaveAssets();
            MapLabMapLook.ResetCache();
        }

        private static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
        }
    }
}
