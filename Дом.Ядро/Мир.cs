// Перенос house/world.py: метель, инфраструктура и события (GDD 3, 21).
//
// Три типа событий, как в документе:
//   1) скриптовые по дням — одинаковы в каждой жизни;
//   2) порождённые симуляцией — их делают сами люди (это не здесь);
//   3) внешние случайные — меняют условия, но никогда не убивают сами по себе.
using System.Text.Json;

namespace Дом.Ядро;

public static class Мир
{
    /// <summary>
    /// Составить календарь внешних событий на всю жизнь заранее (GDD 21).
    ///
    /// Один раз, своим потоком случайности, до первого действия. Тогда
    /// вмешательство игрока меняет дом, но не погоду — и повтор жизни
    /// ощущается честным. На каждый день заготавливается до трёх событий
    /// по весу: у события может быть условие, и если первое не подходит,
    /// срабатывает следующее. Все кубики брошены здесь, поэтому условия
    /// читают состояние дома, а поток случайности от этого не съезжает.
    /// </summary>
    public static void build_calendar(House h, JsonElement events_data, int days)
    {
        var rng = h.rng.Branch("мир");
        var календарь = new Словарь<int, List<string>>();
        var использованные = new HashSet<string>(StringComparer.Ordinal);
        var все = events_data.Массив("случайные").EnumerateArray().ToList();
        for (int день = 1; день <= days; день++)
        {
            var подходят = все.Where(ev =>
            {
                var окно = ev.Массив("окно").EnumerateArray().Select(x => x.GetInt32()).ToList();
                int от = окно.Count > 0 ? окно[0] : 1, до = окно.Count > 1 ? окно[1] : 99;
                return от <= день && день <= до;
            }).ToList();
            var свежие = подходят.Where(ev => !использованные.Contains(ev.Строка("id"))).ToList();
            if (свежие.Count == 0)
            {
                // набор исчерпан — можно снова с начала (GDD 21: повторов быть
                // не должно, пока игрок не увидел всё)
                использованные.Clear();
                свежие = подходят;
            }
            if (свежие.Count == 0 || !rng.Chance(h.B["шанс_случайного_события"]))
                continue;
            var кандидаты = new List<string>();
            var пул = свежие.Select(ev => (ev, ev.Число("вес", 5))).ToList();
            for (int i = 0; i < 3; i++)
            {
                if (пул.Count == 0)
                    break;
                var ev = rng.Weighted(пул);
                string id = ev.Строка("id");
                кандидаты.Add(id);
                пул = пул.Where(п => !string.Equals(п.Item1.Строка("id"), id,
                                                     StringComparison.Ordinal)).ToList();
            }
            использованные.Add(кандидаты[0]);
            календарь[день] = кандидаты;
        }
        h.календарь.события.Очистить();
        foreach (var (д, к) in календарь)
            h.календарь.события[д] = к;
        build_weather(h, days);
    }

