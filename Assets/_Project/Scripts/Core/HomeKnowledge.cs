using System;
using System.Collections.Generic;

// ПР-07Б (PR07_HOME_SPEC §12 с правкой §0.2): что игрок знает о Доме.
// Снимок при фактическом выходе — только для экрана «Дом — последние
// сведения», не второй GameState и не источник симуляции. Домашние новости,
// возникшие, пока отряд в пути, откладываются в сохраняемую очередь и
// выдаются при физическом возвращении одной записью «Пока вас не было…».
[Serializable]
public sealed class HomeKnowledgeData
{
    // Пока герой дома, подготовлен снимок следующего выхода. У старого
    // сохранения, загруженного уже в пути, признака нет — и сведения на
    // момент выхода не выдумываются.
    public bool ArmedForDeparture;

    // JsonUtility не умеет null — наличие снимка хранится явно.
    public bool HasSnapshot;
    public int Day;
    public double Hour;
    public int Gold;
    public int Food;
    public int HomePresent;
    public int Members;
    public string Defense = string.Empty;
    public List<string> Lines = new List<string>();

    // Новости Дома: Pending — выдать сразу (герой дома), Away — отложены до
    // возвращения.
    public List<string> PendingNews = new List<string>();
    public List<string> AwayNews = new List<string>();
}

public static class HomeKnowledge
{
    public static HomeKnowledgeData Get(GameState state)
    {
        if (state.HomeKnowledge == null)
            state.HomeKnowledge = new HomeKnowledgeData();
        HomeKnowledgeData data = state.HomeKnowledge;
        if (data.Lines == null)
            data.Lines = new List<string>();
        if (data.PendingNews == null)
            data.PendingNews = new List<string>();
        if (data.AwayNews == null)
            data.AwayNews = new List<string>();
        if (data.Defense == null)
            data.Defense = string.Empty;
        return data;
    }

    // Домашняя новость: дома — в общий поток сообщений (или в очередь на
    // немедленную выдачу), в пути — откладывается до возвращения.
    public static void Report(GameState state, List<string> messages, string text)
    {
        if (state == null || string.IsNullOrEmpty(text))
            return;

        if (HomePeopleService.HasDeparted(state))
        {
            Get(state).AwayNews.Add(text);
            return;
        }

        if (messages != null)
            messages.Add(text);
        else
            Get(state).PendingNews.Add(text);
    }

    public static List<string> TakePendingNews(GameState state)
    {
        HomeKnowledgeData data = Get(state);
        List<string> news = new List<string>(data.PendingNews);
        data.PendingNews.Clear();
        return news;
    }

    // Каждый кадр UI: дома — снимок следующего выхода «взведён»; в пути —
    // один раз сохраняется то, что было известно в момент выхода; по
    // возвращении снимок снимается, а отложенные новости выдаются одной
    // записью. Возвращает текст «Пока вас не было…» или null. Идемпотентно.
    public static string Refresh(GameState state, Func<GameState, IList<string>> describeHome)
    {
        if (state == null)
            return null;

        HomeKnowledgeData data = Get(state);
        bool away = HomePeopleService.HasDeparted(state);

        if (away)
        {
            if (data.ArmedForDeparture && !data.HasSnapshot)
                Capture(state, data, describeHome);
            data.ArmedForDeparture = false;
            return null;
        }

        string summary = null;
        if (data.HasSnapshot || data.AwayNews.Count > 0)
            summary = BuildReturnSummary(state, data);

        data.HasSnapshot = false;
        data.Lines.Clear();
        data.AwayNews.Clear();
        data.ArmedForDeparture = true;
        return summary;
    }

    private static void Capture(GameState state, HomeKnowledgeData data, Func<GameState, IList<string>> describeHome)
    {
        data.HasSnapshot = true;
        data.Day = state.Day;
        data.Hour = ContinuousSimulationSystem.GetClock(state).HourOfDay;
        data.Gold = state.Gold;
        data.Food = state.Food;
        data.HomePresent = HomePeopleService.CountHomePresent(state);
        data.Members = HomePeopleService.CountHomeMembers(state);
        data.Defense = HomeOverview.DescribeDefense(state);
        data.Lines.Clear();
        if (describeHome != null)
            data.Lines.AddRange(describeHome(state));
    }

    private static string BuildReturnSummary(GameState state, HomeKnowledgeData data)
    {
        List<string> lines = new List<string>();
        if (data.HasSnapshot)
        {
            if (data.Food != state.Food)
                lines.Add("Запасы Дома: было " + data.Food + ", стало " + state.Food + ".");
            if (data.Gold != state.Gold)
                lines.Add("Деньги: было " + data.Gold + ", стало " + state.Gold + ".");
        }
        lines.AddRange(data.AwayNews);

        return lines.Count == 0 ? null : "Пока вас не было…\n" + string.Join("\n", lines);
    }

    // Текст экрана «Дом — последние сведения».
    public static string DescribeLastKnown(GameState state)
    {
        HomeKnowledgeData data = Get(state);
        if (!data.HasSnapshot)
            return "Сведения на момент выхода не сохранены. Подробности станут известны после возвращения.";

        List<string> lines = new List<string>
        {
            "Известно на момент выхода: день " + data.Day + ", " + ContinuousSimulationSystem.FormatClock(data.Hour) + ".",
            "Деньги: " + data.Gold + ". Запасы Дома: " + data.Food + ".",
            "Дома оставалось " + data.HomePresent + " из " + data.Members + ". " + data.Defense
        };
        lines.AddRange(data.Lines);
        lines.Add("Это не свежие вести: что изменилось, станет известно по возвращении.");
        return string.Join("\n", lines);
    }
}
