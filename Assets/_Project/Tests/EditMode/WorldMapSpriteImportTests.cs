using System.Collections.Generic;
using System.IO;
using KingdomSurvival.WorldMapVisual.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Рисунки для пулов генератора карты: файл с диска, папка проекта и
// картинка, импортированная не как спрайт, — всё превращается в спрайты.
public class WorldMapSpriteImportTests
{
    private const string ProjectFolder = "Assets/__KsSpriteImportTest";
    private const string Subfolder = "__KsSpriteImportTest";
    private string diskFolder;
    private bool diskRootExisted;
    private System.Func<string, bool> originalConfirm;

    [SetUp]
    public void SetUp()
    {
        originalConfirm = WorldMapSpriteImport.ConfirmConversion;
        WorldMapSpriteImport.ConfirmConversion = _ => true;
        diskRootExisted = AssetDatabase.IsValidFolder(WorldMapSpriteImport.DiskFolder);
        diskFolder = Path.Combine(Path.GetTempPath(), "KsSpriteImportTest");
        Directory.CreateDirectory(diskFolder);
    }

    [TearDown]
    public void TearDown()
    {
        WorldMapSpriteImport.ConfirmConversion = originalConfirm;
        AssetDatabase.DeleteAsset(ProjectFolder);
        AssetDatabase.DeleteAsset(WorldMapSpriteImport.DiskFolder + "/" + Subfolder);
        if (!diskRootExisted)
            AssetDatabase.DeleteAsset(WorldMapSpriteImport.DiskFolder);
        if (Directory.Exists(diskFolder))
            Directory.Delete(diskFolder, true);
    }

    private static void WritePng(string path)
    {
        Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        byte[] bytes = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        File.WriteAllBytes(path, bytes);
    }

    [Test]
    public void FilesFromDisk_AreCopiedIntoTheProject_AsSprites()
    {
        string a = Path.Combine(diskFolder, "reed-a.png");
        string b = Path.Combine(diskFolder, "reed-b.png");
        WritePng(a);
        WritePng(b);

        List<Sprite> sprites = WorldMapSpriteImport.Collect(null, new[] { diskFolder }, Subfolder, false);

        Assert.That(sprites.Count, Is.EqualTo(2), "Папка с диска отдаёт обе картинки.");
        foreach (Sprite sprite in sprites)
        {
            string path = AssetDatabase.GetAssetPath(sprite);
            Assert.That(path, Does.StartWith(WorldMapSpriteImport.DiskFolder + "/" + Subfolder));
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
        }
    }

    [Test]
    public void ProjectFolder_WithPlainTextures_GivesSprites()
    {
        AssetDatabase.CreateFolder("Assets", "__KsSpriteImportTest");
        WritePng(ProjectFolder + "/hill-1.png");
        WritePng(ProjectFolder + "/hill-2.png");
        AssetDatabase.Refresh();
        foreach (string path in new[] { ProjectFolder + "/hill-1.png", ProjectFolder + "/hill-2.png" })
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.SaveAndReimport();
        }

        Object folder = AssetDatabase.LoadAssetAtPath<Object>(ProjectFolder);
        List<Sprite> sprites = WorldMapSpriteImport.Collect(new[] { folder }, null, Subfolder, false);

