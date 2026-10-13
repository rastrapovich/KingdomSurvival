using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Поле боя, собранное в Базе локаций (место вида «Поле боя» с BattlefieldId
// поля полигона): бой рисует под доской живой кадр места, а фигуры доски —
// рендерер места (под его светом). Запись добавляется в базу только в памяти.
public sealed class BattlefieldScenePlayModeTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static object Member(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        if (field != null) return field.GetValue(target);
        return target.GetType().GetProperty(name, AnyInstance)?.GetValue(target);
    }

    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, AnyInstance).Invoke(target, args);

    [UnityTest]
    public IEnumerator SandboxOnAssembledField_RendersLiveScene_WithFigures()
    {
        CampaignSession.Reset();
        LocalLocationDatabaseAsset locations = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath);
        BattlefieldDatabaseAsset fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
        BattlefieldDefinitionData field = fields.GetSandboxBattlefield();
        Assert.IsNotNull(field);
        // Своей сборки у поля полигона в базе может уже быть — тогда она и проверяется.
        bool added = !locations.locations.Any(item => item.IsBattlefield && item.BattlefieldId == field.Id);
        LocalLocationDefinition scene = new LocalLocationDefinition
        {
            Id = "zz_field_scene", DisplayName = "Поле · тест", Kind = LocalLocationKind.Battlefield, BattlefieldId = field.Id
        };
        LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = scene.Id, UseWorldLighting = false };
        if (added)
        {
            locations.locations.Add(scene);
            locations.visuals.Add(visual);
        }
        try
        {
            SceneManager.LoadScene("BattleSandbox");
            for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != "BattleSandbox"; i++)
                yield return null;
            MonoBehaviour sandbox = null;
            for (int i = 0; i < 120 && (sandbox == null || !(bool)Member(sandbox, "initialized")); i++)
            {
                sandbox = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(item => item.GetType().Name == "BattleSandboxController");
                yield return null;
            }
            Assert.IsNotNull(sandbox);
            Invoke(sandbox, "ChangeCount", false, "guard", 1);
            Invoke(sandbox, "ChangeCount", true, "wolf", 1);
            Invoke(sandbox, "StartBattle");
            for (int i = 0; i < 20; i++)
                yield return null;

            BattlefieldView surface = (BattlefieldView)Member(sandbox, "BattlefieldSurface");
            Assert.IsNotNull(surface);
            Assert.IsTrue(surface.HasSceneTexture, "Под доской — живой кадр места, а не картинка поля.");
            object board = Member(sandbox, "Board");
            Assert.IsTrue((bool)Member(board, "ExternalFigures"), "Фигуры рисует рендерер места.");
            GameObject figures = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Select(item => item.gameObject).FirstOrDefault(item => item.name == "Фигуры боя");
            Assert.IsNotNull(figures, "У рендерера места есть слой фигур боя.");
            Assert.GreaterOrEqual(figures.transform.childCount, 2, "Боец и противник — фигурами места.");
            // Сетка — в мире места, под фигурами, а не элементом поля поверх кадра.
            Assert.IsTrue(surface.GridDrawnBelow, "Поле не рисует сетку поверх кадра.");
            GameObject grid = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Select(item => item.gameObject).FirstOrDefault(item => item.name == "Сетка боя");
            Assert.IsNotNull(grid, "Сетка боя — в мире места.");
            Assert.IsTrue(grid.activeInHierarchy, "Сетка боя видна.");
            Assert.Less(grid.GetComponent<MeshRenderer>().sortingOrder, figures.GetComponentsInChildren<SpriteRenderer>().Where(item => item.name == "Фигура").Min(item => item.sortingOrder),
                "Сетка — под фигурами.");

            // Снимок для просмотра.
            RenderTexture texture = (RenderTexture)surface.Q<Image>("battlefield-background").image;
            Assert.IsNotNull(texture);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D shot = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/LocationLighting"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "battlefield-scene.png"), shot.EncodeToPNG());
            Object.Destroy(shot);
        }
        finally
        {
            if (added)
            {
                locations.locations.Remove(scene);
                locations.visuals.Remove(visual);
            }
            CampaignSession.Reset();
        }
    }
}
