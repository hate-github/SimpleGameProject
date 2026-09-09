// Перенос house/conflict.py, строки 341–508 и 781–1010: выбытие из дома.
//
// Три разных выхода, и дом узнаёт о них по-разному. Смерть — её видят только
// те, кто был за той же дверью. Изгнание — его видят все, потому что человека
// выводят на глазах у дома. Уход к пункту обогрева — дом видит, как он уходит
// с мешком, и не узнаёт никогда, дошёл ли.
//
// Общее у всех трёх — квартира: она не заводится и не исчезает, она была
// всегда, и после выбытия просто остаётся открытой.

namespace Дом.Ядро;

public static partial class Конфликт
{
    /// <summary>
    /// Дом выставляет человека за дверь. Почти всегда — смертный приговор,
    /// но руки формально чистые (GDD 12.5).
    /// </summary>
    public static void exile(House h, NPC person, NPC? by = null, string reason = "воровство")
    {
        person.exiled = true;
        person.cause = $"{Util.Vb(person.sex, "изгнан")} из дома ({reason})";
        person.died_day = h.day;
        h.bump("изгнаний");
        h.событие(ВидСобытия.ИЗГНАНИЕ, кто: by?.id, кому: person.id, что: reason,
                  где: person.apt);
        // выставили на глазах у всех: об этом знает каждый, кто в доме
        foreach (var w in h.alive())
            if (!string.Equals(w.id, person.id, StringComparison.Ordinal))
                w.memory.Add(new Память
                {
                    день = h.day, вид = Вид.ВИДЕЛ_ИЗГНАНИЕ, кто = person.id,
                });
        string who = by is not null ? $"{by.@short} и остальные" : "соседи";
        h.journal.line($"{who} вывели {person.form("acc")} на улицу "
                       + "и закрыли дверь подъезда.", 2);
        h.note($"{person.@short} {Util.Vb(person.sex, "изгнан")} ({reason})");
        Социальное.house_shock(h, panic: 10, mood: -12);
        Сожительство.cut_ties(h, person);
        var flat = release_flat(h, person);
        foreach (string res in person.stock.Ключи)
            flat.stock[res] = flat.stock.Взять(res, 0.0) + person.stock.Взять(res, 0.0);
        person.stock.Очистить();
        // на мороз выставляют с пустыми руками: и запасы, и оружие, и ключи
        // от погреба остаются в доме
        сложить_оружие(h, person, flat);
        foreach (string k in person.ключи_кладовых)
            flat.ключи.Add(k);
        person.ключи_кладовых.Clear();
        if (person.dependents > 0)
            _orphan(h, person);
    }

