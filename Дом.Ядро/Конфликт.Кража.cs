// Перенос house/conflict.py, строки 227–340 и 450–600: кража и подозрение.
//
// Кража — единственное место, где дом судит не по тому, что было, а по тому,
// что он думает. Наутро хозяин видит, что запас стал меньше, и называет
// виноватого; здесь и рождаются несправедливые обиды, которых в мире
// с полным знанием не бывает вовсе.

namespace Дом.Ядро;

public static partial class Конфликт
{
    /// <summary>
    /// Шанс унести чужое незамеченным: дверь, дежурство, собственная
    /// ловкость.
    ///
    /// <paramref name="известно"/> = false — это прикидка заранее, днём.
    /// Тогда про сегодняшнюю ночь вор ещё ничего не знает и судит
    /// по привычке: сколько ночей за метель у соседа горел свет. Пока эта
    /// разница не была проведена, вор читал `tonight` — то есть решение,
    /// которое сосед примет только вечером.
    /// </summary>
    public static double theft_chance(House h, NPC thief, NPC target, bool известно = true)
    {
        var b = h.B;
        double p = b["кража_база_успеха"];
        if (thief.ключи.Contains(target.apt))
            p += b["кража_по_ключам"];      // своим ключом, без шума и следов
        else
            p += b["кража_за_уровень_двери"] * target.shelter.Взять("дверь", 0.0);
        // в квартиру, которую уже вскрыли осадой, входят через ту же дыру:
        // пролом в стене отменяет любой засов
        p += b["дыра_кража"] * h.where(target).дыр();
        if (target.away && !h.household(target).Skip(1).Any(g => !g.away))
            p += b["кража_хозяин_ушёл"];    // ушёл, и дома никого не оставил
        if (известно)
        {
            // правда: исполнение: известно=true только ночью, когда кража уже идёт
            if (target.tonight == Ночь.ДЕЖУРИТЬ)
                p += b["кража_дежурство"];
        }
        else
        {
            double привычка = target.ночей_дежурства / Math.Max(1.0, (double)h.day);
            p += b["кража_дежурство"] * Util.Clamp(привычка, 0.0, 1.0);
        }
        p += (stealth(thief) - 0.5) * 0.7;
        return Util.Clamp(p, 0.05, 0.93);
    }

    /// <summary>Чем кончилась ночная кража.</summary>
    public enum ИсходКражи { УСПЕХ, ИЗГНАН, ОТПУГНУЛИ, ПОЙМАН, СОРВАЛОСЬ }

