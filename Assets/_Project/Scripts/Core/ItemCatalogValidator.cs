using System;
using System.Collections.Generic;

// 12Т: проверка определений вещей до запуска игры — «База предметов» показывает
// эти строки рядом с записью. Пустой результат — запись готова.
public static class ItemCatalogValidator
{
    public static List<string> Validate(ItemDefinition item, IEnumerable<ItemDefinition> all = null)
    {
        List<string> problems = new List<string>();
        if (item == null)
        {
            problems.Add("Пустая запись.");
            return problems;
        }
        if (string.IsNullOrWhiteSpace(item.Id))
            problems.Add("Нет ID.");
        else if (item.Id.Trim() != item.Id || item.Id.Contains(" "))
            problems.Add("ID с пробелами: «" + item.Id + "».");
        if (string.IsNullOrWhiteSpace(item.Name))
            problems.Add("Нет названия.");
        if (item.Price < 0)
            problems.Add("Цена меньше нуля.");
        if (item.Uses < 0)
            problems.Add("Число применений меньше нуля.");

        if (all != null && !string.IsNullOrWhiteSpace(item.Id))
        {
            int same = 0;
            foreach (ItemDefinition other in all)
            {
                if (other != null && other.Id == item.Id)
                    same++;
            }
            if (same > 1)
                problems.Add("ID «" + item.Id + "» повторяется (" + same + " записи).");
        }

        bool hasEffects = item.UseEffects != null && item.UseEffects.Count > 0;
        if (item.Kind == ItemKind.Consumable)
        {
            if (!hasEffects)
                problems.Add("Расходуемая вещь без эффектов: её нечем «использовать».");
            if (item.Uses <= 0)
                problems.Add("Расходуемой вещи нужно хотя бы одно применение.");
        }
        else if (hasEffects)
        {
            problems.Add("Эффекты «при использовании» работают только у расходуемых вещей.");
        }

        if (hasEffects)
        {
            foreach (ItemUseEffect effect in item.UseEffects)
            {
                if (effect == null)
                {
                    problems.Add("Пустой эффект.");
                    continue;
                }
                switch (effect.Type)
                {
                    case ItemUseEffectType.RestoreHitPoints:
                    case ItemUseEffectType.AddSupplies:
                        if (effect.Amount <= 0)
                            problems.Add(ItemUseService.DescribeEffect(effect) + ": количество должно быть больше нуля.");
                        break;
                    case ItemUseEffectType.GrantKnowledge:
                        if (string.IsNullOrWhiteSpace(effect.KnowledgeId))
                            problems.Add("«Даёт сведения»: не указан ID знания.");
                        break;
                }
            }
        }

        if (item.Slot == ItemSlot.None && !item.Modifier.IsZero)
            problems.Add("Прибавки «пока надето» есть, но вещь не надевается (вид: " + item.Kind + ").");
        if (item.IsStory && (item.CanSell || item.CanBuy))
            problems.Add("Сюжетную вещь нельзя продавать и покупать — снимите галочки.");
        if (item.Kind == ItemKind.TradeGood && item.Price <= 0)
            problems.Add("Товар без цены.");
        return problems;
    }
}
