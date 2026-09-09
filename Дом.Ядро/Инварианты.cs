// Перенос house/checks.py, строки 32–242: то, что обязано быть верно всегда.
//
// Не проверка порта, а проверка самого дома: она была у прототипа и должна
// быть у порта, иначе движок останется без единственного средства заметить,
// что запас ушёл в минус или что союз стал односторонним.
//
// Две разные вещи. `invariants` — правила, которые нельзя нарушить ни на каком
// дне. `ledger` — учёт: было плюс пришло равно осталось плюс израсходовано.
// Второе ловит то, чего первое не видит вовсе: банка, которая завелась
// из ниоткуда, каждое правило проходит.

namespace Дом.Ядро;

public static class Инварианты
{
    private static readonly string[] SCALES_100 =
        { "satiety", "hydration", "warmth", "rest", "mood", "health", "panic" };

    /// <summary>
    /// «Движок» считается наравне с банками по той же причине, по какой
    /// считается оружие: он один на весь дом, он ходит из рук в руки
    /// и расходуется, когда из него собирают генератор.
    /// </summary>
    private static readonly string[] RESOURCES =
        { "еда", "вода", "топливо", "материалы", "лекарства", "патроны", "мясо", "движок" };

    private static readonly string[] ВИДЫ_ДЫР = { "окно", "стена", "пол", "потолок" };

    /// <summary>Что обязано быть верно всегда. Возвращает список нарушений.</summary>
    public static List<string> invariants(House h)
    {
        var bad = new List<string>();
        void say(string текст) => bad.Add($"день {h.day}: {текст}");

        foreach (var p in h.people.Значения)
        {
            bool живой = p.здесь();

            foreach (string res in p.stock.Ключи)
            {
                double v = p.stock.Взять(res, 0.0);
                if (v < -1e-9)
                    say($"{p.@short}: запас «{res}» ушёл в минус ({Текст.Ф(v, 3)})");
            }
            foreach (string поле in SCALES_100)
            {
                double v = Шкала(p, поле);
                if (!(-1e-9 <= v && v <= 100 + 1e-9))
                    say($"{p.@short}: {поле} = {Текст.Ф(v, 2)}, а должно быть 0..100");
            }
            // знать можно только о том, кто и правда умер: знание о смерти
            // живого было бы ошибкой, а не слухом
            foreach (string who in p.знает_о_смерти.OrderBy(x => x, StringComparer.Ordinal))
            {
                var кто = h.get(who);
                if (кто is null)
                    say($"{p.@short} знает о смерти несуществующего «{who}»");
                else if (кто.alive && !кто.ушёл)
                    say($"{p.@short} считает {кто.@short} мёртвым, а тот жив");
            }
            foreach (string who in p.trust.Ключи)
            {
                double v = p.trust.Взять(who, 0.0);
                if (!(-1e-9 <= v && v <= 10 + 1e-9))
                    say($"{p.@short}: доверие к {who} = {Текст.Ф(v, 2)}, "
                        + "а должно быть 0..10");
            }
            Шкала100(p.hate, "ненависть");
            Шкала100(Осведомлённость(p), "осведомлённость");
            Шкала100(p.страх, "страх");
            void Шкала100(Словарь<string, double> шкала, string имя)
            {
                foreach (string who in шкала.Ключи)
                {
                    double v = шкала.Взять(who, 0.0);
                    if (!(-1e-9 <= v && v <= 100 + 1e-9))
                        say($"{p.@short}: {имя} к {who} = {Текст.Ф(v, 2)}, "
                            + "а должно быть 0..100");
                }
            }
            foreach (string who in p.близость.Ключи)
            {
                double v = p.близость.Взять(who, 0.0);
                if (!(-1e-9 <= v && v <= 10 + 1e-9))
                    say($"{p.@short}: близость с {who} = {Текст.Ф(v, 2)}, "
                        + "а должно быть 0..10");
            }
            foreach (string who in p.счёты.Ключи)
            {
                double v = p.счёты.Взять(who, 0.0);
                if (!(-1e-9 <= v && v <= 100 + 1e-9))
                    say($"{p.@short}: обида на {who} = {Текст.Ф(v, 2)}, "
                        + "а должно быть 0..100");
            }
            foreach (string who in p.не_прощу.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!h.people.Есть(who))
                    say($"{p.@short} не прощает несуществующего «{who}»");
                if (p.счёты.Взять(who, 0.0) <= 0)
                    say($"{p.@short} не прощает {who}, но обиды на него нет");
            }
            if (p.time_left < -0.01)
                say($"{p.@short}: часов в дне осталось {Текст.Ф(p.time_left, 3)}");

            // память — единственный след того, что человек видел и слышал,
            // и она читается по виду. Опечатка в виде тихо ничего не найдёт,
            // поэтому вид — из перечисления, а «о ком» — существующий жилец
            foreach (var m in p.memory)
            {
                if (!Enum.IsDefined(typeof(Вид), m.вид))
                    say($"{p.@short}: запись памяти неизвестного вида «{m.вид}»");
                if (m.кто is null || !h.people.Есть(m.кто))
                    say($"{p.@short}: память о несуществующем «{m.кто}»");
                if (m.день > h.day)
                    say($"{p.@short}: запись памяти из будущего (день {m.день})");
            }

            // ребёнок: число рук, которые он связывает, и его состояние —
            // одно и то же
            if (живой && p.дети.Count != p.dependents)
                say($"{p.@short}: иждивенцев {p.dependents}, "
                    + $"а состояний детей {p.дети.Count}");
            foreach (var р in p.дети)
                foreach (var (поле, v) in new (string, double)[]
                         { ("сытость", р.сытость), ("тепло", р.тепло),
                           ("здоровье", р.здоровье) })
                    if (!(-1e-9 <= v && v <= 100 + 1e-9))
                        say($"{р.имя} у {p.@short}: {поле} = {Текст.Ф(v, 2)}, "
                            + "а должно быть 0..100");

            // выбывший — это мёртвый, изгнанный ИЛИ ушедший к пункту обогрева.
            // Третье состояние для этих двух проверок ничем не отличается
            // от первых двух: человека в подъезде нет
            if (!живой && Util.Sum(p.stock.Ключи.Select(k => p.stock.Взять(k, 0.0))) > 1e-9)
                say($"{p.@short} выбыл, но запасы при нём");
            if (!живой && p.allies.Count > 0)
                say($"{p.@short} выбыл, но числится в союзе");
            if (p.ушёл && !p.alive)
                say($"{p.@short} ушёл и одновременно числится мёртвым");
            if (p.ушёл && h.people.Значения.Any(o => o.знает_о_смерти.Contains(p.id)))
                say($"{p.@short} ушёл, но дом считает, что он умер");

            if (живой && !string.IsNullOrEmpty(p.living_with))
            {
                var host = h.get(p.living_with!);
                if (host is null)
                    say($"{p.@short} живёт у несуществующего «{p.living_with}»");
                else if (!host.здесь())
                    say($"{p.@short} живёт у выбывшего {host.@short} — "
                        + "топить свою печь он уже не может");
                else if (!host.guests.Contains(p.id))
                    say($"{p.@short} живёт у {host.@short}, а тот об этом не знает");
            }
            foreach (string gid in p.guests.OrderBy(x => x, StringComparer.Ordinal))
            {
                var g = h.get(gid);
                if (g is null)
                    say($"у {p.@short} в гостях несуществующий «{gid}»");
                else if (!string.Equals(g.living_with, p.id, StringComparison.Ordinal))
                    say($"{p.@short} считает гостем {g.@short}, "
                        + $"а тот живёт у «{g.living_with}»");
                else if (!g.здесь())
                    say($"у {p.@short} в гостях выбывший {g.@short}");
            }
            foreach (var other in h.people.Значения)
                if (!string.Equals(other.id, p.id, StringComparison.Ordinal)
                    && other.allies.Contains(p.id) && !p.allies.Contains(other.id))
                    say($"союз односторонний: {other.@short} считает {p.@short} "
                        + "союзником, а тот нет");
        }

