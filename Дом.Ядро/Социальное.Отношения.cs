// Перенос house/social.py, разделы «отношения», «нормальность»,
// «союзы и группы».
//
// Пять шкал (доверие, ненависть, страх, близость, осведомлённость) плюс счёт
// обид — и одна дверь, через которую они все меняются: `adjust`. Рост везде
// затухает: последние проценты даются тяжело, иначе за пару дней все всё
// знают и всем доверяют.

namespace Дом.Ядро;

public static partial class Социальное
{
    /// <summary>
    /// Все изменения шкал идут через эту дверь. Рост затухает: последние
    /// проценты доверия и осведомлённости даются тяжело.
    /// </summary>
    public static void adjust(NPC a, string b_npc_id, double trust = 0.0, double hate = 0.0,
                              double aware = 0.0, double страх = 0.0)
    {
        // наблюдателю — до затухания: он мерит намерение, а не остаток
        a._h?.hooks.Зов_adjust(a, b_npc_id, trust, hate, aware, страх);
        if (trust != 0.0)
        {
            double cur = a.trust.Взять(b_npc_id, 3.0);
            if (trust > 0)
                trust *= Math.Max(0.12, 1.0 - cur / 11.0);
            a.trust[b_npc_id] = Util.Clamp(cur + trust, 0.0, 10.0);
        }
        if (hate != 0.0)
        {
            // прощение — тоже пунктик: злопамятному добрый поступок стоит
            // дешевле, чем всем остальным (обиду он спишет только частично)
            if (hate < 0)
                hate *= Util.Clamp(1.0 + a.пунктик("прощение"), 0.0, 2.0);
            double cur = a.hate.Взять(b_npc_id, 0.0);
            // и рост злости затухает так же, как рост доверия, осведомлённости
            // и страха. Она была единственной шкалой, которая шла к потолку
            // по прямой, — и упиралась в него: с двенадцатого дня у половины
            // дома максимальная злость стояла на 90 из 100
            if (hate > 0)
                hate *= Math.Max(0.12, 1.0 - cur / 115.0);
            a.hate[b_npc_id] = Util.Clamp(cur + hate);
        }
        if (aware != 0.0)
        {
            if (!a.сведения.TryGetValue(b_npc_id, out var св))
            {
                св = new Сведения();
                a.сведения[b_npc_id] = св;
            }
            double cur = св.aware;
            if (aware > 0)
                aware *= Math.Max(0.08, 1.0 - cur / 105.0);
            св.aware = Util.Clamp(cur + aware);
        }
        if (страх != 0.0)
        {
            // страх набирается так же трудно, как осведомлённость: второй раз
            // увидеть тот же топор — уже не то же самое, что первый
            double cur = a.страх.Взять(b_npc_id, 0.0);
            if (страх > 0)
                страх *= Math.Max(0.12, 1.0 - cur / 115.0);
            a.страх[b_npc_id] = Util.Clamp(cur + страх);
        }
    }

    /// <summary>
    /// То, что произошло между двоими, сближает обоих (GDD 12.3). Дверь одна
    /// и симметричная нарочно: разговор, донесённая банка, отданный долг
    /// и общая печка — это события пары, а не поступки одного.
    /// </summary>
    public static void сблизились(House h, NPC? a, NPC? b_npc, double сила)
    {
        if (a is null || b_npc is null
            || string.Equals(a.id, b_npc.id, StringComparison.Ordinal) || сила <= 0)
            return;
        foreach (var (кто, кого) in new[] { (a, b_npc), (b_npc, a) })
        {
            if (!кто.здесь())
                continue;
            // своим человек тому, кто выносил его квартиру, уже не станет
            if (кто.не_прощу.Contains(кого.id))
                continue;
            double cur = кто.близость.Взять(кого.id, 0.0);
            // и второе затухание, важнее первого: у того, кто уже со всеми
            // свой, новая близость даётся тяжело. Вечер, проведённый у одной
            // двери, — это вечер, не проведённый у остальных
            double сумма = Util.Sum(кто.близость.Значения);
            double занят = сумма / (10.0 * Math.Max(1, кто.близость.Count));
            double шаг = сила * Math.Max(0.15, 1.0 - cur / 11.0)
                         * Math.Max(0.2, 1.0 - занят * h.B["близость_внимание"]);
            кто.близость[кого.id] = Util.Clamp(cur + шаг, 0.0, 10.0);
        }
    }