    /// <summary>
    /// Календарь погодных режимов (GDD 3, 21). Метель — не одна прямая. Буран
    /// на третий день делает из −14 сразу −24, и выйти нельзя никому; через
    /// день-два он стихает и оставляет после себя затишье — день, когда
    /// решается неделя: кто вышел, тот доживёт.
    /// </summary>
    public static void build_weather(House h, int days)
    {
        var b = h.B;
        var rng = h.rng.Branch("погода");
        var календарь = new Словарь<int, (Режим, double)>();
        int день = 1;
        double последний = -99;
        while (день <= days)
        {
            if (день < b["буран_не_раньше"] || день - последний < b["погода_перерыв_дней"])
            {
                день += 1;
                continue;
            }
            if (rng.Chance(b["буран_шанс_в_день"]))
            {
                int сколько = rng.Rint((int)b["буран_дней_мин"], (int)b["буран_дней_макс"]);
                double сдвиг = rng.Uni(b["буран_градусов_мин"], b["буран_градусов_макс"]);
                for (int i = 0; i < сколько; i++)
                    if (день + i <= days)
                        календарь[день + i] = (Режим.БУРАН, сдвиг);
                // после бурана ветер меняется, и это то самое затишье
                int затих = день + сколько;
                if (rng.Chance(b["затишье_после_бурана"]) && затих <= days)
                {
                    int тихих = rng.Rint((int)b["затишье_дней_мин"], (int)b["затишье_дней_макс"]);
                    double тепло = rng.Uni(b["затишье_градусов_мин"], b["затишье_градусов_макс"]);
                    for (int i = 0; i < тихих; i++)
                        if (затих + i <= days)
                            календарь[затих + i] = (Режим.ЗАТИШЬЕ, тепло);
                    сколько += тихих;
                }
                последний = день + сколько;
                день = (int)последний;
                continue;
            }
            if (rng.Chance(b["затишье_шанс_в_день"]))
            {
                int тихих = rng.Rint((int)b["затишье_дней_мин"], (int)b["затишье_дней_макс"]);
                double тепло = rng.Uni(b["затишье_градусов_мин"], b["затишье_градусов_макс"]);
                for (int i = 0; i < тихих; i++)
                    if (день + i <= days)
                        календарь[день + i] = (Режим.ЗАТИШЬЕ, тепло);
                последний = день + тихих;
                день = (int)последний;
                continue;
            }
            день += 1;
        }
        h.календарь.погода.Очистить();
        foreach (var (д, п) in календарь)
            h.календарь.погода[д] = п;
    }

    /// <summary>Новое утро: погода, отключения, события.</summary>
    public static void start_of_day(House h, JsonElement events_data)
    {
        h.day += 1;
        var b = h.B;

        _отсчёт_эффектов(h);

        // район выгребают не только эти пятнадцать: магазин на Заречной обирает
        // весь квартал, а гаражи за домом — почти никто. Поэтому магазин
        // пустеет сам собой, и через неделю очевидное место перестаёт быть
        // лучшим
        foreach (var м in Улица.МЕСТА)
            h.места[м.имя] = Math.Max(b["вылазка_минимум_богатства"],
                h.богатство_места(м.имя) - b["вылазка_район_чистит"] * м.район);

        // --- температура (GDD 3: падает с каждым днём) ---
        double @base = b["температура_день1"] - b["температура_падение_в_день"] * (h.day - 1);
        var (режим, погода_сдвиг) = h.календарь.погода.Взять(h.day, (Режим.МЕТЕЛЬ, 0.0));
        h.режим = режим;
        h.outside = @base + h.температура_сдвиг + погода_сдвиг;
        if (режим != h.режим_вчера)
        {
            h.режим_вчера = режим;
            if (режим == Режим.БУРАН)
            {
                h.journal.line("Поднялся буран. За окном белая стена, слышно только ветер "
                               + "в шахте лифта.", 2);
                h.note("буран");
            }
            else if (режим == Режим.ЗАТИШЬЕ)
            {
                h.journal.line("К утру метель осела. Стало слышно двор — и стало видно, "
                               + "кто выходит.", 2);
                h.note("затишье");
            }
            else
                h.journal.line("Ветер вернулся к обычному.", 1);
        }

        // --- снег (GDD 3) ---
        // Оттепели здесь не бывает: снег не тает, он оседает и слёживается
        // под собственным весом. Поэтому прирост постоянный, а убыль — доля
        // от того, что уже лежит, и высота сама выходит на потолок
        double прирост = b["снег_за_день"]
                         + (режим == Режим.БУРАН ? b["снег_за_буран"] : 0.0);
        h.снег += прирост - h.снег * b["снег_уплотнение"];
        if (режим == Режим.ЗАТИШЬЕ)
            h.снег -= b["снег_оседает"];
        h.снег = Util.Clamp(h.снег, 0.0, b["снег_потолок"]);

        // --- расписание отключений (GDD 21, скриптовые события) ---
        // каждое отключение — событие, а не ежедневное слагаемое: холодные
        // батареи роняют привычное в тот день, когда они остыли
        var было = (h.heating, h.water_on, h.power_on, h.network > 0);
        if (h.day >= b["день_отключения_отопления"])
            h.heating = false;
        if (h.day >= b["день_отключения_воды"])
            h.water_on = false;
        if (h.day >= b["день_отключения_электричества"])
            h.power_on = false;
        // GDD 19: «После дня 0 связь деградирует… к дню 10 сеть исчезает совсем»
        if (h.day >= b["день_потери_связи"])
            h.network = 0.0;
        else
            h.network = Util.Clamp(1.0 - h.day / b["день_потери_связи"], 0.0, 1.0);
        // деньги (GDD 18). Банк и магазин закрываются по своему расписанию
        // и в «погасло» ниже не считаются: у каждого своё скриптовое событие
        h.банки = h.day < b["день_отключения_банков"];
        h.магазины = h.day <= b["день_закрытия_магазинов"];
        if (h.банки)
            // касса банкомата на день: район снимает наличные быстрее,
            // чем их подвозят
            h.банкомат = b["банкомат_касса"];

        var стало = (h.heating, h.water_on, h.power_on, h.network > 0);
        int погасло = 0;
        if (было.Item1 && !стало.Item1) погасло++;
        if (было.Item2 && !стало.Item2) погасло++;
        if (было.Item3 && !стало.Item3) погасло++;
        if (было.Item4 && !стало.Item4) погасло++;
        if (погасло != 0)
        {
            Социальное.громкое(h);
            foreach (var p in h.alive())
                Социальное.видел(h, p, погасло * b["нормальность_за_отключение"]);
        }

        // --- скриптовое событие дня ---
        // GDD 21: «одинаковы в каждой жизни, ЕСЛИ ИГРОК НЕ ВМЕШАЛСЯ». Значит,
        // у события может быть условие отмены
        foreach (var ev in events_data.Массив("скриптовые").EnumerateArray())
        {
            if (ev.Целое("день") != h.day)
                continue;
            if (ev.Есть("отменяется_если") && _отменено(h, ev.GetProperty("отменяется_если")))
            {
                h.journal.line(ev.Строка("текст_отмены", "…обошлось."), 1);
                h.bump("событий_отменено");
                continue;
            }
            h.journal.@event(ev.Строка("текст"), scripted: true);
            apply_effects(h, ev.Объект("эффекты"));
        }

        // --- случайное внешнее событие ---
        _roll_random_event(h, events_data);
    }

