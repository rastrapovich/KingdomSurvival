using System;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Картинки Unity для мастерской: портреты говорящих и иллюстрации сцен
    // уменьшаются и складываются в QuestWorkshop/unity-media/*.png. Мастерская
    // показывает их рядом с персонажами, репликами и сценами. Повторная
    // выгрузка пропускает картинки, у которых не изменился исходный ассет.
    public static class QuestWorkshopMedia
    {
        public const string FolderName = "unity-media";
        private const int PortraitSize = 384;
        private const int IllustrationSize = 960;

        // Возвращает карту «ключ → относительный путь» для снимка.
        public static Dictionary<string, string> Export(string workshopFolder, DialogueDatabaseAsset dialogues)
        {
            Dictionary<string, string> media = new Dictionary<string, string>(StringComparer.Ordinal);
            if (dialogues == null || string.IsNullOrEmpty(workshopFolder) || !Directory.Exists(workshopFolder))
                return media;

            string folder = Path.Combine(workshopFolder, FolderName);
            Directory.CreateDirectory(folder);
            string indexPath = Path.Combine(folder, "index.json");
            Dictionary<string, string> versions = LoadIndex(indexPath);
            Dictionary<string, string> nextVersions = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (DialogueSpeakerData speaker in dialogues.Speakers)
                if (speaker != null && speaker.Portrait != null && !string.IsNullOrEmpty(speaker.Id))
                    Put(media, versions, nextVersions, folder, "speaker:" + speaker.Id, "speaker_" + Safe(speaker.Id), speaker.Portrait, PortraitSize);

            foreach (DialogueDefinitionData dialogue in dialogues.Dialogues)
                if (dialogue != null && dialogue.SceneIllustration != null && !string.IsNullOrEmpty(dialogue.Id))
                    Put(media, versions, nextVersions, folder, "dialogue:" + dialogue.Id, "dialogue_" + Safe(dialogue.Id), dialogue.SceneIllustration, IllustrationSize);

            Dictionary<string, object> index = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in nextVersions)
                index[pair.Key] = pair.Value;
            File.WriteAllText(indexPath, QuestWorkshopJson.Serialize(index), new System.Text.UTF8Encoding(false));
            return media;
        }

        private static void Put(
            Dictionary<string, string> media,
            Dictionary<string, string> versions,
            Dictionary<string, string> nextVersions,
            string folder,
            string key,
            string fileName,
            Sprite sprite,
            int maxSize)
        {
            string relative = FolderName + "/" + fileName + ".png";
            string path = Path.Combine(folder, fileName + ".png");
            string assetPath = AssetDatabase.GetAssetPath(sprite);
            string version = AssetDatabase.GetAssetDependencyHash(assetPath).ToString() + "|" + sprite.name + "|" + sprite.rect + "|" + maxSize;
            if (versions.TryGetValue(key, out string old) && old == version && File.Exists(path))
            {
                media[key] = relative;
                nextVersions[key] = version;
                return;
            }

            try
            {
                byte[] png = RenderSprite(sprite, maxSize);
                if (png == null)
                    return;
                File.WriteAllBytes(path, png);
                media[key] = relative;
                nextVersions[key] = version;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Мастерская квестов: не удалось выгрузить картинку " + key + ": " + error.Message);
            }
        }

        // Рисует область спрайта через RenderTexture: работает и для
        // нечитаемых текстур, и для атласов.
        private static byte[] RenderSprite(Sprite sprite, int maxSize)
        {
            Texture2D texture = sprite.texture;
            if (texture == null)
                return null;
            Rect rect = sprite.textureRect;
            float scale = Mathf.Min(1f, maxSize / Mathf.Max(rect.width, rect.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));

            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Vector2 uvScale = new Vector2(rect.width / texture.width, rect.height / texture.height);
                Vector2 uvOffset = new Vector2(rect.x / texture.width, rect.y / texture.height);
                Graphics.Blit(texture, target, uvScale, uvOffset);
                RenderTexture.active = target;
                result = new Texture2D(width, height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                return result.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (result != null)
                    Object.DestroyImmediate(result);
            }
        }

        private static Dictionary<string, string> LoadIndex(string path)
        {
            Dictionary<string, string> versions = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path))
                return versions;
            try
            {
                if (QuestWorkshopJson.Parse(File.ReadAllText(path)) is Dictionary<string, object> map)
                    foreach (KeyValuePair<string, object> pair in map)
                        if (pair.Value is string version)
                            versions[pair.Key] = version;
            }
            catch (Exception)
            {
            }
            return versions;
        }

        private static string Safe(string id)
        {
            char[] chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-' && chars[i] != '.')
                    chars[i] = '_';
            return new string(chars);
        }

        // ── Портрет из мастерской в Unity ───────────────────────────────────

        public const string WorkshopPortraitFolder = "Assets/_Project/Art/characters/Workshop";

        // Сохраняет картинку из мастерской (data:image/…;base64) как спрайт и
        // возвращает его. Существующий файл с тем же именем перезаписывается.
        public static Sprite ImportPortrait(string speakerId, string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl))
                return null;
            int comma = dataUrl.IndexOf(',');
            if (!dataUrl.StartsWith("data:image/", StringComparison.Ordinal) || comma < 0)
                return null;
            string mime = dataUrl.Substring(5, dataUrl.IndexOf(';') - 5);
            string extension = mime == "image/jpeg" ? ".jpg" : mime == "image/webp" ? ".webp" : ".png";
            if (extension == ".webp")
                return null; // Unity не импортирует WebP
            byte[] bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));

            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, WorkshopPortraitFolder));
            string assetPath = WorkshopPortraitFolder + "/" + Safe(speakerId) + extension;
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath), bytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}