    /// <summary>
    /// Между этими двумя произошло то, что не улаживается само.
    ///
    /// Несимметрично нарочно: обиду держит тот, КОГО обидели. Обидчик получает
    /// свою долю — он знает, что нажил врага, — но вчетверо меньшую. Обида
    /// не оседает от времени; гасится только поступками и только до дна.
    /// </summary>
    public static void обидели(House h, NPC? кого, NPC? кто, double вес,
                               bool непрощаемо = false, bool вслух = true)
    {
        if (кого is null || кто is null
            || string.Equals(кого.id, кто.id, StringComparison.Ordinal))
            return;
        var b = h.B;
        кого.счёты[кто.id] = Util.Clamp(кого.обида(кто.id) + вес, 0.0, 100.0);
        кто.счёты[кого.id] = Util.Clamp(кто.обида(кого.id) + вес * b["обида_обидчику"],
                                        0.0, 100.0);
        // когда это было. Нужно ровно для одного: загладить вину в тот же день
        // нельзя — человек, который час назад отнял еду на лестнице,
        // не приходит с банкой до темноты
        h.обиды_дни[(кто.id, кого.id)] = h.day;
        if (непрощаемо && !кого.не_прощу.Contains(кто.id))
        {
            кого.не_прощу.Add(кто.id);
            h.bump("непрощаемых_обид");
            if (вслух)
            {
                // печатается не здесь: к двери приходят втроём, и три
                // одинаковые строки про одного и того же человека — это
                // не три события, а одно
                if (!h.ожидает.не_простил.TryGetValue(кого.id, out var список))
                {
                    список = new List<string>();
                    h.ожидает.не_простил[кого.id] = список;
                }
                список.Add(кто.id);
            }
        }
        h.bump("ссор_в_доме");
    }

    /// <summary>
    /// Поступок в сторону того, кого обидел. Гасит обиду — но не до конца.
    /// Гасится только безвозмездным: донесённая банка, перевязка, выход
    /// на площадку за него при осаде.
    /// </summary>
    public static void загладил(House h, NPC? кто, NPC? кому, double сила)
    {
        if (кто is null || кому is null
            || string.Equals(кто.id, кому.id, StringComparison.Ordinal))
            return;
        if (кому.не_прощу.Contains(кто.id))
            return;
        double было = кому.обида(кто.id);
        if (было <= 0.0)
            return;
        double стало = Math.Max(h.B["обида_дно"], было - сила);
        кому.счёты[кто.id] = стало;
        if (было >= h.B["обида_порог_вражды"] && h.B["обида_порог_вражды"] > стало)
        {
            h.bump("обид_улажено");
            h.journal.line($"{кому.@short} {Util.Vb(кому.sex, "взял")} то, что "
                           + $"{кто.@short} {Util.Vb(кто.sex, "принёс")}, и "
                           + $"{Util.Vb(кому.sex, "кивнул")}. Разговаривать они снова стали.", 1);
        }
    }

    /// <summary>
    /// Односторонний ход: обидели меня — своим он перестал быть именно мне.
    /// Отказавший не отдаляется от того, кому отказал: для него ничего
    /// не произошло, он просто не дал банку.
    /// </summary>
    public static void отдалились(NPC a, string b_npc_id, double сила)
    {
        if (сила == 0.0)
            return;
        a.близость[b_npc_id] = Util.Clamp(a.близость.Взять(b_npc_id, 0.0) - сила, 0.0, 10.0);
    }