    /// <summary>
    /// Человек ушёл к пункту обогрева и не вернулся (GDD 21).
    ///
    /// Дошёл он до школы №7 или замёрз на объездной — дом не узнаёт никогда,
    /// и это правильно: изнутри подъезда оба исхода выглядят одинаково.
    /// </summary>
    public static void уйти_из_дома(House h, NPC person)
    {
        var b = h.B;
        person.exiled = true;          // для всего кода это обычное выбытие
        person.ушёл = true;
        person.died_day = h.day;
        string с_кем = person.dependents > 0
            ? " с " + (person.dependent_ins.Length > 0 ? person.dependent_ins
                                                       : person.dependent_name)
            : "";
        person.cause = Util.Vb(person.sex, "ушёл")
                       + (с_кем.Length > 0 ? с_кем : " к пункту обогрева")
                       + ", не " + Util.Vb(person.sex, "вернулся");
        h.bump("ушедших");
        var свои = new List<string>();
        foreach (string k in person.stock.Ключи.OrderBy(x => x, StringComparer.Ordinal))
        {
            double v = person.stock.Взять(k, 0.0);
            if (v >= 0.05)
                свои.Add($"{k} {Текст.Ф(v, 1)}".TrimEnd('0').TrimEnd('.'));
        }
        string список = string.Join(", ", свои);
        h.journal.line($"{person.@short} {Util.Vb(person.sex, "ушёл")}{с_кем} к школе №7. "
                       + $"{(string.Equals(person.sex, "ж", StringComparison.Ordinal) ? "Взяла" : "Взял")} "
                       + $"что {Util.Vb(person.sex, "смог")} унести"
                       + (список.Length > 0 ? $": {список}." : "."), 2);
        h.note($"{person.@short} ушёл к пункту обогрева" + с_кем);
        // дом видел, как он уходил с мешком: это все шесть окон разом
        Социальное.house_shock(h, panic: b["уйти_паника_дома"],
                               mood: b["уйти_настроение_дома"]);
        Сожительство.cut_ties(h, person);
        // то, что унёс, уходит из мира — иначе учёт не сойдётся с первого ухода
        double несёт = b["уйти_унесёт"] * (1.0 + 0.5 * person.dependents);
        var осталось = Словари.Строкой<double>();
        foreach (string k in person.stock.Ключи)
            осталось[k] = person.stock.Взять(k, 0.0);
        var взял = Словари.Строкой<double>();
        foreach (string res in осталось.Ключи.OrderBy(x => x, StringComparer.Ordinal).ToList())
        {
            if (несёт <= 0)
                break;
            double v = Math.Min(осталось.Взять(res, 0.0), несёт);
            if (v > 0)
            {
                взял[res] = v;
                осталось[res] = осталось.Взять(res, 0.0) - v;
                несёт -= v;
            }
        }
        foreach (string res in взял.Ключи)
            h.stats["унесено_" + res] = h.stats.Взять("унесено_" + res, 0.0)
                                        + взял.Взять(res, 0.0);
        // квартира остаётся открытой и брошенной. `owner_died` не ставим:
        // никто здесь не умирал, и знание о смерти эту дверь не запирает
        var flat = release_flat(h, person, чья_смерть: false);
        foreach (string res in осталось.Ключи)
        {
            double v = осталось.Взять(res, 0.0);
            if (v > 0)
                flat.stock[res] = flat.stock.Взять(res, 0.0) + v;
        }
        person.stock.Очистить();
        // оружие он забирает с собой: в такую дорогу без ножа не выходят.
        // Отдельным счётчиком, иначе оружие «исчезает» из баланса
        if (person.weapon != Оружие.НЕТ)
        {
            h.stats["оружия_унесено"] = h.stats.Взять("оружия_унесено", 0.0) + 1;
            person.weapon = Оружие.НЕТ;
        }
        // а ключ от погреба остаётся на гвозде: ему он больше ни к чему
        foreach (string k in person.ключи_кладовых)
            flat.ключи.Add(k);
        person.ключи_кладовых.Clear();
        bool дошёл = h.rng.Chance(b["пункт_дошёл"]);
        h.journal.secret($"{person.@short} " + (дошёл
            ? Util.Vb(person.sex, "дошёл") + " до школы №7"
            : Util.Vb(person.sex, "замёрз") + " на объездной, не дойдя"));
        h.bump(дошёл ? "дошли_до_пункта" : "замёрзли_по_дороге");
        person.дошёл = дошёл;
    }

    /// <summary>
    /// Квартира выбывшего. Заводить её больше не нужно — она была всегда.
    /// Пустой её делает не запись в списке, а то, что в ней никто не живёт.
    /// </summary>
    public static Flat release_flat(House h, NPC person, bool чья_смерть = true)
    {
        var flat = h.flats[person.apt];
        if (чья_смерть)
            flat.owner_died ??= person.id;
        // после того как оттуда вынесли человека, дверь так и остаётся
        // открытой: ломать её больше не нужно никому
        flat.открыта = true;
        return flat;
    }

    /// <summary>
    /// Кто узнаёт о смерти сразу, без слухов.
    ///
    /// Убийца знает, потому что убил. Тот, кто спал за той же дверью, знает,
    /// потому что утром не смог его добудиться. Остальные не знают ничего:
    /// человек умер один в своей квартире на пятом этаже.
    /// </summary>
    public static HashSet<string> свидетели_смерти(House h, NPC dead, NPC? killer = null)
    {
        var кто = new HashSet<string>(StringComparer.Ordinal);
        if (killer is not null)
            кто.Add(killer.id);
        foreach (var p in h.alive())
            if (!string.Equals(p.id, dead.id, StringComparison.Ordinal)
                && h.под_одной_крышей(p, dead))
                кто.Add(p.id);
        return кто;
    }

    /// <summary>Слышали все: выстрел на лестнице, выломанная дверь, драка
    /// в подъезде.</summary>
    public static HashSet<string> весь_дом(House h)
        => new(h.alive().Select(p => p.id), StringComparer.Ordinal);