        // квартиры. Список пустых считается, а не ведётся руками, поэтому
        // «дважды пустая» и «пустая под живым» стали невозможны по устройству
        foreach (var p in h.people.Значения)
            if (!h.flats.Есть(p.apt))
                say($"{p.@short} прописан в кв.{p.apt}, которой нет в доме");
        foreach (var f in h.flats.Значения)
        {
            if (!(-1e-9 <= f.вентиляция && f.вентиляция <= 1.0 + 1e-9))
                say($"кв.{f.apt}: вентиляция = {Текст.Ф(f.вентиляция, 3)}, "
                    + "а должно быть 0..1");
            foreach (string вид in f.дыры.Ключи)
            {
                int n = f.дыры.Взять(вид, 0);
                if (n < 0)
                    say($"кв.{f.apt}: проломов «{вид}» {n}, а должно быть 0 и больше");
                if (!ВИДЫ_ДЫР.Contains(вид, StringComparer.Ordinal))
                    say($"кв.{f.apt}: пролом неизвестного вида «{вид}»");
            }
        }
        var занято = new Dictionary<int, string>();
        foreach (var p in h.alive())
        {
            if (!string.IsNullOrEmpty(p.living_with))
                continue;
            if (занято.TryGetValue(p.apt, out var кто_уже))
                say($"кв.{p.apt} считают своей двое: {кто_уже} и {p.@short}");
            занято[p.apt] = p.@short;
        }