    /// <summary>
    /// Одна дверь на все источники страха (GDD 12.3). Смелому страшно меньше,
    /// но не «совсем не страшно»: храбрость делит, а не отменяет.
    /// </summary>
    public static double испугался(House h, NPC? witness, NPC? кого, double сила)
    {
        if (witness is null || кого is null
            || string.Equals(witness.id, кого.id, StringComparison.Ordinal) || сила <= 0)
            return 0.0;
        if (!witness.здесь())
            return 0.0;
        сила *= 1.4 - witness.t01("храбрость");
        adjust(witness, кого.id, страх: сила);
        return сила;
    }

    /// <summary>
    /// Соседа увидели с оружием в руках (GDD 17). Не «у него есть топор»
    /// вообще, а «он стоял с ним на площадке»: страх берётся из увиденного.
    /// Поэтому охотник, который ни разу не снял винтовку со стены, никого
    /// и не пугает.
    /// </summary>
    public static void увидел_оружие(House h, NPC? witness, NPC кто,
                                     IEnumerable<NPC>? свидетели = null, double доля = 1.0)
    {
        var b = h.B;
        double вес = Таблицы.ОРУЖИЕ_ВЕС[кто.weapon];
        if (вес <= 0)
            return;
        double сила = b["страх_за_оружие"] * вес * доля;
        if (Таблицы.ОГНЕСТРЕЛ.Contains(кто.weapon))
            сила += b["страх_за_огнестрел"] * доля;
        var все = new List<NPC>();
        if (witness is not null)
            все.Add(witness);
        if (свидетели is not null)
            все.AddRange(свидетели);
        foreach (var w in все)
        {
            испугался(h, w, кто, сила);
            // заодно узнают, чем он вооружён: это и есть тот самый
            // «свидетель инвентаря» из GDD 12.3
            adjust(w, кто.id, aware: 4.0);
        }
    }

    /// <summary>
    /// Свидетель убийства (GDD 11). Смотря чем убили: выстрел в подъезде
    /// и драка на кулаках оставляют после себя разный дом.
    /// </summary>
    public static double видел_убийство(House h, NPC witness, NPC убийца,
                                        Оружие оружие = Оружие.НЕТ)
    {
        var b = h.B;
        double сила = b["страх_за_убийство"]
                      + b["страх_убийство_оружие"] * Таблицы.ОРУЖИЕ_ВЕС[оружие];
        return испугался(h, witness, убийца, сила);
    }

    /// <summary>Сколько происшествий было за последние дни. Дом забывает старое.</summary>
    public static int recent_incidents(House h, int window = 4)
    {
        int n = 0;
        foreach (var d in h.календарь.происшествия_дни)
            if (h.day - d < window)
                n++;
        return n;
    }

    /// <summary>
    /// 0..1 — «в доме уже неспокойно», глазами этого человека. Три вещи,
    /// которые он видит и слышит сам: что случилось за последние дни, сколько
    /// раз ему отказали и насколько в доме уже открыто злы друг на друга.
    /// </summary>
    public static double напряжение_дома(House h, NPC npc)
    {
        var b = h.B;
        double v = recent_incidents(h, 6) * b["напряжение_за_происшествие"];
        double отказов = Util.Sum(npc.asking.Значения.Select(r => r.отказали));
        v += Math.Min(b["напряжение_отказов_потолок"], отказов) * b["напряжение_за_отказ"];
        // самая громкая ссора в доме — как её видно с утра
        v += h.сутки.злость_дома / 100.0 * b["напряжение_за_злость"];
        return Util.Clamp(v, 0.0, 1.0);
    }

    /// <summary>Чужая паника заражает (GDD 12.3).</summary>
    public static void spread_panic(House h)
    {
        var people = h.alive();
        if (people.Count < 2)
            return;
        double avg = Util.Sum(people.Select(p => p.panic)) / people.Count;
        double k = h.B["паника_заражение"];
        foreach (var p in people)
            // общительные заражаются сильнее, замкнутые меньше
            p.panic = Util.Clamp(p.panic + (avg - p.panic) * k
                                 * (0.5 + 0.1 * p.trait("общительность")));
    }