    /// <summary>Жива ли ещё эта коммуналка. Одно место на события, реплики
    /// и проверку.</summary>
    public static bool работает(House h, string что) => что switch
    {
        "отопление" => h.heating,
        "вода" => h.water_on,
        "свет" => h.power_on,
        "связь" => h.network > 0,
        "банк" => h.банки,
        "магазин" => h.магазины,
        _ => throw new KeyNotFoundException(что),
    };

    /// <summary>
    /// Выполнено ли условие события. Одна дверь на два случая:
    /// `отменяется_если` у скриптового события и `условие` у случайного.
    /// Виды перечислены в `Схема.УСЛОВИЯ` и проверяются при загрузке: условие,
    /// которого никто не понимает, тихо считалось бы невыполненным.
    /// </summary>
    public static bool условие_верно(House h, JsonElement условие)
    {
        if (условие.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;
        // условий может быть несколько, и тогда верны должны быть все
        if (условие.ValueKind == JsonValueKind.Array)
        {
            foreach (var у in условие.EnumerateArray())
                if (!условие_верно(h, у))
                    return false;
            return true;
        }
        if (условие.ValueKind != JsonValueKind.Object || условие.EnumerateObject().Any() == false)
            return false;
        string вид = условие.Строка("вид");
        switch (вид)
        {
            case "жив_с_умением":
            {
                string умение = условие.Строка("умение");
                double порог = условие.Число("здоровье", 40);
                return h.alive().Any(p => p.skills.Contains(умение, StringComparer.Ordinal)
                                          && p.health > порог);
            }
            case "нет_происшествий":
                return Социальное.recent_incidents(h, условие.Целое("дней", 5)) == 0;
            case "было_происшествие":
                return Социальное.recent_incidents(h, условие.Целое("дней", 5)) > 0;
            case "все_в_тепле":
            {
                double тепло = условие.Число("тепло", 45);
                return h.alive().All(p => p.warmth > тепло);
            }
            case "была_смерть":
                return h.stats.Взять("смертей", 0.0) != 0.0;
            case "есть_раненый":
                return h.alive().Any(p => p.injuries.Count > 0 || p.sick is not null);
            case "пусто_снаружи":
            {
                double порог = условие.Число("осталось", 0.45);
                return Улица.МЕСТА.All(м => h.богатство_места(м.имя) <= порог);
            }
            case "есть_пустая_квартира":
                return h.пустые().Count >= условие.Целое("сколько", 1);
            case "холоднее":
                return h.outside <= условие.Число("градусов", -25);
            case "мало_живых":
                return h.alive().Count <= условие.Целое("сколько", 3);
            case "есть_тело":
                return h.пустые().Any(f => f.body is not null && f.body.порций > 0);
            case "режим":
                return string.Equals(h.режим.Текст(), условие.Строка("какой", "буран"),
                                     StringComparison.Ordinal);
            case "отключено":
                return !работает(h, условие.Строка("что"));
            case "работает":
                return работает(h, условие.Строка("что"));
            case "до_дня":
                return h.day < условие.Целое("день");
            case "деньги_ничего_не_стоят":
                return h.курс() <= 0.0;
            case "подъезд_открыт":
                // заколоченная общими силами дверь — единственное, что дом
                // может сделать против улицы
                return h.day - h.календарь.подъезд_заколочен > h.B["дверь_подъезда_дней"];
            default:
                return false;
        }
    }

    /// <summary>Отобрать реплики, которые сегодня не врут (GDD 19).</summary>
    public static List<string> подходящие(House h, JsonElement варианты)
    {
        var годные = new List<string>();
        foreach (var в in варианты.EnumerateArray())
        {
            if (в.ValueKind == JsonValueKind.String)
                годные.Add(в.GetString()!);
            else if (!в.Есть("условие") || условие_верно(h, в.GetProperty("условие")))
                годные.Add(в.Строка("текст"));
        }
        return годные;
    }

    /// <summary>
    /// Подходит ли эта реплика вот этому человеку. Ключи складываются как «и».
    /// Быт — единственное место в игре, где человек ничем не связан и делает
    /// то, что он есть; поэтому у его вариантов условия личные, а не домовые.
    /// </summary>
    public static bool про_кого(House h, NPC npc, JsonElement кто)
    {
        foreach (var п in кто.EnumerateObject())
        {
            switch (п.Name)
            {
                case "умение":
                    if (!npc.skills.Contains(п.Value.GetString()!, StringComparer.Ordinal))
                        return false;
                    break;
                case "оружие":
                {
                    string знач = п.Value.GetString()!;
                    var w = npc.weapon;
                    bool ок = знач switch
                    {
                        "любое" => w != Оружие.НЕТ,
                        "огнестрел" => Таблицы.ОГНЕСТРЕЛ.Contains(w),
                        _ => string.Equals(w.Текст(), знач, StringComparison.Ordinal),
                    };
                    if (!ок)
                        return false;
                    break;
                }
                case "ребёнок":
                    if (npc.dependents != 0 != Истина(п.Value))
                        return false;
                    break;
                case "ребёнку_плохо":
                    // ребёнок на руках голоден, промёрз или болен. Отдельно
                    // от самого факта ребёнка: когда с ним всё в порядке, мать
                    // занята чем угодно, а когда нет — она сидит рядом
                    if (npc.ребёнку_плохо() >= h.B["быт_ребёнку_плохо"] != Истина(п.Value))
                        return false;
                    break;
                case "пунктик":
                    if (!npc.пунктики.Contains(п.Value.GetString()!, StringComparer.Ordinal))
                        return false;
                    break;
                case "ценит":
                    if (!npc.ценит(п.Value.GetString()!))
                        return false;
                    break;
                case "черта":
                {
                    // {"черта": ["жадность", 6]} — не ниже шести из десяти
                    var пара = п.Value.EnumerateArray().ToList();
                    if (npc.trait(пара[0].GetString()!) < пара[1].GetDouble())
                        return false;
                    break;
                }
                case "один":
                {
                    bool один = npc.guests.Count == 0 && string.IsNullOrEmpty(npc.living_with);
                    if (один != Истина(п.Value))
                        return false;
                    break;
                }
                case "мёрзнет":
                    if (npc.warmth < h.B["критичный_порог"] != Истина(п.Value))
                        return false;
                    break;
                case "цел":
                    if ((npc.injuries.Count > 0 || npc.sick is not null) == Истина(п.Value))
                        return false;
                    break;
            }
        }
        return true;
    }

    private static bool Истина(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.GetDouble() != 0.0,
        JsonValueKind.String => e.GetString()!.Length > 0,
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        _ => true,
    };

    /// <summary>
    /// Реплики, которые сегодня не врут и годятся именно этому человеку.
    /// Возвращает тройки (текст, эффект, занятие): у варианта быта может быть
    /// последействие и имя занятия, по которому его выбирает привычка.
    /// </summary>
    public static List<(string текст, string? делает, string? занятие)>
        подходящие_кому(House h, NPC npc, JsonElement варианты)
    {
        var годные = new List<(string, string?, string?)>();
        foreach (var в in варианты.EnumerateArray())
        {
            if (в.ValueKind == JsonValueKind.String)
            {
                годные.Add((в.GetString()!, null, null));
                continue;
            }
            if (в.Есть("условие") && !условие_верно(h, в.GetProperty("условие")))
                continue;
            if (в.Есть("кто") && !про_кого(h, npc, в.GetProperty("кто")))
                continue;
            годные.Add((в.Строка("текст"),
                        в.Есть("делает") ? в.Строка("делает") : null,
                        в.Есть("занятие") ? в.Строка("занятие") : null));
        }
        return годные;
    }

    /// <summary>Сработало ли условие отмены скриптового события.</summary>
    private static bool _отменено(House h, JsonElement условие) => условие_верно(h, условие);

    /// <summary>Уменьшить срок действия временных модификаторов.</summary>
    private static void _отсчёт_эффектов(House h)
    {
        if (h.температура_дней > 0)
        {
            h.температура_дней -= 1;
            if (h.температура_дней <= 0)
                h.температура_сдвиг = 0.0;
        }
        if (h.опасность_дней > 0)
        {
            h.опасность_дней -= 1;
            if (h.опасность_дней <= 0)
                h.опасность_множитель = 1.0;
        }
    }

    /// <summary>
    /// Событие дня берётся из календаря, составленного до начала жизни.
    /// Срабатывает первый кандидат, чьё условие выполнено. Если ни одно
    /// не подходит — день проходит без события, и это правильно: «чужие
    /// ломятся в квартиру умершего» не должно случаться в доме, где ещё
    /// никто не умер.
    /// </summary>
    private static void _roll_random_event(House h, JsonElement events_data)
    {
        var кандидаты = h.календарь.события.Взять(h.day, new List<string>());
        var по_id = new Словарь<string, JsonElement>(StringComparer.Ordinal);
        foreach (var ev in events_data.Массив("случайные").EnumerateArray())
            по_id[ev.Строка("id")] = ev;
        foreach (var eid in кандидаты)
        {
            if (!по_id.TryGetValue(eid, out var ev))
                continue;
            if (ev.Есть("условие") && !условие_верно(h, ev.GetProperty("условие")))
            {
                h.bump("событий_не_подошло");
                continue;
            }
            h.journal.@event(ev.Строка("текст"));
            apply_effects(h, ev.Объект("эффекты"));
            h.bump("событий_случилось");
            return;
        }
    }

    /// <summary>Применить эффекты события ко всему дому.</summary>
    public static void apply_effects(House h, JsonElement eff)
    {
        if (eff.Есть("паника"))
            foreach (var p in h.alive())
                Социальное.add_panic(p,
                    eff.Число("паника") * (0.7 + 0.6 * p.t01("вспыльчивость")));
        if (eff.Есть("настроение"))
            foreach (var p in h.alive())
                p.mood = Util.Clamp(p.mood + eff.Число("настроение"));
        if (eff.Есть("богатство"))
            h.scav_richness = Util.Clamp(h.scav_richness + eff.Число("богатство"), 0.0, 1.6);
        if (eff.Есть("связь"))
            h.network = Util.Clamp(h.network + eff.Число("связь"), 0.0, 1.0);
        if (eff.Есть("температура"))
        {
            // GDD 21: внешнее событие «никогда не убивает само по себе».
            // Поэтому похолодание бьёт по улице, но не проламывает то, что
            // человек успел построить: у сдвига есть предел
            var t = eff.GetProperty("температура");
            double сдвиг = t.Число("градусов");
            if (сдвиг < 0)
                сдвиг = Math.Max(сдвиг, -h.B["событие_холод_потолок"]);
            h.температура_сдвиг = сдвиг;
            h.температура_дней = t.Целое("дней");
            h.outside += сдвиг;
        }
        if (eff.Есть("опасность_вылазки"))
        {
            var d = eff.GetProperty("опасность_вылазки");
            h.опасность_множитель = d.Число("множитель");
            h.опасность_дней = d.Целое("дней");
        }
        if (eff.Есть("пункт_обогрева"))
            // объявление на двери и голос в приёмнике — вещи, а не знание:
            // на площадку в этот день выходили не все. Дальше это расходится
            // разговором, как и всё остальное (GDD 19)
            foreach (var p in h.alive())
                if (!p.знает_пункт && h.rng.Chance(eff.Число("пункт_обогрева")))
                {
                    p.знает_пункт = true;
                    h.bump("узнали_про_пункт");
                }
        if (Истина(eff.Есть("укрепление_порыв")
                   ? eff.GetProperty("укрепление_порыв") : default))
            h.календарь.укрепление_порыв = h.day;
        if (eff.Есть("нормальность"))
        {
            // чужие в подъезде, стук в дверь ночью, крик за стеной: это дёшево
            // и сильно бьёт по тому, что человек считает обычной жизнью
            Социальное.громкое(h);
            foreach (var p in h.alive())
                Социальное.видел(h, p, eff.Число("нормальность"));
        }
        if (eff.Есть("болезнь_шанс"))
            foreach (var p in h.alive())
            {
                double risk = eff.Число("болезнь_шанс") * (p.warmth < 40 ? 1.4 : 1.0)
                              * (p.satiety < 35 ? 1.3 : 1.0);
                if (p.sick is null && h.rng.Chance(risk))
                {
                    p.sick = "простуда";
                    h.journal.line($"{p.label()} слёг: жар, кашель.", 1);
                }
            }
        if (Истина(eff.Есть("кража_в_доме") ? eff.GetProperty("кража_в_доме") : default))
            _scripted_theft(h);
        if (Истина(eff.Есть("смерть_от_холода")
                   ? eff.GetProperty("смерть_от_холода") : default))
            _scripted_cold_death(h);
        Социальное.spread_panic(h);
    }

    /// <summary>
    /// GDD 21: «день 5 — первая кража в доме». Скриптовое событие должно уметь
    /// запускать происшествие, а не только двигать шкалы: вмешаться в прибавку
    /// паники нельзя. Если дом уже обворовали сам собой, сценарий молчит.
    /// </summary>
    private static void _scripted_theft(House h)
    {
        if (h.stats.Взять("краж", 0.0) != 0.0 || h.stats.Взять("попыток_кражи", 0.0) != 0.0)
            return;
        var люди = h.alive();
        if (люди.Count < 2)
            return;
        NPC вор = люди[0];
        double лучший = double.NegativeInfinity;
        foreach (var p in люди)
        {
            double s = p.trait("жадность") - p.trait("лояльность") + p.desperation() * 3;
            if (s > лучший) { лучший = s; вор = p; }
        }
        // в свою же квартиру не влезают: у соседа по комнате не крадут
        var цели = люди.Where(p => !string.Equals(p.id, вор.id, StringComparison.Ordinal)
                                   && !h.под_одной_крышей(вор, p)).ToList();
        if (цели.Count == 0)
            return;
        NPC жертва = цели[0];
        double лучшая = double.NegativeInfinity;
        foreach (var p in цели)
        {
            double s = вор.loot_value(p.id);
            if (s > лучшая) { лучшая = s; жертва = p; }
        }
        Конфликт.steal(h, вор, жертва);
    }

    /// <summary>
    /// GDD 21: «день 14 — смерть первого соседа от холода». Тоже только если
    /// дом ещё никого не потерял: расписание — это то, что случается «если
    /// игрок не вмешался», а не добавка к уже случившемуся.
    /// </summary>
    private static void _scripted_cold_death(House h)
    {
        if (h.stats.Взять("смертей", 0.0) != 0.0)
            return;
        var люди = h.alive().Where(p => p.dependents == 0).ToList();
        if (люди.Count < 3)
            return;
        NPC жертва = люди[0];
        double худшее = double.PositiveInfinity;
        foreach (var p in люди)
        {
            double s = p.warmth + p.health * 0.5;
            if (s < худшее) { худшее = s; жертва = p; }
        }
        // температуру берём настоящую, ночную: печку никто не топит во сне
        int ночью = (int)Math.Round(h.flat_temp(h.where(жертва), burning: false,
                                                powered: h.powered(жертва)),
                                    MidpointRounding.ToEven);
        string сколько = ночью != 0
            ? ночью.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture) + "°"
            : "около нуля";
        Конфликт.умер(h, жертва, "холод",
            строка: $"† {жертва.name} не {Util.Vb(жертва.sex, "проснулся")}. "
                    + $"В квартире было {сколько}.");
    }

