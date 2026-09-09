// Перенос house/meeting.py, строки 193–506: само собрание.
//
// Дом, который сумел договориться, — обещание ГДД, и здесь оно проверяется
// напрямую: собрание может не собраться, сорваться в крик, не договориться
// и договориться впустую. Каждый выход настоящий, и ни один не подкручен —
// поэтому первое, что видно в замерах, это как редко дом договаривается.

namespace Дом.Ядро;

public static partial class Площадка
{
    /// <summary>Пять тем, и каждая живёт в четырёх местах: острота, голос,
    /// дело и заголовок для журнала.</summary>
    public static readonly string[] ТЕМЫ =
        { "дежурство", "котёл", "приговор", "квартира", "дверь_подъезда" };

    private static readonly Словарь<string, string> ЗАГОЛОВКИ = Заголовки();

    private static Словарь<string, string> Заголовки()
    {
        var т = Словари.Строкой<string>();
        т["дежурство"] = "Речь о том, чтобы кто-то ночью не спал.";
        т["котёл"] = "Речь о том, чтобы топить одну квартиру на всех.";
        т["приговор"] = "Речь о том, что с этим больше жить нельзя.";
        т["квартира"] = "Речь о пустой квартире: кому занимать, кому разбирать.";
        т["дверь_подъезда"] =
            "Речь о подъездной двери: заколотить и не пускать чужих.";
        // тема описана всюду или не описана нигде: неполнота видна сразу,
        // а не через сорок прогонов молчаливым собранием
        foreach (string тема in ТЕМЫ)
            if (!т.Есть(тема))
                throw new InvalidOperationException($"тема собрания без заголовка: {тема}");
        foreach (string тема in т.Ключи)
            if (!ТЕМЫ.Contains(тема, StringComparer.Ordinal))
                throw new InvalidOperationException($"заголовок без темы: {тема}");
        return т;
    }

    /// <summary>Личная мерка поступка (GDD 12.1) — тем же способом,
    /// что в суде дома.</summary>
    public static double социальная_мерка(NPC кто, string tag)
    {
        if (кто.не_терпит(tag))
            return 1.5;
        if (кто.ценит(tag))
            return -1.0;
        return 0.0;
    }

    /// <summary>
    /// Как он голосует: больше нуля — за, меньше — против.
    ///
    /// Вес голоса — доверие к зовущему плюс собственная сила: на площадке
    /// слушают того, кому верят, и того, кого не хочется задевать.
    /// </summary>
    public static double голос(House h, NPC кто, NPC зовущий, string тема, NPC? цель = null)
    {
        var b = h.B;
        double вес = 0.4 + кто.trust.Взять(зовущий.id, 3.0) / 5.0 + кто.power() * 0.3;
        double за = 0.0;
        if (string.Equals(тема, "дежурство", StringComparison.Ordinal))
        {
            за = кто.panic / 35.0 + Социальное.recent_incidents(h, 6) * 0.7
                 + кто.обокрали * 1.2
                 - (1.0 - кто.rest / 100.0) * 2.0 - кто.t01("жадность") * 0.8;
            if (кто.dependents > 0)
                за -= 1.0;                 // ему всё равно не выйти на площадку
        }
        else if (string.Equals(тема, "котёл", StringComparison.Ordinal))
        {
            double своя = h.flat_temp(h.flats[кто.apt],
                burning: Социальное.своя_топится(h, кто), powered: h.power_on);
            за = (b["комфортная_температура"] - своя) * b["собрание_котёл_голос"]
                 + кто.невмоготу() * 2.5 - кто.t01("жадность") * 2.5;
            if (цель is not null && string.Equals(цель.id, кто.id, StringComparison.Ordinal))
            {
                за += 1.0;                 // к нему и придут: тепло на всех
                за -= кто.t01("жадность") * 1.5;
            }
        }
        else if (string.Equals(тема, "приговор", StringComparison.Ordinal))
        {
            за = кто.hate.Взять(цель!.id, 0.0) / 18.0
                 - кто.trust.Взять(цель.id, 3.0) * 0.4
                 - (кто.allies.Contains(цель.id) ? 3.0 : 0.0)
                 - кто.t01("лояльность") * 1.5;
            if (цель.раскрыт)
                за += 2.5;
            за += социальная_мерка(кто, цель.раскрыт ? "табу" : "воровство");
        }
        else if (string.Equals(тема, "квартира", StringComparison.Ordinal))
        {
            var своя = h.flats[кто.apt];
            double лучшая = 0.0;
            bool есть = false;
            foreach (var f in h.пустые())
            {
                double v = h.ценность_жилья(f, кто) - h.ценность_жилья(своя, кто);
                if (!есть || v > лучшая)
                    лучшая = v;
                есть = true;
            }
            if (!есть)
                лучшая = 0.0;
            за = лучшая * 0.4 + (кто.stock.Взять("материалы", 0.0) < 2 ? 1.5 : 0.0) - 0.5;
        }
        else if (string.Equals(тема, "дверь_подъезда", StringComparison.Ordinal))
            за = кто.panic / 30.0 + Социальное.recent_incidents(h, 6) * 0.5
                 - (кто.stock.Взять("материалы", 0.0) < 2 ? 1.0 : 0.0);
        // за или против — отвечает сам голосующий. Вес голоса остаётся тем же,
        // а знак теперь его ответ: NPC за, когда перевесило, игрок волен иначе,
        // и на площадке это видно всем
        if (Решение.по_правилу(h, кто, Вопрос.ГОЛОС_НА_СОБРАНИИ, "за", "против", за))
            return вес * Math.Abs(за);
        return -вес * Math.Abs(за);
    }