    /// <summary>
    /// Человек узнал, что сосед мёртв. Возвращает true, если это новость.
    /// Здесь и только здесь смерть превращается в то, что с домом делает:
    /// шок, просевшее привычное, счёт происшествий.
    /// </summary>
    public static bool узнал_о_смерти(House h, NPC кто, NPC умерший)
    {
        h.hooks.Зов_узнал_о_смерти(h, кто, умерший);
        bool итог = _узнал_о_смерти(h, кто, умерший);
        h.hooks.Зов_после_узнал_о_смерти(h, кто, умерший, итог);
        return итог;
    }

    private static bool _узнал_о_смерти(House h, NPC кто, NPC умерший)
    {
        var b = h.B;
        if (умерший.alive || кто.знает_о_смерти.Contains(умерший.id)
            || string.Equals(кто.id, умерший.id, StringComparison.Ordinal))
            return false;
        кто.знает_о_смерти.Add(умерший.id);
        // убитого помнят иначе, чем замёрзшего: это урок о том, чем кончается
        if ((умерший.cause ?? "").StartsWith("убит", StringComparison.Ordinal))
            кто.memory.Add(new Память
                { день = h.day, вид = Вид.ВИДЕЛ_УБИЙСТВО, кто = умерший.id });
        // кем он был этому человеку. До сих пор смерть весила для всех
        // одинаково: смерть Лиды для Оксаны, с которой они пара в 95 % жизней,
        // значила ровно столько же, сколько смерть соседа с пятого этажа
        double свой = Util.Clamp(кто.свой(умерший.id)
            + кто.trust.Взять(умерший.id, 3.0) / 10.0 * b["горе_за_доверие"], 0.0, 1.0);
        double горе = 1.0 + свой * b["горе_за_близость"];
        add_panic(кто, b["паника_от_смерти_в_доме"]
                       * (0.7 + 0.6 * кто.t01("вспыльчивость")) * горе);
        кто.mood = Util.Clamp(кто.mood + b["настроение_от_смерти"] * горе);
        // смерть в доме — самое сильное «так теперь бывает» из всех (GDD 12.4)
        видел(h, кто, (b["нормальность_за_смерть"] + b["нормальность_за_происшествие"]) * горе);
        // и то, чем горе отличается от испуга: после своего человек
        // не возвращается прежним. Опускается сам пол нормальности
        if (свой >= b["горе_порог_своего"])
        {
            кто.нормальность_пол = Math.Max(0.0, кто.нормальность_пол - b["горе_пол"] * свой);
            кто.bump("потерял_своего");
            h.bump("смертей_своего");
            h.journal.line($"{кто.@short} {Util.Vb(кто.sex, "узнал")}, что {умерший.@short} "
                + $"{Util.Vb(умерший.sex, "мёртв")}. Для {кто.form("gen")} это "
                + (умерший.sex == "ж" ? "была не просто соседка" : "был не просто сосед") + ".", 2);
        }
        кто.день_известия_о_смерти = h.day;
        if (!h.смерть_объявлена.Contains(умерший.id))
        {
            h.смерть_объявлена.Add(умерший.id);
            // привычное уже сбито строкой выше — здесь только счёт происшествий
            register_incident(h, "смерть", null, witnesses: new List<NPC>());
            h.stats["задержка_известия"] = h.stats.Взять("задержка_известия", 0.0)
                                           + (h.day - (умерший.died_day ?? h.day));
            h.bump("смертей_дом_узнал");
        }
        return true;
    }

