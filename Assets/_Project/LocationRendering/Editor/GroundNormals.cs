using System.IO;
using KingdomSurvival.ArtAssets.Editor;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12М: нормали земли, снятые наклонённой камерой. Ровная земля в такой
    // карте «смотрит» вверх по экрану (средняя нормаль у ground1 — 0; 0,875;
    // 0,46: камера наклонена на 62°), а 2D-свет Unity стоит перед экраном —
    // земля светится только ниже огня. Поворот всех нормалей на угол наклона
    // (вокруг горизонтали экрана) ставит ровную землю лицом к камере, рельеф
    // остаётся. Пишется копия «…_ровно.png» рядом; исходник не меняется.
    public static class GroundNormals
    {
        // Наклон меньше — карта уже ровная.
        public const float LevelTolerance = 5;

        public static Vector3 MeanNormal(Color32[] pixels)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < pixels.Length; i += 7)
                sum += Decode(pixels[i]);
            return sum.sqrMagnitude > 0 ? sum.normalized : Vector3.forward;
        }

        // Наклон средней нормали вверх по экрану от взгляда камеры, градусы.
        public static float TiltDegrees(Vector3 mean) => Mathf.Atan2(mean.y, mean.z) * Mathf.Rad2Deg;

        public static void Level(Color32[] pixels, float tiltDegrees)
        {
            float angle = tiltDegrees * Mathf.Deg2Rad, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            for (int i = 0; i < pixels.Length; i++)
            {
                Vector3 n = Decode(pixels[i]);
                Vector3 level = new Vector3(n.x, n.y * cos - n.z * sin, n.y * sin + n.z * cos).normalized;
                pixels[i] = new Color32(Encode(level.x), Encode(level.y), Encode(level.z), pixels[i].a);
            }
        }

        // Копия карты с выровненной землёй рядом с исходником; null — не нужна или не вышло.
        public static Texture2D LevelCopy(Texture2D normal, out string message)
        {
            message = null;
            string path = AssetDatabase.GetAssetPath(normal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                message = "Карта нормалей не найдена в проекте.";
                return null;
            }
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path), false))
                {
                    message = "Не удалось прочитать " + path;
                    return null;
                }
                Color32[] pixels = image.GetPixels32();
                float tilt = TiltDegrees(MeanNormal(pixels));
                if (Mathf.Abs(tilt) < LevelTolerance)
                {
                    message = "Земля уже смотрит на камеру (наклон " + tilt.ToString("0") + "°) — выравнивать нечего.";
                    return null;
                }
                Level(pixels, tilt);
                image.SetPixels32(pixels);
                image.Apply(false);
                string target = AssetDatabase.GenerateUniqueAssetPath(
                    Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_ровно.png").Replace("\\", "/"));
                File.WriteAllBytes(target, image.EncodeToPNG());
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                Texture2D result = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
                if (result != null) SpriteNormalMaps.PrepareNormalTexture(result);
                message = "Нормали земли выровнены (наклон камеры " + tilt.ToString("0") + "°): " + target;
                return result;
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        private static Vector3 Decode(Color32 c) => new Vector3(c.r / 255f * 2 - 1, c.g / 255f * 2 - 1, c.b / 255f * 2 - 1);
        private static byte Encode(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt((value * .5f + .5f) * 255), 0, 255);
    }
}