    /// <summary>Собрание от начала до конца. Возвращает строку исхода.</summary>
    public static string провести(House h, NPC зовущий)
    {
        var b = h.B;
        h.календарь.последнее_собрание = h.day;
        h.bump("собраний_позвали");

        // кто вышел на площадку
        var пришли = new List<NPC> { зовущий };
        var отказались = new List<NPC>();
        foreach (var p in h.others(зовущий))
        {
            if (p.health < 30)
                continue;
            if (p.hate.Взять(зовущий.id, 0.0) > b["собрание_ненависть_предел"])
            {
                отказались.Add(p);
                continue;
            }
            double охота = p.trust.Взять(зовущий.id, 3.0) * 0.5
                           + p.t01("общительность") * 2.0
                           + Социальное.recent_incidents(h, 6) * 0.5
                           - p.hate.Взять(зовущий.id, 0.0) / 25.0
                           - (1.0 - p.normalcy) * b["собрание_нежелание"];
            // выйти на площадку или остаться за дверью — решает сам сосед
            if (Решение.по_правилу(h, p, Вопрос.ВЫЙТИ_НА_СОБРАНИЕ, "выйти", "остаться",
                                   охота - b["собрание_порог_прихода"]))
                пришли.Add(p);
            else
                отказались.Add(p);
        }

        if (пришли.Count < b["собрание_минимум"])
        {
            h.journal.line($"{зовущий.@short} {Util.Vb(зовущий.sex, "стучал")} "
                           + "по квартирам — вышли не все, и разговора не вышло.", 2);
            h.bump("собраний_не_собралось");
            foreach (var p in пришли.Skip(1))
                Социальное.adjust(p, зовущий.id, trust: -0.3);
            return "не собралось";
        }

        h.bump("собраний");
        string имена = string.Join(", ", пришли.Select(p => p.@short));
        h.событие(ВидСобытия.СОБРАНИЕ, кто: зовущий.id, сколько: пришли.Count);
        // о чём говорить, решает позвавший — из того, что ему самому больнее,
        // и решает он сам: NPC берёт самое острое, как и брал, а игрок в роли
        // позвавшего выбирает
        string? тема = (зовущий.решающий as Решающий)!.выбор(
            h, зовущий, Вопрос.О_ЧЁМ_ГОВОРИТЬ, темы(h, зовущий));
        тема ??= "дежурство";
        NPC? цель = null;
        if (string.Equals(тема, "приговор", StringComparison.Ordinal))
        {
            цель = приговорённый(h, зовущий).кто;
            if (цель is null || !h.alive().Contains(цель))
                тема = "дежурство";
        }
        if (string.Equals(тема, "котёл", StringComparison.Ordinal))
        {
            цель = холоднее_всех(h, пришли).греет;
            if (цель is null)
                тема = "дежурство";
        }
        h.journal.line($"На площадке собрались: {имена}. "
                       + ЗАГОЛОВКИ.Взять(тема, ""), 2);

        // --- сорваться в ссору можно раньше, чем договориться ---
        if (h.rng.Chance(Util.Clamp(b["собрание_ссора"] * жар_ссоры(h, пришли), 0.0, 0.75)))
            return _сорвалось(h, зовущий, пришли, тема);

        // --- голосование ---
        double за = Util.Sum(пришли.Select(
            p => Math.Max(0.0, голос(h, p, зовущий, тема, цель))));
        double против = Util.Sum(пришли.Select(
            p => Math.Max(0.0, -голос(h, p, зовущий, тема, цель))));
        if (за <= против * b["собрание_нужен_перевес"])
        {
            h.journal.line("   Не договорились. Разошлись по квартирам.", 2);
            h.bump("собраний_без_решения");
            foreach (var p in пришли)
                p.mood = Util.Clamp(p.mood - 4);
            return "не договорились";
        }

        h.bump("собраний_решили");
        h.bump("собрание_" + тема);
        foreach (var p in пришли)
        {
            p.mood = Util.Clamp(p.mood + b["собрание_настроение"]);
            foreach (var c in пришли)
                if (!string.Equals(c.id, p.id, StringComparison.Ordinal))
                    Социальное.adjust(p, c.id,
                        trust: b["собрание_доверие_за_решение"], hate: -4);
        }
        // общее дело — это и обычная жизнь тоже: дом, который договорился,
        // держит нормальность своих (GDD 12.4)
        foreach (var p in пришли)
            p.normalcy = Util.Clamp(p.normalcy + b["собрание_нормальность"], 0.0, 1.0);
        return _решение(h, зовущий, пришли, тема, цель);
    }