    /// <summary>
    /// Человек оказался внутри чужой квартиры — и увидел то, что в ней лежит.
    /// Иначе выходит нелепость: сосед разбирает квартиру на доски,
    /// перешагивая через того, о ком думает, что он жив.
    /// </summary>
    public static void вошёл_в_квартиру(House h, NPC npc, Flat? flat)
    {
        if (flat is null || flat.owner_died is null)
            return;
        var хозяин = h.get(flat.owner_died);
        if (хозяин is not null)
            узнал_о_смерти(h, npc, хозяин);
    }

    /// <summary>Насколько тяжело весит для слушателя то, что ему сейчас
    /// рассказывают.</summary>
    public static double вера_в_слова(NPC слушатель, NPC рассказчик)
        => (0.25 + 0.05 * слушатель.trust.Взять(рассказчик.id, 3.0))
           * вес_слов(слушатель, рассказчик);

    /// <summary>
    /// Один человек говорит другому, что сосед мёртв. Кто рассказчику
    /// не верит, принимает не известие, а вопрос — откуда ты знаешь?
    /// </summary>
    public static bool сообщить_о_смерти(House h, NPC рассказчик, NPC слушатель,
                                         NPC? умерший)
    {
        var bal = h.B;
        if (умерший is null || умерший.alive
            || string.Equals(умерший.id, слушатель.id, StringComparison.Ordinal)
            || слушатель.знает_о_смерти.Contains(умерший.id))
            return false;
        // он уже приносил эту весть и ему уже не поверили. Второй раз в тот же
        // день и на той же неделе об этом не заговаривают
        if (h.day - слушатель.ask_record(рассказчик.id).не_поверил.Взять(умерший.id, -99.0)
            < bal["смерть_память_дней"])
            return false;
        if (вера_в_слова(слушатель, рассказчик) < bal["смерть_порог_веры"])
        {
            слушатель.ask_record(рассказчик.id).не_поверил[умерший.id] = h.day;
            adjust(слушатель, рассказчик.id,
                   aware: bal["смерть_подозрение_осведомлённость"],
                   hate: bal["смерть_подозрение_злость"]);
            испугался(h, слушатель, рассказчик, bal["смерть_подозрение_страх"]);
            h.bump("подозрений_в_убийстве");
            h.journal.line($"{слушатель.@short} не {Util.Vb(слушатель.sex, "поверил")} "
                + $"{рассказчик.form("dat")} про {умерший.form("acc")}: откуда ты знаешь?", 1);
            return false;
        }
        if (узнал_о_смерти(h, слушатель, умерший))
            h.journal.line($"{рассказчик.@short} {Util.Vb(рассказчик.sex, "сказал")} "
                + $"{слушатель.form("dat")}, что {умерший.@short} "
                + $"{Util.Vb(умерший.sex, "мёртв")}.", 1);
        return true;
    }

    /// <summary>
    /// Вскрыл дверь и увидел. Тело не бывает частным знанием: тот, кто его
    /// нашёл, тут же зовёт соседей — но говорит он словами, и слова его весят
    /// ровно столько, сколько ему верят.
    /// </summary>
    public static void нашёл_тело(House h, NPC кто, NPC умерший)
    {
        if (!узнал_о_смерти(h, кто, умерший))
            return;
        foreach (var сосед in h.others(кто))
            сообщить_о_смерти(h, кто, сосед, умерший);
    }

    /// <summary>Общая встряска дома: смерть, налёт, выстрел.</summary>
    public static void house_shock(House h, double panic = 0.0, double mood = 0.0,
                                   string? note = null)
    {
        foreach (var p in h.alive())
        {
            if (panic != 0.0)
                add_panic(p, panic * (0.7 + 0.6 * p.t01("вспыльчивость")));
            if (mood != 0.0)
                p.mood = Util.Clamp(p.mood + mood);
        }
        if (note is not null)
            h.journal.line(note, 2);
    }

