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

    [Test]
    public void NothingUsable_GivesNoCandidate()
    {
        Assert.That(WorldMapSpriteImport.HasCandidate(null, new[] { "C:/nowhere/readme.txt" }), Is.False);
        Assert.That(WorldMapSpriteImport.IsImageFile("a/b/tree.PNG"), Is.True);
    }
}
