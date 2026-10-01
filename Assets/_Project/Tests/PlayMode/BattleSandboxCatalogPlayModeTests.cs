using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Полигон BattleSandbox без запроса кампании: две колонки — свой отряд и
// противник, в обеих все типы Базы существ, без автовыбора, готовых
// составов и засады. Контроллер недоступен тестам напрямую — через рефлексию.
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

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        Assert.IsNotNull(field, "Нет поля " + name);
        field.SetValue(target, value);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    private static IEnumerator OpenSandbox(System.Action<MonoBehaviour> ready)
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
        ready(sandbox);
    }

    private static int Count(object battle, string team)
    {
        return ((IEnumerable)GetMember(battle, "Units")).Cast<object>()
            .Count(unit => GetMember(unit, "Team").ToString() == team);
    }

    [UnityTest]
    public IEnumerator Setup_TwoColumns_NoAutoPick_AnyUnitOnEitherSide()
    {
        MonoBehaviour sandbox = null;
        yield return OpenSandbox(found => sandbox = found);
        Assert.IsNull(GetMember(sandbox, "battle"), "Без запроса кампании открыт выбор состава.");

        Button start = (Button)GetMember(sandbox, "startBattleButton");
        Assert.IsFalse(start.enabledSelf, "Автовыбора нет: без бойцов и противников бой не начать.");

        // Гвардеец слева, шесть Шешек и гвардеец справа: любой тип — с любой стороны.
        Invoke(sandbox, "ChangeCount", false, "guard", 1);
        Assert.IsFalse(start.enabledSelf, "Без противника бой не начать.");
        for (int i = 0; i < 6; i++)
            Invoke(sandbox, "ChangeCount", true, "sheshka", 1);
        Invoke(sandbox, "ChangeCount", true, "guard", 1);
        Assert.IsTrue(start.enabledSelf);

        for (int i = 0; i < 5; i++)
            Invoke(sandbox, "ChangeCount", true, "wolf", 1);
        Assert.AreEqual(8, (int)Invoke(sandbox, "SideTotal", true), "Справа не больше восьми.");

        Invoke(sandbox, "StartBattle");
        for (int i = 0; i < 10; i++)
            yield return null;

        object battle = GetMember(sandbox, "battle");
        Assert.IsNotNull(battle, "Бой начался.");
        Assert.AreEqual(1, Count(battle, "Player"));
        Assert.AreEqual(8, Count(battle, "Enemy"));
        Assert.IsTrue(((IEnumerable)GetMember(battle, "Units")).Cast<object>()
            .Any(unit => GetMember(unit, "Team").ToString() == "Enemy" && (string)GetMember(unit, "TypeId") == "guard"),
            "Боец базы может быть противником.");
    }

    // Регрессия: «Повторить бой», нажатый, пока показ прежнего боя не
    // доиграл, оставлял флаги анимации и хода противника взведёнными —
    // в новом бою никто не ходил.
    [UnityTest]
    public IEnumerator RepeatBattle_WhileShowStillRunning_DoesNotFreeze()
    {
        MonoBehaviour sandbox = null;
        yield return OpenSandbox(found => sandbox = found);
        Invoke(sandbox, "ChangeCount", false, "guard", 1);
        Invoke(sandbox, "ChangeCount", true, "lynx", 1);
        Invoke(sandbox, "StartBattle");
        for (int i = 0; i < 5; i++)
            yield return null;

        // Состояние прерванного показа прежнего боя.
        SetField(sandbox, "combatAnimationRunning", true);
        SetField(sandbox, "enemyStepScheduled", true);
        Invoke(sandbox, "StartBattle");
        Assert.IsFalse((bool)GetMember(sandbox, "combatAnimationRunning"), "Флаг показа прежнего боя сброшен.");

        // Новый бой идёт: ходы сменяются (рысь быстрее гвардейца и ходит сама).
        object battle = GetMember(sandbox, "battle");
        int startLog = ((IList)GetMember(sandbox, "battleLog")).Count;
        float deadline = Time.realtimeSinceStartup + 6f;
        while (Time.realtimeSinceStartup < deadline)
        {
            object current = GetMember(battle, "CurrentUnit");
            if (current != null && GetMember(current, "Team").ToString() == "Player" &&
                !(bool)GetMember(sandbox, "combatAnimationRunning"))
            {
                Invoke(sandbox, "EndPlayerActivation");
            }
            if (((IList)GetMember(sandbox, "battleLog")).Count > startLog + 2)
                break;
            yield return null;
        }
        Assert.Greater(((IList)GetMember(sandbox, "battleLog")).Count, startLog + 2, "После повтора бой продолжается.");
    }
}