    /// <summary>
    /// Дом оценивает поступок. У каждого своя мерка (GDD 12.1, «Ценности»).
    ///
    /// <paramref name="участники"/> — те, с кем это произошло, в отличие
    /// от тех, кто видел. Разница в весе, и она не украшение: пока зритель
    /// судил наравне с участником, дом получал общее мнение о человеке там,
    /// где должен был получить чьё-то личное.
    /// </summary>
    public static void judge(House h, NPC actor, string tag, double hate = 0.0,
                             double trust = 0.0, IEnumerable<NPC>? witnesses = null,
                             IEnumerable<NPC>? участники = null)
        => judge(h, actor, new[] { tag }, hate, trust, witnesses, участники);

    public static void judge(House h, NPC actor, IReadOnlyList<string> теги,
                             double hate = 0.0, double trust = 0.0,
                             IEnumerable<NPC>? witnesses = null,
                             IEnumerable<NPC>? участники = null)
    {
        double k = h.B["ценности_вес"];
        double зритель = h.B["суд_вес_зрителя"];
        var свои = new HashSet<string>(
            (участники ?? Enumerable.Empty<NPC>()).Select(u => u.id), StringComparer.Ordinal);
        foreach (var w in witnesses ?? h.others(actor))
        {
            if (string.Equals(w.id, actor.id, StringComparison.Ordinal))
                continue;
            double сила = 1.0;
            foreach (var t in теги)
            {
                if (w.не_терпит(t))
                    сила += k;
                if (w.ценит(t))
                    сила -= hate > 0 ? k * 0.5 : -k;
            }
            сила = Math.Max(0.0, сила) * (свои.Contains(w.id) ? 1.0 : зритель);
            adjust(w, actor.id, hate: hate * сила, trust: trust * сила);
        }
    }

    // ------------------------------------------------------------ нормальность

    /// <summary>
    /// Человек увидел то, чего в обычной жизни не бывает (GDD 12.4).
    ///
    /// Заражение работает через того, кому доверяют. Если дверь заколотил
    /// уважаемый Пётр — это разрешение для всех: так, значит, уже можно.
    /// Если Игорь, которого никто не любит, — это не разрешение, а повод
    /// его бояться.
    /// </summary>
    public static void видел(House h, NPC witness, double вес, NPC? кто = null)
    {
        var b = h.B;
        if (кто is not null && !string.Equals(кто.id, witness.id, StringComparison.Ordinal))
        {
            double доверие = witness.trust.Взять(кто.id, 3.0);
            double доля = Util.Clamp((доверие - b["заражение_доверие_порог"])
                                     / b["заражение_доверие_делитель"], -1.0, 1.0);
            if (доля <= 0.0)
            {
                adjust(witness, кто.id, aware: b["заражение_настороженность"] * вес,
                       hate: b["заражение_злость"] * вес * (0.2 - доля));
                add_panic(witness, b["заражение_паника"] * вес);
                h.bump("чужой_пример_испугал");
                return;
            }
            вес *= доля;
            h.bump("чужой_пример_заразил");
        }
        witness.normalcy = Util.Clamp(witness.normalcy - вес * witness.нормальность_скорость,
                                      witness.нормальность_пол, 1.0);
    }

    /// <summary>
    /// Обратный ход: кто-то на виду делает обычные вещи, и это держит дом.
    /// Это ровно то, что должно работать для игрока: дом можно держать
    /// своим поведением.
    /// </summary>
    public static void держится(House h, NPC witness, NPC кто)
    {
        var b = h.B;
        double доверие = witness.trust.Взять(кто.id, 3.0);
        if (доверие < b["опора_доверие"])
            return;
        witness.normalcy = Util.Clamp(witness.normalcy + b["опора_прибавка"], 0.0, 1.0);
        h.bump("дом_держали");
    }

    /// <summary>
    /// Человек сам сделал то, чего раньше не делал. Свой поступок меняет
    /// сильнее чужого примера и только в первый раз: вторая кража — это уже
    /// привычка, а не порог.
    /// </summary>
    public static void переступил(House h, NPC npc, string key)
    {
        double вес = Каталог.НОРМА.Взять(key, 0.0);
        if (вес < h.B["нормальность_дикость_порог"])
            return;
        if (npc.переступил.Contains(key))
            return;
        npc.переступил.Add(key);
        h.bump("переступили");
        double доля = h.B.Таблица("свой_поступок_доля").Взять(key, 1.0);
        видел(h, npc, вес * h.B["нормальность_за_свой_поступок"] * доля);
    }

