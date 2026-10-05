using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // ПР-12Н: технический демонстрационный набор — воспроизводимые подписанные
    // заглушки, не финальный арт и не канон. Простой предмет (ящик), дерево
    // с раздельной кроной и дом с раздельной крышей; шесть ракурсов, у каждого
    // известная тестовая нормаль. Ракурсы различимы: число белых точек в углу
    // = номер ракурса (Спереди — 1 … Спереди слева — 6), дверь дома и пятно
    // кроны смещаются. Картинки по правилам проекта не идут в git — их .meta
    // идут, поэтому повторный запуск оживляет ссылки на любом компьютере.
    public static class ArtAssetTechnicalSet
    {
        public const string Folder = ArtAssetImporter.ManagedRoot + "/_Technical";
        public const string CrateId = "tech_crate", TreeId = "tech_tree", HouseId = "tech_house";

        [MenuItem("Kingdom Survival/Служебное/База ассетов: технический набор")]
        public static void CreateFromMenu()
        {
            ArtAssetDatabaseAsset catalog = ArtAssetImporter.LoadOrCreateCatalog();
            Ensure(catalog);
            EditorUtility.DisplayDialog("База ассетов", "Технический набор готов: ящик, дерево с кроной, дом с крышей.", "Понятно");
        }

        public static void Ensure(ArtAssetDatabaseAsset catalog)
        {
            Undo.RecordObject(catalog, "Технический набор Базы ассетов");
            ArtAssetDefinition crate = Entry(catalog, CrateId, "Тех. ящик (заглушка)", ArtAssetCategory.Items, 284);
            crate.BlocksMovement = true;
            crate.Tags = new System.Collections.Generic.List<string> { "техническое", "заглушка" };
            foreach (ArtAssetView view in ArtAssetLabels.Views)
            {
                ArtAssetViewSettings settings = crate.Settings(view);
                settings.Pivot = new Vector2(.5f, .1f);
                settings.FootprintSize = new Vector2(.6f, .3f);
                Fill(crate.MainPart, view, "crate", 128, 128, (x, y) => Crate(x, y, view), (x, y) => BoxNormal(x, y, .14f, .86f, .1f, .8f));
            }

            ArtAssetDefinition tree = Entry(catalog, TreeId, "Тех. дерево с кроной (заглушка)", ArtAssetCategory.Trees, 112);
            tree.BlocksMovement = true;
            tree.Tags = new System.Collections.Generic.List<string> { "техническое", "заглушка", "крона" };
            tree.MainPart.Name = "Ствол";
            ArtAssetPart crown = Part(tree, "Крона");
            crown.OrderOffset = 1;
            foreach (ArtAssetView view in ArtAssetLabels.Views)
            {
                ArtAssetViewSettings settings = tree.Settings(view);
                settings.Pivot = new Vector2(.5f, .04f);
                settings.FootprintSize = new Vector2(.3f, .18f);
                Fill(tree.MainPart, view, "tree_trunk", 200, 360, (x, y) => Trunk(x, y, view), (x, y) => CylinderNormal(x, y, .44f, .56f));
                Fill(crown, view, "tree_crown", 200, 360, (x, y) => Crown(x, y, view), (x, y) => SphereNormal(x, y, CrownCenter(view), .42f));
            }

            ArtAssetDefinition house = Entry(catalog, HouseId, "Тех. дом с крышей (заглушка)", ArtAssetCategory.Buildings, 115);
            house.BlocksMovement = true;
            house.OccludesLight = true;
            house.Tags = new System.Collections.Generic.List<string> { "техническое", "заглушка", "крыша" };
            house.MainPart.Name = "Основа";
            ArtAssetPart roof = Part(house, "Крыша");
            roof.OrderOffset = 1;
            foreach (ArtAssetView view in ArtAssetLabels.Views)
            {
                ArtAssetViewSettings settings = house.Settings(view);
                settings.Pivot = new Vector2(.5f, .06f);
                settings.FootprintSize = new Vector2(2.4f, .9f);
                settings.FootprintOffset = new Vector2(0, .2f);
                Fill(house.MainPart, view, "house_base", 320, 300, (x, y) => HouseBase(x, y, view), (x, y) => BoxNormal(x, y, .1f, .9f, .06f, .58f));
                Fill(roof, view, "house_roof", 320, 300, (x, y) => Roof(x, y, view), (x, y) => RoofNormal(x, y));
            }
            AssetDatabase.SaveAssets();
            ArtAssetImporter.Changed(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private static ArtAssetDefinition Entry(ArtAssetDatabaseAsset catalog, string id, string name, ArtAssetCategory category, float pixelsPerUnit)
        {
            ArtAssetDefinition asset = catalog.assets.FirstOrDefault(item => item != null && item.Id == id);
            if (asset == null)
            {
                asset = new ArtAssetDefinition { Id = id, Name = name, Category = category, PixelsPerUnit = pixelsPerUnit, Credit = "Техническая заглушка Kingdom Survival" };
                catalog.assets.Add(asset);
                catalog.MarkChanged();
            }
            return asset;
        }

        private static ArtAssetPart Part(ArtAssetDefinition asset, string name)
        {
            ArtAssetPart part = asset.Parts.FirstOrDefault(item => item != null && item.Name == name);
            if (part == null)
            {
                part = new ArtAssetPart { Id = asset.Id + "_" + asset.Parts.Count, Name = name };
                asset.Parts.Add(part);
            }
            return part;
        }

        // Файлы ракурса: рисуются, только если их нет; назначение — если слот пуст.
        private static void Fill(ArtAssetPart part, ArtAssetView view, string name, int width, int height,
            Func<float, float, Color> color, Func<float, float, Color> normal)
        {
            string stem = Folder + "/" + name + "_" + ArtAssetLabels.ViewFolder(view);
            string colorPath = stem + ".png", normalPath = stem + "_normal.png";
            bool colorNew = Write(colorPath, width, height, color, false);
            bool normalNew = Write(normalPath, width, height, normal, true);
            if (colorNew) ArtAssetImporter.EnsureColorSettings((TextureImporter)AssetImporter.GetAtPath(colorPath));
            if (normalNew && !SpriteNormalMaps.IsNormalMapImport(AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath))) ArtAssetImporter.ConfigureNormal((TextureImporter)AssetImporter.GetAtPath(normalPath));
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(colorPath);
            Texture2D map = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            ArtAssetPartView slot = part.View(view);
            if (slot.Sprite == null) slot.Sprite = sprite;
            if (slot.NormalMap == null) slot.NormalMap = map;
            if (slot.Sprite != null && slot.NormalMap != null) SpriteNormalMaps.Assign(slot.Sprite, slot.NormalMap, out _);
        }

        private static bool Write(string path, int width, int height, Func<float, float, Color> pixel, bool linear)
        {
            if (File.Exists(path)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            Color[] colors = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    colors[y * width + x] = pixel((x + .5f) / width, (y + .5f) / height);
            texture.SetPixels(colors);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        // ------------------------------------------------------------------
        // Рисунки (x, y — доли, Y вверх)
        // ------------------------------------------------------------------

        // Подпись ракурса: столько точек, каков номер ракурса.
        private static bool Dots(float x, float y, ArtAssetView view)
        {
            int count = (int)view + 1;
            for (int i = 0; i < count; i++)
            {
                float cx = .08f + i * .07f, cy = .95f;
                if ((x - cx) * (x - cx) * 4 + (y - cy) * (y - cy) * 4 < .0012f) return true;
            }
            return false;
        }

        private static Color Crate(float x, float y, ArtAssetView view)
        {
            if (Dots(x, y, view)) return Color.white;
            if (x < .14f || x > .86f || y < .1f || y > .8f) return Color.clear;
            bool frame = x < .22f || x > .78f || y < .18f || y > .72f;
            bool back = view == ArtAssetView.Back || view == ArtAssetView.BackLeft || view == ArtAssetView.BackRight;
            Color wood = back ? new Color(.42f, .34f, .23f) : new Color(.56f, .45f, .30f);
            return frame ? wood * .7f + new Color(0, 0, 0, .3f) : wood;
        }

        private static Color Trunk(float x, float y, ArtAssetView view)
        {
            if (Dots(x, y, view)) return Color.white;
            float half = .06f + (1 - y) * .03f;
            return Mathf.Abs(x - .5f) < half && y > .02f && y < .62f ? new Color(.36f, .27f, .19f) : Color.clear;
        }

        private static Vector2 CrownCenter(ArtAssetView view)
        {
            float shift = view == ArtAssetView.FrontRight || view == ArtAssetView.BackRight ? .04f
                : view == ArtAssetView.FrontLeft || view == ArtAssetView.BackLeft ? -.04f : 0;
            return new Vector2(.5f + shift, .7f);
        }

        private static Color Crown(float x, float y, ArtAssetView view)
        {
            Vector2 center = CrownCenter(view);
            float dx = (x - center.x) / .42f, dy = (y - center.y) / .27f;
            return dx * dx + dy * dy < 1 ? new Color(.25f, .40f, .20f) : Color.clear;
        }

        private static Color HouseBase(float x, float y, ArtAssetView view)
        {
            if (Dots(x, y, view)) return Color.white;
            if (x < .1f || x > .9f || y < .06f || y > .58f) return Color.clear;
            float door = view == ArtAssetView.Front ? .5f : view == ArtAssetView.FrontRight ? .3f : view == ArtAssetView.FrontLeft ? .7f : -1;
            if (door > 0 && Mathf.Abs(x - door) < .06f && y < .36f) return new Color(.22f, .16f, .11f);
            bool log = Mathf.Repeat(y * 22, 1) < .12f;
            return log ? new Color(.40f, .31f, .21f) : new Color(.52f, .41f, .28f);
        }

        private static Color Roof(float x, float y, ArtAssetView view)
        {
            if (y < .54f || y > .95f) return Color.clear;
            float half = .46f * (1 - (y - .54f) / .41f);
            return Mathf.Abs(x - .5f) < half + .02f ? new Color(.44f, .40f, .33f) : Color.clear;
        }

        // ------------------------------------------------------------------
        // Известные тестовые нормали
        // ------------------------------------------------------------------

        private static Color Encode(Vector3 normal)
        {
            normal.Normalize();
            return new Color(normal.x * .5f + .5f, normal.y * .5f + .5f, normal.z * .5f + .5f, 1);
        }

        private static Color BoxNormal(float x, float y, float x0, float x1, float y0, float y1)
        {
            float bevel = .06f;
            Vector3 n = Vector3.forward;
            if (x < x0 + bevel) n += Vector3.left * .8f;
            else if (x > x1 - bevel) n += Vector3.right * .8f;
            if (y > y1 - bevel) n += Vector3.up * .8f;
            else if (y < y0 + bevel) n += Vector3.down * .8f;
            return Encode(n);
        }

        private static Color CylinderNormal(float x, float y, float x0, float x1)
        {
            float t = Mathf.Clamp((x - (x0 + x1) / 2) / ((x1 - x0) / 2 + .04f), -.95f, .95f);
            return Encode(new Vector3(t, 0, Mathf.Sqrt(1 - t * t)));
        }

        // Полусфера — та же тестовая нормаль, что в PlayMode-проверке света.
        private static Color SphereNormal(float x, float y, Vector2 center, float radius)
        {
            float nx = Mathf.Clamp((x - center.x) / radius, -1, 1), ny = Mathf.Clamp((y - center.y) / (radius * .65f), -1, 1);
            return Encode(new Vector3(nx, ny, Mathf.Sqrt(Mathf.Max(.05f, 1 - nx * nx - ny * ny))));
        }

        private static Color RoofNormal(float x, float y) => Encode(new Vector3(x < .5f ? -.6f : .6f, .5f, 1));
    }
}