    /// <summary>Дом после сорванного собрания хуже, чем до него: все услышали,
    /// кто что думает.</summary>
    private static string _сорвалось(House h, NPC зовущий, List<NPC> пришли, string тема)
    {
        var b = h.B;
        h.bump("собраний_сорвалось");
        var крикун = пришли[0];
        foreach (var p in пришли)
            if (p.trait("вспыльчивость") + p.panic / 50.0
                > крикун.trait("вспыльчивость") + крикун.panic / 50.0)
                крикун = p;
        NPC? обиженный = null;
        foreach (var p in пришли)
        {
            if (string.Equals(p.id, крикун.id, StringComparison.Ordinal))
                continue;
            if (обиженный is null
                || крикун.hate.Взять(p.id, 0.0) > крикун.hate.Взять(обиженный.id, 0.0))
                обиженный = p;
        }
        h.journal.line($"   Начали с дела, кончили криком. {крикун.@short} "
            + $"{Util.Vb(крикун.sex, "сказал")} вслух то, что думает"
            + (обиженный is not null ? $" про {обиженный.form("acc")}." : "."), 2);
        foreach (var p in пришли)
            foreach (var c in пришли)
            {
                if (string.Equals(c.id, p.id, StringComparison.Ordinal))
                    continue;
                // услышанное вслух не забывается, и сведения тоже никуда
                // не денутся
                Социальное.adjust(p, c.id, hate: b["собрание_ссора_ненависть"],
                                  trust: -b["собрание_ссора_доверие"], aware: 10);
            }
        if (обиженный is not null)
            Социальное.adjust(обиженный, крикун.id,
                hate: b["собрание_ссора_ненависть"] * 1.5, trust: -1.5);
        Социальное.emit(h, зовущий, 4, "ссора", night: false);
        Социальное.house_shock(h, panic: b["собрание_ссора_паника"], mood: -8);
        Социальное.register_incident(h, "ссора", null);
        h.note($"собрание сорвалось ({тема})");
        return "сорвалось";
    }

