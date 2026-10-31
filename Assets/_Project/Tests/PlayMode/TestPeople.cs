using System.Collections.Generic;

// Канон v1.59: новая партия начинается с одного Командира — бойцов и свиты
// исходного Дома больше нет. Тесты, которым нужен отряд, явно принимают
// персонажей-заготовок (CharacterTemplates) тем же входом, что будет у
// содержания (PeopleService.AdmitFromTemplate).
internal static class TestPeople
{
    public static readonly string[] Fighters = { "garrick", "edric", "marta", "torvin", "agnessa" };
    public static readonly string[] Retinue = { "ostafiy", "lada" };

    public static GameState Admit(GameState state, params string[] templateIds)
    {
        foreach (string id in templateIds)
            PeopleService.AdmitFromTemplate(state, id);
        return state;
    }

    // Прежний тестовый отряд: пять бойцов и два специалиста свиты.
    public static GameState AdmitAll(GameState state)
    {
        List<string> ids = new List<string>(Fighters);
        ids.AddRange(Retinue);
        return Admit(state, ids.ToArray());
    }
}
