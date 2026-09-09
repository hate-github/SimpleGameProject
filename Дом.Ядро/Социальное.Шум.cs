// Перенос house/social.py, раздел «шум»: как правда становится знанием
// с ошибкой (GDD 13).
//
// Восприятие здесь — не объект, а функция: шум, запах и взгляд превращают
// то, что есть на самом деле, в то, что человек об этом думает. Порядок
// бросков в этом файле — часть поведения: он решает, кто что услышал.

namespace Дом.Ядро;

public static partial class Социальное
{
    /// <summary>
    /// Насколько дом слышит и видит сам себя сегодня (GDD 3, 13).
    ///
    /// В буран ветер глушит всё: никто ничего не слышит и не чует, дом
    /// на сутки становится пятнадцатью отдельными квартирами. В затишье
    /// слышно каждый шаг на лестнице — и именно поэтому в затишье решается
    /// неделя.
    /// </summary>
    public static double слышимость(House h)
    {
        if (h.режим == Режим.БУРАН)
            return h.B["буран_шум"];
        if (h.режим == Режим.ЗАТИШЬЕ)
            return h.B["затишье_шум"];
        return 1.0;
    }

    /// <summary>
    /// Издать шум. Возвращает список тех, кто услышал.
    /// level 1..5 (GDD 13: тихо / средне / громко).
    /// </summary>
    public static List<NPC> emit(House h, NPC src, int level, string kind,
                                 bool night = false, NPC? text_for = null)
    {
        var heard = new List<NPC>();
        if (level <= 0)
            return heard;
        var b = h.B;
        string ключ = ((int)level).ToString(System.Globalization.CultureInfo.InvariantCulture);
        double @base = b.Таблица("шум_слышимость").Взять(ключ, 0.5);
        // дальность зависит от громкости: выстрел слышит весь стояк, шаги —
        // соседняя площадка. Раньше затухание было одинаковым для шёпота
        // и для выстрела, и «громко» из GDD 13 на практике означало меньше
        // половины дома
        double дальность = b.Таблица("шум_дальность").Взять(ключ, 1.0);
        double видно = ЧтоЗначит(kind).видно;
        // погода за окном глушит и звук, и вид: в буран не слышно и не видно
        double погода = слышимость(h);
        видно *= погода;
        // шумит не человек, а квартира, в которой он сейчас сидит
        var дом = место(h, src);
        foreach (var other in h.others(src))
        {
            if (other.away)
                continue;
            // сосед по комнате слышит всё и без броска: он в двух метрах
            if (string.Equals(место(h, other).id, дом.id, StringComparison.Ordinal))
            {
                heard.Add(other);
                _hear(h, other, src, kind, level, дом);
                continue;
            }
            double p = @base * погода;
            p *= Math.Pow(b["шум_затухание_на_этаж"],
                          h.floor_gap(дом, место(h, other)) / Math.Max(0.35, дальность));
            if (night)
            {
                // ночью фон тише: звук идёт дальше, но спящий может его
                // пропустить, а громкое (4-5) будит всех
                p *= b["шум_ночью_множитель"];
                if (other.tonight == Ночь.СПАТЬ && level < 4)
                    p *= b["шум_спящий"];
            }
            if (дом.shelter.Взять("звукоизоляция", 0.0) != 0.0)
                p *= b["шум_звукоизоляция"];
            // закрытые окна из GDD 13: утеплённые окна и тепло держат, и скрывают
            double окна = Math.Max(0.35,
                1.0 - b["шум_за_утепление"] * дом.shelter.Взять("утепление", 0.0));
            p *= окна;
            // пустая квартира МЕЖДУ источником и слушателем глушит (GDD 13).
            // Раньше проверялось только соседство с источником, поэтому
            // множитель был одинаков для всех и к третьей неделе включён всегда
            var сл = место(h, other);
            int низ = Math.Min(дом.floor, сл.floor), верх = Math.Max(дом.floor, сл.floor);
            int между = 0;
            foreach (var f in h.пустые())
                if (низ <= f.floor && f.floor <= верх)
                    между++;
            if (между != 0)
                p *= Math.Pow(b["шум_пустая_квартира"], между);
            // заметность идёт своим каналом: по этажам не глохнет, прячут занавески
            bool замечено = видно > 0 && h.rng.Chance(Util.Clamp(видно * окна, 0.0, 0.9));
            if (h.rng.Chance(Util.Clamp(p, 0.0, 0.97)) || замечено)
            {
                heard.Add(other);
                _hear(h, other, src, kind, level, дом);
            }
        }
        if (heard.Count > 0 && level >= 4)
            foreach (var p in heard)
                p.panic = Util.Clamp(p.panic + b["паника_от_громкого_шума"]);
        return heard;
    }