    /// <summary>
    /// Единственный путь смерти: поля, строка в журнал и то, что смерть
    /// делает с домом.
    ///
    /// Семь мест ставили alive/health/cause/died_day руками и звали
    /// <c>on_death</c> каждое по-своему. <paramref name="свидетели"/> — кто
    /// видел это сам: считает вызывающий, иначе изменится, кто что узнал.
    /// </summary>
    public static void умер(House h, NPC кто, string причина, NPC? killer = null,
                            Оружие? оружие = null, IReadOnlySet<string>? свидетели = null,
                            string? строка = null)
    {
        кто.health = 0.0;
        кто.alive = false;
        кто.cause = причина;
        кто.died_day = h.day;
        h.событие(ВидСобытия.СМЕРТЬ, кто: кто.id, кому: killer?.id, что: причина,
                  где: кто.apt);
        if (строка is not null && строка.Length > 0)
            h.journal.line(строка, 2);
        on_death(h, кто, killer: killer, оружие: оружие, свидетели: свидетели);
    }

    /// <summary>
    /// Смерть в доме: паника, настроение, осиротевший ребёнок, пустая
    /// квартира. Зовётся только из <c>умер</c>.
    ///
    /// Всё, что смерть делает с домом, достаётся видевшим, а не всем разом.
    /// Остальные узнают слухом или не узнают вовсе.
    /// </summary>
    public static void on_death(House h, NPC dead, NPC? killer = null, bool quiet = false,
                                Оружие? оружие = null, IReadOnlySet<string>? свидетели = null)
    {
        var b = h.B;
        h.bump("смертей");
        свидетели ??= свидетели_смерти(h, dead, killer);
        var видевшие = h.alive().Where(p => свидетели.Contains(p.id)).ToList();
        if (killer is not null)
            foreach (var p in видевшие)
            {
                if (string.Equals(p.id, killer.id, StringComparison.Ordinal))
                    continue;
                Социальное.adjust(p, killer.id, trust: -2.5,
                                  hate: 25 + 15 * p.t01("лояльность"));
                // ненависть к убийце дом чувствует весь; страх — отдельно
                // и сильнее, и зависит от того, чем убили (GDD 11, 17)
                Социальное.видел_убийство(h, p, killer, оружие ?? killer.weapon);
            }
        // мёртвый выпадает из всех союзов и из чужих квартир
        Сожительство.cut_ties(h, dead);
        foreach (var p in видевшие)
            Социальное.узнал_о_смерти(h, p, dead);
        h.note($"{dead.@short}: {dead.cause}");

        // ребёнок остаётся один (GDD 12.6: семья как моральный центр)
        if (dead.dependents > 0)
            _orphan(h, dead);

        // квартира становится пустой и доступной (GDD 12.2)
        var flat = release_flat(h, dead);
        foreach (string res in dead.stock.Ключи)
            flat.stock[res] = flat.stock.Взять(res, 0.0) + dead.stock.Взять(res, 0.0);
        // его тулуп остался висеть в прихожей: вещь, которая ему уже не нужна,
        // а кому-то откроет улицу ещё на неделю
        if (dead.одежда >= b["одежда_максимум"])
            flat.тулуп = true;
        // и оружие — там же, в углу за дверью. Тулуп с мёртвого снимали
        // и раньше, а топор исчезал вместе с телом
        сложить_оружие(h, dead, flat);
        // и ключ от погреба — на гвозде в прихожей: иначе запас в подвале
        // выпадает из игры вместе с хозяином
        foreach (string k in dead.ключи_кладовых)
            flat.ключи.Add(k);
        dead.ключи_кладовых.Clear();
        flat.body = new Тело
        {
            кто = dead.@short, вин = dead.form("acc"), падеж = dead.form("gen"),
            день = h.day, порций = b["тело_порций"],
        };
        dead.stock.Очистить();
    }

