// Перенос house/runner.py: как гонять много жизней — со сводкой и разбросом.
//
// Одна история ничего не доказывает. Но и сто историй ничего не доказывают,
// если смотреть на голое среднее: при пятнадцати жильцах разброс выживаемости
// таков, что две честные выборки по 50 прогонов расходятся на треть человека.
// Поэтому здесь средние всегда идут с интервалом, а сравнение двух настроек —
// на парных зёрнах.
//
// Чего здесь нет: многопроцессности. В прототипе `many` раскидывает зёрна
// по ядрам, потому что CPython считает один поток; здесь прогон впятеро
// быстрее, и параллельность не окупает своей цены — ни в сложности,
// ни в том, что наблюдатель в чужом процессе отработал бы и пропал.

namespace Дом.Ядро;

/// <summary>Сводка одной прожитой жизни.</summary>
public sealed record Итог
{
    public required long зерно { get; init; }
    public required int выжило { get; init; }
    public required int ушло { get; init; }
    public required double паника { get; init; }
    public required List<string> имена { get; init; }
    public required List<string> причины { get; init; }
    public required Словарь<string, (int? день, string причина)> судьбы { get; init; }
    public required Словарь<string, double> stats { get; init; }
    /// <summary>Поток событий: сюда возвращается только счёт по видам —
    /// сами записи остаются там, где считались.</summary>
    public required Словарь<string, int> события { get; init; }
    public required double богатство { get; init; }
    public required List<string> нарушения { get; init; }
}

public static class Прогоны
{
    /// <summary>«Убита выстрелом» и «убит выстрелом» — одна и та же причина
    /// смерти.</summary>
    public static string без_рода(string cause)
    {
        foreach (var (ж, м) in new[]
                 { ("убита", "убит"), ("умерла", "умер"),
                   ("изгнана", "изгнан"), ("угорела", "угорел") })
            if (cause.StartsWith(ж, StringComparison.Ordinal))
                return м + cause[ж.Length..];
        return cause;
    }

    /// <summary>Одна жизнь: прожить и свести к сводке.</summary>
    public static Итог run_one(string каталог, long зерно, int дней = 30,
                               IReadOnlyDictionary<string, double>? ручки = null,
                               Хуки? hooks = null)
    {
        var прогон = new Прогон(каталог, seed: зерно, days: дней, ручки: ручки,
                                hooks: hooks, журнал: new ЗаглушкаЖурнала(0));
        var start = Инварианты.snapshot(прогон.h);
        var h = прогон.run();
        var alive = h.people.Значения.Where(p => p.здесь()).ToList();
        // ушедший не выжил и не погиб — он ушёл. Считать его в любую из двух
        // колонок значило бы утверждать то, чего дом не знает
        var ушли = h.people.Значения.Where(p => p.ушёл).ToList();
        var судьбы = new Словарь<string, (int?, string)>(StringComparer.Ordinal);
        foreach (var p in h.people.Значения)
            судьбы[p.@short] = (p.died_day, без_рода(Первое(p.cause, "")));
        var события = new Словарь<string, int>(StringComparer.Ordinal);
        foreach (var е in h.события)
            события[е.вид.Текст()] = события.Взять(е.вид.Текст(), 0) + 1;
        var числа = Словари.Числа();
        foreach (string k in h.stats.Ключи)
            числа[k] = h.stats.Взять(k, 0.0);
        return new Итог
        {
            зерно = зерно,
            выжило = alive.Count,
            ушло = ушли.Count,
            паника = alive.Count > 0
                     ? Util.Sum(alive.Select(p => p.panic)) / alive.Count : 0.0,
            имена = alive.Select(p => p.@short)
                         .OrderBy(x => x, StringComparer.Ordinal).ToList(),
            причины = h.people.Значения.Where(p => !p.здесь() && !p.ушёл)
                       .Select(p => без_рода(Первое(p.cause, "?"))).ToList(),
            судьбы = судьбы,
            stats = числа,
            события = события,
            богатство = h.scav_richness,
            нарушения = Инварианты.invariants(h)
                .Concat(Инварианты.ledger(h, start)).ToList(),
        };
    }

