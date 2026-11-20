using UnityEditor;

namespace KingdomSurvival.BattleSandbox.Editor
{
    // Пакет «Боевые состояния»: значки состояний (BattleSandbox/Resources/
    // StatusIcons, 128×128, прозрачность снаружи жетона) — экранный UI:
    // Sprite, sRGB, без mipmap, clamp, без сжатия с потерей альфы.
    public sealed class StatusIconImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/BattleSandbox/Resources/StatusIcons/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder))
                return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 256;
        }
    }
}