    /// <summary>Пометить, что сегодня в доме что-то случилось. Тишину считают
    /// отсюда.</summary>
    public static void громкое(House h) => h.календарь.последнее_громкое = h.day;

    /// <summary>
    /// Происшествие в доме. После первого дом начинает делиться на группы
    /// (GDD 12.4). Пустой список свидетелей значит, что привычное уже сбито
    /// в другом месте, а здесь считается только сам счёт происшествий.
    /// </summary>
    public static void register_incident(House h, string kind, string? text,
                                         IEnumerable<NPC>? witnesses = null)
    {
        h.incidents += 1;
        h.календарь.происшествия_дни.Add(h.day);
        громкое(h);
        foreach (var p in witnesses ?? h.alive())
            видел(h, p, h.B["нормальность_за_происшествие"]);
        if (h.first_incident_day is null)
        {
            h.first_incident_day = h.day;
            h.note($"первое происшествие в доме ({kind}) — дом начал делиться");
        }
        h.bump($"происшествий_{kind}");
        if (text is not null)
            h.journal.line(text, 2);
    }

    // ------------------------------------------------------- союзы и группы

    /// <summary>Союзы складываются из взаимного доверия (GDD 12.4).</summary>
    public static void alliance_check(House h)
    {
        var b = h.B;
        foreach (var a in h.alive())
            foreach (var c in h.others(a))
            {
                double mutual = Math.Min(a.trust.Взять(c.id, 3.0), c.trust.Взять(a.id, 3.0));
                var пара = Пара(a.id, c.id);
                double cooldown = h.разрывы.Взять(пара, -99);
                // мало доверять — надо, чтобы люди успели друг другу что-то
                // сделать. Пока это условие кончалось на «или шестой день
                // метели», союз складывался из одной общей репутации
                bool earned = Math.Min(a.свой(c.id), c.свой(a.id)) >= b["союз_близость"];
                if (mutual >= b["доверие_порог_союза"] && earned
                    && !a.allies.Contains(c.id) && h.day - cooldown >= 5)
                {
                    a.allies.Add(c.id);
                    c.allies.Add(a.id);
                    сблизились(h, a, c, b["близость_за_союз"]);
                    h.bump("союзов_заключено");
                    h.journal.line(
                        $"{a.@short} и {c.@short} договорились держаться вместе.", 2);
                    h.note($"союз: {a.@short} + {c.@short}");
                }
                else if (mutual < b["доверие_разрыв_союза"] && a.allies.Contains(c.id))
                {
                    a.allies.Remove(c.id);
                    c.allies.Remove(a.id);
                    h.разрывы[пара] = h.day;
                    h.bump("союзов_распалось");
                    h.journal.line($"{a.@short} и {c.@short} больше не разговаривают.", 2);
                    h.note($"союз распался: {a.@short} + {c.@short}");
                }
            }
    }