    /// <summary>
    /// Единственное честное завершение детской шкалы (GDD 12.6).
    ///
    /// Правило ГДД остаётся в силе: ребёнок никогда не становится едой
    /// и его тела в мире не появляется. Но смерть от холода и болезни
    /// возможна — и она ломает мать сильнее любого другого события:
    /// у неё опускается даже пол нормальности.
    /// </summary>
    public static void смерть_ребёнка(House h, NPC кто, Ребёнок р)
    {
        var b = h.B;
        кто.дети.Remove(р);
        кто.dependents = Math.Max(0, кто.dependents - 1);
        if (кто.дети.Count == 0)
        {
            кто.dependent_name = "";
            кто.dependent_acc = "";
        }
        h.bump("смертей_детей");
        кто.bump("потерял_ребёнка");
        string причина = р.болен is not null
            ? "от болезни"
            : (р.сытость <= р.тепло ? "от голода" : "от холода");
        h.journal.line($"† {р.имя} умер {причина}. {кто.@short} "
            + $"{(string.Equals(кто.sex, "ж", StringComparison.Ordinal) ? "сидела" : "сидел")} "
            + "рядом до утра.", 2);
        h.note($"{р.имя} умер {причина} ({кто.form("gen")})");
        кто.mood = Util.Clamp(кто.mood - b["ребёнок_смерть_настроение"]);
        Социальное.add_panic(кто, b["ребёнок_смерть_паника"]);
        кто.нормальность_пол = Math.Max(0.0, кто.нормальность_пол - b["ребёнок_смерть_пол"]);
        кто.normalcy = Util.Clamp(кто.normalcy - b["ребёнок_смерть_нормальность"],
                                  кто.нормальность_пол, 1.0);
        // и то, чего ей уже не забыть: кто отказал, когда она просила
        foreach (var o in h.others(кто))
        {
            double отказов = кто.asking.Есть(o.id) ? кто.asking.Взять(o.id, null!).отказали : 0.0;
            if (отказов > 0)
                Социальное.adjust(кто, o.id,
                    hate: b["ребёнок_смерть_ненависть"] * Math.Min(2.0, отказов),
                    trust: -2.0);
        }
        Социальное.house_shock(h, panic: b["ребёнок_смерть_дом_паника"],
                               mood: b["ребёнок_смерть_дом_настроение"]);
        Социальное.register_incident(h, "смерть_ребёнка", null);
        foreach (var p in h.alive())
            Социальное.видел(h, p, b["нормальность_за_смерть"]);
    }

    /// <summary>
    /// Кого-то надо взять к себе. Или не взять.
    ///
    /// Ребёнок переходит вместе со своей шкалой: он тот же самый, промёрзший
    /// и голодный ровно настолько, насколько был при матери.
    /// </summary>
    public static void _orphan(House h, NPC dead)
    {
        var дети = dead.дети.Count > 0
            ? new List<Ребёнок>(dead.дети)
            : new List<Ребёнок>
            {
                new()
                {
                    имя = dead.dependent_name.Length > 0 ? dead.dependent_name : "ребёнок",
                    вин = dead.dependent_acc.Length > 0 ? dead.dependent_acc
                          : (dead.dependent_name.Length > 0 ? dead.dependent_name : "ребёнка"),
                    род = dead.dependent_gen.Length > 0 ? dead.dependent_gen
                          : (dead.dependent_name.Length > 0 ? dead.dependent_name : "ребёнка"),
                    твор = dead.dependent_ins.Length > 0 ? dead.dependent_ins
                           : (dead.dependent_name.Length > 0 ? dead.dependent_name : "ребёнком"),
                    сытость = 60.0, тепло = 55.0, здоровье = 80.0,
                },
            };
        dead.дети.Clear();
        dead.dependents = 0;
        foreach (var р in дети)
        {
            var candidates = new List<(NPC, double)>();
            foreach (var p in h.alive())
            {
                double w = p.trait("лояльность") * 1.8 + p.trust.Взять(dead.id, 3.0) * 0.8
                           - p.desperation() * 3.5;
                w += p.skills.Contains("медик", StringComparer.Ordinal) ? 2.0 : 0.0;
                if (w > 0)
                    candidates.Add((p, w));
            }
            if (candidates.Count > 0)
            {
                var taker = h.rng.Weighted(candidates)!;
                taker.dependents += 1;
                taker.дети.Add(р);
                taker.dependent_name = р.имя;
                taker.mood = Util.Clamp(taker.mood + 6);
                // все четыре формы, а не две: без родительного и творительного
                // у нового родителя выходило «просидела рядом с Ваня»
                taker.dependent_acc = р.вин;
                taker.dependent_gen = р.род.Length > 0 ? р.род : р.имя;
                taker.dependent_ins = р.твор.Length > 0 ? р.твор : р.имя;
                h.journal.line($"{р.имя} остался один. {taker.@short} "
                               + $"{Util.Vb(taker.sex, "забрал")} его к себе.", 2);
                h.note($"{taker.@short} {Util.Vb(taker.sex, "взял")} {р.вин}");
                foreach (var p in h.alive())
                    Социальное.adjust(p, taker.id, trust: 1.0);
            }
            else
            {
                h.journal.line($"{р.имя} остался один. Никто не взял.", 2);
                h.note($"{р.имя} остался один — никто не взял");
                Социальное.house_shock(h, panic: 10, mood: -14);
                h.bump("детей_брошено");
            }
        }
    }

