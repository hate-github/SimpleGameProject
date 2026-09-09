// Перенос house/discoveries.py: утренние обнаружения.
//
// То, что ночью положили в `h.ожидает`, утром разбирают. Один узор на четыре
// случая: сорванный замок в подвале, вещь под чужой дверью, пропажа
// из квартиры и невысказанная обида — всё это случилось раньше, а дом узнаёт
// об этом, когда просыпается, и узнаёт не всё и не наверняка.
//
// Здесь только обнаружение и его последствия; сами поступки — в действиях,
// конфликте и социальном.

namespace Дом.Ядро;

public static class Обнаружения
{
    /// <summary>Хозяин спускается к кладовке и находит сорванный замок —
    /// или не находит.</summary>
    public static void вскрытые_кладовые(House h)
    {
        // сорванный замок в подвале хозяин видит не сразу: он туда не каждый
        // день ходит. Тем же путём, что и пропажа из квартиры, — наутро
        var очередь = new List<(string вор, string кладовка)>(h.ожидает.вскрытые_кладовые);
        h.ожидает.вскрытые_кладовые.Clear();
        foreach (var (вор_id, kid) in очередь)
        {
            var к = h.кладовые.Взять(kid, null);
            var вор = h.get(вор_id);
            var хозяин = к is not null ? h.хозяин_кладовой(к) : null;
            if (к is null || вор is null || хозяин is null || !хозяин.здесь())
                continue;
            if (!h.rng.Chance(h.B["кладовая_шанс_заметить"]))
                continue;
            h.journal.line($"{хозяин.@short} {Util.Vb(хозяин.sex, "спустился")} к своей "
                           + "кладовке — замок сорван, дверь настежь.", 2);
            хозяин.mood = Util.Clamp(хозяин.mood - 12);
            хозяин.panic = Util.Clamp(хозяин.panic + 14);
            Социальное.register_incident(h, "вскрытие", null);
            // кто это был, он знает не всегда: подвал общий, следов на бетоне
            // не остаётся. Это тот же вопрос, что и с ночной кражей
            if (h.rng.Chance(h.B["кладовая_шанс_узнать_вора"]))
            {
                Социальное.adjust(хозяин, вор.id, trust: -3.0,
                                  hate: h.B["кладовая_вскрытие_ненависть"], aware: 20);
                Социальное.испугался(h, хозяин, вор, h.B["страх_за_насилие"] * 0.5);
                h.journal.line($"   {хозяин.@short} {Util.Vb(хозяин.sex, "уверен")}, "
                               + $"что это {вор.@short}.", 2);
                h.bump("вскрытий_раскрыто");
            }
            else
                foreach (var p in h.alive())
                    if (!string.Equals(p.id, хозяин.id, StringComparison.Ordinal))
                        Социальное.adjust(хозяин, p.id, hate: 3.0, aware: 5);
        }
    }