    /// <summary>Пара как ключ: два id, отсортированные ordinal — так их
    /// складывает `tuple(sorted(...))` в прототипе.</summary>
    public static (string, string) Пара(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    /// <summary>
    /// Группы складываются после первого инцидента (GDD 12.4). Мирное ядро
    /// вокруг самых лояльных, агрессивное — вокруг самых храбрых и жадных,
    /// остальные примыкают к тому, кому доверяют.
    /// </summary>
    public static void update_groups(House h)
    {
        if (h.first_incident_day is null)
            return;
        var people = h.alive();
        if (people.Count < 3)
            return;
        NPC peace_leader = people[0], aggr_leader = people[0];
        double лучший_мир = double.NegativeInfinity, лучший_зло = double.NegativeInfinity;
        foreach (var p in people)
        {
            double мир = p.trait("лояльность") + p.trait("общительность") * 0.4;
            if (мир > лучший_мир) { лучший_мир = мир; peace_leader = p; }
            double зло = p.trait("храбрость") + p.trait("жадность")
                         - p.trait("лояльность") * 0.5;
            if (зло > лучший_зло) { лучший_зло = зло; aggr_leader = p; }
        }
        if (string.Equals(peace_leader.id, aggr_leader.id, StringComparison.Ordinal))
            return;
        var b = h.B;
        var changed = new List<NPC>();
        foreach (var p in people)
        {
            string? old = p.group;
            if (string.Equals(p.id, peace_leader.id, StringComparison.Ordinal))
                p.group = "мирные";
            else if (string.Equals(p.id, aggr_leader.id, StringComparison.Ordinal))
                p.group = "агрессивные";
            else
            {
                double tp = p.trust.Взять(peace_leader.id, 3.0);
                double ta = p.trust.Взять(aggr_leader.id, 3.0);
                // своя злость тоже тянет в агрессивное ядро
                ta += p.t01("жадность") * 1.5 + p.desperation() * 2.0 - p.t01("лояльность") * 1.5;
                if (Math.Max(tp, ta) < b["группа_порог_доверия"] - 1.5)
                    p.group = null;
                else
                    p.group = tp >= ta ? "мирные" : "агрессивные";
            }
            if (!string.Equals(p.group, old, StringComparison.Ordinal))
                changed.Add(p);
        }
        foreach (var p in changed)
            if (p.group is not null)
                h.journal.line($"{p.@short} держится "
                    + (p.group == "мирные" ? "мирных" : "тех, кто готов брать своё") + ".", 1);
    }

    /// <summary>Память притупляется, злость оседает, сведения устаревают.</summary>
    public static void daily_decay(House h)
    {
        var b = h.B;
        double к = b["оценка_спад_в_день"];
        foreach (var p in h.alive())
        {
            foreach (var св in p.сведения.Значения)
                св.aware = Util.Clamp(св.aware - b["осведомлённость_спад_в_день"]);
            // у злопамятного злость оседает медленнее — это его пунктик,
            // а не черта
            double спад = b["ненависть_спад_в_день"]
                          * Util.Clamp(1.0 + p.пунктик("ненависть_спад"), 0.0, 3.0);
            foreach (var k in p.hate.Ключи)
                p.hate[k] = Util.Clamp(p.hate[k] - спад);
            // страх забывается медленнее злости: обиду прощают, ружьё помнят
            foreach (var k in p.страх.Ключи)
                p.страх[k] = Util.Clamp(p.страх[k] - b["страх_спад_в_день"]);
            // долги забываются — и тем, кто должен, и тем, кому должны. Без
            // этого дом помнит каждую банку до конца метели и возвращает её
            // при первом излишке
            foreach (var k in p.дал.Ключи)
                p.дал[k] = Math.Max(0.0, p.дал[k] - b["долг_забывается_в_день"]);
            foreach (var rec in p.asking.Значения)
                if (rec.должен > 0)
                    rec.должен = Math.Max(0.0, rec.должен - b["долг_забывается_в_день"]);
            // близость держится тем, что происходит, и без этого гаснет:
            // неделю не разговаривали — уже не свои
            foreach (var кому in p.близость.Ключи)
                p.близость[кому] = Math.Max(0.0,
                    p.близость[кому] - b["близость_спад_в_день"]);
            // без свежих сигналов оценка чужих запасов ползёт к «не знаю»
            foreach (var св in p.сведения.Значения)
                foreach (var res in св.est.Ключи)
                {
                    double нейтраль = Таблицы.ДОГАДКА.TryGetValue(res, out var д)
                        ? д : b["оценка_нейтральная"];
                    св.est[res] += (нейтраль - св.est[res]) * к;
                }
        }
    }
}