    /// <summary>
    /// Утро после ночи у соседа: ребёнка забирают. Или не отдают.
    ///
    /// Не благотворительность. Если принявший считает мать не жильцом —
    /// то есть несколько дней подряд видел её такой, что списал со счетов, —
    /// ребёнок остаётся у него. Механизм тот же, которым дети переходят
    /// после смерти, только повод другой и мать ещё жива.
    /// </summary>
    public static void вернуть_детей(House h)
    {
        var b = h.B;
        foreach (var мать in h.alive().ToList())
            foreach (var р in мать.дети.ToList())
            {
                var хозяин = string.IsNullOrEmpty(р.у) ? null : h.живой(р.у);
                р.у = null;
                if (хозяин is null)
                    continue;
                // не отдать чужого ребёнка — решение, а не следствие
                // приговора. Нужно и то, что принявший считает мать
                // не жильцом, и то, что ему есть чем его кормить, и то,
                // что жизнь для него уже достаточно не своя, чтобы взять
                // чужое живое
                double оставит = 0.0;
                if (хозяин.не_жилец.Contains(мать.id))
                    оставит = хозяин.t01("лояльность") * b["невозврат_лояльность"]
                              + (1.0 - хозяин.normalcy) * b["невозврат_распад"]
                              + (хозяин.ценит("ребёнок") ? b["невозврат_ценность"] : 0.0)
                              - хозяин.desperation() * b["невозврат_нужда"]
                              - хозяин.свой(мать.id) * b["невозврат_близость"]
                              - хозяин.боится(мать.id) * 3.0
                              - b["невозврат_решимость"];
                if (оставит > 0)
                {
                    мать.дети.Remove(р);
                    мать.dependents = Math.Max(0, мать.dependents - 1);
                    if (мать.дети.Count == 0)
                    {
                        мать.dependent_name = "";
                        мать.dependent_acc = "";
                    }
                    хозяин.dependents += 1;
                    хозяин.дети.Add(р);
                    хозяин.dependent_name = р.имя;
                    хозяин.dependent_acc = р.вин;
                    хозяин.dependent_gen = р.род.Length > 0 ? р.род : р.имя;
                    хозяин.dependent_ins = р.твор.Length > 0 ? р.твор : р.имя;
                    h.bump("детей_не_вернули");
                    h.journal.line($"{хозяин.@short} не {Util.Vb(хозяин.sex, "отдал")} "
                        + $"{р.вин} утром: {Util.Vb(хозяин.sex, "сказал")}, "
                        + $"что {мать.@short} его не выходит.", 2);
                    h.note($"{хозяин.@short} оставил"
                        + (string.Equals(хозяин.sex, "ж", StringComparison.Ordinal)
                           ? "а" : "")
                        + $" {р.вин} у себя");
                    Социальное.adjust(мать, хозяин.id, hate: 45, trust: -4.0);
                    Социальное.обидели(h, мать, хозяин, b["обида_за_ребёнка"]);
                    мать.mood = Util.Clamp(мать.mood - 20);
                    // дом это видит и делится: одни считают, что так и надо
                    foreach (var w in h.others(хозяин))
                        if (!string.Equals(w.id, мать.id, StringComparison.Ordinal))
                            Социальное.adjust(w, хозяин.id,
                                              trust: w.ценит("ребёнок") ? 1.0 : -1.0);
                }
                else
                {
                    h.bump("детей_вернули");
                    h.journal.line($"Утром {мать.@short} {Util.Vb(мать.sex, "забрал")} "
                                   + $"{р.вин} обратно.", 1);
                    Социальное.сблизились(h, мать, хозяин, b["близость_за_ночёвку"]);
                    Социальное.adjust(мать, хозяин.id, trust: b["доверие_за_ночёвку"]);
                }
            }
    }
}