    /// <summary>
    /// Запах готовки идёт по подъезду. Возвращает тех, кто учуял.
    ///
    /// Отличия от звука: вверх по стояку доходит лучше, чем вниз;
    /// звукоизоляция не помогает; спящий учует утром — запах не исчезает
    /// вместе со звуком.
    /// </summary>
    public static List<NPC> smell(House h, NPC src, bool hot = false)
    {
        var b = h.B;
        var дом = место(h, src);
        var caught = new List<NPC>();
        foreach (var other in h.others(src))
        {
            if (other.away)
                continue;
            bool своя_комната = string.Equals(место(h, other).id, дом.id, StringComparison.Ordinal);
            int gap = место(h, other).floor - дом.floor;
            double p = b["запах_база"] * (hot ? 1.15 : 1.0) * слышимость(h);
            p *= gap >= 0 ? Math.Pow(b["запах_вверх"], gap)
                          : Math.Pow(b["запах_вниз"], Math.Abs(gap));
            // тот, кто сидит у той же печки, чует наверняка
            if (!своя_комната && !h.rng.Chance(Util.Clamp(p, 0.0, 0.95)))
                continue;
            caught.Add(other);
            // «у меня нет еды» — и через час по подъезду тянет его ужином
            проверить_ложь(h, src, other, "своё", "еда");
            adjust(other, src.id, aware: b["запах_осведомлённость"]);
            double cur = other.believed(src.id, "еда");
            note_signal(other, src.id, "еда", Math.Min(cur + 1.8, 7.0), 0.5);
            other.memory.Add(new Память { день = h.day, вид = Вид.УЧУЯЛ, кто = src.id });
            // голодный человек, которому пахнет чужим ужином, злится по-настоящему
            double hunger = Util.Clamp((55 - other.satiety) / 55.0, 0.0, 1.0);
            if (hunger > 0.1)
            {
                adjust(other, src.id, hate: b["запах_зависть"] * hunger);
                other.mood = Util.Clamp(other.mood - 2.0 * hunger);
                other.panic = Util.Clamp(other.panic + 1.5 * hunger);
            }
        }
        // в журнале это событие, а не бытовой шум: пары строк за день достаточно
        var seen = h.сутки.запах_журнал;
        if (seen.Взять("день", double.NaN) != h.day)
        {
            seen.Очистить();
            seen["день"] = h.day;
        }
        var hungry = caught.Where(p => p.satiety < 55).ToList();
        if (caught.Count > 0 && seen.Взять("строк", 0.0) < 2
            && (hungry.Count > 0 || h.rng.Chance(0.35)))
        {
            seen["строк"] = seen.Взять("строк", 0.0) + 1;
            string who = string.Join(", ", caught.Select(p => p.@short));
            string tail = hungry.Count >= 2 ? " — и это слышно по их лицам" : "";
            h.journal.line($"По подъезду тянет едой из кв.{дом.apt}. Учуяли: {who}.{tail}", 1);
        }
        return caught;
    }

