using System;
using System.Collections.Generic;

// Канон v1.59: персонажи удалённой кампании «Дом на чужой воде» сохранены как
// неактивные заготовки для будущего содержания. Каталог ничего не делает сам:
// новая партия их не создаёт, в отряд, свиту и знакомые они не попадают.
// Знакомство возможно только через отдельное реально доступное содержание.
//
// Портреты — у говорящих Базы диалогов (DialogueSpeakerId); боевой шаблон —
// тип UnitDatabase (UnitTypeId). Описания — нейтральные рабочие тексты без
// ссылок на события удалённой кампании; это не канон личности и биографии.
public sealed class CharacterTemplate
{
    public string Id;
    public string DisplayName;
    public string RoleLabel;
    public string ShortDescription;
    public string DialogueSpeakerId = string.Empty;
    public ResidentAgeGroup AgeGroup = ResidentAgeGroup.Adult;
    public ResidentTravelRole TravelRole = ResidentTravelRole.None;
    public string UnitTypeId = string.Empty;
    // Снаряжение, с которым заготовка задумана (ID «Базы предметов»).
    public string[] KitItemIds = Array.Empty<string>();
}

public static class CharacterTemplates
{
    public static readonly IReadOnlyList<CharacterTemplate> All = new List<CharacterTemplate>
    {
        new CharacterTemplate
        {
            Id = "ostafiy", DisplayName = "Остафий", RoleLabel = "хранитель старых работ", DialogueSpeakerId = "ostafiy",
            AgeGroup = ResidentAgeGroup.Elder, TravelRole = ResidentTravelRole.Retinue,
            ShortDescription = "Помнит, как делали работы раньше. Может ошибаться в причинах, но не в порядке действий. В пути замечает следы прежних работ."
        },
        new CharacterTemplate
        {
            Id = "lada", DisplayName = "Лада", RoleLabel = "мастер по дереву", DialogueSpeakerId = "lada",
            TravelRole = ResidentTravelRole.Retinue,
            ShortDescription = "Работает с деревом, креплениями и настилами. В пути может укрепить опору."
        },
        new CharacterTemplate
        {
            Id = "miron", DisplayName = "Мирон", RoleLabel = "мельник", DialogueSpeakerId = "miron",
            ShortDescription = "Слышит колесо раньше, чем видит."
        },
        new CharacterTemplate
        {
            Id = "ulyana", DisplayName = "Ульяна", RoleLabel = "держит быт", DialogueSpeakerId = "ulyana",
            ShortDescription = "Видит цену любого решения через тех, кого потом кормить. Если Марты нет, берёт уход на себя — как умеет."
        },
        new CharacterTemplate
        {
            Id = "tikhon", DisplayName = "Тихон", RoleLabel = "рыбак", DialogueSpeakerId = "tikhon",
            TravelRole = ResidentTravelRole.Combatant, UnitTypeId = "militia",
            ShortDescription = "Читает течение и дно, знает лодку. Защищаться умеет как ополченец.",
            KitItemIds = new[] { ItemCatalog.Axe, ItemCatalog.FisherCoat }
        },
        new CharacterTemplate
        {
            Id = "varvara", DisplayName = "Варвара", RoleLabel = "жена Тихона", DialogueSpeakerId = "varvara",
            ShortDescription = "Решает наравне с мужем."
        },
        new CharacterTemplate
        {
            Id = "garrick", DisplayName = "Гаррик", RoleLabel = "гвардеец", TravelRole = ResidentTravelRole.Combatant, UnitTypeId = "guard",
            ShortDescription = "Опытный защитник. Судит решение по тому, кто будет отвечать за его исполнение.",
            KitItemIds = new[] { ItemCatalog.SwordAndShield, ItemCatalog.MailShirt }
        },
        new CharacterTemplate
        {
            Id = "edric", DisplayName = "Эдрик", RoleLabel = "лучник", TravelRole = ResidentTravelRole.Combatant, UnitTypeId = "archer",
            ShortDescription = "Молодой лучник. Слишком быстро объявляет себя готовым — но в трудную минуту не бросает своих.",
            KitItemIds = new[] { ItemCatalog.Bow, ItemCatalog.Jacket }
        },
        new CharacterTemplate
        {
            Id = "marta", DisplayName = "Марта", RoleLabel = "лекарь", TravelRole = ResidentTravelRole.Combatant, UnitTypeId = "healer",
            ShortDescription = "Лекарь. В походе — боевой лекарь отряда.",
            KitItemIds = new[] { ItemCatalog.Knife, ItemCatalog.Jacket, ItemCatalog.HealerBag }
        },
        new CharacterTemplate
        {
            Id = "torvin", DisplayName = "Торвин", RoleLabel = "копейщик", DialogueSpeakerId = "torvin",
            TravelRole = ResidentTravelRole.Combatant, UnitTypeId = "spearman",
            ShortDescription = "Копейщик. Не любит чужих решений о своём времени, но простой ремонт сделает по образцу.",
            KitItemIds = new[] { ItemCatalog.Spear, ItemCatalog.PaddedCoat }
        },
        new CharacterTemplate
        {
            Id = "agnessa", DisplayName = "Агнесса", RoleLabel = "разведчица", DialogueSpeakerId = "agnessa",
            TravelRole = ResidentTravelRole.Combatant, UnitTypeId = "scout",
            ShortDescription = "Разведчица. Сначала смотрит на следы, потом верит рассказам.",
            KitItemIds = new[] { ItemCatalog.Bow, ItemCatalog.Jacket }
        }
    };

    public static CharacterTemplate Find(string id)
    {
        foreach (CharacterTemplate template in All)
        {
            if (template.Id == id)
                return template;
        }
        return null;
    }
}
