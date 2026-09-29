using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// ПР-12Ж: тестовый бой с готовым составом каталога существ. Сцена
// BattleSandbox без запроса кампании открывает выбор состава; состав
// «Много мелочи» (6 Шешек) запускается, существа без рисунка рисуются
// жетонами. Контроллер недоступен тестам напрямую — через рефлексию.
public sealed class BattleSandboxCatalogPlayModeTests
{
    private const string BattleScene = "BattleSandbox";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        CampaignSession.Reset();
    }

    private static MonoBehaviour FindBehaviour(string typeName)
    {
        return Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(behaviour => behaviour.GetType().Name == typeName);
    }

    private static object GetMember(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        if (field != null)
            return field.GetValue(target);
        PropertyInfo property = target.GetType().GetProperty(name, AnyInstance);
        Assert.IsNotNull(property, "Нет члена " + name);
        return property.GetValue(target);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    [UnityTest]
    public IEnumerator SwarmPreset_StartsBattleWithSixTokens()
    {
        SceneManager.LoadScene(BattleScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != BattleScene; i++)
            yield return null;
        MonoBehaviour sandbox = null;
        for (int i = 0; i < 120 && (sandbox == null || !(bool)GetMember(sandbox, "initialized")); i++)
        {
            sandbox = FindBehaviour("BattleSandboxController");
            yield return null;
        }
        Assert.IsNotNull(sandbox);
        Assert.IsNull(GetMember(sandbox, "battle"), "Без запроса кампании открыт выбор состава.");

        object content = GetMember(sandbox, "unitContent");
        IList presets = (IList)GetMember(content, "Presets");
        int swarm = -1;
        for (int i = 0; i < presets.Count; i++)
        {
            if ((string)GetMember(presets[i], "Id") == "preset.swarm")
                swarm = i;
        }
        Assert.GreaterOrEqual(swarm, 0, "Составы из Базы существ дошли до тестового боя.");

        Invoke(sandbox, "SelectPreset", swarm);
        Invoke(sandbox, "StartBattle");
        for (int i = 0; i < 10; i++)
            yield return null;

        object battle = GetMember(sandbox, "battle");
        Assert.IsNotNull(battle, "Бой начался.");
        IEnumerable units = (IEnumerable)GetMember(battle, "Units");
        int enemies = units.Cast<object>().Count(unit => GetMember(unit, "Team").ToString() == "Enemy");
        Assert.AreEqual(6, enemies);

        object board = GetMember(sandbox, "board");
        for (int i = 0; i < 30 && ((IDictionary)GetMember(board, "unitTokenLabels")).Count < 6; i++)
            yield return null;
        Assert.AreEqual(6, ((IDictionary)GetMember(board, "unitTokenLabels")).Count, "Шешки без рисунка — жетоны с буквами.");
    }
}