    /// <summary>
    /// Услышал — значит узнал. Осведомлённость растёт (GDD 12.3).
    ///
    /// Заодно это и есть сводка дня из GDD 4.4: сосед запоминает не «Виктор
    /// топил буржуйку», а «из окна кв.14 идёт дым». Формулировки лежат
    /// в третьем поле <see cref="NOISE_MEANING"/>.
    /// </summary>
    public static void _hear(House h, NPC listener, NPC src, string kind, int level,
                             NPC? дом = null)
    {
        var b = h.B;
        дом ??= src;
        var з = ЧтоЗначит(kind);
        if (з.как_видно.Length > 0)
        {
            string строка = з.как_видно
                .Replace("{apt}", дом.apt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                         StringComparison.Ordinal)
                .Replace("{кто}", src.@short, StringComparison.Ordinal)
                .Replace("{вернулся}", Util.Vb(src.sex, "вернулся"), StringComparison.Ordinal)
                .Replace("{спускался}", Util.Vb(src.sex, "спускался"), StringComparison.Ordinal);
            if (!h.ожидает.сводка.TryGetValue(listener.id, out var сводка))
            {
                сводка = new List<string>();
                h.ожидает.сводка[listener.id] = сводка;
            }
            if (!сводка.Contains(строка, StringComparer.Ordinal))
                сводка.Add(строка);
        }
        double gain = b["осведомлённость_за_шум"] * (0.6 + 0.15 * level);
        adjust(listener, src.id, aware: gain);
        // дым из окна — против «у меня дров нет»
        if (kind is "буржуйка" or "генератор")
            проверить_ложь(h, src, listener, "своё", "топливо");
        // про воду прямых сигналов не бывает — её можно только вывести: у кого
        // горит печь, тот наверняка топит на ней и снег, потому что это выгодно.
        // Догадаться дано не каждому, и это единственное место, где решает
        // сообразительность
        if (kind is "буржуйка" or "генератор" && !h.water_on)
        {
            if (h.rng.Chance(listener.t01("сообразительность") * b["догадка_про_воду"]))
            {
                note_signal(listener, src.id, "вода",
                            Math.Min(listener.believed(src.id, "вода") + 1.4, 5.0), 0.35);
                listener.memory.Add(new Память
                    { день = h.day, вид = Вид.ДОГАДАЛСЯ, кто = src.id, что = "вода" });
            }
        }
        if (з.рес is not null)
        {
            double cur = listener.believed(src.id, з.рес);
            // звук говорит «у него это есть», но не «у него этого гора»:
            // без потолка оценка растёт от каждого чиха и весь дом идёт грабить
            note_signal(listener, src.id, з.рес, Math.Min(cur + з.подсказка, 6.0), 0.35);
        }
        // генератор — единственный сигнал не о ресурсе, а о достатке. В метель,
        // когда во всём доме темно, гул и свет в окне говорят не «у него есть
        // солярка», а «у него есть всё»: так это и читается снаружи. Отсюда же
        // и зависть — та же, что от чужого ужина по стояку
        if (kind == "генератор")
        {
            foreach (var r in new[] { "еда", "материалы", "лекарства" })
                note_signal(listener, src.id, r,
                    Math.Min(listener.believed(src.id, r) + b["генератор_достаток"], 7.0), 0.3);
            double нужда = listener.desperation();
            if (нужда > 0.1)
            {
                adjust(listener, src.id, hate: b["генератор_зависть"] * нужда);
                listener.mood = Util.Clamp(
                    listener.mood - b["генератор_зависть"] * 0.3 * нужда);
            }
        }
        listener.memory.Add(new Память
            { день = h.day, вид = Вид.СЛЫШАЛ, кто = src.id, что = kind });
    }

    /// <summary>
    /// Человек посмотрел на соседа и что-то о нём понял (GDD 12.3).
    ///
    /// Не про шкаф, а про самого человека: сыт ли он, цел ли, не мёрзнет ли.
    /// Видно это по лицу и по тому, как он держится, — и потому взгляд врёт
    /// тем сильнее, чем темнее в подъезде и чем хуже они знакомы. Оценка
    /// не подменяется правдой, а ползёт к ней.
    /// </summary>
    public static void разглядел(House h, NPC? watcher, NPC? target, double точность = 1.0)
    {
        var b = h.B;
        if (watcher is null || target is null
            || string.Equals(watcher.id, target.id, StringComparison.Ordinal))
            return;
        if (!watcher.здесь())
            return;
        double к = точность * (h.powered(watcher) ? 1.0 : b["взгляд_в_темноте"]);
        к = Util.Clamp(к * (b["взгляд_база"] + watcher.свой(target.id) * b["взгляд_за_близость"]),
                       0.0, 1.0);
        if (!watcher.вид.TryGetValue(target.id, out var v))
        {
            v = new Взгляд();
            watcher.вид[target.id] = v;
        }
        double ошибка = b["взгляд_ошибка"] * (1.0 - к);
        // правда: видно: лицо соседа видно каждый день — и оценка ползёт к нему
        foreach (var (поле, правда) in new (string, double)[]
                 { ("сыт", target.satiety), ("цел", target.health), ("тепло", target.warmth) })
        {
            double кажется = Util.Clamp(правда + h.rng.Uni(-ошибка, ошибка), 0.0, 100.0);
            double? было = поле switch
            {
                "сыт" => v.сыт,
                "цел" => v.цел,
                _ => v.тепло,
            };
            double стало = было is null ? кажется : было.Value + (кажется - было.Value) * к;
            switch (поле)
            {
                case "сыт": v.сыт = стало; break;
                case "цел": v.цел = стало; break;
                default: v.тепло = стало; break;
            }
        }
        // перевязанная рука, шина, кашель за стеной — это не оценка, а факт,
        // но заметить его всё равно надо. Рану и кашель медик замечает вернее
        // прочих: это его ремесло, а не впечатление
        double к_хворь = watcher.skills.Contains("медик", StringComparer.Ordinal)
            ? Math.Min(1.0, к * b["взгляд_медика"]) : к;
        if (h.rng.Chance(к_хворь))
            v.хворь = target.injuries.Count > 0 || target.sick is not null;  // правда: видно: рана и кашель заметны
        v.день = h.day;
        // и вывод, который человек делает сам: этот долго не протянет.
        // Не с одного взгляда — с одного взгляда человек бывает просто
        // не выспавшимся. Нужно увидеть его таким несколько раз подряд
        double плох = watcher.плох(target.id);
        if (плох >= b["не_жилец_порог"])
        {
            v.подряд += 1.0;
            if (v.подряд >= b["не_жилец_взглядов"] && !watcher.не_жилец.Contains(target.id))
            {
                watcher.не_жилец.Add(target.id);
                h.bump("списан_со_счетов");
            }
        }
        else if (плох < b["не_жилец_отмена"])
        {
            v.подряд = 0.0;
            watcher.не_жилец.Remove(target.id);
        }
    }