    /// <summary>Применить принятое. Пять дел, каждое опирается
    /// на существующее.</summary>
    private static string _решение(House h, NPC зовущий, List<NPC> пришли,
                                   string тема, NPC? цель)
    {
        var b = h.B;
        if (string.Equals(тема, "дежурство", StringComparison.Ordinal))
        {
            // расписание на неделю: по одному на ночь, по кругу
            var очередь = пришли.Where(p => p.dependents == 0 && p.health > 45)
                                .Select(p => p.id).ToList();
            if (очередь.Count == 0)
            {
                h.journal.line("   Договорились, но дежурить оказалось некому.", 2);
                return "решили впустую";
            }
            h.дежурство = new Дежурство
            {
                очередь = очередь, до = h.day + (int)b["дежурство_дней"], начало = h.day,
            };
            h.journal.line("   Расписали ночи на неделю: "
                + string.Join(", ", очередь.Select(i => h.get(i)!.@short)) + ".", 2);
            h.note("дом договорился о дежурстве");
            return "дежурство";
        }

        if (string.Equals(тема, "котёл", StringComparison.Ordinal))
        {
            var хозяин = цель!;
            int сколько = 0;
            foreach (var p in пришли)
            {
                if (string.Equals(p.id, хозяин.id, StringComparison.Ordinal)
                    || string.Equals(p.living_with, хозяин.id, StringComparison.Ordinal))
                    continue;
                if (сколько >= b["собрание_котёл_гостей"])
                    break;
                double своя = h.flat_temp(h.flats[p.apt],
                    burning: Социальное.своя_топится(h, p), powered: h.power_on);
                double если_вместе = h.flat_temp(h.flats[хозяин.apt],
                    burning: true, powered: h.power_on);
                if (если_вместе <= своя + 2)
                    continue;
                if (p.guests.Count > 0)
                {
                    foreach (string g in p.guests.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        var гость = h.get(g);
                        if (гость is not null)
                        {
                            гость.living_with = null;
                            Сожительство.occupy_flat(h, гость);
                        }
                    }
                    p.guests.Clear();
                }
                if (!string.IsNullOrEmpty(p.living_with))
                {
                    var прежний = h.get(p.living_with!);
                    прежний?.guests.Remove(p.id);
                }
                p.living_with = хозяин.id;
                p.переехал_день = h.day;
                // в котёл человека привело решение дома, а не свой расчёт на шкаф
                p.переезд_умысел = 0.0;
                хозяин.guests.Add(p.id);
                double дрова = p.stock.Взять("топливо", 0.0);
                хозяин.stock["топливо"] = хозяин.stock.Взять("топливо", 0.0) + дрова;
                p.stock["топливо"] = 0.0;
                Социальное.adjust(p, хозяин.id, trust: 1.5, hate: -10);
                Социальное.adjust(хозяин, p.id, trust: 1.0);
                сколько += 1;
            }
            if (сколько == 0)
            {
                h.journal.line("   Договорились, а сходиться не стали: у всех своё.", 2);
                return "решили впустую";
            }
            h.bump("общий_котёл");
            h.journal.line($"   Сложились и перебрались к {хозяин.form("dat")} "
                + $"в кв.{хозяин.apt}: {сколько} чел. Топят одну печку на всех.", 2);
            h.note($"общий котёл у {хозяин.form("gen")} ({сколько} чел.)");
            return "котёл";
        }

        if (string.Equals(тема, "приговор", StringComparison.Ordinal))
        {
            Конфликт.exile(h, цель!, by: зовущий, reason: "решение дома");
            h.приговор_нужен = null;
            h.bump("приговоров_дома");
            return "приговор";
        }

        if (string.Equals(тема, "квартира", StringComparison.Ordinal))
        {
            var пустые = h.пустые();
            if (пустые.Count == 0)
                return "решили впустую";
            // кому занимать: тому, кому своя хуже всех
            var занимает = пришли[0];
            foreach (var p in пришли)
                if (h.ценность_жилья(h.flats[p.apt], p)
                    < h.ценность_жилья(h.flats[занимает.apt], занимает))
                    занимает = p;
            var flat = пустые[0];
            foreach (var f in пустые)
                if (h.ценность_жилья(f, занимает) > h.ценность_жилья(flat, занимает))
                    flat = f;
            NPC? разбирает = null;
            foreach (var p in пришли)
            {
                if (string.Equals(p.id, занимает.id, StringComparison.Ordinal))
                    continue;
                if (разбирает is null
                    || -p.stock.Взять("материалы", 0.0)
                       > -разбирает.stock.Взять("материалы", 0.0))
                    разбирает = p;
            }
            if (flat.apt != занимает.apt && h.чей(flat) is null)
            {
                var старая = h.flats[занимает.apt];
                занимает.apt = flat.apt;
                занимает.floor = flat.floor;
                Сожительство.occupy_flat(h, занимает);
                h.bump("занято_квартир");
                h.journal.line($"   Кв.{flat.apt} отдали {занимает.form("dat")}; "
                    + $"{занимает.@short} {Util.Vb(занимает.sex, "перебрался")} туда "
                    + $"из кв.{старая.apt}.", 2);
            }
            if (разбирает is not null)
            {
                double доска = b["разбор_материалов"];
                разбирает.stock["материалы"] =
                    разбирает.stock.Взять("материалы", 0.0) + доска;
                h.stats["наразобрано_материалы"] =
                    h.stats.Взять("наразобрано_материалы", 0.0) + доска;
                h.journal.line($"   Разбирать пустую разрешили {разбирает.form("dat")} "
                               + $"(+{Текст.G(доска)} материалов).", 2);
            }
            h.note("дом разделил пустую квартиру");
            return "квартира";
        }

        if (string.Equals(тема, "дверь_подъезда", StringComparison.Ordinal))
        {
            double собрали = 0.0;
            foreach (var p in пришли)
            {
                double взяли = Math.Min(1.0, p.stock.Взять("материалы", 0.0));
                p.stock["материалы"] = p.stock.Взять("материалы", 0.0) - взяли;
                h.stats["израсходовано_материалы"] =
                    h.stats.Взять("израсходовано_материалы", 0.0) + взяли;
                собрали += взяли;
            }
            if (собрали < b["дверь_подъезда_материалы"])
            {
                h.journal.line("   Досок на дверь не набрали. Так и разошлись.", 2);
                return "решили впустую";
            }
            h.календарь.подъезд_заколочен = h.day;
            h.bump("подъезд_заколочен");
            foreach (var p in h.alive())
            {
                Социальное.add_panic(p, -b["дверь_подъезда_паника"]);
                p.mood = Util.Clamp(p.mood + 4);
            }
            h.journal.line("   Заколотили подъездную дверь наглухо, оставили щель "
                           + "для своих. Теперь чужой в подъезд не войдёт.", 2);
            h.note("подъезд заколочен общими силами");
            return "дверь_подъезда";
        }
        return "ничего";
    }
}
