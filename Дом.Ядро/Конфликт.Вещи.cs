// Перенос house/conflict.py, строки 92–225: чужое, переложенное себе.
//
// Отъём один, а поводов пять: ночная кража, осада, драка на лестнице,
// разграбление после боя и то, что человек несёт в руках. Разница между
// ними — в жадности и в пределе, а не в самом перекладывании.

namespace Дом.Ядро;

public static partial class Конфликт
{
    /// <summary>Что выносят и в каком порядке. Порядок — поведение: жадность
    /// съедает предел по мере обхода.</summary>
    private static readonly string[] ВЫНОСЯТ =
        { "еда", "топливо", "лекарства", "вода", "патроны", "материалы", "деньги" };

    /// <summary>То же, но из рук: денег в пакете не носят.</summary>
    private static readonly string[] НЕСУТ =
        { "еда", "топливо", "лекарства", "вода", "патроны", "материалы" };

    /// <summary>
    /// Забрать чужое оружие силой. Только если своё хуже: второго в руках
    /// не унести.
    ///
    /// Самый злой из всех источников: за топором теперь можно прийти.
    /// И самый страшный для потерпевшего — его не просто обобрали,
    /// его обезоружили, а завтра тот же человек придёт снова.
    /// </summary>
    public static Оружие? отнять_оружие(House h, NPC victim, NPC taker, double шанс)
    {
        if (victim.weapon == Оружие.НЕТ || !victim.alive)
            return null;
        if (Таблицы.ОРУЖИЕ_ВЕС[victim.weapon] <= Таблицы.ОРУЖИЕ_ВЕС[taker.weapon])
            return null;
        if (!h.rng.Chance(шанс))
            return null;
        var чем = victim.weapon;
        victim.weapon = Оружие.НЕТ;
        if (вооружиться(h, taker, чем.Текст()) is null)
        {
            victim.weapon = чем;
            return null;
        }
        var b = h.B;
        Социальное.adjust(victim, taker.id, hate: b["ненависть_за_оружие"], trust: -2.0);
        Социальное.испугался(h, victim, taker, b["страх_за_оружие"] * Таблицы.ОРУЖИЕ_ВЕС[чем]);
        h.bump("оружия_отнято");
        return чем;
    }

    /// <summary>
    /// Вынести чужое оружие из квартиры — при краже или при осаде.
    ///
    /// Отличие от <c>отнять_оружие</c> в том, что здесь никто никому в лицо
    /// не смотрит: берут не потому, что нужнее, а потому, что плохо лежит.
    /// Если своё лучше — несут к себе в угол, а не в руках.
    /// </summary>
    public static string? унести_оружие(House h, NPC victim, NPC taker, double шанс)
    {
        if (victim.weapon == Оружие.НЕТ || !h.rng.Chance(шанс))
            return null;
        var чем = victim.weapon;
        victim.weapon = Оружие.НЕТ;
        h.bump("оружия_вынесено");
        if (Таблицы.ОРУЖИЕ_ВЕС[чем] > Таблицы.ОРУЖИЕ_ВЕС[taker.weapon])
            вооружиться(h, taker, чем.Текст(), вслух: false);
        else
            h.flats[taker.apt].оружие.Add(чем);
        // злость и подозрение здесь не наводят: ночью хозяин спит и наутро
        // гадает, кто это был, — за это отвечает notice_theft и suspect
        return Таблицы.ОРУЖИЕ_ВИН.TryGetValue(чем, out var в) ? в : чем.Текст();
    }