    /// <summary>Ночная кража (GDD 12.5). Тихий вариант отъёма.</summary>
    public static (ИсходКражи исход, Словарь<string, double> moved) steal(
        House h, NPC thief, NPC target)
    {
        var b = h.B;
        double p = theft_chance(h, thief, target);
        var пусто = Словари.Строкой<double>();

        thief.bump("попыток_кражи");
        h.bump("попыток_кражи");
        // свою первую кражу человек себе не забывает: после неё чужая дверь
        // перестаёт быть чужой (GDD 12.4)
        Социальное.переступил(h, thief, "кража");
        // и если он обещал к этой двери не подходить — слово нарушено
        Социальное.нарушил(h, thief, target, "не_делать", target.id);
        if (h.rng.Chance(p))
        {
            var moved = take_from(h, target, thief, greed: h.rng.Uni(0.25, 0.5),
                                  limit: b["кража_унос_макс"]);
            // топор стоит в прихожей, а хозяин спит в комнате. Редко, но это
            // самая злая пропажа из всех: наутро человек безоружен и не знает,
            // у кого теперь его топор
            string? унёс = унести_оружие(h, target, thief, b["оружие_при_краже"]);
            if (унёс is not null)
                moved[унёс] = 1;
            thief.memory.Add(new Память { день = h.day, вид = Вид.УКРАЛ, кто = target.id });
            thief.bump("краж");
            h.bump("краж");
            h.событие(ВидСобытия.КРАЖА, кто: thief.id, кому: target.id, где: target.apt,
                      сколько: moved.Count > 0
                               ? Util.Sum(moved.Ключи.Select(k => moved.Взять(k, 0.0)))
                               : 0.0);
            thief.mood = Util.Clamp(thief.mood - 4 * thief.t01("лояльность"));
            h.journal.secret($"ночью {thief.@short} вынес из кв.{target.apt}: {_fmt(moved)}");
            // хозяин обнаружит пропажу утром (GDD 4.4 — сводка дня)
            if (moved.Count > 0)
            {
                h.ожидает.пропажи.Add((thief.id, target.id));
                target.обокрали += 1;
            }
            return (ИсходКражи.УСПЕХ, moved);
        }

        // поймали
        Социальное.emit(h, target, 4, "ссора", night: true);
        bool caught_seen = h.rng.Chance(
            0.7 + 0.2 * (target.tonight == Ночь.ДЕЖУРИТЬ ? 1 : 0));
        h.bump("краж_сорвано");
        if (!caught_seen)
        {
            Социальное.register_incident(h, "кража",
                $"{target.@short} {Util.Vb(target.sex, "проснулся")} от возни в прихожей. "
                + "Кто-то убежал по лестнице.");
            target.panic = Util.Clamp(target.panic + b["паника_от_кражи_у_себя"]);
            suspect(h, target, exclude: null);
            return (ИсходКражи.СОРВАЛОСЬ, пусто);
        }

        Социальное.adjust(target, thief.id, trust: -4.0, hate: b["ненависть_за_кражу"],
                          aware: 20);
        target.memory.Add(new Память
        {
            день = h.day, вид = Вид.ПОЙМАЛ_ВОРА, кто = thief.id,
        });
        Социальное.register_incident(h, "кража",
            $"{target.@short} {Util.Vb(target.sex, "застал")} {thief.form("acc")} "
            + "у себя в квартире.");
        thief.поймали += 1;
        if (house_verdict(h, thief, target))
            return (ИсходКражи.ИЗГНАН, пусто);
        // GDD 17: угроза оружием часто ценнее выстрела. Хозяин со стволом
        // обычно просто выставляет вора, а не убивает его
        bool armed = Таблицы.ОГНЕСТРЕЛ.Contains(target.weapon)
                     && target.stock.Взять("патроны", 0.0) > 0;
        double scare = (0.62 + 0.03 * (10 - thief.trait("храбрость"))) / aggr(h);
        // решает хозяин: выйти со стволом или не связываться. Монета та же,
        // что бросалась здесь раньше
        if (armed && Решение.монета(h, target, Вопрос.ВЫЙТИ_СО_СТВОЛОМ,
                                    "выйти", "не выходить", scare))
        {
            h.journal.line(
                $"{target.@short} "
                + $"{(string.Equals(target.sex, "ж", StringComparison.Ordinal) ? "вышла" : Util.Vb(target.sex, "вышел"))} "
                + $"со стволом. {thief.@short} {Util.Vb(thief.sex, "ушёл")} "
                + "без разговоров.", 1);
            thief.panic = Util.Clamp(thief.panic + 18);
            Социальное.adjust(thief, target.id, hate: 12, aware: 15);
            // и это он запомнит надолго: к этой двери он больше не подойдёт
            // не потому, что раскаялся, а потому что видел ствол (GDD 17)
            Социальное.увидел_оружие(h, thief, target);
            return (ИсходКражи.ОТПУГНУЛИ, пусто);
        }
        // вор в первую очередь бежит, а не дерётся — драка тут крайний случай
        bool escaped = Решение.монета(h, thief, Вопрос.БЕЖАТЬ_ОТ_ХОЗЯИНА,
            "бежать", "остаться",
            Util.Clamp(0.35 + stealth(thief) * 0.5 - target.t01("храбрость") * 0.3,
                       0.1, 0.9));
        if (escaped)
            h.journal.line($"{thief.@short} {Util.Vb(thief.sex, "вырвался")} и "
                           + $"{Util.Vb(thief.sex, "убежал")} по лестнице.", 1);
        // а если не вырвался — хозяину решать, хватать его или дать уйти
        else if (Решение.по_правилу(h, target, Вопрос.ДРАТЬСЯ_С_ВОРОМ,
                     "драться", "отпустить",
                     (target.trait("вспыльчивость") >= 6
                      || target.power() > thief.power() * 1.3) ? 1.0 : 0.0))
            fight(h, new[] { target }, new[] { thief }, place: $"кв.{target.apt}",
                  reason: "вор в квартире");
        var видели = h.others(target)
            .Where(w => !string.Equals(w.id, thief.id, StringComparison.Ordinal)
                        && h.rng.Chance(0.5)).ToList();
        Социальное.judge(h, thief, "воровство", hate: 12.0, trust: -1.2, witnesses: видели);
        return (ИсходКражи.ПОЙМАН, пусто);
    }