    /// <summary>
    /// Причина без скобок: «убит в драке (налёт)» — это «убит в драке».
    ///
    /// Умолчание разное нарочно, как в прототипе: в списке причин смерти
    /// беспричинная смерть — это «?», а в судьбах живого человека причины
    /// нет вовсе, и там пусто.
    /// </summary>
    private static string Первое(string? cause, string если_нет)
    {
        string s = string.IsNullOrEmpty(cause) ? если_нет : cause!;
        int i = s.IndexOf(" (", StringComparison.Ordinal);
        return i < 0 ? s : s[..i];
    }

    /// <summary>Прогнать список зёрен.</summary>
    public static List<Итог> many(string каталог, IEnumerable<long> зёрна, int дней = 30,
                                  IReadOnlyDictionary<string, double>? ручки = null,
                                  Хуки? hooks = null)
        => зёрна.Select(з => run_one(каталог, з, дней, ручки, hooks)).ToList();

    // ---------------------------------------------------------------- статистика

    /// <summary>Среднее и половина 95%-интервала. Без интервала среднее —
    /// просто число.</summary>
    public static (double среднее, double полуинтервал) сводка(IEnumerable<double> значения)
    {
        var v = значения.ToList();
        if (v.Count == 0)
            return (0.0, 0.0);
        double m = Util.Sum(v) / v.Count;
        if (v.Count < 2)
            return (m, 0.0);
        return (m, 1.96 * Разброс(v, m) / Math.Sqrt(v.Count));
    }

    /// <summary>Выборочное отклонение: делитель n−1, как у `statistics.stdev`.</summary>
    private static double Разброс(IReadOnlyList<double> v, double m)
        => Math.Sqrt(Util.Sum(v.Select(x => (x - m) * (x - m))) / (v.Count - 1));

    public static List<double> метрика(IEnumerable<Итог> runs, string ключ,
                                       double умолчание = 0)
        => runs.Select(r => r.stats.Взять(ключ, умолчание)).ToList();

    /// <summary>В какой доле прогонов это случилось хоть раз, в процентах.</summary>
    public static double доля(IReadOnlyCollection<Итог> runs, string ключ)
        => 100.0 * runs.Count(r => r.stats.Взять(ключ, 0.0) != 0)
           / Math.Max(1, runs.Count);

    /// <summary>Средний день первого события — только по прогонам,
    /// где оно было.</summary>
    public static ((double, double) сводка, int сколько) впервые(
        IEnumerable<Итог> runs, string ключ)
    {
        var v = runs.Where(r => r.stats.Есть(ключ))
                    .Select(r => r.stats.Взять(ключ, 0.0)).ToList();
        return v.Count > 0 ? (сводка(v), v.Count) : ((0.0, 0.0), 0);
    }

    /// <summary>
    /// Сравнить две настройки на одних и тех же зёрнах.
    ///
    /// Парность убирает половину шума: один и тот же мир, отличается только
    /// ручка. Возвращает разницу, полуинтервал и то, достоверна ли она.
    /// </summary>
    public static (double разница, double полуинтервал, bool достоверно) парное(
        IReadOnlyList<Итог> a, IReadOnlyList<Итог> b, string ключ)
        => Парно(a.Zip(b, (x, y) => y.stats.Взять(ключ, 0.0) - x.stats.Взять(ключ, 0.0)));

    /// <summary>То же, но по полю сводки, а не по счётчику.</summary>
    public static (double разница, double полуинтервал, bool достоверно) парное_поле(
        IReadOnlyList<Итог> a, IReadOnlyList<Итог> b, Func<Итог, double> поле)
        => Парно(a.Zip(b, (x, y) => поле(y) - поле(x)));

    private static (double, double, bool) Парно(IEnumerable<double> разницы)
    {
        var d = разницы.ToList();
        if (d.Count < 2)
            return (0.0, 0.0, false);
        double m = Util.Sum(d) / d.Count;
        double s = Разброс(d, m);
        if (s == 0)
            return (m, 0.0, m != 0);
        double se = s / Math.Sqrt(d.Count);
        return (m, 1.96 * se, Math.Abs(m / se) >= 1.96);
    }

    /// <summary>Сколько раз какая причина смерти встретилась.</summary>
    public static Словарь<string, int> причины(IEnumerable<Итог> runs)
    {
        var c = Словари.Строкой<int>();
        foreach (var r in runs)
            foreach (string x in r.причины)
                c[x] = c.Взять(x, 0) + 1;
        return c;
    }
}
