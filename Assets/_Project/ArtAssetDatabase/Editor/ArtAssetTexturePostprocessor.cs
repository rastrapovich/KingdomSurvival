using System;
using System.IO;
using UnityEditor;

namespace KingdomSurvival.ArtAssets.Editor
{
    // ПР-12Н: файлы управляемой папки Базы ассетов получают настройки сразу
    // при первом импорте — без второй записи .meta (на Windows только что
    // созданный .meta бывает кратко занят другим процессом). Рисунок — Sprite
    // (sRGB, прозрачность), нормаль (`*_normal*.png`) — Normal map (линейная).
    // Уже настроенные файлы не трогаются: повторная загрузка сохраняет .meta.
    public sealed class ArtAssetTexturePostprocessor : AssetPostprocessor
    {
        public static bool IsManaged(string path) =>
            !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith(ArtAssetImporter.ManagedRoot + "/", StringComparison.Ordinal);

        public static bool IsManagedNormal(string path) =>
            Path.GetFileNameWithoutExtension(path).IndexOf("_normal", StringComparison.OrdinalIgnoreCase) >= 0;

        private void OnPreprocessTexture()
        {
            if (!IsManaged(assetPath) || !(assetImporter is TextureImporter importer) || !importer.importSettingsMissing) return;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 8192;
            if (IsManagedNormal(assetPath))
            {
                importer.textureType = TextureImporterType.NormalMap;
                return;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
        }
    }
}
