using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Аудит A-01: отложенные инициализации карты не размножают свои повторы в
// планировщике UI Toolkit (иначе он уходит в троттлинг и бой на месте
// «застывает»), а карточка осмотра места по ПКМ находится и открывается.
public sealed class WorldMapInitPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, All).Invoke(target, args);

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, All).GetValue(target);

    // Число ждущих задач планировщика панели (внутреннее состояние UI Toolkit,
    // читается рефлексией только в тесте).
    private static int ScheduledCount(VisualElement root)
    {
        object panel = root.panel;
        object scheduler = null;
        for (System.Type t = panel.GetType(); t != null && scheduler == null; t = t.BaseType)
        {
            foreach (FieldInfo f in t.GetFields(All))
            {
                if (f.FieldType.Name.Contains("Scheduler"))
                {
                    scheduler = f.GetValue(panel);
                    break;
                }
            }
        }
        Assert.IsNotNull(scheduler, "Планировщик панели не найден.");
        for (System.Type t = scheduler.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo list = t.GetField("m_ScheduledItems", All);
            if (list != null)
                return ((ICollection)list.GetValue(scheduler)).Count;
        }
        Assert.Fail("Список задач планировщика не найден.");
        return -1;
    }

    private static IEnumerator StartFreePlay(System.Action<MonoBehaviour> ready)
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
            .First(behaviour => behaviour.GetType().Name == "PrototypeUIController");
        Invoke(main, "StartNewGameFromMenu");
        yield return Frames(30);
        ready(main);
    }

    [UnityTest]
    public IEnumerator MapInitRetries_DoNotAccumulate_InALongSession()
    {
        MonoBehaviour main = null;
        yield return StartFreePlay(m => main = m);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;

        yield return Frames(300);
        int settled = ScheduledCount(root);

        yield return Frames(900);
        GameState campaign = CampaignSession.Current;
        campaign.ArmySupply = 60;
        LocationData target = campaign.Locations.First(location => !location.IsWaypoint && !location.IsSettlement);
        target.IsVisibleOnMap = true;
        target.IsDiscovered = true;
        Assert.IsTrue(campaign.TryStartExpedition(target.Id, new List<string>(), out string message), message);
        yield return Frames(900);
        int later = ScheduledCount(root);

        Assert.IsTrue((bool)Field(main, "worldMapLocationActionsInitialized"), "Действия карточки места привязаны.");
        Assert.LessOrEqual(later, settled + 4, "Задачи планировщика не копятся: было " + settled + ", стало " + later + ".");
        Assert.Less(later, 20, "Очередь планировщика мала.");
    }

    [UnityTest]
    public IEnumerator RightClickOnPlace_OpensInspectionCard()
    {
        MonoBehaviour main = null;
        yield return StartFreePlay(m => main = m);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        GameState campaign = CampaignSession.Current;
        LocationData place = campaign.Locations.First(location => !location.IsWaypoint && !location.IsSettlement);
        place.IsVisibleOnMap = true;
        place.IsDiscovered = true;
        Invoke(main, "RefreshWorldMapPanel");
        yield return Frames(60);

        VisualElement node = root.Q("world-map-node-" + place.Id);
        Assert.IsNotNull(node, "Место есть на карте.");
        using (PointerDownEvent evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1, clickCount = 1, mousePosition = node.worldBound.center }))
        {
            evt.target = node;
            node.SendEvent(evt);
        }
        using (PointerUpEvent evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 1, mousePosition = node.worldBound.center }))
        {
            evt.target = node;
            node.SendEvent(evt);
        }
        yield return Frames(5);

        VisualElement card = root.Q("world-map-location-inspection-card");
        Assert.AreEqual(DisplayStyle.Flex, card.resolvedStyle.display, "ПКМ по месту открывает карточку осмотра.");
        Assert.AreEqual(place.Name.ToUpper(), root.Q<Label>("world-map-location-inspection-title").text, "Карточка — о выбранном месте.");
    }
}
