using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.BattleSandbox;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// ПР-16: способности существ в настоящем бою полигона — панель способностей
// игрока (выбор цели и применение) и ИИ существ через общий исполнитель.
// Любая ошибка консоли проваливает тест.
public sealed class CreatureAbilitiesPlayModeTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [SetUp]
    public void SetUp() => CampaignSession.Reset();

    [TearDown]
    public void TearDown() => CampaignSession.Reset();

    private static object Get(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        if (field != null)
            return field.GetValue(target);
        return target.GetType().GetProperty(name, AnyInstance).GetValue(target);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        return target.GetType().GetMethod(method, AnyInstance).Invoke(target, args);
    }

    // Кадр панели UI (поле, метки, панель способностей) через RenderTexture:
    // в пакетном режиме WaitForEndOfFrame не наступает.
    private static IEnumerator Shot(MonoBehaviour host, string name)
    {
        string dir = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir))
            yield break;
        UnityEngine.UIElements.PanelSettings settings = host.GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings;
        RenderTexture previous = settings.targetTexture;
        RenderTexture texture = new RenderTexture(1920, 1080, 24);
        settings.targetTexture = texture;
        for (int i = 0; i < 4; i++)
            yield return null;
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        image.Apply();
        RenderTexture.active = active;
        settings.targetTexture = previous;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
        Object.Destroy(image);
        texture.Release();
        Object.Destroy(texture);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SandboxBattle_PlayerUsesAbilityBar_AndCreaturesUseAbilities()
    {
        SceneManager.LoadScene("BattleSandbox");
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != "BattleSandbox"; i++)
            yield return null;
        BattleSandboxController sandbox = null;
        for (int i = 0; i < 120 && (sandbox == null || !(bool)Get(sandbox, "initialized")); i++)
        {
            sandbox = Object.FindAnyObjectByType<BattleSandboxController>();
            yield return null;
        }
        Assert.IsNotNull(sandbox);
        BattlePresentationSpeed.Index = BattlePresentationSpeed.Steps.Length - 1;

        Invoke(sandbox, "ChangeCount", false, "lynx", 1);
        Invoke(sandbox, "ChangeCount", false, "guard", 1);
        Invoke(sandbox, "ChangeCount", false, "elk_bull", 1);
        foreach (string id in new[] { "blood_kleshchen", "zvonnik", "podkopen", "shipovik", "nitnik", "gnezdovaya_matka", "rechnoy_uzel", "wolf" })
            Invoke(sandbox, "ChangeCount", true, id, 1);
        Invoke(sandbox, "StartBattle");

        float arenaDeadline = Time.realtimeSinceStartup + 60f;
        while (MapLabArenaDriver.Loading && Time.realtimeSinceStartup < arenaDeadline)
            yield return null;
        Assert.IsFalse(MapLabArenaDriver.Loading, "Арена собрана.");

        SandboxBattle battle = (SandboxBattle)Get(sandbox, "battle");
        Assert.IsNotNull(battle);
        Assert.IsTrue(battle.Telegraphs.Count == 0 || battle.Telegraphs.All(item => item.Kind != SandboxTelegraphKind.BurrowExit));
        Assert.IsTrue(battle.CellStates.Values.Any(state => (state & SandboxCellState.ShallowWater) != 0), "у Речного узла стартовая вода");
        int eggs = battle.Objects.Count(item => item.Kind == SandboxObjectKind.Egg);
        Assert.That(eggs >= 1 && eggs <= 2, "стартовые яйца матки (меньше — если рядом нет места): " + eggs);

        bool usedBar = false;
        bool shotTargeting = false;
        float deadline = Time.realtimeSinceStartup + 150f;
        while (Time.realtimeSinceStartup < deadline && battle.Phase == SandboxBattlePhase.InProgress && battle.Round < 7)
        {
            SandboxUnitState current = battle.CurrentUnit;
            if (current != null && current.Team == SandboxTeam.Player && !(bool)Get(sandbox, "combatAnimationRunning"))
            {
                yield return null;
                List<SandboxAbilityState> states = battle.GetAbilityStates(current.Id);
                SandboxAbilityState ready = states.FirstOrDefault(state => state.Available && !state.NoTarget);
                if (ready != null && !usedBar)
                {
                    int slot = states.IndexOf(ready);
                    Invoke(sandbox, "OnAbilitySlotClicked", slot);
                    BoardAbilityTargeting targeting = sandbox.Board.AbilityTargeting;
                    Assert.IsNotNull(targeting, "нажатие ячейки включило выбор цели");
                    Assert.Greater(targeting.Cells.Count, 0, "подсвечены клетки-кандидаты");
                    if (!shotTargeting)
                    {
                        shotTargeting = true;
                        yield return Shot(sandbox, "pr16_targeting");
                    }
                    HexCoord cell = targeting.Cells.OrderBy(item => item).First();
                    SandboxAbilityPreview preview = targeting.Preview(cell);
                    Assert.IsTrue(preview.IsValid, preview.Reason);
                    if (ready.NeedsSecondCell)
                    {
                        Invoke(sandbox, "OnAbilityCellClicked", cell);
                        cell = sandbox.Board.AbilityTargeting.Cells.OrderBy(item => item).First();
                    }
                    Invoke(sandbox, "OnAbilityCellClicked", cell);
                    usedBar = true;
                    continue;
                }
                if (battle.CurrentUnit == current && !(bool)Get(sandbox, "combatAnimationRunning"))
                    Invoke(sandbox, "EndPlayerActivation");
            }
            yield return null;
        }
        yield return Shot(sandbox, "pr16_battle");

        List<string> log = ((IList)Get(sandbox, "battleLog")).Cast<string>().ToList();
        string all = string.Join("\n", log);
        Debug.Log("PR16 battle log (last 40):\n" + string.Join("\n", log.Skip(System.Math.Max(0, log.Count - 40))));
        Assert.IsTrue(usedBar, "игрок применил способность через панель");
        Assert.IsTrue(battle.Round >= 3 || battle.Phase != SandboxBattlePhase.InProgress, "бой идёт: раунд " + battle.Round);
        string[] enemyAbilities = { "Цепляется", "Ошеломляющий плевок", "Метит", "Удар звона", "Подкоп", "Сплести нить", "Захват жгутом", "Отложить яйцо", "Яйцо", "разлив" };
        Assert.IsTrue(enemyAbilities.Any(title => all.Contains(title)), "ИИ существ пользуется способностями:\n" + all);
    }
}