        // кладовые: имущество за порогом квартиры живёт по тем же правилам,
        // что и всё остальное имущество
        foreach (var к in h.кладовые.Значения)
        {
            foreach (string res in к.stock.Ключи)
            {
                double v = к.stock.Взять(res, 0.0);
                if (v < -1e-9)
                    say($"{к.имя}: запас «{res}» ушёл в минус ({Текст.Ф(v, 3)})");
            }
            if (!h.flats.Есть(к.apt))
                say($"{к.имя} приписана к кв.{к.apt}, которой нет в доме");
        }
        foreach (var p in h.people.Значения)
        {
            foreach (string kid in p.ключи_кладовых.OrderBy(x => x, StringComparer.Ordinal))
                if (!h.кладовые.Есть(kid))
                    say($"{p.@short}: ключ от несуществующей кладовой «{kid}»");
            if (!p.здесь() && p.ключи_кладовых.Count > 0)
                say($"{p.@short} выбыл, но ключи от кладовых при нём");
        }

        return bad;
    }

    /// <summary>Осведомлённость отдельной шкалой: в модели она лежит внутри
    /// сведений, а проверяется наравне с ненавистью и страхом.</summary>
    private static Словарь<string, double> Осведомлённость(NPC p)
    {
        var ш = Словари.Числа();
        foreach (string k in p.сведения.Ключи)
            ш[k] = p.сведения.Взять(k, null!).aware;
        return ш;
    }

    private static double Шкала(NPC p, string имя) => имя switch
    {
        "satiety" => p.satiety,
        "hydration" => p.hydration,
        "warmth" => p.warmth,
        "rest" => p.rest,
        "mood" => p.mood,
        "health" => p.health,
        _ => p.panic,
    };

    /// <summary>
    /// Сошёлся ли приход с расходом. <paramref name="start"/> — снимок мира
    /// до первого дня.
    ///
    /// Правило: было + пришло == осталось + израсходовано + потеряно.
    /// Всё, что не сходится, — либо забытый счётчик, либо утечка.
    /// </summary>
    public static List<string> ledger(House h, Словарь<string, double> start)
    {
        var bad = new List<string>();
        foreach (string res in RESOURCES)
        {
            double было = start.Взять(res, 0.0);
            double пришло = h.stats.Взять("принесено_" + res, 0.0)
                            + h.stats.Взять("наразобрано_" + res, 0.0)
                            + h.stats.Взять("натоплено_" + res, 0.0);
            // ушедший унёс свою еду с собой: для дома она ушла из мира ровно
            // так же, как съеденная
            double ушло = h.stats.Взять("израсходовано_" + res, 0.0)
                          + h.stats.Взять("потеряно_" + res, 0.0)
                          + h.stats.Взять("унесено_" + res, 0.0);
            double осталось = world_total(h, res);
            double расхождение = (было + пришло) - (осталось + ушло);
            if (Math.Abs(расхождение) > 0.01)
                bad.Add($"{res}: было {Текст.Ф(было, 1)} + пришло {Текст.Ф(пришло, 1)} "
                        + $"≠ осталось {Текст.Ф(осталось, 1)} + ушло {Текст.Ф(ушло, 1)} "
                        + $"(расхождение {Текст.Знак(расхождение, 2)})");
        }
        double было_о = start.Взять("_оружие", 0.0);
        double стало_о = оружие_всего(h);
        double найдено = h.stats.Взять("оружия_найдено", 0.0);
        double унесено = h.stats.Взять("оружия_унесено", 0.0);
        if (стало_о != было_о + найдено - унесено)
            bad.Add($"оружие: было {Текст.G(было_о)} + найдено {Текст.G(найдено)} "
                    + $"− унесено {Текст.G(унесено)} ≠ осталось {Текст.G(стало_о)}. "
                    + "Оно не должно ни исчезать вместе с человеком, "
                    + "ни заводиться само");
        return bad;
    }

    /// <summary>
    /// Сколько ресурса есть в доме — у живых, у мёртвых, в квартирах
    /// и в кладовых.
    ///
    /// Кладовая — третье место, где лежат банки, и не считать её нельзя:
    /// тогда первая же поднятая из погреба банка выглядит приходом из ниоткуда.
    /// </summary>
    public static double world_total(House h, string res)
        => Util.Sum(h.people.Значения.Select(p => p.stock.Взять(res, 0.0)))
           + Util.Sum(h.flats.Значения.Select(f => f.stock.Взять(res, 0.0)))
           + Util.Sum(h.кладовые.Значения.Select(k => k.stock.Взять(res, 0.0)));

    /// <summary>
    /// Сколько единиц оружия в доме — в руках и в стенах.
    ///
    /// Тот же учёт, что и у банок, и по той же причине: оружие ходит из рук
    /// в руки, а значит, может начать исчезать (забыли положить в квартиру
    /// за мёртвым) или заводиться само (взяли, не отдав своё).
    /// </summary>
    public static int оружие_всего(House h)
        => h.people.Значения.Count(p => p.weapon != Оружие.НЕТ)
           + h.flats.Значения.Sum(f => f.оружие.Count)
           + h.кладовые.Значения.Sum(k => k.оружие.Count);

    /// <summary>Сколько чего в мире — то, с чем сверяется учёт.</summary>
    public static Словарь<string, double> snapshot(House h)
    {
        var s = Словари.Числа();
        foreach (string res in RESOURCES)
            s[res] = world_total(h, res);
        s["_оружие"] = оружие_всего(h);
        return s;
    }
}