    /// <summary>Вещь под чужой дверью: дом видит её и хозяина двери, а того,
    /// кто положил, — нет.</summary>
    public static void подброшенное(House h)
    {
        var b_n = h.B;
        // то, что за ночь оказалось под чужой дверью. Дом видит вещь
        // и хозяина двери, а того, кто её положил, не видит никто:
        // в этом весь смысл подброса
        var очередь = h.ожидает.подброшено;
        h.ожидает.подброшено = new Словарь<int, Словарь<string, object>>();
        foreach (int apt in очередь.Ключи.ToList())
        {
            var что = очередь.Взять(apt, null!);
            var хозяин = h.чей(h.flats[apt]);
            if (хозяин is null || !хозяин.здесь())
                continue;
            string положил = (string)что.Взять("кто", "")!;
            string вещь_ключ = (string)что.Взять("что", "")!;
            var нашли = h.alive().Where(
                w => !string.Equals(w.id, хозяин.id, StringComparison.Ordinal)
                     && !string.Equals(w.id, положил, StringComparison.Ordinal)
                     && h.rng.Chance(b_n["подброс_заметность"]
                                     * (h.floor_gap(w, хозяин) == 0 ? 1.6 : 1.0))).ToList();
            if (нашли.Count == 0)
                continue;
            foreach (var w in нашли)
            {
                Социальное.adjust(w, хозяин.id, hate: b_n["подброс_ненависть"],
                                  trust: -b_n["подброс_доверие"], aware: 12);
                Социальное.испугался(h, w, хозяин, b_n["подброс_страх"]);
                Социальное.отдалились(w, хозяин.id, b_n["близость_за_обиду"]);
            }
            Социальное.register_incident(h, "подброс", null, witnesses: нашли);
            bool мясо = string.Equals(вещь_ключ, "мясо", StringComparison.Ordinal);
            string вещь = мясо ? "кусок мяса" : "чужая аптечка";
            var один = нашли.Count == 1 ? нашли[0] : null;
            h.journal.line($"У двери кв.{apt} с утра лежал{(мясо ? "" : "а")} {вещь}. "
                + string.Join(", ", нашли.Select(w => w.@short)) + " "
                + (один is not null
                   ? $"{Util.Vb(один.sex, "видел")} и {Util.Vb(один.sex, "сделал")}"
                   : "видели и сделали")
                + " свои выводы.", 2);
            var кто_положил = h.get(положил)!;
            h.journal.secret("положил"
                + (string.Equals(кто_положил.sex, "ж", StringComparison.Ordinal) ? "а" : "")
                + $" это {кто_положил.@short}.");
            h.note($"под дверью кв.{apt} нашли {вещь}");
            h.bump("подбросов_сработало");
        }
    }

    /// <summary>Ночные пропажи замечают утром (GDD 4.4 — сводка).</summary>
    public static void пропажи(House h)
    {
        var losses = new List<(string вор, string жертва)>(h.ожидает.пропажи);
        h.ожидает.пропажи.Clear();
        foreach (var (thief_id, victim_id) in losses)
        {
            var victim = h.get(victim_id);
            if (victim is not null && victim.alive
                && h.rng.Chance(h.B["кража_шанс_заметить_пропажу"]))
                Конфликт.notice_theft(h, victim, thief_id: thief_id);
        }
    }

    /// <summary>
    /// Сказать вслух то, что накопилось за одно событие, — по одной строке.
    ///
    /// После осады втроём это было три одинаковые строки подряд: «Костя
    /// сказал это вслух, при всех» — про Лиду, про Виктора и про Игоря
    /// по отдельности. Человек говорит это один раз и про всех сразу.
    /// </summary>
    public static void огласить_непрощённых(House h)
    {
        var накоплено = new List<(string кого, List<string> кто)>();
        foreach (string кого_id in h.ожидает.не_простил.Ключи)
            накоплено.Add((кого_id, h.ожидает.не_простил.Взять(кого_id, null!)));
        h.ожидает.не_простил.Очистить();
        if (накоплено.Count == 0)
            return;
        foreach (var (кого_id, кто_ids) in накоплено)
        {
            var кого = h.get(кого_id);
            var кто_список = кто_ids.Select(i => h.get(i)).Where(x => x is not null)
                                    .Select(x => x!).ToList();
            if (кого is null || кто_список.Count == 0)
                continue;
            string имена = string.Join(", ",
                кто_список.Take(кто_список.Count - 1).Select(x => x.@short));
            имена = имена.Length > 0
                ? $"{имена} и {кто_список[^1].@short}" : кто_список[^1].@short;
            string сделали = кто_список.Count > 1
                ? "сделали" : Util.Vb(кто_список[0].sex, "сделал");
            h.journal.line($"{кого.@short} {Util.Vb(кого.sex, "сказал")} это вслух, "
                + $"при всех: того, что {имена} {сделали}, "
                + $"{кого.@short} не {Util.Vb(кого.sex, "простил")} и не простит.", 2);
            h.note($"{кого.@short} не простит "
                   + string.Join(", ", кто_список.Select(x => x.form("dat"))));
        }
    }
}