    /// <summary>
    /// Общий множитель опасности вылазки (собаки, чужие, мороз). Улица звереет
    /// не сразу: в первые дни там ещё работают магазины и никто не отнимает
    /// пакет у прохожего.
    /// </summary>
    public static double outing_danger(House h)
    {
        double m = h.опасность_множитель != 0.0 ? h.опасность_множитель : 1.0;
        if (h.outside < -22)
            m *= 1.3;
        return m;
    }

    /// <summary>Жгли ли сегодня в этих стенах: печку, костёр или генератор.</summary>
    public static bool топили(House h, Flat flat)
    {
        if (flat.костёр == h.day)
            return true;
        foreach (var p in h.alive())
        {
            if (h.where(p).apt != flat.apt)
                continue;
            if (p.burning
                || (flat.shelter.Взять("генератор", 0.0) != 0.0 && h.powered(p)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Лёд в вентиляционной шахте (GDD 15). Ловушка здесь нарочная и хорошая:
    /// чем плотнее человек заклеил окна, тем меньше в квартире сквозняка и тем
    /// быстрее обмерзает шахта. Утепление — единственная защита от холода
    /// и единственная дорога к угару.
    /// </summary>
    public static void вытяжка_мёрзнет(House h)
    {
        var b = h.B;
        if (h.outside >= b["вентиляция_мороз"])
            return;
        foreach (var flat in h.flats.Значения)
        {
            if (flat.вентиляция <= 0.0)
                continue;
            double k = 1.0 + flat.shelter.Взять("утепление", 0.0) * b["вентиляция_за_утепление"];
            if (топили(h, flat))
                k *= b["вентиляция_за_топку"];
            flat.вентиляция = Util.Clamp(flat.вентиляция - b["вентиляция_замерзает"] * k,
                                         0.0, 1.0);
        }
    }

    /// <summary>
    /// Тихая смерть: печка горит, а тяги нет (GDD 6.2, 15). Предупреждение
    /// даётся всегда, прежде чем убить: голова, тошнота, свеча у окна, которая
    /// ночью гасла сама. Дежурный на площадке уцелеет — он всю ночь просидел
    /// за дверью, и наутро он и найдёт остальных.
    /// </summary>
    public static void угар(House h)
    {
        var b = h.B;
        foreach (var flat in h.flats.Значения.ToList())
        {
            if (flat.вентиляция > b["вентиляция_признак"] || !топили(h, flat))
                continue;
            var спали = h.alive().Where(p => h.where(p).apt == flat.apt
                                             && p.tonight != Ночь.ДЕЖУРИТЬ).ToList();
            if (спали.Count == 0)
                continue;
            if (flat.вентиляция > b["вентиляция_смерть"])
            {
                foreach (var p in спали)
                {
                    p.health = Util.Clamp(p.health - h.rng.Uni(b["угар_здоровье_мин"],
                                                               b["угар_здоровье_макс"]));
                    p.rest = Util.Clamp(p.rest - b["угар_сон"]);
                    p.угар_признак = h.day;
                    p.bump("угорал");
                }
                h.bump("угарных_признаков");
                string кто = string.Join(" и ", спали.Select(p => p.@short));
                string спал = спали.Count > 1 ? "спали" : Util.Vb(спали[0].sex, "спал");
                h.journal.line($"В кв.{flat.apt} к утру тяжёлая голова и тошнота: "
                               + $"{кто} почти не {спал}. Свеча у окна ночью гасла сама.", 1);
                continue;
            }
            // дежурный просидел ночь на площадке и утром вошёл первым. Если
            // в квартире не дежурил никто, о смерти не узнаёт вообще никто
            var нашёл = new HashSet<string>(
                h.alive().Where(p => h.where(p).apt == flat.apt
                                     && p.tonight == Ночь.ДЕЖУРИТЬ).Select(p => p.id),
                StringComparer.Ordinal);
            foreach (var p in спали.ToList())
            {
                if (!p.здесь())
                    continue;
                foreach (var р in p.дети.ToList())
                    Конфликт.смерть_ребёнка(h, p, р);
                h.bump("смертей_от_угара");
                Конфликт.умер(h, p, Util.Vb(p.sex, "угорел") + " во сне", свидетели: нашёл,
                    строка: $"† {p.name} не {Util.Vb(p.sex, "проснулся")}. "
                            + $"Печка в кв.{flat.apt} горела всю ночь, "
                            + "а вытяжку затянуло льдом ещё неделю назад.");
            }
        }
    }

    /// <summary>
    /// Тихая смерть перестаёт быть тихой сама собой (GDD 13). Сигнал по стояку —
    /// как запах готовки, только другой. Дом от него ещё ничего не узнаёт:
    /// он только начинает догадываться, что за той дверью что-то не так.
    /// </summary>
    public static void запах_по_стояку(House h)
    {
        var b = h.B;
        foreach (var flat in h.flats.Значения.OrderBy(f => f.apt))
        {
            var тело = flat.body;
            if (тело is null || тело.запах is not null || h.day - тело.день < b["запах_дней"])
                continue;
            NPC? мёртвый = null;
            foreach (var p in h.people.Значения)
                if (!p.alive && p.apt == flat.apt && p.died_day == тело.день)
                {
                    мёртвый = p;
                    break;
                }
            var не_знают = h.alive().Where(p => мёртвый is not null
                && !p.знает_о_смерти.Contains(мёртвый.id)
                && Math.Abs(h.where(p).floor - flat.floor) <= 1).ToList();
            тело.запах = h.day;
            if (не_знают.Count > 0)
                h.journal.line($"По стояку от кв.{flat.apt} который день тянет "
                               + "сладковатым. Вслух пока никто не сказал, чем именно.", 1);
        }
    }

    /// <summary>
    /// Во что сегодня превратилась сама дорога: снег, темнота, мороз.
    /// Множитель к шансу сломать ногу — и только к нему.
    /// </summary>
    public static double дорожные_условия(House h)
    {
        var b = h.B;
        double k = 1.0 + h.снег * b["снег_травма"];
        if (!h.power_on)
            k *= b["травма_без_фонарей"];
        k *= 1.0 + Math.Max(0.0, -h.outside - b["травма_мороз_от"]) * b["травма_за_градус"];
        return k;
    }

    /// <summary>
    /// Насколько опасны на улице люди, а не мороз. Ноль в первый день
    /// и единица примерно к десятому: отбирать пакеты начинают тогда же,
    /// когда это начинается в доме, и по той же причине.
    /// </summary>
    public static double улица_злая(House h)
        => Util.Clamp((h.day - 1) / h.B["опасность_разгон_дней"], 0.0, 1.0);
}
