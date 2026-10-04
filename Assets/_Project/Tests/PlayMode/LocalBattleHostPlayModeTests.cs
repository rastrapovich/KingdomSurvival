using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using KingdomSurvival.BattleSandbox;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// ПР-12К (канон v1.53 §28.3, §28.10): бой на месте идёт внутри основной
// сцены — без загрузки BattleSandbox, на поле места с его стенами. Союзное
// существо берёт шаблон из общего каталога, хотя его нет в отряде; неверный
// тип в данных места — ошибка, а не подмена.
public sealed class LocalBattleHostPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        PlayModeSaveIsolation.Begin();
    }

    [TearDown]
    public void TearDown()
    {
        CampaignSession.Reset();
        PlayModeSaveIsolation.End();
    }

    private static IEnumerator LoadMain()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        for (int i = 0; i < 20; i++)
            yield return null;
    }

    private static VisualElement NewContainer()
    {
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(behaviour => behaviour.GetType().Name == "PrototypeUIController");
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        VisualElement container = new VisualElement { name = "test-local-battle" };
        container.style.position = Position.Absolute;
        container.style.left = 0f;
        container.style.right = 0f;
        container.style.top = 0f;
        container.style.bottom = 0f;
        root.Add(container);
        return container;
    }

    private static CampaignBattleRequest MineRequest(string allyType)
    {
        CampaignBattleRequest request = new CampaignBattleRequest
        {
            BattleId = "test.local.battle",
            SourceKind = CampaignBattleSourceKind.Local,
            BattlefieldId = "old_mine_01",
            LocalLocationId = "local.old_mine",
            EncounterId = "mine.lair",
            AllowRetreat = true
        };
        request.Participants.Add(new CampaignBattleParticipant
        {
            PersonId = "hero", DisplayName = "Командир", UnitTypeId = "militia", IsHero = true,
            HasCell = true, CellQ = 1, CellR = 3
        });
        request.Participants.Add(new CampaignBattleParticipant
        {
            PersonId = "test.ally", DisplayName = "Союзный зверь", UnitTypeId = allyType,
            Origin = CampaignParticipantOrigin.CreatureAlly, HasCell = true, CellQ = 2, CellR = 3
        });
        request.Enemies.Add(new CampaignBattleEnemy
        {
            UnitTypeId = "forest_beast", InstanceId = "mine.beast.1", HasCell = true, CellQ = 8, CellR = 3, CurrentHitPoints = 4
        });
        return request;
    }

    [UnityTest]
    public IEnumerator LocalBattle_RunsInMainScene_WithCatalogAlly_AndReturnsResultToCaller()
    {
        yield return LoadMain();
        VisualElement container = NewContainer();
        CampaignBattleResult received = null;

        BattleSandboxController controller = BattleSandboxController.HostLocalBattle(
            container, MineRequest("forest_beast"), result => received = result, out string error);
        Assert.IsNotNull(controller, error);
        yield return null;

        Assert.AreEqual(MainScene, SceneManager.GetActiveScene().name, "Сцена не меняется.");
        SandboxBattle battle = (SandboxBattle)controller.GetType().GetField("battle", AnyInstance).GetValue(controller);
        SandboxUnitState ally = battle.GetUnit("player:test.ally");
        Assert.IsNotNull(ally, "Союзное существо в бою, хотя его нет в отряде.");
        Assert.AreEqual(SandboxTeam.Player, ally.Team, "Тег зверя не делает существо врагом.");
        Assert.AreEqual("forest_beast", ally.TypeId);
        Assert.AreEqual(new HexCoord(1, 3), battle.GetUnit("player:hero").Position, "Клетка из места, не край арены.");
        Assert.AreEqual(4, battle.GetUnit("enemy:mine.beast.1").HitPoints);
        Assert.IsFalse(battle.IsInside(new HexCoord(5, 2)), "Стена шахты — стена и в бою.");
        Assert.IsNotNull(container.Q("battle-sandbox-board"), "Бой показан в контейнере основной сцены.");

        controller.GetType().GetMethod("RetreatFromCampaignBattle", AnyInstance).Invoke(controller, null);
        yield return null;

        Assert.IsNotNull(received, "Итог вернулся вызывающему, без смены сцены.");
        Assert.AreEqual(CampaignBattleOutcome.Retreat, received.Outcome);
        Assert.AreEqual(CampaignBattleSourceKind.Local, received.SourceKind);
        Assert.IsTrue(received.Survivors.Any(survivor => survivor.PersonId == "test.ally"), "Итог по ID участника, не по типу.");
        Assert.AreEqual("mine.beast.1", received.Enemies.Single().InstanceId);
        Assert.IsNull(container.Q("battle-sandbox-board"), "Боевой экран закрыт.");
        Assert.AreEqual(MainScene, SceneManager.GetActiveScene().name);
    }

    [UnityTest]
    public IEnumerator LocalBattle_WithUnknownAllyType_DoesNotStart()
    {
        yield return LoadMain();
        VisualElement container = NewContainer();
        LogAssert.Expect(LogType.Error, new Regex("Бой на месте"));

        BattleSandboxController controller = BattleSandboxController.HostLocalBattle(
            container, MineRequest("no_such_creature"), _ => Assert.Fail("Итога быть не должно."), out string error);
        yield return null;

        Assert.IsNull(controller, "Неверный тип — ошибка данных, бой не начинается.");
        Assert.IsNotEmpty(error);
        Assert.IsNull(container.Q("battle-sandbox-board"));
    }
}
