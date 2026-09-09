// Перенос house/conflict.py, строки 1017–1162: ночь в общей квартире.
//
// Тот, кого пустили к печке, спит в двух метрах от чужого шкафа. Отсюда два
// исхода, которых нет больше нигде: уйти под утро со всем, что было, — и то,
// что тяжелее. Оба тем и отличаются от кражи через дверь, что виноватого
// наутро гадать не приходится: у хозяина ночевал ровно один человек.

namespace Дом.Ядро;

public static partial class Конфликт
{
    /// <summary>
    /// Метки поступка для суда дома: взято чужое и сломано доверие сразу.
    /// Дубль `Каталог.ТЕГИ["обобрать"]`.
    /// </summary>
    private static readonly string[] ТЕГИ_ОБОБРАТЬ = { "воровство", "предательство" };

    /// <summary>Ночь в общей квартире, второй её исход. Возвращает true,
    /// если вышло.</summary>
    public static bool обобрать_и_уйти(House h, NPC гость, NPC хозяин)
    {
        var b = h.B;
        гость.bump("обобрал");
        h.bump("попыток_обобрать");
        Социальное.переступил(h, гость, "обобрать");
        double тихо = Util.Clamp(b["обобрать_тихо"] + (stealth(гость) - 0.5) * 0.6, 0.2, 0.95);
        if (!h.rng.Chance(тихо))
        {
            // хозяин проснулся, а он стоит посреди комнаты с его мешком
            Социальное.emit(h, гость, 4, "ссора", night: true);
            h.journal.line($"{хозяин.@short} {Util.Vb(хозяин.sex, "проснулся")} от того, что "
                + $"{гость.@short} {Util.Vb(гость.sex, "собирал")} "
                + $"{(string.Equals(хозяин.sex, "ж", StringComparison.Ordinal) ? "её" : "его")} "
                + "шкаф в мешок.", 2);
            Социальное.adjust(хозяин, гость.id, trust: -8.0, hate: b["ненависть_за_кражу"],
                              aware: 25);
            Социальное.обидели(h, хозяин, гость, b["обида_за_обобрать"] * 0.6);
            Социальное.register_incident(h, "кража", null);
            Социальное.judge(h, гость, ТЕГИ_ОБОБРАТЬ, hate: 15.0, trust: -3.0);
            scuffle(h, хозяин, гость, place: $"кв.{хозяин.apt}");
            Сожительство.cut_ties(h, гость);
            Сожительство.occupy_flat(h, гость);
            h.bump("обобрать_сорвалось");
            return false;
        }

        // получилось: он знает эту квартиру и берёт всё, что унесёт
        var moved = take_from(h, хозяин, гость, greed: b["обобрать_доля"],
                              limit: b["обобрать_предел"]);
        // и дрова, которые сносил к этой печке сам, — их он считает своими
        double дрова = Math.Min(гость.снёс_дров, хозяин.stock.Взять("топливо", 0.0));
        if (дрова > 0)
        {
            хозяин.stock["топливо"] = хозяин.stock.Взять("топливо", 0.0) - дрова;
            гость.stock["топливо"] = гость.stock.Взять("топливо", 0.0) + дрова;
            moved["топливо"] = moved.Взять("топливо", 0.0) + дрова;
        }
        гость.снёс_дров = 0.0;
        гость.living_with = null;
        хозяин.guests.Remove(гость.id);
        Сожительство.occupy_flat(h, гость);
        гость.mood = Util.Clamp(гость.mood - b["обобрать_настроение"]);
        хозяин.mood = Util.Clamp(хозяин.mood - 20);
        хозяин.panic = Util.Clamp(хозяин.panic + 18);
        h.bump("обобрал_хозяина");
        h.journal.line($"{гость.@short} {Util.Vb(гость.sex, "ушёл")} ночью и "
            + $"{Util.Vb(гость.sex, "унёс")} всё, что было в шкафу у "
            + $"{хозяин.form("gen")}: {_fmt(moved)}. {хозяин.@short} "
            + $"{Util.Vb(хозяин.sex, "пустил")} {гость.form("acc")} к своей печке.", 2);
        h.note($"{гость.@short} обобрал {хозяин.form("acc")} и ушёл");
        // хозяин знает наверняка: у него ночевал ровно один человек. И говорит
        // он об этом всем — это не ночная кража через дверь, где виноватого гадают
        Социальное.adjust(хозяин, гость.id, trust: -10.0,
                          hate: b["ненависть_за_кражу"] * 1.5, aware: 30);
        Социальное.обидели(h, хозяин, гость, b["обида_за_обобрать"], непрощаемо: true);
        Социальное.испугался(h, хозяин, гость, b["страх_за_насилие"] * 0.4);
        Социальное.register_incident(h, "кража", null);
        Социальное.judge(h, гость, ТЕГИ_ОБОБРАТЬ, hate: b["суд_обобрать_злость"],
                         trust: -b["суд_обобрать_доверие"], witnesses: h.others(гость),
                         участники: new[] { хозяин });
        гость.под_подозрением = 1;
        приговор_дома(h, гость, "обобрал",
                      "то, что он вынес у того, кто его пустил");
        return true;
    }