    /// <summary>
    /// Двое сошлись лицом к лицу, и каждый увидел, как выглядит второй.
    /// Одна дверь на все встречи: разговор в дверях, просьба, обмен,
    /// перевязка, отъём на лестнице. Взгляд взаимный — у двери стоят двое.
    /// </summary>
    public static void встретились(House h, NPC a, NPC b_npc, double? точность = null)
    {
        double т = точность ?? h.B["взгляд_вблизи"];
        разглядел(h, a, b_npc, т);
        разглядел(h, b_npc, a, т);
    }

    /// <summary>Сдвинуть оценку чужих запасов в сторону нового сигнала.</summary>
    public static void note_signal(NPC a, string target_id, string res,
                                   double hint_value, double weight)
    {
        if (!a.сведения.TryGetValue(target_id, out var св))
        {
            св = new Сведения();
            a.сведения[target_id] = св;
        }
        double cur = св.est.Взять(res, 2.0);
        св.est[res] = Math.Max(0.0, cur * (1.0 - weight) + hint_value * weight);
    }

    /// <summary>
    /// Наблюдение: подсмотреть, как живёт сосед. Точнее любого шума.
    /// Заодно это единственный способ увидеть то, что сосед прячет.
    /// </summary>
    public static void observe(House h, NPC watcher, NPC target)
    {
        var b = h.B;
        // и заодно — как он сам выглядит. Издали и односторонне: смотрящий
        // видит соседа, сосед смотрящего не видит, в этом и смысл наблюдения
        разглядел(h, watcher, target, b["взгляд_мельком"]);
        if (target.stock.Взять("мясо", 0.0) > 0 && h.rng.Chance(0.5))  // правда: видно: наблюдение и есть чтение чужого шкафа
            Конфликт.reveal_taboo(h, target, witness: watcher);
        adjust(watcher, target.id, aware: b["осведомлённость_за_наблюдение"]);
        // увиденный шкаф: самое надёжное разоблачение из всех
        foreach (var res in new[] { "еда", "топливо" })
            if (target.stock.Взять(res, 0.0) >= b["ложь_видно_запаса"])   // правда: видно: наблюдение
                проверить_ложь(h, target, watcher, "своё", res);
        foreach (var res in new[] { "еда", "топливо", "лекарства", "вода" })
        {
            double true_v = target.stock.Взять(res, 0.0);                 // правда: видно: наблюдение
            double noise = h.rng.Uni(0.75, 1.25);
            note_signal(watcher, target.id, res, true_v * noise, 0.75);
        }
        // и то, что стоит у него в прихожей. Это единственный мирный способ
        // узнать, с чем сосед откроет дверь, — и главный источник страха
        // до первой крови
        if (target.weapon != Оружие.НЕТ && h.rng.Chance(b["страх_видно_оружие"]))  // правда: видно: оружие в прихожей
            увидел_оружие(h, watcher, target);
        watcher.memory.Add(new Память { день = h.day, вид = Вид.СМОТРЕЛ, кто = target.id });
    }
}