    /// <summary>
    /// После второй-третьей поимки дом решает, что с вором делать.
    ///
    /// Пока есть кому собраться — решают на площадке, а не тут: выгнать
    /// человека на мороз должно быть решением людей, которые могут
    /// и не согласиться.
    /// </summary>
    public static bool house_verdict(House h, NPC thief, NPC victim)
    {
        double caught = thief.поймали;
        if (caught < 2)
            return false;
        if (h.others(thief).Count(p => p.health > 35) >= h.B["собрание_минимум"])
        {
            h.приговор_нужен = new Приговор { кто = thief.id, день = h.day };
            return false;
        }
        // выгнать человека на мороз — решение, которое дом принимает тяжело:
        // нужно, чтобы злы были почти все и чтобы сил хватило
        var judges = h.others(thief)
            .Where(p => p.hate.Взять(thief.id, 0.0) > 55 && p.health > 45).ToList();
        if (judges.Count < 3 && !(judges.Count == 2 && h.alive().Count <= 3))
            return false;
        if (Util.Sum(judges.Select(p => p.power())) < thief.power() * 1.8)
            return false;
        int hard = judges.Count(p => p.trait("лояльность") < 5
                                     || p.trait("вспыльчивость") > 7);
        if (!h.rng.Chance(0.18 + 0.12 * hard))
            return false;
        exile(h, thief, by: victim, reason: "воровство");
        return true;
    }

    /// <summary>Утреннее обнаружение пропажи и поиск виноватого.</summary>
    public static void notice_theft(House h, NPC victim, string? thief_id = null)
    {
        var b = h.B;
        victim.panic = Util.Clamp(victim.panic + b["паника_от_кражи_у_себя"]);
        victim.mood = Util.Clamp(victim.mood - 12);
        // теперь он знает: крадут
        victim.memory.Add(new Память { день = h.day, вид = Вид.ПРОПАЖА, кто = victim.id });
        Социальное.register_incident(h, "кража",
            $"{victim.label()} {Util.Vb(victim.sex, "обнаружил")}, что запасы стали меньше.");
        suspect(h, victim, exclude: null, real: thief_id);
    }

    /// <summary>Кого обвинят. Здесь и рождаются несправедливые обиды.</summary>
    public static NPC? suspect(House h, NPC victim, NPC? exclude = null, string? real = null)
    {
        var b = h.B;
        var pool = new List<(NPC кто, double вес)>();
        foreach (var other in h.others(victim))
        {
            double w = 1.0;
            w += victim.hate.Взять(other.id, 0.0) / 20.0;
            w += (5.0 - victim.trust.Взять(other.id, 3.0)) * 0.4;
            // репутация вора: того, кого уже ловили. Раньше здесь стояло число
            // удавшихся краж — то есть ровно то, чего дом про человека не знает:
            // чем чище он работал, тем охотнее его подозревали
            w += other.поймали * 2.5;
            // кого называли вором ПРИ НЁМ, того и подозревают: слово в общем
            // чате работает как наговор (GDD 14). По своей памяти, а не
            // по счёту дома: разговор, которого он не слышал, на его мысли
            // не влияет
            if (victim.слышал_обвинение(other.id, h.day, b["чат_подозрение_дней"]))
                w += b["чат_подозрение_вес"];
            // голодного подозревают охотнее — того, кто голоден ПО МОИМ
            // СВЕДЕНИЯМ: оценка его запаса на его семью, и чем меньше я о нём
            // знаю, тем ближе она к «как у меня»
            w += Math.Max(0.0, 1.0 - Социальное.believed_days(victim, other, "еда") / 4.0) * 2.0;
            if (victim.memory.Any(m => m.вид == Вид.СЛЫШАЛ
                                       && string.Equals(m.кто, other.id, StringComparison.Ordinal)
                                       && m.день == h.day))
                w += 1.5;
            w = Math.Max(0.05, w);
            pool.Add((other, w));
        }
        if (pool.Count == 0)
            return null;
        pool = pool.OrderByDescending(x => x.вес).ToList();
        // если никто не выделяется — человек просто не знает, на кого думать
        if (pool.Count > 1 && pool[0].вес < pool[1].вес * 1.6)
        {
            h.journal.line($"{victim.@short} не {Util.Vb(victim.sex, "понял")}, "
                           + "кто это был.", 1);
            foreach (var (other, _) in pool.Take(2))
                Социальное.adjust(victim, other.id, trust: -0.6, hate: 5);
            return null;
        }
        var accused = h.rng.Weighted(pool)!;
        Социальное.adjust(victim, accused.id, trust: -2.5,
                          hate: b["ненависть_за_подозрение"]);
        bool right = real is not null
                     && string.Equals(accused.id, real, StringComparison.Ordinal);
        h.journal.line($"{victim.@short} {Util.Vb(victim.sex, "уверен")}, "
                       + $"что это {accused.@short}." + (right ? "" : " (а это был не он)"), 1);
        if (!right)
        {
            h.bump("ложных_обвинений");
            h.note($"{victim.@short} обвинил {accused.@short} напрасно");
        }
        // обвинение расходится по дому
        foreach (var w in h.others(victim))
        {
            if (string.Equals(w.id, accused.id, StringComparison.Ordinal))
                continue;
            if (h.rng.Chance(0.4 + 0.05 * victim.trait("общительность")))
                Социальное.adjust(w, accused.id, trust: -0.8, hate: 8);
        }
        // обвинённому тоже обидно
        Социальное.adjust(accused, victim.id, trust: -1.5, hate: 10);
        return accused;
    }