    /// <summary>Ночь в общей квартире. Возвращает true, если получилось.</summary>
    public static bool убить_соседа(House h, NPC killer, NPC victim)
    {
        var b = h.B;
        killer.bump("покушений");
        h.bump("покушений_на_соседа");
        Социальное.переступил(h, killer, "убить_соседа");
        double шанс = b["убийство_база"] + (stealth(killer) - 0.5) * 0.4;
        if (killer.weapon == Оружие.НОЖ || killer.weapon == Оружие.ТОПОР)
            шанс += 0.10;
        шанс -= victim.power() * 0.05;
        шанс -= victim.tonight == Ночь.ДЕЖУРИТЬ ? 0.12 : 0.0;
        if (!h.rng.Chance(Util.Clamp(шанс, 0.30, 0.95)))
        {
            // проснулся
            Социальное.emit(h, killer, 5, "ссора", night: true);
            h.journal.line($"{victim.@short} {Util.Vb(victim.sex, "проснулся")} от того, что "
                + $"{killer.@short} "
                + $"{(string.Equals(killer.sex, "ж", StringComparison.Ordinal) ? "стояла" : "стоял")} "
                + $"над {(string.Equals(victim.sex, "ж", StringComparison.Ordinal) ? "ней" : "ним")}.",
                2);
            Социальное.adjust(victim, killer.id, trust: -10.0,
                              hate: b["ненависть_за_убийство_соседа"]);
            // его он теперь боится по-настоящему: этот человек стоял над ним с ножом
            Социальное.испугался(h, victim, killer, b["страх_за_покушение"]);
            Социальное.register_incident(h, "покушение", null);
            Социальное.judge(h, killer, "насилие", hate: 25.0, trust: -4.0);
            Сожительство.cut_ties(h,
                string.IsNullOrEmpty(killer.living_with) ? victim : killer);
            fight(h, new[] { killer }, new[] { victim }, place: $"кв.{victim.apt}",
                  reason: "ночью в одной квартире");
            приговор_дома(h, killer, "покушение", "то, что он сделал ночью");
            return false;
        }

        // получилось
        bool гость_был = string.Equals(killer.living_with, victim.id, StringComparison.Ordinal);
        foreach (string res in victim.stock.Ключи.ToList())
        {
            double v = victim.stock.Взять(res, 0.0);
            if (v != 0.0)
            {
                killer.stock[res] = killer.stock.Взять(res, 0.0) + v;
                victim.stock[res] = 0.0;
            }
        }
        killer.bump("убийств");
        h.bump("убийств");
        h.bump("убийств_соседа");
        h.событие(ВидСобытия.УБИЙСТВО, кто: killer.id, кому: victim.id,
                  что: "сосед по квартире", где: victim.apt);
        killer.mood = Util.Clamp(killer.mood - b["убийство_настроение"]);
        killer.panic = Util.Clamp(killer.panic + 10);
        if (гость_был)
        {
            // он остаётся здесь: ради этих стен всё и было
            killer.apt = victim.apt;
            killer.floor = victim.floor;
        }
        h.journal.secret($"ночью {killer.@short} убил {victim.form("gen")} и забрал всё");
        // без killer: дом ещё не знает, кто это
        умер(h, victim, Util.Vb(victim.sex, "убит") + " ночью, в собственной квартире");
        if (гость_был)
            killer.living_with = null;
        h.note($"{victim.@short}: {victim.cause}");

        // убитый жил за одной дверью с убийцей, и наутро тот сам говорит, что
        // сосед не проснулся. Дом узнаёт о смерти — но не о том, отчего она.
        // Это и есть та цена, которую платит единственный источник известия
        foreach (var w in h.others(killer))
            Социальное.узнал_о_смерти(h, w, victim);

        // дом видит тело с раной и понимает, кто был рядом
        double подозрение = Util.Clamp(
            b["убийство_подозрение"] * (1.4 - stealth(killer)), 0.05, 0.95);
        var узнали = h.others(killer).Where(_ => h.rng.Chance(подозрение)).ToList();
        foreach (var w in узнали)
        {
            Социальное.adjust(w, killer.id, trust: -5.0,
                              hate: b["ненависть_за_убийство_соседа"], aware: 20);
            // тихое убийство пугает не меньше громкого: этот человек живёт
            // с ними в одном подъезде и однажды ночью уже вставал с ножом
            Социальное.видел_убийство(h, w, killer, killer.weapon);
        }
        if (узнали.Count > 0)
        {
            killer.под_подозрением = 1;
            h.journal.line($"{victim.@short} {Util.Vb(victim.sex, "умер")} ночью, "
                           + $"а рядом был только {killer.@short}. Дом это сложил.", 2);
            Социальное.register_incident(h, "убийство", null);
            Социальное.judge(h, killer, "насилие", hate: 20.0, trust: -3.0,
                             witnesses: узнали);
            приговор_дома(h, killer, "убийство", "смерть соседа по квартире");
        }
        else
            h.journal.line($"{victim.@short} не {Util.Vb(victim.sex, "проснулся")}. "
                + $"{killer.@short} {Util.Vb(killer.sex, "сказал")}, что ночью было тихо.", 2);
        return true;
    }
}
