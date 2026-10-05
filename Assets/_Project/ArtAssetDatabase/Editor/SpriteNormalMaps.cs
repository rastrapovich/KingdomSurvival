using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // ПР-12М/12Н: карта нормалей подключается к спрайту как вторая текстура
    // `_NormalMap` в настройках импорта — так её находит 2D-свет Unity, и для
    // отдельного рисунка, и для страницы атласа. Вторая текстура принадлежит
    // всей исходной текстуре: все спрайты одного листа получают одну карту.
    public static class SpriteNormalMaps
    {
        public const string SecondaryName = "_NormalMap";

        public static bool Assign(Sprite sprite, Texture2D normal, out string message)
        {
            message = string.Empty;
            if (sprite == null)
            {
                message = "Нормали подключаются к рисунку-спрайту; у заглушки их нет.";
                return false;
            }
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                message = "У рисунка нет настроек импорта: " + path;
                return false;
            }
            if (normal != null)
                PrepareNormalTexture(normal);
            Texture2D current = Find(importer);
            if (current == normal) return true;
            List<SecondarySpriteTexture> textures = (importer.secondarySpriteTextures ?? Array.Empty<SecondarySpriteTexture>())
                .Where(item => item.name != SecondaryName).ToList();
            if (normal != null)
                textures.Add(new SecondarySpriteTexture { name = SecondaryName, texture = normal });
            importer.secondarySpriteTextures = textures.ToArray();
            string guid = normal != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(normal)) : null;
            if (!SaveVerified(importer, meta => guid != null ? meta.Contains(guid) : !meta.Contains("name: " + SecondaryName)))
            {
                message = "Не удалось записать настройки импорта " + path + " (файл .meta занят) — повторите.";
                return false;
            }
            return true;
        }

        // Карта нормалей импортируется как «Normal map» (линейная, без sRGB).
        public static void PrepareNormalTexture(Texture2D normal)
        {
            string path = AssetDatabase.GetAssetPath(normal);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.textureType == TextureImporterType.NormalMap)
                return;
            importer.textureType = TextureImporterType.NormalMap;
            SaveVerified(importer, meta => MetaValue(meta, "textureType") == ((int)TextureImporterType.NormalMap).ToString());
        }

        // Значение «ключ: значение» из .meta (первое вхождение).
        public static string MetaValue(string meta, string key)
        {
            using (StringReader reader = new StringReader(meta ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith(key + ":", StringComparison.Ordinal))
                        return trimmed.Substring(key.Length + 1).Trim();
                }
            }
            return null;
        }

        // Запись настроек импорта с проверкой по самому .meta: на Windows только
        // что созданный .meta бывает кратко занят другим процессом, и запись
        // молча не проходит. Несколько попыток с паузой.
        public static bool SaveVerified(TextureImporter importer, Func<string, bool> metaOk)
        {
            string meta = importer.assetPath + ".meta";
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (attempt > 0)
                {
                    System.Threading.Thread.Sleep(120 * attempt);
                    EditorUtility.SetDirty(importer);
                }
                importer.SaveAndReimport();
                try
                {
                    if (File.Exists(meta) && metaOk(File.ReadAllText(meta))) return true;
                }
                catch (IOException)
                {
                    // Файл ещё занят — следующая попытка.
                }
            }
            Debug.LogWarning("База ассетов: настройки импорта не записаны в " + meta);
            return false;
        }

        public static Texture2D Find(Sprite sprite)
        {
            if (sprite == null) return null;
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            return AssetImporter.GetAtPath(path) is TextureImporter importer ? Find(importer) : null;
        }

        public static Texture2D Find(TextureImporter importer)
        {
            foreach (SecondarySpriteTexture item in importer.secondarySpriteTextures ?? Array.Empty<SecondarySpriteTexture>())
                if (item.name == SecondaryName) return item.texture;
            return null;
        }

        public static bool IsNormalMapImport(Texture2D texture) =>
            texture != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer &&
            importer.textureType == TextureImporterType.NormalMap;

        // Рисунок — один из нескольких спрайтов листа (атлас).
        public static bool IsSheet(Sprite sprite) =>
            sprite != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite.texture)) is TextureImporter importer &&
            importer.spriteImportMode == SpriteImportMode.Multiple;

        // Размер исходного файла (до сжатия и ограничения размера).
        public static Vector2Int SourceSize(Texture texture)
        {
            if (texture == null) return Vector2Int.zero;
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer)
            {
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                return new Vector2Int(width, height);
            }
            return new Vector2Int(texture.width, texture.height);
        }
    }
}
