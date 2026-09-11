using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UILayout;
using NUnit.Framework;
using UnityEngine;

// UI-M08: world-map-location-inspection-card (карточка осмотра локации по
// ПКМ на маркере) стала постоянным статичным узлом внутри world-map в
// Prototype_Main.uxml — раньше её по частям строили два разных файла
// (WorldMapInteractionPolish.cs — заголовок/детали/кнопка закрытия;
// WorldMapLocationActions.cs — метка присутствия армии/кнопка
// "ИССЛЕДОВАТЬ", вставлявшиеся между ними через RemoveFromHierarchy+Add).
public sealed class WorldMapLocationCardLayoutTests
{
    private const string MapOverlaysScreenId = "map-overlays";

    private static UILayoutDatabaseAsset LoadDatabase()
    {
        UILayoutDatabaseAsset database = Resources.Load<UILayoutDatabaseAsset>(UILayoutDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база UILayout Resources/" + UILayoutDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    private static string ReadPrototypeMainUxml()
    {
        string path = Path.Combine(
            Application.dataPath, "_Project", "UI", "Prototype", "Prototype_Main.uxml");
        return File.ReadAllText(path);
    }

    [Test]
    public void World_Map_Location_Inspection_Card_Is_A_Static_Node_In_Uxml()
    {
        string uxml = ReadPrototypeMainUxml();

        StringAssert.Contains("name=\"world-map-location-inspection-card\"", uxml);
        StringAssert.Contains("name=\"world-map-location-inspection-title\"", uxml);
        StringAssert.Contains("name=\"world-map-location-inspection-details\"", uxml);
        StringAssert.Contains("name=\"world-map-location-inspection-presence\"", uxml);
        StringAssert.Contains("name=\"world-map-location-inspection-research-button\"", uxml);
        StringAssert.Contains("name=\"world-map-location-inspection-close-button\"", uxml);
    }

    [Test]
    public void Map_Overlays_Database_Passes_Full_Validation()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // Раньше здесь была осиротевшая запись quick-location-static-image-label
    // (parentId: world-map-location-inspection-card) — карточка никогда не
    // содержала элемента-картинки, только заголовок/детали/кнопку, запись
    // никогда ни на что не резолвилась и удалена.
    [Test]
    public void Map_Overlays_Screen_Has_No_Orphaned_Image_Label_Entry()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(MapOverlaysScreenId);
        Assert.IsNotNull(screen);

        Assert.IsNull(screen.FindElement("quick-location-static-image-label"));
    }

    // map-overlays — экран с пустым rootName (scope = весь interfaceRoot,
    // UILayoutScreenBinder.ResolveScope), поэтому targetName ищется по всему
    // дереву документа, а не только внутри Prototype_Main.uxml напрямую —
    // но узел всё равно обязан существовать хоть где-то в разметке.
    [Test]
    public void World_Map_Location_Inspection_Card_TargetName_Exists_In_Uxml()
    {
        UILayoutDatabaseAsset database = LoadDatabase();
        UILayoutScreenDefinition screen = database.FindScreen(MapOverlaysScreenId);
        Assert.IsNotNull(screen);

        UILayoutElementDefinition element = screen.FindElement("world-map-location-inspection-card");
        Assert.IsNotNull(element);

        string uxml = ReadPrototypeMainUxml();
        StringAssert.Contains("name=\"" + element.TargetName + "\"", uxml);
    }
}