        Assert.That(sprites.Count, Is.EqualTo(2), "Папка из Project с обычными текстурами отдаёт спрайты после перевода.");
        TextureImporter converted = (TextureImporter)AssetImporter.GetAtPath(ProjectFolder + "/hill-1.png");
        Assert.That(converted.textureType, Is.EqualTo(TextureImporterType.Sprite));
        TextureImporterSettings settings = new TextureImporterSettings();
        converted.ReadTextureSettings(settings);
        Assert.That(settings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.BottomCenter), "Знак стоит основанием на точке карты.");
    }

    [Test]
    public void TextureFromDisk_GetsRepeatForSeamlessTiling()
    {
        string file = Path.Combine(diskFolder, "paper.png");
        WritePng(file);

        Texture2D texture = WorldMapSpriteImport.CollectTexture(null, new[] { file }, Subfolder);

        Assert.That(texture, Is.Not.Null);
        Assert.That(((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture))).wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
    }

    // Картинка, которую Unity нарезала на большой кусок и обрывок 3×3.
    private static string MakeAccidentallySlicedPicture()
    {
        AssetDatabase.CreateFolder("Assets", "__KsSpriteImportTest");
        string path = ProjectFolder + "/mountain.png";
        Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[64 * 64];
        for (int y = 0; y < 40; y++)
        {
            for (int x = 0; x < 40; x++)
                pixels[y * 64 + x] = new Color32(120, 110, 90, 255);
        }
        for (int y = 60; y < 63; y++)
        {
            for (int x = 60; x < 63; x++)
                pixels[y * 64 + x] = new Color32(60, 40, 20, 255);
        }
        texture.SetPixels32(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        // Файл импорта — как его пишет Unity при автоматической нарезке.
        File.WriteAllText(path + ".meta",
            "fileFormatVersion: 2\nguid: " + System.Guid.NewGuid().ToString("N") + "\nTextureImporter:\n" +
            "  serializedVersion: 13\n  spriteMode: 2\n  textureType: 8\n  alphaIsTransparency: 1\n" +
            "  spriteSheet:\n    serializedVersion: 2\n    sprites:\n" +
            Piece("mountain_0", 0, 0, 40, 40, "11111111111111110800000000000000", 1001) +
            Piece("mountain_1", 60, 60, 3, 3, "22222222222222220800000000000000", 1002) +
            "    nameFileIdTable:\n      mountain_0: 1001\n      mountain_1: 1002\n");
        AssetDatabase.Refresh();
        return path;
    }

    private static string Piece(string name, int x, int y, int w, int h, string spriteId, long internalId) =>
        "    - serializedVersion: 2\n      name: " + name + "\n      rect:\n        serializedVersion: 2\n" +
        "        x: " + x + "\n        y: " + y + "\n        width: " + w + "\n        height: " + h + "\n" +
        "      alignment: 0\n      pivot: {x: 0, y: 0}\n      spriteID: " + spriteId + "\n      internalID: " + internalId + "\n";

    [Test]
    public void AccidentallySlicedPicture_IsRepairedIntoOneSprite()
    {
        string path = MakeAccidentallySlicedPicture();
        List<Sprite> pool = new List<Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Sprite sprite)
                pool.Add(sprite);
        }
        Assert.That(pool.Count, Is.EqualTo(2), "Подготовка: кусок и обрывок.");
        Assert.That(WorldMapSpriteImport.IsAccidentallySliced(path), Is.True);
        Assert.That(WorldMapSpriteImport.CountBrokenPictures(new[] { pool }), Is.EqualTo(1));

        Assert.That(WorldMapSpriteImport.RepairPool(pool, false), Is.True);

        Assert.That(pool.Count, Is.EqualTo(1), "Одна картинка — один рисунок в пуле.");
        Assert.That(pool[0].rect.width, Is.EqualTo(64f), "Спрайт — вся картинка, а не кусок.");
        Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
        Assert.That(WorldMapSpriteImport.RepairPool(pool, false), Is.False, "Повторно чинить нечего.");
    }

    [Test]
    public void AddingASlicedPicture_GivesOneSprite_NotItsDebris()
    {
        string path = MakeAccidentallySlicedPicture();
        Object folder = AssetDatabase.LoadAssetAtPath<Object>(ProjectFolder);
        List<Sprite> sprites = WorldMapSpriteImport.Collect(new[] { folder }, null, Subfolder, false);
        Assert.That(sprites.Count, Is.EqualTo(1));
        Assert.That(AssetDatabase.GetAssetPath(sprites[0]), Is.EqualTo(path));
        Assert.That(sprites[0].rect.width, Is.EqualTo(64f));
    }

    [Test]
    public void NothingUsable_GivesNoCandidate()
    {
        Assert.That(WorldMapSpriteImport.HasCandidate(null, new[] { "C:/nowhere/readme.txt" }), Is.False);
        Assert.That(WorldMapSpriteImport.IsImageFile("a/b/tree.PNG"), Is.True);
    }
}
