using System.Collections.Generic;

// Значения «Базы развития» по умолчанию для перечня особенностей, приёмов и
// приказов. Сформировано 27.09.2026 из рабочего каталога
// ProjectDocs/FEATURES_REFERENCES_ADAPTATION.md (§3.1–§4); дальше каталог
// правится в окне «База развития», а этот список — исходное содержимое базы
// и значения ядра без неё (EditMode-тесты). Всё [РАБОЧЕЕ]: названия, условия
// и числа не утверждены. Активны только особенности, у которых есть код; заглушки ждут своей механики.
public static class ProgressionFeatureDefaults
{
    public static void AddTo(List<TraitCatalogEntry> list)
    {
        TraitCatalogEntry e;

        // Б-01 Держать строй
        e = New("Б-01", "derzhat_stroy", "Держать строй", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Both, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "I: союзник рядом с носителем щита получает +1 Защиту против первой ближней атаки за раунд. II: и против первой дальней («За моей спиной»). III (Командир строя: Щит и строй 4 + Характер 7): бонус у всех соседей носителя";
        e.UnlockText = "след «держал строй»; Щит и строй 2";
        e.Support = "доработка: признак «со щитом» от вещи";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Держать строй ×4, Стена (часть), За моей спиной, Командир строя · канон §27.1.1, Eador, Heroes V, Arkham, KCD2";
        Rank(e, "", "Союзник рядом с носителем щита получает +1 Защиту против первой ближней атаки за раунд.");
        Rank(e, "", "И против первой дальней («За моей спиной»).");
        Rank(e, "Командир строя", "Бонус у всех соседей носителя. Требует: Щит и строй 4 + Характер 7.");
        Require(e, FeatureRequirementKind.Competency, "shield_and_line", 2);
        list.Add(e);

        // Б-02 Спиной к спине
        e = New("Б-02", "spinoy_k_spine", "Спиной к спине", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Если рядом ≥2 союзника: противник не получает бонус окружения против носителя, первая попытка сдвинуть носителя за раунд проваливается";
        e.UnlockText = "след «бился рядом с одним и тем же человеком»";
        e.Support = "нужно: окружение и толчок";
        e.Dependency = "окружение и толчок";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Спиной к спине (Eador), Плечом к плечу (Arkham)";
        Rank(e, "", "Если рядом ≥2 союзника: противник не получает бонус окружения против носителя, первая попытка сдвинуть носителя за раунд проваливается");
        list.Add(e);

        // Б-03 Щит товарища
        e = New("Б-03", "shchit_tovarishcha", "Щит товарища", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Stub);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Limit = FeatureLimit.Round;
        e.Description = "I: 1/раунд принять на себя ближнюю атаку по соседнему союзнику (урон — по Защите носителя). Вариант I (выбрать один — по силе несовместимы): вместо полного перехвата принять на себя до 2 урона от атаки по соседу. II: перехват не тратит ответный удар носителя. III: прикрытый может сразу отойти на 1 гекс";
        e.UnlockText = "след «прикрывал союзника»";
        e.Support = "нужно: реакция-перехват";
        e.Dependency = "реакция-перехват";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Прикрыть товарища (Eador, Корсары), За другого, Прикрой его · канон «Не оставляет своих» (частично)";
        Rank(e, "", "1/раунд принять на себя ближнюю атаку по соседнему союзнику (урон — по Защите носителя). Вариант I (выбрать один — по силе несовместимы): вместо полного перехвата принять на себя до 2 урона от атаки по соседу.");
        Rank(e, "", "Перехват не тратит ответный удар носителя.");
        Rank(e, "", "Прикрытый может сразу отойти на 1 гекс");
        list.Add(e);

        // Б-04 Основная защита
        e = New("Б-04", "osnovnaya_zashchita", "Основная защита", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "I: первая атака по носителю за раунд −1 Атака. II (Крепкая защита): если носитель не двигался в свой ход — ещё +1 Защита до следующего хода";
        e.UnlockText = "след «много защит»; Щит и строй 1";
        e.Support = "есть (формула атаки)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Основная/Крепкая защита (Корсары), Осторожность (King's Bounty)";
        Rank(e, "", "Первая атака по носителю за раунд −1 Атака.");
        Rank(e, "Крепкая защита", "Если носитель не двигался в свой ход — ещё +1 Защита до следующего хода");
        Require(e, FeatureRequirementKind.Competency, "shield_and_line", 1);
        list.Add(e);

        // Б-05 Держит удар
        e = New("Б-05", "derzhit_udar", "Держит удар", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После второго попадания по носителю за раунд +1 Защита до его следующего хода";
        e.UnlockText = "много пережитых тяжёлых боёв";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Resilience (Heroes VI; в исходнике — «Закалённый»)";
        Rank(e, "", "После второго попадания по носителю за раунд +1 Защита до его следующего хода");
        list.Add(e);

        // Б-06 Не пройти
        e = New("Б-06", "ne_proyti", "Не пройти", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Первый враг за раунд, вошедший в соседний гекс носителя, теряет 1 оставшееся движение";
        e.UnlockText = "копьё или щит; след «оборонял позицию»";
        e.Support = "доработка";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Не отдаёт шаг (Eador), Не пройти (Arkham)";
        Rank(e, "", "Первый враг за раунд, вошедший в соседний гекс носителя, теряет 1 оставшееся движение");
        list.Add(e);

        // Б-07 Упрямец
        e = New("Б-07", "upryamets", "Упрямец", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Both, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Тяжёлая рана по итогам боя наступает при HP ≤ 1/8 максимума вместо 1/4; будущие штрафы ранения в бою подавлены до конца боя";
        e.UnlockText = "след «сражался тяжелораненым»";
        e.Support = "есть (порог тяжёлой раны в итоге боя)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Упрямец (канон §27.1.1), Привык к боли, Закалённый (Eador), Стиснуть зубы";
        Rank(e, "", "Тяжёлая рана по итогам боя наступает при HP ≤ 1/8 максимума вместо 1/4; будущие штрафы ранения в бою подавлены до конца боя");
        list.Add(e);

        // Б-08 Ещё на ногах
        e = New("Б-08", "eshchyo_na_nogakh", "Ещё на ногах", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Both, FeatureKind.Limited, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Limit = FeatureLimit.Battle;
        e.Description = "1/бой урон, который вывел бы носителя из строя, оставляет 1 HP; после боя — обязательная тяжёлая рана";
        e.UnlockText = "след «пережил тяжёлые раны»";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Ещё на ногах (Arkham), Последний на ногах (Heroes V), Божественная броня (King's Bounty)";
        Rank(e, "", "1/бой урон, который вывел бы носителя из строя, оставляет 1 HP; после боя — обязательная тяжёлая рана");
        list.Add(e);

        // Б-09 Загнанный
        e = New("Б-09", "zagnannyy", "Загнанный", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "При первом падении HP ниже 1/4 за бой: бесплатный шаг на 1 гекс, до конца боя +1 Защита";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Загнанный (KCD2), Последний резерв, Подранок опаснее, Второе дыхание (Eador, Корсары)";
        Rank(e, "", "При первом падении HP ниже 1/4 за бой: бесплатный шаг на 1 гекс, до конца боя +1 Защита");
        list.Add(e);

        // Б-10 Кровь горячит
        e = New("Б-10", "krov_goryachit", "Кровь горячит", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После полученного урона следующая атака носителя до конца его следующего хода +1 Атака; не складывается";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Arkham";
        Rank(e, "", "После полученного урона следующая атака носителя до конца его следующего хода +1 Атака; не складывается");
        list.Add(e);

        // Б-11 Не дрогнет
        e = New("Б-11", "ne_drognet", "Не дрогнет", FeatureLayer.Feature, "Строй и защита", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первый страх, паника или оцепенение за бой не действуют";
        e.UnlockText = "—";
        e.Support = "нужно: боевые состояния страха";
        e.Dependency = "боевые состояния страха";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Не дрогнет, Железная воля, Стойкость Севера, Сбросить оцепенение, Willpower";
        Rank(e, "", "Первый страх, паника или оцепенение за бой не действуют");
        list.Add(e);

        // Б-12 Контрудар
        e = New("Б-12", "kontrudar", "Контрудар", FeatureLayer.Feature, "Ответ и реакция", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "I: ответный удар доступен дважды за раунд. II: первый ответный удар за раунд +1 Урон";
        e.UnlockText = "след «много ответных ударов»";
        e.Support = "есть (ответ раз в раунд)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Контрудар (King's Bounty, Корсары), Встречный боец (Eador), Ответный удар (Heroes VI, канон §27.1.1), Неожиданный отпор";
        Rank(e, "", "Ответный удар доступен дважды за раунд.");
        Rank(e, "", "Первый ответный удар за раунд +1 Урон");
        list.Add(e);

        // Б-13 Упреждающий удар
        e = New("Б-13", "uprezhdayushchiy_udar", "Упреждающий удар", FeatureLayer.Feature, "Ответ и реакция", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Если носитель в защитной стойке, его ответ на ближнюю атаку наносится до удара противника";
        e.UnlockText = "след «защищался в стойке»";
        e.Support = "есть (стойка + ответ)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Упреждающий удар (Heroes V/VII/OE), First Strike (Eador)";
        Rank(e, "", "Если носитель в защитной стойке, его ответ на ближнюю атаку наносится до удара противника");
        list.Add(e);

        // Б-14 Парирование
        e = New("Б-14", "parirovanie", "Парирование", FeatureLayer.Feature, "Ответ и реакция", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если ответ носителя в этом раунде уже израсходован, входящий ближний урон −1";
        e.UnlockText = "Рубящее оружие или Копейное дело 3";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Parry (Heroes VI)";
        Rank(e, "", "Если ответ носителя в этом раунде уже израсходован, входящий ближний урон −1");
        Require(e, FeatureRequirementKind.Competency, "chopping_weapons", 3);
        Require(e, FeatureRequirementKind.Competency, "spearcraft", 3);
        e.RequirementsAnyOf = true;
        list.Add(e);

        // Б-15 Встречает первым
        e = New("Б-15", "vstrechaet_pervym", "Встречает первым", FeatureLayer.Feature, "Ответ и реакция", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Stub);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Limit = FeatureLimit.Round;
        e.Description = "I: 1/раунд, когда враг входит в соседний гекс, носитель бьёт первым. II (Боевой дозор): то же с любым ближним оружием, если носитель закончил ход без атаки";
        e.UnlockText = "Копейное дело 2; след «встречал сближающегося врага»";
        e.Support = "нужно: реакция на вход в соседний гекс";
        e.Dependency = "реакция на вход в соседний гекс";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Встречает первым (Eador), Быстрый ответ, Перехват (Корсары), Overwatch (Olden Era)";
        Rank(e, "", "1/раунд, когда враг входит в соседний гекс, носитель бьёт первым.");
        Rank(e, "Боевой дозор", "То же с любым ближним оружием, если носитель закончил ход без атаки");
        Require(e, FeatureRequirementKind.Competency, "spearcraft", 2);
        list.Add(e);

        // Б-16 Не отпущу
        e = New("Б-16", "ne_otpushchu", "Не отпущу", FeatureLayer.Feature, "Ответ и реакция", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Round;
        e.Description = "Выход противника из соседнего гекса вызывает атаку носителя (1/раунд)";
        e.UnlockText = "ближний бой, опыт преследования";
        e.Support = "нужно: правило выхода из контакта";
        e.Dependency = "правило выхода из контакта";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Grappling (Корсары)";
        Rank(e, "", "Выход противника из соседнего гекса вызывает атаку носителя (1/раунд)");
        list.Add(e);

        // Б-17 Пристрелялся
        e = New("Б-17", "pristrelyalsya", "Пристрелялся", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: после атаки по цели следующие атаки по ней +1 Атака, пока носитель не сменил цель. II (Не отпускай): бонус держится ещё ход после перемещения";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Пристрелялся, Выбрал цель, Не отпускай (Arkham)";
        Rank(e, "", "После атаки по цели следующие атаки по ней +1 Атака, пока носитель не сменил цель.");
        Rank(e, "Не отпускай", "Бонус держится ещё ход после перемещения");
        list.Add(e);

        // Б-18 Холодный глаз
        e = New("Б-18", "kholodnyy_glaz", "Холодный глаз", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "I: стрелок, не двигавшийся в этот ход, игнорирует 1 Защиту цели (из трёх исходных версий нужно выбрать одну). II (Ищет щель): после действия «Прицелиться» (П-16) выстрел частично обходит защиту цели";
        e.UnlockText = "Стрельба 2; след «стрелял с места»";
        e.Support = "I — есть; II — нужно: действие «Прицелиться»";
        e.Dependency = "действие «Прицелиться»";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Холодный глаз (Eador), Steady Aim (KCD2), Точная дистанция, Выверенный выстрел, Ищет щель";
        Rank(e, "", "Стрелок, не двигавшийся в этот ход, игнорирует 1 Защиту цели (из трёх исходных версий нужно выбрать одну).");
        Rank(e, "Ищет щель", "После действия «Прицелиться» (П-16) выстрел частично обходит защиту цели");
        Require(e, FeatureRequirementKind.Competency, "shooting", 2);
        list.Add(e);

        // Б-19 Дальний выстрел
        e = New("Б-19", "dalniy_vystrel", "Дальний выстрел", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "+1 гекс дальности";
        e.UnlockText = "Стрельба 3";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Long Range Shoot (Корсары)";
        Rank(e, "", "+1 гекс дальности");
        Require(e, FeatureRequirementKind.Competency, "shooting", 3);
        list.Add(e);

        // Б-20 Быстрый стрелок
        e = New("Б-20", "bystryy_strelok", "Быстрый стрелок", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В первом раунде инициатива стрелка +2. II (Засадник): при подготовленном начале боя первый выстрел +1 Урон";
        e.UnlockText = "Стрельба 2; составная II: Скрытное движение 3 + Стрельба 3";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Quick Draw (King's Bounty), Быстрая стрельба, Засадный стрелок (Heroes)";
        Rank(e, "", "В первом раунде инициатива стрелка +2.");
        Rank(e, "Засадник", "При подготовленном начале боя первый выстрел +1 Урон");
        Require(e, FeatureRequirementKind.Competency, "shooting", 2);
        list.Add(e);

        // Б-21 На ходу
        e = New("Б-21", "na_khodu", "На ходу", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первый гекс движения перед атакой не тратит движение";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "На ходу (Eador), Боевой шаг (приём)";
        Rank(e, "", "Первый гекс движения перед атакой не тратит движение");
        list.Add(e);

        // Б-22 Ударил — ушёл
        e = New("Б-22", "udaril_ushyol", "Ударил — ушёл", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Limit = FeatureLimit.Turn;
        e.Description = "После атаки 1/ход бесплатно сместиться на 1 свободный гекс";
        e.UnlockText = "след «бил и отходил»";
        e.Support = "есть (сейчас атака обнуляет движение)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Ударил — ушёл (Eador), Отступить с боем, После выстрела — шаг, Стрелок-профессионал, Резкий отход (Корсары), Отход с боем";
        Rank(e, "", "После атаки 1/ход бесплатно сместиться на 1 свободный гекс");
        list.Add(e);

        // Б-23 Разошёлся
        e = New("Б-23", "razoshyolsya", "Разошёлся", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После того как носитель вывел врага из строя, следующая атака +1 Урон; не складывается";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Frenzy (King's Bounty), Адреналин";
        Rank(e, "", "После того как носитель вывел врага из строя, следующая атака +1 Урон; не складывается");
        list.Add(e);

        // Б-24 Не останавливаясь
        e = New("Б-24", "ne_ostanavlivayas", "Не останавливаясь", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первое выведение врага из строя за раунд возвращает 2 движения (не новый ход)";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Cleave (Heroes VI), Не стой (Arkham)";
        Rank(e, "", "Первое выведение врага из строя за раунд возвращает 2 движения (не новый ход)");
        list.Add(e);

        // Б-25 Врасплох
        e = New("Б-25", "vrasplokh", "Врасплох", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Both, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первая атака носителя в бою +1 Атака; если цель ещё не действовала в этом раунде — её Инициатива −2 до конца раунда";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Врасплох (KCD2), Первый удар (Arkham)";
        Rank(e, "", "Первая атака носителя в бою +1 Атака; если цель ещё не действовала в этом раунде — её Инициатива −2 до конца раунда");
        list.Add(e);

        // Б-26 Охотник на крупных
        e = New("Б-26", "okhotnik_na_krupnykh", "Охотник на крупных", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Против целей с тегом «крупный» первая атака за ход +1 Урон";
        e.UnlockText = "след «бился с крупным зверем»";
        e.Support = "доработка: тег в Базе существ";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Убийца великанов (Heroes VI/VII), Охотник на крупных (Arkham)";
        Rank(e, "", "Против целей с тегом «крупный» первая атака за ход +1 Урон");
        list.Add(e);

        // Б-27 Против числом
        e = New("Б-27", "protiv_chislom", "Против числом", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если в начале боя противников больше, чем союзников: +1 Защита и +1 Инициатива";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Against All Odds (KCD2)";
        Rank(e, "", "Если в начале боя противников больше, чем союзников: +1 Защита и +1 Инициатива");
        list.Add(e);

        // Б-28 Вторая линия
        e = New("Б-28", "vtoraya_liniya", "Вторая линия", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Копейщик атакует через соседнего союзника (дальность 2 по прямой)";
        e.UnlockText = "Копейное дело 3";
        e.Support = "нужно: досягаемость копья";
        e.Dependency = "досягаемость копья";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Arkham";
        Rank(e, "", "Копейщик атакует через соседнего союзника (дальность 2 по прямой)");
        Require(e, FeatureRequirementKind.Competency, "spearcraft", 3);
        list.Add(e);

        // Б-29 Добить опасного
        e = New("Б-29", "dobit_opasnogo", "Добить опасного", FeatureLayer.Feature, "Нападение и стиль", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Против врага с HP ≤ 1/4 +1 Атака; награды за добивание нет (канон §27.3)";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Arkham";
        Rank(e, "", "Против врага с HP ≤ 1/4 +1 Атака; награды за добивание нет (канон §27.3)");
        list.Add(e);

        // Б-30 Один клинок
        e = New("Б-30", "odin_klinok", "Один клинок", FeatureLayer.Feature, "Стойки (взаимоисключающие)", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "+1 Инициатива, −1 Защита";
        e.UnlockText = "одноручное оружие без щита";
        e.Support = "доработка: признаки вещи";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "KCD2";
        Rank(e, "", "+1 Инициатива, −1 Защита");
        e.ExcludesIds.Add("stena");
        list.Add(e);

        // Б-31 Стена
        e = New("Б-31", "stena", "Стена", FeatureLayer.Feature, "Стойки (взаимоисключающие)", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "+1 Защита, −1 движение";
        e.UnlockText = "щит + союзник рядом";
        e.Support = "доработка";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "KCD2";
        Rank(e, "", "+1 Защита, −1 движение");
        e.ExcludesIds.Add("odin_klinok");
        list.Add(e);

        // Б-32 Тяжёлая рука
        e = New("Б-32", "tyazhyolaya_ruka", "Тяжёлая рука", FeatureLayer.Feature, "Открывают приём", FeatureOwner.Fighter, FeatureKind.OpensTechnique, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Открывает приём П-18 «Сбивающий удар»";
        e.UnlockText = "Рубящее оружие 3, тяжёлое оружие";
        e.Support = "нужно: приёмы в бою";
        e.Dependency = "приёмы в бою";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Крепкая рука (канон), Тяжёлая рука (Eador, Корсары, Caribbean Legend)";
        Rank(e, "", "Открывает приём П-18 «Сбивающий удар»");
        Require(e, FeatureRequirementKind.Competency, "chopping_weapons", 3);
        e.OpensIds.Add("technique_sbivayushchiy_udar");
        list.Add(e);

        // Б-33 Ломает защиту
        e = New("Б-33", "lomaet_zashchitu", "Ломает защиту", FeatureLayer.Feature, "Открывают приём", FeatureOwner.Fighter, FeatureKind.OpensTechnique, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Открывает приём П-19 «Пробой»; после попадания цель −1 Защита до своего хода";
        e.UnlockText = "след «бил защищённых»";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Ломает защиту (Eador), Сбить защиту, Неотразимый удар (Корсары)";
        Rank(e, "", "Открывает приём П-19 «Пробой»; после попадания цель −1 Защита до своего хода");
        e.OpensIds.Add("technique_proboy");
        list.Add(e);

        // Б-34 Танец клинка
        e = New("Б-34", "tanets_klinka", "Танец клинка", FeatureLayer.Feature, "Открывают приём", FeatureOwner.Fighter, FeatureKind.OpensTechnique, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Открывает приём П-20 «Круговой удар»";
        e.UnlockText = "след «бился против нескольких»";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Blade Dancer (Корсары), Round Attack (Eador), Фланкировка";
        Rank(e, "", "Открывает приём П-20 «Круговой удар»");
        e.OpensIds.Add("technique_krugovoy_udar");
        list.Add(e);

        // Б-35 Два быстрых выстрела
        e = New("Б-35", "dva_bystrykh_vystrela", "Два быстрых выстрела", FeatureLayer.Feature, "Открывают приём", FeatureOwner.Fighter, FeatureKind.OpensTechnique, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Открывает приём П-21 «Два выстрела»";
        e.UnlockText = "Стрельба 4";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Double Shot (Eador)";
        Rank(e, "", "Открывает приём П-21 «Два выстрела»");
        Require(e, FeatureRequirementKind.Competency, "shooting", 4);
        e.OpensIds.Add("technique_dva_vystrela");
        list.Add(e);

        // Б-36 Подмечает чужой приём
        e = New("Б-36", "podmechaet_chuzhoy_priyom", "Подмечает чужой приём", FeatureLayer.Feature, "Открывают приём", FeatureOwner.Both, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Увидев незнакомый приём в бою 2+ раза, персонаж может учиться ему у носителя (приём попадает в доступное обучение, бесплатно не выдаётся)";
        e.UnlockText = "—";
        e.Support = "нужно: приёмы и обучение";
        e.Dependency = "приёмы и обучение";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Eagle Eye, Arcane Intuition (Heroes), Ученик (King's Bounty)";
        Rank(e, "", "Увидев незнакомый приём в бою 2+ раза, персонаж может учиться ему у носителя (приём попадает в доступное обучение, бесплатно не выдаётся)");
        list.Add(e);

        // Б-37 Берсерк
        e = New("Б-37", "berserk", "Берсерк", FeatureLayer.Feature, "Двусторонние", FeatureOwner.Fighter, FeatureKind.TwoSided, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Limit = FeatureLimit.Battle;
        e.Description = "1/бой включить на 2 раунда: +2 Урон, но нет защитной стойки и ответного удара";
        e.UnlockText = "след «бился на низком HP»";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Раж, Берсерк (Корсары, WotN)";
        Rank(e, "", "1/бой включить на 2 раунда: +2 Урон, но нет защитной стойки и ответного удара");
        list.Add(e);

        // Б-38 Не умеет отступать
        e = New("Б-38", "ne_umeet_otstupat", "Не умеет отступать", FeatureLayer.Feature, "Двусторонние", FeatureOwner.Both, FeatureKind.TwoSided, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "HP < 1/2: +1 Защита; добровольный отход из боя даёт носителю изнеможение";
        e.UnlockText = "биография";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "×2 в исходнике (KCD, Arkham)";
        Rank(e, "", "HP < 1/2: +1 Защита; добровольный отход из боя даёт носителю изнеможение");
        list.Add(e);

        // Б-39 Тактик
        e = New("Б-39", "taktik", "Тактик", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Перед боем, начатым отрядом или разведанным, Командир расставляет бойцов в расширенной стартовой зоне";
        e.UnlockText = "след «сам выбирал время боя»";
        e.Support = "нужно: свободная расстановка (сейчас позиции фиксированы)";
        e.Dependency = "свободная расстановка (сейчас позиции фиксированы";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Тактика (King's Bounty, все Heroes), Тактическая расстановка";
        Rank(e, "", "Перед боем, начатым отрядом или разведанным, Командир расставляет бойцов в расширенной стартовой зоне");
        e.OpensIds.Add("order_takticheskaya_rasstanovka");
        list.Add(e);

        // Б-40 Выбрать место
        e = New("Б-40", "vybrat_mesto", "Выбрать место", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Limited, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если противник известен заранее — перед боем выбрать один из 2–3 подходов (сторона, стартовая позиция, избежать плохой местности)";
        e.UnlockText = "Следопытство 2";
        e.Support = "нужно: варианты поля";
        e.Dependency = "варианты поля";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Выбрать место, Подготовить место, Зайти с другой стороны (Eador)";
        Rank(e, "", "Если противник известен заранее — перед боем выбрать один из 2–3 подходов (сторона, стартовая позиция, избежать плохой местности)");
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 2);
        e.OpensIds.Add("order_vybrat_mesto");
        list.Add(e);

        // Б-41 Засада
        e = New("Б-41", "zasada", "Засада", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если встреча началась подготовленно (обнаружена заранее) — весь отряд +1 Инициатива в первом раунде. Со Скрытным движением 3: враг в первом раунде не получает бонусов позиции";
        e.UnlockText = "—";
        e.Support = "есть (подготовленное начало уже есть у «Знающего дорогу»)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Засада (Heroes VI), Первый натиск (King's Bounty), Onslaught";
        Rank(e, "", "Если встреча началась подготовленно (обнаружена заранее) — весь отряд +1 Инициатива в первом раунде. Со Скрытным движением 3: враг в первом раунде не получает бонусов позиции");
        list.Add(e);

        // Б-42 Подготовленный строй
        e = New("Б-42", "podgotovlennyy_stroy", "Подготовленный строй", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Permanent, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если противник обнаружен заранее: первая атака по каждому бойцу отряда в бою −1 Атака";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Battle Preparation, Осторожность (King's Bounty)";
        Rank(e, "", "Если противник обнаружен заранее: первая атака по каждому бойцу отряда в бою −1 Атака");
        list.Add(e);

        // Б-43 Диверсант
        e = New("Б-43", "diversant", "Диверсант", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Limited, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "После успешного скрытного подхода к людям до боя: задержать одного противника на первый раунд или подготовить ловушку";
        e.UnlockText = "Скрытное движение 3; след «скрытно подходил к людям»";
        e.Support = "нужно: предбоевой узел";
        e.Dependency = "предбоевой узел";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Diversions (King's Bounty)";
        Rank(e, "", "После успешного скрытного подхода к людям до боя: задержать одного противника на первый раунд или подготовить ловушку");
        Require(e, FeatureRequirementKind.Competency, "stealth", 3);
        list.Add(e);

        // Б-44 Веду примером
        e = New("Б-44", "vedu_primerom", "Веду примером", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Reaction, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После попадания Командира по врагу союзники, атакующие этого врага в том же раунде, получают +1 Атаку. Вариант (Ещё раз): после промаха союзника его следующая атака по той же цели +1 — не дополнительная атака. Бонус только от одного источника за раз";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Leading by Example (Heroes VII), Ещё раз (Arkham), По команде (Eador), Не добивай — держи, Окружить";
        Rank(e, "", "После попадания Командира по врагу союзники, атакующие этого врага в том же раунде, получают +1 Атаку. Вариант (Ещё раз): после промаха союзника его следующая атака по той же цели +1 — не дополнительная атака. Бонус только от одного источника за раз");
        list.Add(e);

        // Б-45 Перестроение
        e = New("Б-45", "perestroenie", "Перестроение", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Limited, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Battle;
        e.Description = "1/бой два соседних союзника меняются местами, не тратя действий";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Перестроение (Eador), Смена мест (Arkham)";
        Rank(e, "", "1/бой два соседних союзника меняются местами, не тратя действий");
        e.OpensIds.Add("order_perestroenie");
        list.Add(e);

        // Б-46 Приказ: сейчас!
        e = New("Б-46", "prikaz_seychas", "Приказ: сейчас!", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.OpensTechnique, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Battle;
        e.Description = "Открывает командирский приём: 1/бой выбранный боец действует сразу после Командира";
        e.UnlockText = "Характер 6";
        e.Support = "доработка: очередь инициативы";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Warlord's Command (Heroes VI/Online)";
        Rank(e, "", "Открывает командирский приём: 1/бой выбранный боец действует сразу после Командира");
        Require(e, FeatureRequirementKind.Quality, "Character", 6);
        e.OpensIds.Add("order_prikaz_seychas");
        list.Add(e);

        // Б-47 Собраться!
        e = New("Б-47", "sobratsya", "Собраться!", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Limited, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Battle;
        e.Description = "1/бой снять с соседнего союзника страх или оцепенение";
        e.UnlockText = "Характер 6";
        e.Support = "нужно: боевые состояния";
        e.Dependency = "боевые состояния";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Oratory (King's Bounty), Не рассыпаться, Красноречие";
        Rank(e, "", "1/бой снять с соседнего союзника страх или оцепенение");
        Require(e, FeatureRequirementKind.Quality, "Character", 6);
        e.OpensIds.Add("order_sobratsya");
        list.Add(e);

        // Б-48 Не бросает своих
        e = New("Б-48", "ne_brosaet_svoikh", "Не бросает своих", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Limited, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "1/поход при отходе из боя один выведенный из строя боец не погибает, а выносится с тяжёлой раной";
        e.UnlockText = "след «прерывал выгоду ради раненых»";
        e.Support = "есть (отход и павшие уже в итоге боя)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Не бросает своих (Eador, Корсары, Arkham), Вернулся за своими (KCD), Прикрыть отход (канон)";
        Rank(e, "", "1/поход при отходе из боя один выведенный из строя боец не погибает, а выносится с тяжёлой раной");
        list.Add(e);

        // Б-49 Воевода
        e = New("Б-49", "voevoda", "Воевода", FeatureLayer.Feature, "Командир: до и во время боя", FeatureOwner.Commander, FeatureKind.Mastery, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Перед боем — дополнительный начальный приказ: один боец начинает в защитной стойке или одна пара меняется местами";
        e.UnlockText = "Тактик + Подготовленный строй + Засада + Щит и строй 4";
        e.Support = "нужно: Тактик";
        e.Dependency = "Тактик";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Dark Side (King's Bounty)";
        Rank(e, "", "Перед боем — дополнительный начальный приказ: один боец начинает в защитной стойке или одна пара меняется местами");
        Require(e, FeatureRequirementKind.Competency, "shield_and_line", 4);
        Require(e, FeatureRequirementKind.Feature, "podgotovlennyy_stroy", 1);
        Require(e, FeatureRequirementKind.Feature, "zasada", 1);
        Require(e, FeatureRequirementKind.Feature, "taktik", 1);
        e.OpensIds.Add("order_takticheskaya_rasstanovka");
        list.Add(e);

        // Б-50 Быстрая перезарядка
        e = New("Б-50", "bystraya_perezaryadka", "Быстрая перезарядка", FeatureLayer.Feature, "Боезапас и последствия боя", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Stub);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Battle;
        e.Description = "I: первая перезарядка в бою не тратит действие. II (Приготовленный выстрел): 1/бой мгновенная перезарядка";
        e.UnlockText = "Стрельба 2";
        e.Support = "нужно: перезарядка (запланирована); до неё — неактивная заглушка";
        e.Dependency = "перезарядка (запланирована";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "Быстрая перезарядка, Приготовленный выстрел (Корсары)";
        Rank(e, "", "Первая перезарядка в бою не тратит действие.");
        Rank(e, "Приготовленный выстрел", "1/бой мгновенная перезарядка");
        Require(e, FeatureRequirementKind.Competency, "shooting", 2);
        list.Add(e);

        // Б-51 Бережёт стрелы
        e = New("Б-51", "berezhyot_strely", "Бережёт стрелы", FeatureLayer.Feature, "Боезапас и последствия боя", FeatureOwner.Fighter, FeatureKind.Permanent, true, FeatureStatus.Stub);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После боя возвращается часть действительно выпущенных стрел; без отдельной рутины подсчёта";
        e.UnlockText = "Стрельба 2";
        e.Support = "нужно: боезапас (запланирован); заглушка";
        e.Dependency = "боезапас (запланирован";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "исходный список";
        Rank(e, "", "После боя возвращается часть действительно выпущенных стрел; без отдельной рутины подсчёта");
        Require(e, FeatureRequirementKind.Competency, "shooting", 2);
        list.Add(e);

        // Б-52 После боя
        e = New("Б-52", "posle_boya", "После боя", FeatureLayer.Feature, "Боезапас и последствия боя", FeatureOwner.Both, FeatureKind.Limited, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После победы — временное боевое преимущество на следующие часы. Слабый вариант: риск снежного кома и награды за повторяемые лёгкие встречи";
        e.UnlockText = "—";
        e.Support = "есть (время идёт)";
        e.Display = "Бой: строка «из чего сложилось» и запись в журнале боя; карточка человека";
        e.MergedFrom = "исходный список (сохранён по правилу §0.1)";
        Rank(e, "", "После победы — временное боевое преимущество на следующие часы. Слабый вариант: риск снежного кома и награды за повторяемые лёгкие встречи");
        list.Add(e);

        // П-01 Смена мест
        e = New("П-01", "technique_smena_mest", "Смена мест", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Practice | FeatureSource.Teacher;
        e.Limit = FeatureLimit.Turn;
        e.Description = "1/ход поменяться гексами с соседним союзником за 1 движение, действие не тратится";
        e.UnlockText = "практика, учитель";
        e.Support = "доработка";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список; ср. Б-45";
        Rank(e, "", "1/ход поменяться гексами с соседним союзником за 1 движение, действие не тратится");
        list.Add(e);

        // П-02 Рывок
        e = New("П-02", "technique_ryvok", "Рывок", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Practice;
        e.Limit = FeatureLimit.Battle;
        e.Description = "1/бой +2 движения на текущий ход, атака остаётся доступна";
        e.UnlockText = "практика";
        e.Support = "есть";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список";
        Rank(e, "", "1/бой +2 движения на текущий ход, атака остаётся доступна");
        list.Add(e);

        // П-03 Выждать
        e = New("П-03", "technique_vyzhdat", "Выждать", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Basic;
        e.Description = "Перенести свой ход на любую позицию инициативы позже в этом раунде";
        e.UnlockText = "базовое";
        e.Support = "доработка";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список";
        Rank(e, "", "Перенести свой ход на любую позицию инициативы позже в этом раунде");
        list.Add(e);

        // П-04 Защитная стойка
        e = New("П-04", "technique_zashchitnaya_stoyka", "Защитная стойка", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Basic;
        e.Description = "Действие: +Защита до следующего хода";
        e.UnlockText = "уже есть в BattleSandbox (процент)";
        e.Support = "есть";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходная «Оборонительная стойка +2» — это наша стойка";
        Rank(e, "", "Действие: +Защита до следующего хода");
        list.Add(e);

        // П-05 Натиск
        e = New("П-05", "technique_natisk", "Натиск", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Practice;
        e.Description = "Если перед ближней атакой боец прошёл ≥2 гекса к цели — атака +1 Урон";
        e.UnlockText = "практика";
        e.Support = "есть";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "Charge (Eador), Разгон, Натиск (King's Bounty)";
        Rank(e, "", "Если перед ближней атакой боец прошёл ≥2 гекса к цели — атака +1 Урон");
        list.Add(e);

        // П-06 Поддержать
        e = New("П-06", "technique_podderzhat", "Поддержать", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Teacher;
        e.Description = "Действие: соседний союзник +1 Защита до своего хода";
        e.UnlockText = "учитель";
        e.Support = "есть";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "Прикрой его (Arkham)";
        Rank(e, "", "Действие: соседний союзник +1 Защита до своего хода");
        list.Add(e);

        // П-07 Навести удар
        e = New("П-07", "technique_navesti_udar", "Навести удар", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Teacher;
        e.Description = "Действие: соседний союзник +1 к следующей атаке";
        e.UnlockText = "учитель";
        e.Support = "есть";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список";
        Rank(e, "", "Действие: соседний союзник +1 к следующей атаке");
        list.Add(e);

        // П-08 Толчок
        e = New("П-08", "technique_tolchok", "Толчок", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Practice;
        e.Description = "Атака без урона: цель отталкивается на 1 гекс, если место свободно";
        e.UnlockText = "практика";
        e.Support = "доработка";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список";
        Rank(e, "", "Атака без урона: цель отталкивается на 1 гекс, если место свободно");
        list.Add(e);

        // П-09 Сберечь силы
        e = New("П-09", "technique_sberech_sily", "Сберечь силы", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Basic;
        e.Description = "Если ход закончен с ≥1 движения — следующий ход +1 движение (максимум +1)";
        e.UnlockText = "базовое";
        e.Support = "есть";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список";
        Rank(e, "", "Если ход закончен с ≥1 движения — следующий ход +1 движение (максимум +1)");
        list.Add(e);

        // П-10 Встреча копьём
        e = New("П-10", "technique_vstrecha_kopyom", "Встреча копьём", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.Teacher;
        e.Description = "Копьём ударить врага, вошедшего в соседний гекс, до его атаки";
        e.UnlockText = "учитель (пример: Остафий)";
        e.Support = "нужно: реакция";
        e.Dependency = "реакция";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "ср. Б-15";
        Rank(e, "", "Копьём ударить врага, вошедшего в соседний гекс, до его атаки");
        list.Add(e);

        // П-11 Подставить щит
        e = New("П-11", "technique_podstavit_shchit", "Подставить щит", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.Teacher;
        e.Description = "Принять удар по соседу на свой щит";
        e.UnlockText = "учитель (ветеран дружины)";
        e.Support = "нужно: реакция";
        e.Dependency = "реакция";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "ср. Б-03";
        Rank(e, "", "Принять удар по соседу на свой щит");
        list.Add(e);

        // П-12 Выстрел навстречу
        e = New("П-12", "technique_vystrel_navstrechu", "Выстрел навстречу", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.Teacher;
        e.Limit = FeatureLimit.Battle;
        e.Description = "Выстрел по сближающемуся врагу вне своей очереди, 1/бой";
        e.UnlockText = "учитель (охотник)";
        e.Support = "нужно: реакция";
        e.Dependency = "реакция";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходник";
        Rank(e, "", "Выстрел по сближающемуся врагу вне своей очереди, 1/бой");
        list.Add(e);

        // П-13 Особый удар топором
        e = New("П-13", "technique_osobyy_udar_toporom", "Особый удар топором", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.Teacher;
        e.Description = "Авторский приём чужого воина";
        e.UnlockText = "учитель";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходник (идея); ещё одна идея исходника — серия ударов тяжёлым оружием, эффект не задан";
        Rank(e, "", "Авторский приём чужого воина");
        list.Add(e);

        // П-14 Отступить с боем
        e = New("П-14", "technique_otstupit_s_boem", "Отступить с боем", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.Practice;
        e.Description = "Активно: после своей атаки отойти на 1 гекс за оставшееся движение";
        e.UnlockText = "практика";
        e.Support = "есть (сейчас атака обнуляет движение — нужно правило)";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список; пассивная версия — Б-22, по чистовому каталогу это разные записи";
        Rank(e, "", "Активно: после своей атаки отойти на 1 гекс за оставшееся движение");
        list.Add(e);

        // П-15 Уверенный удар
        e = New("П-15", "technique_uverennyy_udar", "Уверенный удар", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Practice;
        e.Description = "Ближняя атака без перемещения в этот ход +1 Атака";
        e.UnlockText = "практика";
        e.Support = "есть";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "исходный список; граница с особенностью — вопрос 6";
        Rank(e, "", "Ближняя атака без перемещения в этот ход +1 Атака");
        list.Add(e);

        // П-16 Прицелиться
        e = New("П-16", "technique_pritselitsya", "Прицелиться", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Basic;
        e.Description = "Действие стрелка: следующий выстрел по выбранной цели точнее; нужен для II ступени Б-18 «Ищет щель»";
        e.UnlockText = "базовое";
        e.Support = "доработка";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "чистовой каталог";
        Rank(e, "", "Действие стрелка: следующий выстрел по выбранной цели точнее; нужен для II ступени Б-18 «Ищет щель»");
        list.Add(e);

        // П-17 Парирование (активное)
        e = New("П-17", "technique_parirovanie", "Парирование (активное)", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.Teacher;
        e.Limit = FeatureLimit.Round;
        e.Description = "Реакция: 1/раунд отвести ближний удар вместо ответного удара";
        e.UnlockText = "учитель";
        e.Support = "нужно: реакция";
        e.Dependency = "реакция";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "чистовой каталог относит Парирование к приёмам; пассивный вариант — Б-14";
        Rank(e, "", "Реакция: 1/раунд отвести ближний удар вместо ответного удара");
        list.Add(e);

        // П-18 Сбивающий удар
        e = New("П-18", "technique_sbivayushchiy_udar", "Сбивающий удар", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Description = "При попадании цель отталкивается на 1 гекс или теряет 2 Инициативы вместо доп. урона";
        e.UnlockText = "Б-32";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "Оглушающий удар (Eador)";
        Rank(e, "", "При попадании цель отталкивается на 1 гекс или теряет 2 Инициативы вместо доп. урона");
        list.Add(e);

        // П-19 Пробой
        e = New("П-19", "technique_proboy", "Пробой", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Limit = FeatureLimit.Battle;
        e.Description = "1/бой удар игнорирует бонус щита и стойки цели";
        e.UnlockText = "Б-33";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "Armorpiercing (Eador)";
        Rank(e, "", "1/бой удар игнорирует бонус щита и стойки цели");
        list.Add(e);

        // П-20 Круговой удар
        e = New("П-20", "technique_krugovoy_udar", "Круговой удар", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Limit = FeatureLimit.Turn;
        e.Description = "После попадания — удар половинным уроном по второй соседней цели, 1/ход";
        e.UnlockText = "Б-34";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "Round Attack";
        Rank(e, "", "После попадания — удар половинным уроном по второй соседней цели, 1/ход");
        list.Add(e);

        // П-21 Два выстрела
        e = New("П-21", "technique_dva_vystrela", "Два выстрела", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Limit = FeatureLimit.Battle;
        e.Description = "1/бой два выстрела за действие, каждый −1 Урон";
        e.UnlockText = "Б-35";
        e.Support = "нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "Double Shot";
        Rank(e, "", "1/бой два выстрела за действие, каждый −1 Урон");
        list.Add(e);

        // П-22 Подсечка / Остановить
        e = New("П-22", "technique_podsechka", "Подсечка / Остановить", FeatureLayer.Technique, "Приёмы", FeatureOwner.Both, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.Practice;
        e.Description = "Атака с уроном −1: цель −1 движение на следующий ход (вблизи — Подсечка, издалека — Остановить)";
        e.UnlockText = "практика";
        e.Support = "доработка";
        e.Display = "Экран боя: действие бойца; в карточке человека — чем открыто";
        e.MergedFrom = "Корсары";
        Rank(e, "", "Атака с уроном −1: цель −1 движение на следующий ход (вблизи — Подсечка, издалека — Остановить)");
        list.Add(e);

        // ПК-01 Перестроение
        e = New("ПК-01", "order_perestroenie", "Перестроение", FeatureLayer.Order, "Приказы Командира", FeatureOwner.Commander, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Description = "Два соседних союзника меняются местами, не тратя действий (из трёх триггеров семейства — Перестроение, Не стой, Смена мест — выбрать один базовый)";
        e.Support = "есть";
        e.Display = "Где отдаётся: экран боя";
        Rank(e, "", "Два соседних союзника меняются местами, не тратя действий (из трёх триггеров семейства — Перестроение, Не стой, Смена мест — выбрать один базовый)");
        list.Add(e);

        // ПК-02 Приказ: сейчас!
        e = New("ПК-02", "order_prikaz_seychas", "Приказ: сейчас!", FeatureLayer.Order, "Приказы Командира", FeatureOwner.Commander, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Description = "Выбранный боец действует сразу после Командира";
        e.Support = "доработка: очередь инициативы";
        e.Display = "Где отдаётся: экран боя";
        Rank(e, "", "Выбранный боец действует сразу после Командира");
        list.Add(e);

        // ПК-03 Собраться!
        e = New("ПК-03", "order_sobratsya", "Собраться!", FeatureLayer.Order, "Приказы Командира", FeatureOwner.Commander, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Description = "Снять с соседнего союзника страх или оцепенение";
        e.Support = "нужно: боевые состояния";
        e.Dependency = "боевые состояния";
        e.Display = "Где отдаётся: экран боя";
        Rank(e, "", "Снять с соседнего союзника страх или оцепенение");
        list.Add(e);

        // ПК-04 Выбрать место
        e = New("ПК-04", "order_vybrat_mesto", "Выбрать место", FeatureLayer.Order, "Приказы Командира", FeatureOwner.Commander, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Description = "Выбор одного из 2–3 подходов к известному противнику";
        e.Support = "нужно: варианты поля";
        e.Dependency = "варианты поля";
        e.Display = "Где отдаётся: разведка — экран диалога, расстановка — экран боя";
        Rank(e, "", "Выбор одного из 2–3 подходов к известному противнику");
        list.Add(e);

        // ПК-05 Тактическая расстановка
        e = New("ПК-05", "order_takticheskaya_rasstanovka", "Тактическая расстановка", FeatureLayer.Order, "Приказы Командира", FeatureOwner.Commander, FeatureKind.Action, true, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Description = "Расстановка бойцов в расширенной стартовой зоне";
        e.Support = "нужно: свободная расстановка";
        e.Dependency = "свободная расстановка";
        e.Display = "Где отдаётся: экран боя";
        Rank(e, "", "Расстановка бойцов в расширенной стартовой зоне");
        list.Add(e);

        // ПК-06 Форсированный марш
        e = New("ПК-06", "order_forsirovannyy_marsh", "Форсированный марш", FeatureLayer.Order, "Приказы Командира", FeatureOwner.Commander, FeatureKind.Action, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Action;
        e.Sources = FeatureSource.OpenedByFeature;
        e.Description = "Ускорить участок пути ценой изнеможения или Припасов; цена видна до выбора";
        e.Support = "есть";
        e.Display = "Где отдаётся: карта";
        Rank(e, "", "Ускорить участок пути ценой изнеможения или Припасов; цена видна до выбора");
        list.Add(e);

        // Н-01 Счастливчик
        e = New("Н-01", "schastlivchik", "Счастливчик", FeatureLayer.Feature, "Проверки и риск", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "1/поход переброс одного d6 любой активной проверки";
        e.UnlockText = "нейтральная";
        e.Support = "есть (2d6 детерминированы)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Luck (Heroes), Вторая попытка (Arkham, была 1/встречу)";
        Rank(e, "", "1/поход переброс одного d6 любой активной проверки");
        e.Note = "Решение автора 27.09.2026: переброс 1/поход. Срабатывает сам при провале активной проверки.";
        list.Add(e);

        // Н-02 Набитая рука
        e = New("Н-02", "nabitaya_ruka", "Набитая рука", FeatureLayer.Feature, "Проверки и риск", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Scene;
        e.Description = "1/сцену при проверке этой компетенции — переброс";
        e.UnlockText = "компетенция 4+";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Набитая рука, Спокойствие мастера";
        Rank(e, "", "1/сцену при проверке этой компетенции — переброс");
        list.Add(e);

        // Н-03 Почти получилось
        e = New("Н-03", "pochti_poluchilos", "Почти получилось", FeatureLayer.Feature, "Проверки и риск", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Провал на 1–2: следующая проверка в этой сцене получает переброс";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Почти получилось, Собрался после ошибки, На ошибках учатся (Arkham)";
        Rank(e, "", "Провал на 1–2: следующая проверка в этой сцене получает переброс");
        e.Note = "Решение автора 27.09.2026: цена первой ошибки остаётся; переброс — следующей проверке в этой сцене.";
        list.Add(e);

        // Н-04 Другой подход
        e = New("Н-04", "drugoy_podkhod", "Другой подход", FeatureLayer.Feature, "Проверки и риск", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После провала профильной проверки 1 раз сменить компетенцию и способ, если сцена допускает второй путь; первый провал и его цена остаются";
        e.UnlockText = "—";
        e.Support = "доработка: альтернативная проверка в узле";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Другой подход (Fallout), Свой способ, Не сходится, Недоверчивый-2 (Arkham)";
        Rank(e, "", "После провала профильной проверки 1 раз сменить компетенцию и способ, если сцена допускает второй путь; первый провал и его цена остаются");
        list.Add(e);

        // Н-05 На пределе
        e = New("Н-05", "na_predele", "На пределе", FeatureLayer.Feature, "Проверки и риск", FeatureOwner.Both, FeatureKind.TwoSided, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "1/поход: потерять 2 HP → +2 к результату проверки";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Arkham";
        Rank(e, "", "1/поход: потерять 2 HP → +2 к результату проверки");
        list.Add(e);

        // Н-06 Всё или ничего
        e = New("Н-06", "vsyo_ili_nichego", "Всё или ничего", FeatureLayer.Feature, "Проверки и риск", FeatureOwner.Both, FeatureKind.TwoSided, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "До броска объявить риск: +2; при провале последствие на ступень хуже";
        e.UnlockText = "—";
        e.Support = "нужно: ступени провала";
        e.Dependency = "ступени провала";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Arkham";
        Rank(e, "", "До броска объявить риск: +2; при провале последствие на ступень хуже");
        list.Add(e);

        // Н-07 Вторая версия
        e = New("Н-07", "vtoraya_versiya", "Вторая версия", FeatureLayer.Feature, "Проверки и риск", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Провал возвратной проверки не закрывает её навсегда: после нового факта можно вернуться";
        e.UnlockText = "Расследование или Следопытство 2";
        e.Support = "есть (возвратные проверки + разблокировка)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Вторая версия (Fallout), По следу ошибки (Arkham)";
        Rank(e, "", "Провал возвратной проверки не закрывает её навсегда: после нового факта можно вернуться");
        Require(e, FeatureRequirementKind.Competency, "investigation", 2);
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 2);
        e.RequirementsAnyOf = true;
        list.Add(e);

        // Н-08 Первый взгляд
        e = New("Н-08", "pervyy_vzglyad", "Первый взгляд", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первая Наблюдательность после входа в новую локацию — переброс. Вариант (Приметливый, канон §25.4): +1 к пассивной Наблюдательности — проще, не требует новых реплик";
        e.UnlockText = "Наблюдательность 1";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Первый взгляд, Приметливый";
        Rank(e, "", "Первая Наблюдательность после входа в новую локацию — переброс. Вариант (Приметливый, канон §25.4): +1 к пассивной Наблюдательности — проще, не требует новых реплик");
        Require(e, FeatureRequirementKind.Competency, "observation", 1);
        list.Add(e);

        // Н-09 Зацепка
        e = New("Н-09", "zatsepka", "Зацепка", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После успешной Наблюдательности следующее Расследование в той же сцене +1";
        e.UnlockText = "Наблюдательность 2";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "По следу причины (KCD), Зацепка (Arkham), Память на детали";
        Rank(e, "", "После успешной Наблюдательности следующее Расследование в той же сцене +1");
        Require(e, FeatureRequirementKind.Competency, "observation", 2);
        e.Note = "Решение автора 27.09.2026: +1 к следующему Расследованию в той же сцене.";
        list.Add(e);

        // Н-10 След не врёт
        e = New("Н-10", "sled_ne_vryot", "След не врёт", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После успешного Следопытства первая Наблюдательность по найденному следу +1";
        e.UnlockText = "Следопытство 2";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "KCD";
        Rank(e, "", "После успешного Следопытства первая Наблюдательность по найденному следу +1");
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 2);
        list.Add(e);

        // Н-11 Связать факты
        e = New("Н-11", "svyazat_fakty", "Связать факты", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После успешного Расследования — переброс следующей Проницательности по той же истории";
        e.UnlockText = "Расследование 2";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Arkham";
        Rank(e, "", "После успешного Расследования — переброс следующей Проницательности по той же истории");
        Require(e, FeatureRequirementKind.Competency, "investigation", 2);
        list.Add(e);

        // Н-12 Слово за слово
        e = New("Н-12", "slovo_za_slovo", "Слово за слово", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Commander, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После успешной Проницательности — переброс следующих Переговоров с тем же человеком, и наоборот. Вариант (Уверенный голос): первая проверка Переговоров в разговоре +1; причинный триггер предпочтительнее";
        e.UnlockText = "Проницательность 2";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Слово за слово, Умею слушать (Arkham), Уверенный голос";
        Rank(e, "", "После успешной Проницательности — переброс следующих Переговоров с тем же человеком, и наоборот. Вариант (Уверенный голос): первая проверка Переговоров в разговоре +1; причинный триггер предпочтительнее");
        Require(e, FeatureRequirementKind.Competency, "insight", 2);
        list.Add(e);

        // Н-13 Знаю, что искать
        e = New("Н-13", "znayu_chto_iskat", "Знаю, что искать", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если есть проверенное (не слух) сведение о месте или вопросе, первая профильная проверка там — переброс";
        e.UnlockText = "—";
        e.Support = "нет: сведения в игре — только ID, без степени уверенности и привязки к месту";
        e.Dependency = "сведения со степенью уверенности (слух / проверено) и привязкой к месту или вопросу";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Знаю, что искать, Проверяй дважды (Arkham)";
        Rank(e, "", "Если есть проверенное (не слух) сведение о месте или вопросе, первая профильная проверка там — переброс");
        list.Add(e);

        // Н-14 Знающий старые обычаи
        e = New("Н-14", "znayushchiy_starye_obychai", "Знающий старые обычаи", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если есть знание о народе, месте или явлении: +1 к Преданиям и Обрядовому знанию по той же теме; нового знания не даёт";
        e.UnlockText = "Предания 2";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "канон §25.4 (рабочий банк)";
        Rank(e, "", "Если есть знание о народе, месте или явлении: +1 к Преданиям и Обрядовому знанию по той же теме; нового знания не даёт");
        Require(e, FeatureRequirementKind.Competency, "lore", 2);
        list.Add(e);

        // Н-15 Старая память
        e = New("Н-15", "staraya_pamyat", "Старая память", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После успешных Преданий первое Обрядовое знание по той же сущности или месту +1 (переброс, если герой видел объект сам)";
        e.UnlockText = "Предания 2";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Старая память (KCD), Практик, не книжник (Arkham)";
        Rank(e, "", "После успешных Преданий первое Обрядовое знание по той же сущности или месту +1 (переброс, если герой видел объект сам)");
        Require(e, FeatureRequirementKind.Competency, "lore", 2);
        list.Add(e);

        // Н-16 Запомнил несоответствие
        e = New("Н-16", "zapomnil_nesootvetstvie", "Запомнил несоответствие", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: если реплика противоречит известному факту Хроники или сведений — отмечается, какой факт не сходится (не кто лжёт). II: появляется вариант указать на противоречие";
        e.UnlockText = "Проницательность 3";
        e.Support = "доработка: тег факта у реплики";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Запомнил несоответствие (Fallout), Дожимает противоречие (Eador), Помнит сказанное (канон)";
        Rank(e, "", "Если реплика противоречит известному факту Хроники или сведений — отмечается, какой факт не сходится (не кто лжёт).");
        Rank(e, "", "Появляется вариант указать на противоречие");
        Require(e, FeatureRequirementKind.Competency, "insight", 3);
        list.Add(e);

        // Н-17 След не пропадёт
        e = New("Н-17", "sled_ne_propadyot", "След не пропадёт", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "При уходе из локации сообщается, что там остался замеченный, но неосмотренный значимый след или тайник; сам не раскрывается";
        e.UnlockText = "составная: Наблюдательность 3 + Расследование 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "След не пропадёт (Fallout), Следователь (Heroes); ср. Н-116";
        Rank(e, "", "При уходе из локации сообщается, что там остался замеченный, но неосмотренный значимый след или тайник; сам не раскрывается");
        Require(e, FeatureRequirementKind.Competency, "observation", 3);
        Require(e, FeatureRequirementKind.Competency, "investigation", 2);
        list.Add(e);

        // Н-109 Глаз промысловика (лес / поле / вода / скот)
        e = New("Н-109", "glaz_promyslovika", "Глаз промысловика (лес / поле / вода / скот)", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В своей среде профильная компетенция (Лесное, Земельное, Рыбацкое, Скотное дело) сразу отличает природное нарушение от следа вмешательства человека или сущности; если причина физическая, может заменить Расследование";
        e.UnlockText = "выбирается для одной среды; профильная компетенция 3";
        e.Support = "доработка: тег среды";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Лес говорит, Человек земли (KCD); канон §27.11.4";
        Rank(e, "", "В своей среде профильная компетенция (Лесное, Земельное, Рыбацкое, Скотное дело) сразу отличает природное нарушение от следа вмешательства человека или сущности; если причина физическая, может заменить Расследование");
        list.Add(e);

        // Н-18 Причина смерти
        e = New("Н-18", "prichina_smerti", "Причина смерти", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "При осмотре тела видна очевидная причина: оружие / зверь / вода / болезнь / неизвестно";
        e.UnlockText = "Лечение или Расследование 2";
        e.Support = "доработка: тег сцены";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Living Anatomy (Fallout)";
        Rank(e, "", "При осмотре тела видна очевидная причина: оружие / зверь / вода / болезнь / неизвестно");
        Require(e, FeatureRequirementKind.Competency, "investigation", 2);
        Require(e, FeatureRequirementKind.Competency, "healing", 2);
        e.RequirementsAnyOf = true;
        list.Add(e);

        // Н-19 Лечу по признакам
        e = New("Н-19", "lechu_po_priznakam", "Лечу по признакам", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: у больного или отравленного виден класс причины (растение / пища / укус / заражение / неизвестно). II (Знает тело): после успешного Лечения видны точная тяжесть раны и срок восстановления";
        e.UnlockText = "Лечение 2 / Травничество 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Лечу по признакам, Знает тело (KCD), Living Anatomy";
        Rank(e, "", "У больного или отравленного виден класс причины (растение / пища / укус / заражение / неизвестно).");
        Rank(e, "Знает тело", "После успешного Лечения видны точная тяжесть раны и срок восстановления");
        Require(e, FeatureRequirementKind.Competency, "herbalism", 2);
        Require(e, FeatureRequirementKind.Competency, "healing", 2);
        e.RequirementsAnyOf = true;
        list.Add(e);

        // Н-20 Опытный глаз
        e = New("Н-20", "opytnyy_glaz", "Опытный глаз", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "При осмотре человека или существа словами: состояние, тип защиты, чем сейчас опасен";
        e.UnlockText = "Наблюдательность 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Awareness, Living Anatomy (Fallout)";
        Rank(e, "", "При осмотре человека или существа словами: состояние, тип защиты, чем сейчас опасен");
        Require(e, FeatureRequirementKind.Competency, "observation", 2);
        list.Add(e);

        // Н-21 Знает слабость
        e = New("Н-21", "znaet_slabost", "Знает слабость", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если слабость существа установлена, в бою открывается действие против неё";
        e.UnlockText = "знание о существе";
        e.Support = "нужно: знания о существах в бою";
        e.Dependency = "знания о существах в бою";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Knowledge of Weakness (King's Bounty)";
        Rank(e, "", "Если слабость существа установлена, в бою открывается действие против неё");
        list.Add(e);

        // Н-22 Книжник / Подмечает метод
        e = New("Н-22", "knizhnik", "Книжник / Подмечает метод", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Содержательный трактат или обучение выполняет условие «новое знание» для роста компетенции выше потолка практики; выученный метод сохраняется";
        e.UnlockText = "—";
        e.Support = "есть (наставник/потолок уже в коде)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Bookworm (King's Bounty), Подмечает метод; «Профессиональная память» без исключения не нужна — знания и так сохраняются у всех";
        Rank(e, "", "Содержательный трактат или обучение выполняет условие «новое знание» для роста компетенции выше потолка практики; выученный метод сохраняется");
        list.Add(e);

        // Н-23 Быстро схватывает
        e = New("Н-23", "bystro_skhvatyvaet", "Быстро схватывает", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первое применение компетенции в новом содержательном контексте за поход: +1 практики";
        e.UnlockText = "нейтральная";
        e.Support = "есть (антифарм по ID источника)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Learning, Enlightenment (Heroes)";
        Rank(e, "", "Первое применение компетенции в новом содержательном контексте за поход: +1 практики");
        list.Add(e);

        // Н-24 Чужая речь
        e = New("Н-24", "chuzhaya_rech", "Чужая речь", FeatureLayer.Feature, "Наблюдение, знания, расследование", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Опознаёт знакомый говор или письменность, открывает часть смысла чужих надписей";
        e.UnlockText = "Предания 3";
        e.Support = "нужно: говоры и письмена";
        e.Dependency = "говоры и письмена";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Linguistics (King's Bounty)";
        Rank(e, "", "Опознаёт знакомый говор или письменность, открывает часть смысла чужих надписей");
        Require(e, FeatureRequirementKind.Competency, "lore", 3);
        list.Add(e);

        // Н-25 Читаю людей
        e = New("Н-25", "chitayu_lyudey", "Читаю людей", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: перед социальной проверкой видна её сложность словами (лёгкая / обычная / трудная / почти невозможная). II: у реплик видна ожидаемая немедленная реакция (расположит / заденет / нейтрально), без последствий";
        e.UnlockText = "Проницательность 2";
        e.Support = "есть (сложность известна) / доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Empathetic (KCD), Empathy (Fallout)";
        Rank(e, "", "Перед социальной проверкой видна её сложность словами (лёгкая / обычная / трудная / почти невозможная).");
        Rank(e, "", "У реплик видна ожидаемая немедленная реакция (расположит / заденет / нейтрально), без последствий");
        Require(e, FeatureRequirementKind.Competency, "insight", 2);
        list.Add(e);

        // Н-26 Слово прежде железа
        e = New("Н-26", "slovo_prezhde_zheleza", "Слово прежде железа", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "I: перед боем с людьми, если отряд не застигнут врасплох, — одна проверка Переговоров; успех отменяет немедленный бой и открывает разговор. II: +2 к этой проверке и к первым Переговорам, пока отряд не начал насилие";
        e.UnlockText = "Переговоры 2; след «решал переговорами»";
        e.Support = "нужно: предбоевой узел";
        e.Dependency = "предбоевой узел";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "канон §25.4, Не первый клинок (KCD2): II можно дать отдельной карточкой; считаются действия отряда в этой встрече, не глобальная шкала миролюбия";
        Rank(e, "", "Перед боем с людьми, если отряд не застигнут врасплох, — одна проверка Переговоров; успех отменяет немедленный бой и открывает разговор.");
        Rank(e, "", "+2 к этой проверке и к первым Переговорам, пока отряд не начал насилие");
        Require(e, FeatureRequirementKind.Competency, "negotiation", 2);
        list.Add(e);

        // Н-27 Грозное присутствие
        e = New("Н-27", "groznoe_prisutstvie", "Грозное присутствие", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag | FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: в разговоре с тегом «можно запугать» появляется [Пригрозить] — уступка ценой отношения, может оставить страх или месть в Хронике. II: перед боем с людьми успешная угроза снижает готовность части врагов драться";
        e.UnlockText = "Характер 6 или Сила 7";
        e.Support = "доработка: тег; II — нужно: готовность к бою";
        e.Dependency = "готовность к бою";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Terrifying Presence (Fallout NV), Грозный голос / Dreaded Warrior (KCD)";
        Rank(e, "", "В разговоре с тегом «можно запугать» появляется [Пригрозить] — уступка ценой отношения, может оставить страх или месть в Хронике.");
        Rank(e, "", "Перед боем с людьми успешная угроза снижает готовность части врагов драться");
        Require(e, FeatureRequirementKind.Quality, "Character", 6);
        Require(e, FeatureRequirementKind.Quality, "Strength", 7);
        e.RequirementsAnyOf = true;
        list.Add(e);

        // Н-28 Последнее слово
        e = New("Н-28", "poslednee_slovo", "Последнее слово", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Conversation;
        e.Description = "I: 1/разговор провал Переговоров, который закрыл бы разговор или сделку либо сделал бы собеседника враждебным, даёт одну повторную попытку; второй провал окончателен. II: при провале на 1 можно принять цену, предложенную сценой, и считать проверку успешной";
        e.UnlockText = "Переговоры 3";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Bluff Master (Fallout Tactics), Final Offer (KCD), Последнее слово (Arkham)";
        Rank(e, "", "1/разговор провал Переговоров, который закрыл бы разговор или сделку либо сделал бы собеседника враждебным, даёт одну повторную попытку; второй провал окончателен.");
        Rank(e, "", "При провале на 1 можно принять цену, предложенную сценой, и считать проверку успешной");
        Require(e, FeatureRequirementKind.Competency, "negotiation", 3);
        list.Add(e);

        // Н-29 Знаю, когда замолчать
        e = New("Н-29", "znayu_kogda_zamolchat", "Знаю, когда замолчать", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первый провал Переговоров в разговоре не ухудшает отношение; вариант закрывается, но к разговору можно вернуться позже";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "KCD, Не дави (Arkham)";
        Rank(e, "", "Первый провал Переговоров в разговоре не ухудшает отношение; вариант закрывается, но к разговору можно вернуться позже");
        list.Add(e);

        // Н-30 Слушает до конца
        e = New("Н-30", "slushaet_do_kontsa", "Слушает до конца", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После провала первой попытки разговора герой узнаёт, почему ему отказали; это может открыть другой подход";
        e.UnlockText = "Проницательность 2";
        e.Support = "доработка: причина отказа в узле";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Слушает до конца (Eador), Дать договорить (Arkham)";
        Rank(e, "", "После провала первой попытки разговора герой узнаёт, почему ему отказали; это может открыть другой подход");
        Require(e, FeatureRequirementKind.Competency, "insight", 2);
        list.Add(e);

        // Н-31 Говорить по делу
        e = New("Н-31", "govorit_po_delu", "Говорить по делу", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если разговор о деле, в котором герой компетентен, проверку Переговоров можно заменить профильной компетенцией: Лесное дело с лесорубом, Рыбацкое с рыбаком, Обычай и право в человеческом споре";
        e.UnlockText = "профильная компетенция 3";
        e.Support = "доработка: тег темы";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Говорить по делу (Fallout), Законник (KCD)";
        Rank(e, "", "Если разговор о деле, в котором герой компетентен, проверку Переговоров можно заменить профильной компетенцией: Лесное дело с лесорубом, Рыбацкое с рыбаком, Обычай и право в человеческом споре");
        list.Add(e);

        // Н-32 Свой среди работных
        e = New("Н-32", "svoy_sredi_rabotnykh", "Свой среди работных", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag | FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Biography;
        e.Description = "С ремесленником, земледельцем, промысловиком Переговоры считаются на ступень выше";
        e.UnlockText = "биография или выбор";
        e.Support = "нужно: социальная категория NPC";
        e.Dependency = "социальная категория NPC";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Lowborn (KCD1)";
        Rank(e, "", "С ремесленником, земледельцем, промысловиком Переговоры считаются на ступень выше");
        e.ExcludesIds.Add("slovo_starshego");
        list.Add(e);

        // Н-33 Слово старшего
        e = New("Н-33", "slovo_starshego", "Слово старшего", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Biography;
        e.Description = "Со старостой, главой семьи, старейшиной, властью Переговоры на ступень выше";
        e.UnlockText = "биография или выбор";
        e.Support = "нужно: то же";
        e.Dependency = "то же";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Highborn (KCD1)";
        Rank(e, "", "Со старостой, главой семьи, старейшиной, властью Переговоры на ступень выше");
        e.ExcludesIds.Add("svoy_sredi_rabotnykh");
        list.Add(e);

        // Н-34 Дать слово
        e = New("Н-34", "dat_slovo", "Дать слово", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Option, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "В сделке, где требуют гарантию, герой предлагает обязательство, услугу или отсрочку вместо денег или залога, если это позволяет репутация; нарушение попадает в Хронику";
        e.UnlockText = "след «держал обещания»";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Дать слово (Fallout), Умеет договориться, Знает цену обещанию (Eador)";
        Rank(e, "", "В сделке, где требуют гарантию, герой предлагает обязательство, услугу или отсрочку вместо денег или залога, если это позволяет репутация; нарушение попадает в Хронику");
        list.Add(e);

        // Н-35 Оставляет человеку выход
        e = New("Н-35", "ostavlyaet_cheloveku_vykhod", "Оставляет человеку выход", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Option, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Authored;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "После раскрытия чужого проступка — вариант добиться уступки без публичного унижения";
        e.UnlockText = "след «искал компромисс»";
        e.Support = "авторская";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Eador";
        Rank(e, "", "После раскрытия чужого проступка — вариант добиться уступки без публичного унижения");
        list.Add(e);

        // Н-36 Говорит через посредника
        e = New("Н-36", "govorit_cherez_posrednika", "Говорит через посредника", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Option, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "При плохом отношении NPC к герою разговор ведёт присутствующий подходящий спутник: берётся его ступень компетенции, последствия ложатся на него";
        e.UnlockText = "—";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Говорит через посредника (Eador), Чужими словами (Arkham)";
        Rank(e, "", "При плохом отношении NPC к герою разговор ведёт присутствующий подходящий спутник: берётся его ступень компетенции, последствия ложатся на него");
        list.Add(e);

        // Н-37 Репутация впереди
        e = New("Н-37", "reputatsiya_vperedi", "Репутация впереди", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если NPC достоверно знает о поступке героя в его интересах, первая подходящая социальная проверка — переброс";
        e.UnlockText = "—";
        e.Support = "доработка (канон §26.2)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Arkham";
        Rank(e, "", "Если NPC достоверно знает о поступке героя в его интересах, первая подходящая социальная проверка — переброс");
        list.Add(e);

        // Н-38 Знаю обычай
        e = New("Н-38", "znayu_obychay", "Знаю обычай", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После успешной проверки Обычая и права штраф чужака в сцене не действует";
        e.UnlockText = "Обычай и право 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Знаю обычай (Arkham), Свой среди чужих, Tolerance (King's Bounty)";
        Rank(e, "", "После успешной проверки Обычая и права штраф чужака в сцене не действует");
        Require(e, FeatureRequirementKind.Competency, "custom_and_law", 2);
        list.Add(e);

        // Н-39 Знает чужой порядок
        e = New("Н-39", "znaet_chuzhoy_poryadok", "Знает чужой порядок", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Option, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "При нужной одежде, знании или легенде можно выдать себя за человека соответствующей среды (проверка Обычая и права, без гарантии)";
        e.UnlockText = "Обычай и право 3";
        e.Support = "нужно: маскировка в сценах";
        e.Dependency = "маскировка в сценах";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "флаги наций (Корсары)";
        Rank(e, "", "При нужной одежде, знании или легенде можно выдать себя за человека соответствующей среды (проверка Обычая и права, без гарантии)");
        Require(e, FeatureRequirementKind.Competency, "custom_and_law", 3);
        list.Add(e);

        // Н-40 Ладит с детьми
        e = New("Н-40", "ladit_s_detmi", "Ладит с детьми", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Простые вопросы ребёнку — без Переговоров; появляются детские варианты разговора";
        e.UnlockText = "—";
        e.Support = "доработка: тег «ребёнок»";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Child at Heart (Fallout 3)";
        Rank(e, "", "Простые вопросы ребёнку — без Переговоров; появляются детские варианты разговора");
        list.Add(e);

        // Н-41 Чужое намерение
        e = New("Н-41", "chuzhoe_namerenie", "Чужое намерение", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "До сближения видно состояние встречной группы: ищут встречи / избегают / готовы говорить / готовятся напасть / непонятно";
        e.UnlockText = "Проницательность 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Sagacity (Olden Era)";
        Rank(e, "", "До сближения видно состояние встречной группы: ищут встречи / избегают / готовы говорить / готовятся напасть / непонятно");
        Require(e, FeatureRequirementKind.Competency, "insight", 2);
        list.Add(e);

        // Н-42 Расколоть отряд
        e = New("Н-42", "raskolot_otryad", "Расколоть отряд", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "У колеблющейся человеческой группы до боя — попытка расколоть её (часть людей не вступает в бой)";
        e.UnlockText = "Переговоры 3";
        e.Support = "нужно: готовность к бою";
        e.Dependency = "готовность к бою";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Дипломатия (King's Bounty, Eador)";
        Rank(e, "", "У колеблющейся человеческой группы до боя — попытка расколоть её (часть людей не вступает в бой)");
        Require(e, FeatureRequirementKind.Competency, "negotiation", 3);
        list.Add(e);

        // Н-43 Посредник
        e = New("Н-43", "posrednik", "Посредник", FeatureLayer.Feature, "Разговор и люди", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В конфликте двух сторон, если известны требования обеих: Переговоры +1";
        e.UnlockText = "—";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Arkham (низкий приоритет)";
        Rank(e, "", "В конфликте двух сторон, если известны требования обеих: Переговоры +1");
        list.Add(e);

        // Н-44 Знает цену
        e = New("Н-44", "znaet_tsenu", "Знает цену", FeatureLayer.Feature, "Торговля", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: предложение отмечается как выгодное / обычное / плохое по местным условиям. II (Торг по делу): видно, что собеседник ценит, чего у него в избытке и что он не примет. Слабый вариант: цены у носителя ~5% лучше";
        e.UnlockText = "Торговое дело 2";
        e.Support = "нужно: торговля";
        e.Dependency = "торговля";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Знает цену (King's Bounty, Корсары), Купеческий глаз (Heroes), Торг по делу (Fallout)";
        Rank(e, "", "Предложение отмечается как выгодное / обычное / плохое по местным условиям.");
        Rank(e, "Торг по делу", "Видно, что собеседник ценит, чего у него в избытке и что он не примет. Слабый вариант: цены у носителя ~5% лучше");
        Require(e, FeatureRequirementKind.Competency, "trade", 2);
        list.Add(e);

        // Н-45 Умеет торговаться
        e = New("Н-45", "umeet_torgovatsya", "Умеет торговаться", FeatureLayer.Feature, "Торговля", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Deal;
        e.Description = "1/сделку — вариант лучшей цены проверкой Торгового дела; провал оставляет прежнюю цену. Вариант (Опытный торговец): +1 к Торговому делу — слабее, дублирует компетенцию";
        e.UnlockText = "Торговое дело 2";
        e.Support = "нужно: торговля";
        e.Dependency = "торговля";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Торговаться умею (Arkham), Умеет торговаться (Корсары)";
        Rank(e, "", "1/сделку — вариант лучшей цены проверкой Торгового дела; провал оставляет прежнюю цену. Вариант (Опытный торговец): +1 к Торговому делу — слабее, дублирует компетенцию");
        Require(e, FeatureRequirementKind.Competency, "trade", 2);
        list.Add(e);

        // Н-46 Свой человек на торгу
        e = New("Н-46", "svoy_chelovek_na_torgu", "Свой человек на торгу", FeatureLayer.Feature, "Торговля", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "У знакомого торговца доступна одна придержанная позиция";
        e.UnlockText = "знакомство с торговцем";
        e.Support = "нужно: торговля";
        e.Dependency = "торговля";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Корсары (низкий приоритет)";
        Rank(e, "", "У знакомого торговца доступна одна придержанная позиция");
        list.Add(e);

        // Н-47 Знающий дорогу
        e = New("Н-47", "knows_the_way", "Знающий дорогу", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Biography;
        e.Description = "Обнаруженная дорожная встреча начинается подготовленно: наблюдать, обойти, занять позицию";
        e.UnlockText = "уже в игре";
        e.Support = "есть (`knows_the_way`)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Scout, Pathfinder, Ranger (Fallout)";
        Rank(e, "", "Обнаруженная дорожная встреча начинается подготовленно: наблюдать, обойти, занять позицию");
        list.Add(e);

        // Н-48 Осторожная натура
        e = New("Н-48", "ostorozhnaya_natura", "Осторожная натура", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "При провале обнаружения засады, ловушки или скрытой угрозы — частичное предупреждение: засада становится обычным боем, ловушка — «заметил в последний момент» (проверка ещё возможна)";
        e.UnlockText = "Наблюдательность 2 или след «пережил засады»";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Cautious Nature (Fallout 2), Не растерялся (KCD), Light Step (Fallout)";
        Rank(e, "", "При провале обнаружения засады, ловушки или скрытой угрозы — частичное предупреждение: засада становится обычным боем, ловушка — «заметил в последний момент» (проверка ещё возможна)");
        Require(e, FeatureRequirementKind.Competency, "observation", 2);
        list.Add(e);

        // Н-110 Отступить вовремя
        e = New("Н-110", "otstupit_vovremya", "Отступить вовремя", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "1/поход провал Скрытного движения при разведке не приводит к обнаружению: разведка просто заканчивается без результата";
        e.UnlockText = "Скрытное движение 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "KCD2 (Rapid Flight — модель)";
        Rank(e, "", "1/поход провал Скрытного движения при разведке не приводит к обнаружению: разведка просто заканчивается без результата");
        Require(e, FeatureRequirementKind.Competency, "stealth", 2);
        list.Add(e);

        // Н-111 Тихий шаг
        e = New("Н-111", "tikhiy_shag", "Тихий шаг", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В сценах с тегом «можно подойти незамеченным» появляется скрытный подход";
        e.UnlockText = "Скрытное движение 3";
        e.Support = "доработка: тег";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Eador";
        Rank(e, "", "В сценах с тегом «можно подойти незамеченным» появляется скрытный подход");
        Require(e, FeatureRequirementKind.Competency, "stealth", 3);
        list.Add(e);

        // Н-49 Обходчик
        e = New("Н-49", "obkhodchik", "Обходчик", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: у обнаруженной обходимой опасности всегда есть вариант обхода за дополнительное время. II (Тихий обход): обход вдвое дешевле. Встречу не удаляет";
        e.UnlockText = "Следопытство 2";
        e.Support = "доработка: тег «обходимо»";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Обходчик (Fallout), Знает обход (Корсары), Тихий обход (Heroes V)";
        Rank(e, "", "У обнаруженной обходимой опасности всегда есть вариант обхода за дополнительное время.");
        Rank(e, "Тихий обход", "Обход вдвое дешевле. Встречу не удаляет");
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 2);
        list.Add(e);

        // Н-50 Разведчик
        e = New("Н-50", "razvedchik", "Разведчик", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Разведчик: до решения виден тип угрозы (люди / зверь / следы боя / неизвестно). Глазомер: после наблюдения — точная численность и основное оружие. Читает строй: заметные особые возможности противника";
        e.UnlockText = "цепочка; Разведчик — составная: Следопытство 3 + Наблюдательность 3";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Scouting I–III (King's Bounty), Глазомер (Heroes), Разведать перед дракой (Eador), Дорогу чувствует, Разведчик впереди (Корсары, Arkham)";
        Rank(e, "Разведчик", "до решения виден тип угрозы (люди / зверь / следы боя / неизвестно).");
        Rank(e, "Глазомер", "после наблюдения — точная численность и основное оружие.");
        Rank(e, "Читает строй", "заметные особые возможности противника");
        Require(e, FeatureRequirementKind.Competency, "observation", 3);
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 3);
        list.Add(e);

        // Н-51 Малой дружиной
        e = New("Н-51", "maloy_druzhinoy", "Малой дружиной", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Если с Командиром ≤2 бойцов: путь на 10% быстрее; встречи с настороженными людьми начинаются как контакт малой группы; скрытный подход и отход доступнее. Засады и явную вражду не отменяет";
        e.UnlockText = "след «ходил малым отрядом»";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Малой группой (Fallout), Малой дружиной (Eador), Ход налегке / Лёгкий отряд, Ходил один (KCD)";
        Rank(e, "", "Если с Командиром ≤2 бойцов: путь на 10% быстрее; встречи с настороженными людьми начинаются как контакт малой группы; скрытный подход и отход доступнее. Засады и явную вражду не отменяет");
        list.Add(e);

        // Н-52 Знает местность (лес / болото / вода / поле / холмы)
        e = New("Н-52", "znaet_mestnost", "Знает местность (лес / болото / вода / поле / холмы)", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В этой местности первый штраф за день слабее; с Лесным делом 3 в лесу эффект вдвое сильнее. Короткие пути и броды — Н-115";
        e.UnlockText = "выбирается для одного типа; с профильной компетенцией 3 сильнее";
        e.Support = "нужно: типы местности в правилах пути";
        e.Dependency = "типы местности в правилах пути";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Лесной / Болотный ходок (Eador), Через бурелом, Знает брод (Корсары), Привычная земля (Heroes), Знакомая местность (Arkham)";
        Rank(e, "", "В этой местности первый штраф за день слабее; с Лесным делом 3 в лесу эффект вдвое сильнее. Короткие пути и броды — Н-115");
        list.Add(e);

        // Н-53 Не сбиться с пути
        e = New("Н-53", "ne_sbitsya_s_puti", "Не сбиться с пути", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Штрафы скорости от непогоды и трудной местности −25%";
        e.UnlockText = "Следопытство 2";
        e.Support = "нужно: погода";
        e.Dependency = "погода";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "исходный список, Pathfinding";
        Rank(e, "", "Штрафы скорости от непогоды и трудной местности −25%");
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 2);
        list.Add(e);

        // Н-54 Путь домой
        e = New("Н-54", "put_domoy", "Путь домой", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Прямо к Дому по уже пройденному маршруту — на 10–15% быстрее; не при активной угрозе на дороге";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Путь домой (исходный список), Домой по знакомой дороге (Arkham)";
        Rank(e, "", "Прямо к Дому по уже пройденному маршруту — на 10–15% быстрее; не при активной угрозе на дороге");
        list.Add(e);

        // Н-55 Привык к дороге
        e = New("Н-55", "privyk_k_doroge", "Привык к дороге", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "На уже пройденном маршруте рутинные навигационные проверки не повторяются; состояния мира и новые встречи остаются";
        e.UnlockText = "—";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Roughin' It (Fallout NV)";
        Rank(e, "", "На уже пройденном маршруте рутинные навигационные проверки не повторяются; состояния мира и новые встречи остаются");
        list.Add(e);

        // Н-56 Запас на чёрный день
        e = New("Н-56", "zapas_na_chyornyy_den", "Запас на чёрный день", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "Первая потеря Припасов от события за поход −1";
        e.UnlockText = "нейтральная";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "исходный список, Бережливый (Arkham), Припасливый (Heroes)";
        Rank(e, "", "Первая потеря Припасов от события за поход −1");
        list.Add(e);

        // Н-57 Бережливый поход
        e = New("Н-57", "berezhlivyy_pokhod", "Бережливый поход", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Каждая третья ночёвка расходует на 1 Припас меньше";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "исходный список";
        Rank(e, "", "Каждая третья ночёвка расходует на 1 Припас меньше");
        list.Add(e);

        // Н-58 Запасной путь
        e = New("Н-58", "zapasnoy_put", "Запасной путь", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "I: первая задержка от небоевого дорожного события за поход −1 ч. II: 1/поход после провала дорожной проверки или исследования отменить лишнюю потерю времени за 1 Припас";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Запасной путь, Не сбавлять шага (Arkham), Тропа назад (KCD)";
        Rank(e, "", "Первая задержка от небоевого дорожного события за поход −1 ч.");
        Rank(e, "", "1/поход после провала дорожной проверки или исследования отменить лишнюю потерю времени за 1 Припас");
        e.Note = "Реализован ранг I (12Е-5): задержка от небоевого дорожного события −1 ч, 1/поход.";
        list.Add(e);

        // Н-59 Ранний подъём
        e = New("Н-59", "ranniy_podyom", "Ранний подъём", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Обычная ночёвка — 7 ч вместо 8";
        e.UnlockText = "—";
        e.Support = "есть (ночёвка 8 ч)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "исходный список";
        Rank(e, "", "Обычная ночёвка — 7 ч вместо 8");
        list.Add(e);

        // Н-60 До темноты успеем
        e = New("Н-60", "do_temnoty_uspeem", "До темноты успеем", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если до стоянки ≤2 ч пути, небольшая задержка не заставляет вставать лагерем раньше";
        e.UnlockText = "—";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Arkham (низкий приоритет)";
        Rank(e, "", "Если до стоянки ≤2 ч пути, небольшая задержка не заставляет вставать лагерем раньше");
        list.Add(e);

        // Н-61 Форсированный марш
        e = New("Н-61", "forsirovannyy_marsh", "Форсированный марш", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Ускорить участок пути ценой изнеможения людей или дополнительных Припасов";
        e.UnlockText = "Стойкость 6";
        e.Support = "есть (изнеможение и Припасы)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Forced March (Eador)";
        Rank(e, "", "Ускорить участок пути ценой изнеможения людей или дополнительных Припасов");
        Require(e, FeatureRequirementKind.Quality, "Fortitude", 6);
        e.OpensIds.Add("order_forsirovannyy_marsh");
        list.Add(e);

        // Н-62 Походный порядок
        e = New("Н-62", "pokhodnyy_poryadok", "Походный порядок", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Перед выходом выбрать один дорожный риск (задержка / расход Припасов / внезапное столкновение), который в этом походе слабее";
        e.UnlockText = "—";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Eador";
        Rank(e, "", "Перед выходом выбрать один дорожный риск (задержка / расход Припасов / внезапное столкновение), который в этом походе слабее");
        list.Add(e);

        // Н-63 Ночной ходок
        e = New("Н-63", "nochnoy_khodok", "Ночной ходок", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Ночью +1 к Скрытному движению и Наблюдательности; ночная засада не застаёт врасплох при успешной пассивной Наблюдательности";
        e.UnlockText = "след «ночные переходы»";
        e.Support = "есть (время суток идёт)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Ночная птица (KCD2), Night Operations (King's Bounty)";
        Rank(e, "", "Ночью +1 к Скрытному движению и Наблюдательности; ночная засада не застаёт врасплох при успешной пассивной Наблюдательности");
        list.Add(e);

        // Н-114 Бывалый путник
        e = New("Н-114", "byvalyy_putnik", "Бывалый путник", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Обычное движение по карте на 10% быстрее, пока носитель в походе. Слабый вариант: глобальная скорость грозит стать обязательным выбором; предпочтительнее условные Н-51, Н-54";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Бывалый путник = Ходок (исходный список; сохранён по правилу §0.1)";
        Rank(e, "", "Обычное движение по карте на 10% быстрее, пока носитель в походе. Слабый вариант: глобальная скорость грозит стать обязательным выбором; предпочтительнее условные Н-51, Н-54");
        list.Add(e);

        // Н-115 Знает ходы / Знает брод
        e = New("Н-115", "znaet_khody", "Знает ходы / Знает брод", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Открывает авторски существующий короткий или безопасный подход (брод); географию из воздуха не создаёт";
        e.UnlockText = "достаточное знакомство с местом или водой";
        e.Support = "доработка: тег «есть короткий путь / брод»";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Знает ходы, Знает брод (Корсары)";
        Rank(e, "", "Открывает авторски существующий короткий или безопасный подход (брод); географию из воздуха не создаёт");
        list.Add(e);

        // Н-116 Глаз на местность / Глаз на тайники
        e = New("Н-116", "glaz_na_mestnost", "Глаз на местность / Глаз на тайники", FeatureLayer.Feature, "Путь и местность", FeatureOwner.Commander, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Осмотр области показывает дополнительное свойство места или, после полного осмотра, один существующий скрытый объект; сокровище не гарантируется";
        e.UnlockText = "Наблюдательность 3";
        e.Support = "доработка: тег";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Глаз на местность (Eador), Глаз на тайники (King's Bounty)";
        Rank(e, "", "Осмотр области показывает дополнительное свойство места или, после полного осмотра, один существующий скрытый объект; сокровище не гарантируется");
        Require(e, FeatureRequirementKind.Competency, "observation", 3);
        list.Add(e);

        // Н-64 Полевой ночлег
        e = New("Н-64", "polevoy_nochleg", "Полевой ночлег", FeatureLayer.Feature, "Лагерь, раны, выносливость", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первое действие обустройства ночлега не тратит лагерный выбор (сейчас их 2 за ночь); успешное Следопытство при выборе стоянки даёт ещё +1 HP каждому";
        e.UnlockText = "Следопытство 2";
        e.Support = "есть (CampRest)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Home on the Range (Fallout NV), Хорошее место (Arkham)";
        Rank(e, "", "Первое действие обустройства ночлега не тратит лагерный выбор (сейчас их 2 за ночь); успешное Следопытство при выборе стоянки даёт ещё +1 HP каждому");
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 2);
        e.Note = "Реализовано (12Е-5): +1 дело на ночь, пока носитель в походе. Вторая часть (Следопытство при выборе стоянки) ждёт выбора стоянки с проверкой.";
        list.Add(e);

        // Н-65 Привычный к дороге
        e = New("Н-65", "privychnyy_k_doroge", "Привычный к дороге", FeatureLayer.Feature, "Лагерь, раны, выносливость", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice | FeatureSource.Trace;
        e.Description = "Долгий бой выматывает только после 8 раундов вместо 6; первое изнеможение за поход не снижает инициативу";
        e.UnlockText = "Стойкость 6; след «долгие походы»";
        e.Support = "есть (изнеможение, порог длинного боя)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "канон §25.4, Походник (Eador), Неутомимый, Старый походник (KCD)";
        Rank(e, "", "Долгий бой выматывает только после 8 раундов вместо 6; первое изнеможение за поход не снижает инициативу");
        Require(e, FeatureRequirementKind.Quality, "Fortitude", 6);
        list.Add(e);

        // Н-66 Быстро отлёживается
        e = New("Н-66", "bystro_otlyozhivaetsya", "Быстро отлёживается", FeatureLayer.Feature, "Лагерь, раны, выносливость", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Дома лёгкие раны восстанавливаются быстрее; тяжёлые — без бонуса";
        e.UnlockText = "—";
        e.Support = "есть (уход дома)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Отменное здоровье (Корсары), Быстро отлёживается (Eador)";
        Rank(e, "", "Дома лёгкие раны восстанавливаются быстрее; тяжёлые — без бонуса");
        list.Add(e);

        // Н-67 Через силу
        e = New("Н-67", "cherez_silu", "Через силу", FeatureLayer.Feature, "Лагерь, раны, выносливость", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Encounter;
        e.Description = "1/встречу раненый игнорирует штраф ранения для одной проверки ценой −1 HP";
        e.UnlockText = "—";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Через силу, Видал хуже, Ответ на боль (Arkham)";
        Rank(e, "", "1/встречу раненый игнорирует штраф ранения для одной проверки ценой −1 HP");
        list.Add(e);

        // Н-68 Крепость тела
        e = New("Н-68", "toughness", "Крепость тела", FeatureLayer.Feature, "Лагерь, раны, выносливость", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "+2 HP, не больше 3 раз";
        e.UnlockText = "нейтральная";
        e.Support = "есть (вариант выбора в коде)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Крепкое тело (Корсары)";
        Rank(e, "", "+2 HP, не больше 3 раз");
        list.Add(e);

        // Н-69 Не навреди
        e = New("Н-69", "ne_navredi", "Не навреди", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Провал Лечения не ухудшает состояние; время и средства теряются";
        e.UnlockText = "Лечение 1";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Fallout, KCD";
        Rank(e, "", "Провал Лечения не ухудшает состояние; время и средства теряются");
        Require(e, FeatureRequirementKind.Competency, "healing", 1);
        list.Add(e);

        // Н-70 Полевой лекарь
        e = New("Н-70", "polevoy_lekar", "Полевой лекарь", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Night;
        e.Description = "I: 1/ночёвку в походе один раненый восстанавливает часть HP, тяжёлая рана в дороге не ухудшается. II: ещё снимает одно лёгкое состояние";
        e.UnlockText = "Лечение 2; II — составная: + Травничество 3";
        e.Support = "есть (лагерь «Перевязать» у Марты)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Полевой лекарь ×3 (Корсары, Heroes), Полевой уход, Полевая перевязка (King's Bounty)";
        Rank(e, "", "1/ночёвку в походе один раненый восстанавливает часть HP, тяжёлая рана в дороге не ухудшается.");
        Rank(e, "", "Ещё снимает одно лёгкое состояние");
        Require(e, FeatureRequirementKind.Competency, "healing", 2);
        e.Note = "Реализован ранг I (12Е-5): 1/ночёвку самый тяжело раненый в походе восстанавливает до 1/4 здоровья (решение автора). «Тяжёлая рана не ухудшается» — ухудшения ран в дороге пока нет.";
        list.Add(e);

        // Н-71 Вытащить живым
        e = New("Н-71", "vytashchit_zhivym", "Вытащить живым", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "1/поход боец, погибший бы по итогам боя, остаётся жив с тяжёлой раной, если лекарь жив и в отряде";
        e.UnlockText = "Лечение 4 + Полевой лекарь (+ наставник)";
        e.Support = "есть (павшие в итоге боя)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Resurrection, Valhalla (King's Bounty), Спаситель жизни (Корсары)";
        Rank(e, "", "1/поход боец, погибший бы по итогам боя, остаётся жив с тяжёлой раной, если лекарь жив и в отряде");
        Require(e, FeatureRequirementKind.Competency, "healing", 4);
        Require(e, FeatureRequirementKind.Feature, "polevoy_lekar", 1);
        list.Add(e);

        // Н-72 Знахарь-практик
        e = New("Н-72", "znakhar_praktik", "Знахарь-практик", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В лагере — «Приготовить средство» (от яда, жара, боли) без ручного сбора трав; тратит время";
        e.UnlockText = "Травничество 3 + Лечение 2 + знание средства";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "адаптация Алхимии (Корсары); канон §27.11.1";
        Rank(e, "", "В лагере — «Приготовить средство» (от яда, жара, боли) без ручного сбора трав; тратит время");
        Require(e, FeatureRequirementKind.Competency, "herbalism", 3);
        Require(e, FeatureRequirementKind.Competency, "healing", 2);
        list.Add(e);

        // Н-73 Полевой ремонт
        e = New("Н-73", "polevoy_remont", "Полевой ремонт", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Stub);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Expedition;
        e.Description = "I: 1/поход обычная сломанная вещь → «пригодна» без мастерской. II (Мастеровой): быстрее, до полного состояния, сложные вещи";
        e.UnlockText = "Ремесло 2; II — Ремесло 4";
        e.Support = "нужно: состояние вещей (канон §26.3)";
        e.Dependency = "состояние вещей (канон §26.3";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Jury Rigging (Fallout), Полевая починка / Мастер починки / На ходу (Корсары), Ремесленная сметка (канон)";
        Rank(e, "", "1/поход обычная сломанная вещь → «пригодна» без мастерской.");
        Rank(e, "Мастеровой", "Быстрее, до полного состояния, сложные вещи");
        Require(e, FeatureRequirementKind.Competency, "craft", 2);
        list.Add(e);

        // Н-74 Разобрать, не ломая
        e = New("Н-74", "razobrat_ne_lomaya", "Разобрать, не ломая", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Провал Ремесла на незнакомом механизме не ломает его; герой узнаёт, чего не хватает: инструмента, материала или знания";
        e.UnlockText = "Ремесло 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Разобрать, не ломая (Fallout), Рука помнит (KCD)";
        Rank(e, "", "Провал Ремесла на незнакомом механизме не ломает его; герой узнаёт, чего не хватает: инструмента, материала или знания");
        Require(e, FeatureRequirementKind.Competency, "craft", 2);
        list.Add(e);

        // Н-75 Как сделано — так и ломается
        e = New("Н-75", "kak_sdelano_tak_i_lomaetsya", "Как сделано — так и ломается", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: у препятствия-механизма или постройки Ремесло можно использовать вместо обычной проверки обхода. II (Полевой мастер): сломанная переправа, дверь, телега чинятся быстрее";
        e.UnlockText = "Ремесло 3; II — составная: + Строительное дело 2";
        e.Support = "доработка: тег";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Locksmith (KCD2), Полевой мастер (Heroes IV)";
        Rank(e, "", "У препятствия-механизма или постройки Ремесло можно использовать вместо обычной проверки обхода.");
        Rank(e, "Полевой мастер", "Сломанная переправа, дверь, телега чинятся быстрее");
        Require(e, FeatureRequirementKind.Competency, "craft", 3);
        list.Add(e);

        // Н-76 Бережливый мастер
        e = New("Н-76", "berezhlivyy_master", "Бережливый мастер", FeatureLayer.Feature, "Лечение и ремесло", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В лагере или мастерской разобрать ненужную вещь и сохранить полезную часть";
        e.UnlockText = "Ремесло 3";
        e.Support = "нужно: материалы (риск микроменеджмента)";
        e.Dependency = "материалы (риск микроменеджмента";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Neatness, Artifactor (King's Bounty)";
        Rank(e, "", "В лагере или мастерской разобрать ненужную вещь и сохранить полезную часть");
        Require(e, FeatureRequirementKind.Competency, "craft", 3);
        list.Add(e);

        // Н-77 Зверь не враг
        e = New("Н-77", "zver_ne_vrag", "Зверь не враг", FeatureLayer.Feature, "Звери", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Обычное животное — не голодное, не защищающее детёнышей или территорию, не спровоцированное — не начинает бой само; встреча начинается с наблюдения";
        e.UnlockText = "Охота или Скотное дело 2";
        e.Support = "доработка: тег поведения";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Animal Friend (Fallout)";
        Rank(e, "", "Обычное животное — не голодное, не защищающее детёнышей или территорию, не спровоцированное — не начинает бой само; встреча начинается с наблюдения");
        Require(e, FeatureRequirementKind.Competency, "livestock", 2);
        Require(e, FeatureRequirementKind.Competency, "hunting", 2);
        e.RequirementsAnyOf = true;
        list.Add(e);

        // Н-78 Читает зверя
        e = New("Н-78", "chitaet_zverya", "Читает зверя", FeatureLayer.Feature, "Звери", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "После наблюдения видно состояние зверя: голоден / напуган / защищает / ранен / болен / неясно";
        e.UnlockText = "Охота 2";
        e.Support = "доработка: тег";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Animal Friend + Living Anatomy";
        Rank(e, "", "После наблюдения видно состояние зверя: голоден / напуган / защищает / ранен / болен / неясно");
        Require(e, FeatureRequirementKind.Competency, "hunting", 2);
        list.Add(e);

        // Н-79 Утихомирить
        e = New("Н-79", "utikhomirit", "Утихомирить", FeatureLayer.Feature, "Звери", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В подходящей звериной встрече — успокоить зверя проверкой Охоты или Скотного дела";
        e.UnlockText = "Охота или Скотное дело 3";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "развитие Animal Friend (Fallout 4)";
        Rank(e, "", "В подходящей звериной встрече — успокоить зверя проверкой Охоты или Скотного дела");
        Require(e, FeatureRequirementKind.Competency, "livestock", 3);
        Require(e, FeatureRequirementKind.Competency, "hunting", 3);
        e.RequirementsAnyOf = true;
        list.Add(e);

        // Н-80 Приманить в сторону
        e = New("Н-80", "primanit_v_storonu", "Приманить в сторону", FeatureLayer.Feature, "Звери", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Если зверь реагирует на пищу — потратить Припасы и увести его с пути без боя";
        e.UnlockText = "Охота 2";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "развитие Animal Friend";
        Rank(e, "", "Если зверь реагирует на пищу — потратить Припасы и увести его с пути без боя");
        Require(e, FeatureRequirementKind.Competency, "hunting", 2);
        list.Add(e);

        // Н-81 Наставник
        e = New("Н-81", "nastavnik", "Наставник", FeatureLayer.Feature, "Дом, люди, обучение", FeatureOwner.Both, FeatureKind.Limited, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Night;
        e.Description = "I: после совместного содержательного применения компетенции (наставник 4+, ученик ≤2; бой тоже считается) на ближайшем привале ученик получает +1 практики (1/привал, не выше потолка). II (Наставник дружины, мастерская: оружейная компетенция 5 + реально обученный ученик): передаёт свой приём";
        e.UnlockText = "компетенция 4+";
        e.Support = "есть (практика, потолок) / II — нужно: приёмы";
        e.Dependency = "приёмы";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Наставник ×6 (Fallout, KCD, Heroes, King's Bounty, Корсары), Показывает на деле, Ученик мастера, Наставник в пути, Учитель, Разбор боя; домашнее обучение — Н-112";
        Rank(e, "", "После совместного содержательного применения компетенции (наставник 4+, ученик ≤2; бой тоже считается) на ближайшем привале ученик получает +1 практики (1/привал, не выше потолка).");
        Rank(e, "Наставник дружины, мастерская", "Передаёт свой приём. Требует: оружейная компетенция 5 + реально обученный ученик.");
        e.Note = "Реализован ранг I (12Е-5): кто применял ту же компетенцию с последнего привала (у наставника 4+, у ученика ≤2; бой считается), получает +1 практики, не больше раза за ночь.";
        list.Add(e);

        // Н-82 Подсказать
        e = New("Н-82", "podskazat", "Подсказать", FeatureLayer.Feature, "Дом, люди, обучение", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Limit = FeatureLimit.Encounter;
        e.Description = "1/встречу спутник, проходящий проверку компетенции, которой Командир владеет на 3+, — переброс. Вариант (Делай как я): реальный успех Командира в сцене даёт спутнику +1 к следующей проверке той же компетенции. Без цепочек перебросов и без помощи из Дома";
        e.UnlockText = "—";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Подсказать, Вместе проще (Arkham 3e), Делай как я";
        Rank(e, "", "1/встречу спутник, проходящий проверку компетенции, которой Командир владеет на 3+, — переброс. Вариант (Делай как я): реальный успех Командира в сцене даёт спутнику +1 к следующей проверке той же компетенции. Без цепочек перебросов и без помощи из Дома");
        list.Add(e);

        // Н-83 Вербовщик
        e = New("Н-83", "verbovshchik", "Вербовщик", FeatureLayer.Feature, "Дом, люди, обучение", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "I: при найме видна одна скрытая черта или потенциал; один фактор недоверия можно снять разговором. II: сдавшемуся человеческому противнику можно предложить присоединиться к Дому";
        e.UnlockText = "Проницательность 2";
        e.Support = "доработка (принятие семьи уже есть)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Recruiter (King's Bounty, Heroes), Вызывает доверие (Корсары)";
        Rank(e, "", "При найме видна одна скрытая черта или потенциал; один фактор недоверия можно снять разговором.");
        Rank(e, "", "Сдавшемуся человеческому противнику можно предложить присоединиться к Дому");
        Require(e, FeatureRequirementKind.Competency, "insight", 2);
        list.Add(e);

        // Н-84 Совместитель
        e = New("Н-84", "sovmestitel", "Совместитель", FeatureLayer.Feature, "Дом, люди, обучение", FeatureOwner.Personal, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "Подменяет одну вторую функцию Дома хуже настоящего специалиста";
        e.UnlockText = "биография + две развитые компетенции";
        e.Support = "доработка (функции Дома)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Совместитель (Корсары)";
        Rank(e, "", "Подменяет одну вторую функцию Дома хуже настоящего специалиста");
        list.Add(e);

        // Н-85 Мастер на все руки
        e = New("Н-85", "master_na_vse_ruki", "Мастер на все руки", FeatureLayer.Feature, "Дом, люди, обучение", FeatureOwner.Personal, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "Полноценно выполняет две функции Дома, но одну одновременно";
        e.UnlockText = "биография";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Корсары (ограничено по опыту Age of Pirates)";
        Rank(e, "", "Полноценно выполняет две функции Дома, но одну одновременно");
        list.Add(e);

        // Н-112 Домашний наставник
        e = New("Н-112", "domashniy_nastavnik", "Домашний наставник", FeatureLayer.Feature, "Дом, люди, обучение", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "В Доме обучает резерв одной своей освоенной компетенции до домашнего потолка (§27.9); пока наставник в походе, обучения нет";
        e.UnlockText = "компетенция 4+";
        e.Support = "нужно: обучение в Доме";
        e.Dependency = "обучение в Доме";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "прежний Н-81 II; отделён по чистовому каталогу";
        Rank(e, "", "В Доме обучает резерв одной своей освоенной компетенции до домашнего потолка (§27.9); пока наставник в походе, обучения нет");
        list.Add(e);

        // Н-113 Подхватил ремесло / Ученик приёма
        e = New("Н-113", "podkhvatil_remeslo", "Подхватил ремесло / Ученик приёма", FeatureLayer.Feature, "Дом, люди, обучение", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System | FeatureImplementation.Future;
        e.Sources = FeatureSource.Practice | FeatureSource.Teacher;
        e.Description = "Повторное содержательное совместное применение открывает право выбора подходящей особенности или обучения приёму раньше обычного; владения не выдаёт и учителя не подменяет";
        e.UnlockText = "совместная практика с мастером";
        e.Support = "есть (практика, наставник) / приёмы — нужно";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "канон §27.1.1, Ученик (King's Bounty)";
        Rank(e, "", "Повторное содержательное совместное применение открывает право выбора подходящей особенности или обучения приёму раньше обычного; владения не выдаёт и учителя не подменяет");
        list.Add(e);

        // Н-86 Не сбить обряд
        e = New("Н-86", "ne_sbit_obryad", "Не сбить обряд", FeatureLayer.Feature, "Обряды (только после своей сверхъестественной системы)", FeatureOwner.Both, FeatureKind.Reaction, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Первая помеха не срывает известный обряд";
        e.UnlockText = "Обрядовое знание 3";
        e.Support = "нужно: обряды";
        e.Dependency = "обряды";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Concentration (Eador)";
        Rank(e, "", "Первая помеха не срывает известный обряд");
        Require(e, FeatureRequirementKind.Competency, "rites", 3);
        list.Add(e);

        // Н-87 Оберег
        e = New("Н-87", "obereg", "Оберег", FeatureLayer.Feature, "Обряды (только после своей сверхъестественной системы)", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Перед походом — один известный оберег; первое подходящее сверхъестественное воздействие разрушает его вместо поражения цели";
        e.UnlockText = "Обрядовое знание 3 + знание оберега";
        e.Support = "нужно: обряды";
        e.Dependency = "обряды";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "магическая защита (King's Bounty)";
        Rank(e, "", "Перед походом — один известный оберег; первое подходящее сверхъестественное воздействие разрушает его вместо поражения цели");
        Require(e, FeatureRequirementKind.Competency, "rites", 3);
        list.Add(e);

        // Н-88 Боевой напев
        e = New("Н-88", "boevoy_napev", "Боевой напев", FeatureLayer.Feature, "Обряды (только после своей сверхъестественной системы)", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Перед боем — один известный напев: против страха или на инициативу; одновременно один. Свита может участвовать в сцене, но особенность через прогрессию не получает";
        e.UnlockText = "Предания 3";
        e.Support = "нужно: напевы";
        e.Dependency = "напевы";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Эдда (WotN)";
        Rank(e, "", "Перед боем — один известный напев: против страха или на инициативу; одновременно один. Свита может участвовать в сцене, но особенность через прогрессию не получает");
        Require(e, FeatureRequirementKind.Competency, "lore", 3);
        list.Add(e);

        // Н-89 Быстрый обряд
        e = New("Н-89", "bystryy_obryad", "Быстрый обряд", FeatureLayer.Feature, "Обряды (только после своей сверхъестественной системы)", FeatureOwner.Commander, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Подготовленный обряд за бой — без полной затраты действия";
        e.UnlockText = "Обрядовое знание 4";
        e.Support = "нужно: обряды";
        e.Dependency = "обряды";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Высшая магия (King's Bounty)";
        Rank(e, "", "Подготовленный обряд за бой — без полной затраты действия");
        Require(e, FeatureRequirementKind.Competency, "rites", 4);
        list.Add(e);

        // Н-90 Не первый раз
        e = New("Н-90", "ne_pervyy_raz", "Не первый раз", FeatureLayer.Feature, "Пережитое (не занимает выбор; история может выдать напрямую — §0.1)", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Trace | FeatureSource.Story;
        e.Description = "При повторе: тип угрозы или первая неизвестная способность существа этого семейства узнаются сразу, решение — нет. Частные случаи: Знающий паводок, Знает болотный зов, Пережил мор";
        e.UnlockText = "пережить тип опасности или семейство существ и получить достоверные сведения";
        e.Support = "есть (знания) / доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Не первый раз (Fallout NV), Видел такое раньше (KCD)";
        Rank(e, "", "При повторе: тип угрозы или первая неизвестная способность существа этого семейства узнаются сразу, решение — нет. Частные случаи: Знающий паводок, Знает болотный зов, Пережил мор");
        list.Add(e);

        // Н-91 Битый, но живой
        e = New("Н-91", "bityy_no_zhivoy", "Битый, но живой", FeatureLayer.Feature, "Пережитое (не занимает выбор; история может выдать напрямую — §0.1)", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Trace | FeatureSource.Story;
        e.Description = "Первая тяжёлая рана в следующем походе лечится быстрее";
        e.UnlockText = "пережить 3 тяжёлые раны";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "KCD";
        Rank(e, "", "Первая тяжёлая рана в следующем походе лечится быстрее");
        list.Add(e);

        // Н-92 Знает цену крови
        e = New("Н-92", "znaet_tsenu_krovi", "Знает цену крови", FeatureLayer.Feature, "Пережитое (не занимает выбор; история может выдать напрямую — §0.1)", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Trace | FeatureSource.Story;
        e.Description = "+1 к Лечению только тяжёлых ран";
        e.UnlockText = "несколько раз лечил тяжёлые раны товарищей";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "KCD";
        Rank(e, "", "+1 к Лечению только тяжёлых ран");
        list.Add(e);

        // Н-93 Пережил засаду
        e = New("Н-93", "perezhil_zasadu", "Пережил засаду", FeatureLayer.Feature, "Пережитое (не занимает выбор; история может выдать напрямую — §0.1)", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Trace | FeatureSource.Story;
        e.Description = "При успешной пассивной Наблюдательности — предварительное предупреждение о следующей засаде (усиливает Н-48)";
        e.UnlockText = "пережить несколько настоящих засад";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "KCD";
        Rank(e, "", "При успешной пассивной Наблюдательности — предварительное предупреждение о следующей засаде (усиливает Н-48)");
        list.Add(e);

        // Н-94 Ученик (имя учителя)
        e = New("Н-94", "uchenik", "Ученик (имя учителя)", FeatureLayer.Feature, "Пережитое (не занимает выбор; история может выдать напрямую — §0.1)", FeatureOwner.Both, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Trace | FeatureSource.Story;
        e.Description = "Открывает потолок практики конкретной компетенции выше 3";
        e.UnlockText = "доверие учителя + обучение";
        e.Support = "есть (подъём потолка у наставника)";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Ученик Остафия (Fallout 2-модель), Academy (King's Bounty)";
        Rank(e, "", "Открывает потолок практики конкретной компетенции выше 3");
        list.Add(e);

        // Н-95 Сначала смотрит
        e = New("Н-95", "snachala_smotrit", "Сначала смотрит", FeatureLayer.Feature, "Личные итоги истории бойца (1–2 за игру)", FeatureOwner.Personal, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.PersonalOutcome;
        e.Description = "При обнаруженной опасности до вступления открывается разведка";
        e.UnlockText = "итог личной истории";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "пример исходника (New Vegas-модель)";
        Rank(e, "", "При обнаруженной опасности до вступления открывается разведка");
        list.Add(e);

        // Н-96 Не отступает первым
        e = New("Н-96", "ne_otstupaet_pervym", "Не отступает первым", FeatureLayer.Feature, "Личные итоги истории бойца (1–2 за игру)", FeatureOwner.Personal, FeatureKind.Limited, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.PersonalOutcome;
        e.Description = "При отходе покидает бой последним и выносит одного раненого (ср. Б-48)";
        e.UnlockText = "итог личной истории (альтернатива Н-95)";
        e.Support = "есть";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "пример исходника";
        Rank(e, "", "При отходе покидает бой последним и выносит одного раненого (ср. Б-48)");
        list.Add(e);

        // Н-97 Функция присутствия
        e = New("Н-97", "funktsiya_prisutstviya", "Функция присутствия", FeatureLayer.Feature, "Функции присутствия (пока человек в походе)", FeatureOwner.Personal, FeatureKind.Permanent, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Presence;
        e.Description = "Лекарь — «Осмотреть рану сейчас»; разведчица — раннее предупреждение о следах и засадах; рыбак — прочитать течение и выбрать проход; кузнец — полевой ремонт. Остался дома — возможности нет";
        e.UnlockText = "биография или профессия";
        e.Support = "есть частично (лагерь: «Перевязать» у Марты, «Осмотреть окрестности» у Агнессы и Остафия)";
        e.Display = "Подготовка похода: что уходит вместе с человеком";
        e.MergedFrom = "Scribe Assistant (New Vegas), офицеры (Корсары), Houndmaster (KCD2)";
        Rank(e, "", "Лекарь — «Осмотреть рану сейчас»; разведчица — раннее предупреждение о следах и засадах; рыбак — прочитать течение и выбрать проход; кузнец — полевой ремонт. Остался дома — возможности нет");
        list.Add(e);

        // Н-98 Безрассудный
        e = New("Н-98", "bezrassudnyy", "Безрассудный", FeatureLayer.Feature, "Двусторонние (редкие, биографические)", FeatureOwner.Both, FeatureKind.TwoSided, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "После провала физической проверки — переброс обоих кубиков; повторный провал — потеря HP";
        e.UnlockText = "биография";
        e.Support = "есть";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Arkham";
        Rank(e, "", "После провала физической проверки — переброс обоих кубиков; повторный провал — потеря HP");
        list.Add(e);

        // Н-99 Недоверчивый
        e = New("Н-99", "nedoverchivyy", "Недоверчивый", FeatureLayer.Feature, "Двусторонние (редкие, биографические)", FeatureOwner.Both, FeatureKind.TwoSided, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "Переброс первой Проницательности против незнакомца; −1 к первой просьбе о помощи к нему";
        e.UnlockText = "биография";
        e.Support = "есть";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Arkham. Варианты без минуса (из трёх версий выбрать одну): Читает людей — первая Проницательность в разговоре +1; Не спешит верить — +1 Проницательности при проверке слуха";
        Rank(e, "", "Переброс первой Проницательности против незнакомца; −1 к первой просьбе о помощи к нему");
        list.Add(e);

        // Н-100 Один справлюсь
        e = New("Н-100", "odin_spravlyus", "Один справлюсь", FeatureLayer.Feature, "Двусторонние (редкие, биографические)", FeatureOwner.Commander, FeatureKind.TwoSided, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "С 1–2 спутниками — переброс первой проверки каждой встречи; с 4 бойцами эффекта нет";
        e.UnlockText = "биография";
        e.Support = "есть";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Arkham";
        Rank(e, "", "С 1–2 спутниками — переброс первой проверки каждой встречи; с 4 бойцами эффекта нет");
        list.Add(e);

        // Н-101 Одержимый загадкой
        e = New("Н-101", "oderzhimyy_zagadkoy", "Одержимый загадкой", FeatureLayer.Feature, "Двусторонние (редкие, биографические)", FeatureOwner.Both, FeatureKind.TwoSided, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "После провала Расследования следующая попытка +1, но герой тратит ещё час";
        e.UnlockText = "биография";
        e.Support = "есть";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Arkham";
        Rank(e, "", "После провала Расследования следующая попытка +1, но герой тратит ещё час");
        list.Add(e);

        // Н-102 Слишком уверен
        e = New("Н-102", "slishkom_uveren", "Слишком уверен", FeatureLayer.Feature, "Двусторонние (редкие, биографические)", FeatureOwner.Both, FeatureKind.TwoSided, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.Biography;
        e.Description = "Первая проверка компетенции 4–5 +1; критический провал — последствие хуже";
        e.UnlockText = "биография";
        e.Support = "нужно: ступени провала";
        e.Dependency = "ступени провала";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Arkham";
        Rank(e, "", "Первая проверка компетенции 4–5 +1; критический провал — последствие хуже");
        list.Add(e);

        // Н-103 Тяжёлый сон
        e = New("Н-103", "tyazhyolyy_son", "Тяжёлый сон", FeatureLayer.Feature, "Недостатки (часть стартовой биографии)", FeatureOwner.Both, FeatureKind.Flaw, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "После ночного обязательного события восстанавливается хуже";
        e.UnlockText = "биография";
        e.Support = "есть";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Hardcore (KCD)";
        Rank(e, "", "После ночного обязательного события восстанавливается хуже");
        list.Add(e);

        // Н-104 Плохо заживает
        e = New("Н-104", "plokho_zazhivaet", "Плохо заживает", FeatureLayer.Feature, "Недостатки (часть стартовой биографии)", FeatureOwner.Both, FeatureKind.Flaw, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.Biography;
        e.Description = "Тяжёлые раны лечатся дольше";
        e.UnlockText = "биография";
        e.Support = "есть";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Hardcore (KCD)";
        Rank(e, "", "Тяжёлые раны лечатся дольше");
        list.Add(e);

        // Н-105 Чужой среди старших
        e = New("Н-105", "chuzhoy_sredi_starshikh", "Чужой среди старших", FeatureLayer.Feature, "Недостатки (часть стартовой биографии)", FeatureOwner.Commander, FeatureKind.Flaw, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Tag | FeatureImplementation.Future;
        e.Sources = FeatureSource.Biography;
        e.Description = "Социальные особенности хуже работают против властных NPC";
        e.UnlockText = "биография";
        e.Support = "нужно: социальная категория";
        e.Dependency = "социальная категория";
        e.Display = "Карточка человека: черта биографии";
        e.MergedFrom = "Hardcore (KCD)";
        Rank(e, "", "Социальные особенности хуже работают против властных NPC");
        list.Add(e);

        // Н-106 Следопыт
        e = New("Н-106", "sledopyt", "Следопыт", FeatureLayer.Feature, "Мастерские вершины", FeatureOwner.Commander, FeatureKind.Mastery, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.System;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Обычные засады больше не застают бодрый отряд полностью неподготовленным";
        e.UnlockText = "Разведчик + Глазомер + Ночной ходок + Следопытство 4 + Наблюдательность 4";
        e.Support = "доработка";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Dark Side (King's Bounty)";
        Rank(e, "", "Обычные засады больше не застают бодрый отряд полностью неподготовленным");
        Require(e, FeatureRequirementKind.Competency, "observation", 4);
        Require(e, FeatureRequirementKind.Competency, "fieldcraft", 4);
        Require(e, FeatureRequirementKind.Feature, "nochnoy_khodok", 1);
        Require(e, FeatureRequirementKind.Feature, "razvedchik", 2);
        list.Add(e);

        // Н-107 Знаток людей
        e = New("Н-107", "znatok_lyudey", "Знаток людей", FeatureLayer.Feature, "Мастерские вершины", FeatureOwner.Commander, FeatureKind.Mastery, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "До конфликта видно, кто из человеческих противников действительно колеблется";
        e.UnlockText = "Расколоть отряд + Вербовщик + Переговоры 4 + Проницательность 4";
        e.Support = "нужно: готовность к бою";
        e.Dependency = "готовность к бою";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Dark Side";
        Rank(e, "", "До конфликта видно, кто из человеческих противников действительно колеблется");
        Require(e, FeatureRequirementKind.Competency, "insight", 4);
        Require(e, FeatureRequirementKind.Competency, "negotiation", 4);
        Require(e, FeatureRequirementKind.Feature, "raskolot_otryad", 1);
        Require(e, FeatureRequirementKind.Feature, "verbovshchik", 1);
        list.Add(e);

        // Н-108 Ведун
        e = New("Н-108", "vedun", "Ведун", FeatureLayer.Feature, "Мастерские вершины", FeatureOwner.Commander, FeatureKind.Mastery, false, FeatureStatus.Candidate);
        e.Implementation = FeatureImplementation.Future;
        e.Sources = FeatureSource.LevelChoice;
        e.Description = "Готовит два разных малых оберега вместо одного";
        e.UnlockText = "Обрядовое знание 4 + Предания 4 + несколько изученных обрядов";
        e.Support = "нужно: обряды";
        e.Dependency = "обряды";
        e.Display = "Диалог и итог: цветная метка сработавшей особенности и что она дала";
        e.MergedFrom = "Dark Side";
        Rank(e, "", "Готовит два разных малых оберега вместо одного");
        Require(e, FeatureRequirementKind.Competency, "rites", 4);
        Require(e, FeatureRequirementKind.Competency, "lore", 4);
        list.Add(e);

        // Прежняя особенность игры (каталог §4.1: переделать по модели
        // «Знающего дорогу» — строгие правила вместо «открывает блоки»).
        e = New("—", NarrativeTraitIds.Naturalist, "Натуралист", FeatureLayer.Feature, "Прежние особенности игры", FeatureOwner.Commander, FeatureKind.Option, false, FeatureStatus.Active);
        e.Implementation = FeatureImplementation.Authored;
        e.Sources = FeatureSource.Biography;
        e.Description = "Открывает авторские блоки и варианты, связанные с растениями, животными, погодой, болезнями, водой и природными изменениями.";
        e.Support = "есть: условия в диалогах";
        e.Display = "Диалог: вариант, открытый особенностью";
        e.Note = "Каталог §4.1: переделать по модели «Знающего дорогу» — Н-77…Н-80, Н-19.";
        Rank(e, "", e.Description);
        list.Add(e);
    }

    private static TraitCatalogEntry New(string code, string id, string name, FeatureLayer layer, string group,
        FeatureOwner owner, FeatureKind kind, bool combat, FeatureStatus status)
    {
        return new TraitCatalogEntry
        {
            Code = code,
            Id = id,
            Name = name,
            Layer = layer,
            Group = group,
            Owner = owner,
            Kind = kind,
            Combat = combat,
            Status = status
        };
    }

    private static void Rank(TraitCatalogEntry entry, string name, string effect)
    {
        entry.Ranks.Add(new FeatureRank { Name = name, Effect = effect });
    }

    private static void Require(TraitCatalogEntry entry, FeatureRequirementKind kind, string id, int value)
    {
        entry.Requirements.Add(new FeatureRequirement { Kind = kind, Id = id, Value = value });
    }
}