    /// <summary>Дом узнал. Дальше человек в этом доме не жилец —
    /// так или иначе.</summary>
    public static void reveal_taboo(House h, NPC eater, NPC? witness = null)
    {
        if (eater.раскрыт)
        {
            // уже знают; но пока он продолжает, дом снова и снова
            // возвращается к вопросу
            _verdict_taboo(h, eater);
            return;
        }
        eater.раскрыт = true;
        var b = h.B;
        if (witness is not null)
            h.journal.line($"{witness.@short} {Util.Vb(witness.sex, "увидел")}, "
                           + $"что у {eater.form("gen")} в кастрюле. Дом узнал к вечеру.", 2);
        else
            h.journal.line($"К вечеру весь подъезд знал, чем питается {eater.@short}.", 2);
        h.note($"дом узнал про {eater.form("acc")}");
        h.bump("раскрытых_людоедов");
        Социальное.register_incident(h, "людоедство", null);
        foreach (var p in h.others(eater))
        {
            Социальное.adjust(p, eater.id, trust: -9.0, hate: b["людоедство_ненависть"]);
            p.allies.Remove(eater.id);
            eater.allies.Remove(p.id);
        }
        Социальное.judge(h, eater, "табу", hate: b["людоедство_ненависть"] * 0.4,
                         trust: -2.0);
        Социальное.house_shock(h, panic: b["людоедство_паника_дома"], mood: -22);
        _verdict_taboo(h, eater);
    }

    /// <summary>
    /// Что дом делает с тем, про кого узнал. Одно место на два случая:
    /// людоед и убийца соседа — разница только в словах.
    ///
    /// Решают люди, а не код. Дом выносит вопрос на площадку, и там он может
    /// не собраться, не договориться и сорваться в ссору. Автоматика осталась
    /// на случай, когда собирать уже некого — иначе людоед в доме на двоих
    /// стал бы неприкосновенным.
    /// </summary>
    public static void приговор_дома(House h, NPC кого, string повод,
                                     string причина_изгнания)
    {
        if (!кого.здесь())
            return;
        var judges = h.others(кого).Where(p => p.health > 35).ToList();
        foreach (var p in judges)
            Социальное.adjust(p, кого.id, trust: -3.0, hate: 15);
        if (judges.Count >= h.B["собрание_минимум"])
        {
            h.приговор_нужен = new Приговор { кто = кого.id, день = h.day };
            h.journal.line($"С {кого.form("ins")} больше никто не разговаривает. "
                           + "Дом молчит и ждёт, что скажут все.", 2);
            return;
        }
        if (judges.Count >= 2
            && Util.Sum(judges.Select(p => p.power())) > кого.power() * 1.2)
        {
            if (h.rng.Chance(0.55))
                exile(h, кого, by: judges[0], reason: причина_изгнания);
            else if (h.rng.Chance(0.45))
            {
                h.journal.line($"За {кого.form("ins")} пришли ночью.", 2);
                fight(h, judges, new[] { кого }, place: $"кв.{кого.apt}",
                      reason: "приговор дома");
            }
        }
        else if (judges.Count >= 1)
            // сил выгнать нет — просто перестают существовать друг для друга
            h.journal.line($"С {кого.form("ins")} больше никто не разговаривает.", 1);
    }

    private static void _verdict_taboo(House h, NPC eater)
        => приговор_дома(h, eater, "людоедство",
            $"то, что нашли у {(string.Equals(eater.sex, "ж", StringComparison.Ordinal) ? "неё" : "него")} в квартире");
}