    /// <summary>
    /// Перекладывание чужого себе. <paramref name="greed"/> 0..1 — какая доля
    /// запаса уходит.
    ///
    /// <paramref name="limit"/> — сколько единиц одного ресурса можно унести
    /// за раз. Ночью вор уносит то, что влезает в сумку, а не весь шкаф;
    /// при осаде выносят без ограничений.
    /// </summary>
    public static Словарь<string, double> take_from(House h, NPC victim, NPC taker,
                                                    double greed, double? limit = null)
    {
        var moved = Словари.Строкой<double>();
        foreach (string res in ВЫНОСЯТ)
        {
            double have = victim.stock.Взять(res, 0.0);
            if (have <= 0)
                continue;
            double amount = have * greed;
            // деньги — не банки с тушёнкой: сколько нашёл, столько и в карман,
            // предел на «сколько влезет в сумку» к ним не относится
            if (limit is not null && !string.Equals(res, "деньги", StringComparison.Ordinal))
                amount = Math.Min(amount, limit.Value);
            amount = string.Equals(res, "лекарства", StringComparison.Ordinal)
                     ? Math.Round(amount, MidpointRounding.ToEven)   // round(x)
                     : Math.Truncate(amount);                        // float(int(x))
            if (amount <= 0 && have >= 1 && h.rng.Chance(greed))
                amount = 1.0;
            amount = Math.Min(amount, have);
            if (amount > 0)
            {
                victim.stock[res] = have - amount;
                taker.stock[res] = taker.stock.Взять(res, 0.0) + amount;
                moved[res] = moved.Взять(res, 0.0) + amount;
            }
        }
        return moved;
    }

    /// <summary>Вынести квартиру целиком — со всем, что принесли в неё
    /// жильцы.</summary>
    public static Словарь<string, double> take_household(House h, NPC victim, NPC taker,
                                                         double greed, double? limit = null)
    {
        var moved = Словари.Строкой<double>();
        foreach (var кто in h.household(victim))
        {
            var m = take_from(h, кто, taker, greed, limit);
            foreach (var k in m.Ключи)
                moved[k] = moved.Взять(k, 0.0) + m.Взять(k, 0.0);
        }
        return moved;
    }

    /// <summary>
    /// Пришедшие делят долю поровну и выносят квартиру каждый в свой карман.
    ///
    /// Один вход на откуп, побег через окно, сдачу и разграбление после
    /// драки: в осаде этот цикл стоял пять раз подряд, и забыть слить
    /// добычу в одном из выходов было проще простого.
    /// </summary>
    public static Словарь<string, double> вынести_на_всех(House h, NPC victim,
                                                          IReadOnlyList<NPC> crew, double доля)
    {
        var moved = Словари.Строкой<double>();
        foreach (var p in crew)
        {
            var m = take_household(h, victim, p, greed: доля / Math.Max(1, crew.Count));
            foreach (var k in m.Ключи)
                moved[k] = moved.Взять(k, 0.0) + m.Взять(k, 0.0);
        }
        return moved;
    }

    /// <summary>
    /// Отнять то, что человек несёт в руках, а не весь его шкаф.
    /// На лестнице у него пакет, а не квартира: забирают несколько единиц,
    /// начиная с самого ценного.
    /// </summary>
    public static Словарь<string, double> take_carried(House h, NPC victim, NPC taker,
                                                       double limit)
    {
        var moved = Словари.Строкой<double>();
        int left = (int)Math.Max(1, Math.Round(limit, MidpointRounding.ToEven));
        foreach (string res in НЕСУТ)
        {
            if (left <= 0)
                break;
            int have = (int)victim.stock.Взять(res, 0.0);
            if (have <= 0)
                continue;
            double amount = Math.Min(have,
                Math.Min(left, Math.Max(1, Math.Round(have * 0.3, MidpointRounding.ToEven))));
            if (amount <= 0)
                continue;
            victim.stock[res] = victim.stock.Взять(res, 0.0) - amount;
            taker.stock[res] = taker.stock.Взять(res, 0.0) + amount;
            moved[res] = amount;
            left -= (int)amount;
        }
        return moved;
    }

    /// <summary>Добыча одной строкой: «еда 3, топливо 1.5».</summary>
    public static string _fmt(Словарь<string, double> moved)
    {
        if (moved.Count == 0)
            return "ничего";
        var части = new List<string>();
        foreach (string k in moved.Ключи)
        {
            double v = moved.Взять(k, 0.0);
            if (v == 0.0)
                continue;
            части.Add(v == Math.Floor(v)
                ? $"{k} {(long)v}"
                : $"{k} {Текст.Repr(Текст.Округлить(v, 1))}");
        }
        return string.Join(", ", части);
    }
}
