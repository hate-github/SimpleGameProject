// Перенос house/model.py, строки 1413–1803: дом и состояние с временем жизни.
//
// Состояние разложено по сроку годности (аудит §4): на день — `сутки`
// (новый объект каждое утро), очереди на утро — `ожидает`, даты и погода —
// `календарь`, остальное — поля `House`.

namespace Дом.Ядро;

/// <summary>То, что дом помнит только до утра: House.новый_день заводит новые.</summary>
public sealed class Сутки
{
    // сколько раз кто к кому подходил сегодня: (кто, к кому, за чем) -> раз
    public Словарь<(string, string, string), int> контакты { get; } = new();
    // сколько раз кто повторил одно действие: (кто, действие) -> раз
    public Словарь<(string, string), int> сделано { get; } = new();

    // какие варианты быта человек сегодня уже прожил: один и тот же дважды
    // за день читается как заевшая пластинка («Пётр подмёл площадку у своей
    // двери. Больше некому» — и через час снова)
    public Словарь<string, HashSet<string>> быт_сказано { get; } =
        Словари.Строкой<HashSet<string>>();

    // засада живёт один день: три часа на площадке — это три часа, а не
    // вечное дежурство у чужой двери. Не дождался — потерял день
    public Словарь<string, string> караулят { get; } = Словари.Строкой<string>();  // кого ждут -> кто ждёт
    public Словарь<string, int> не_выходить { get; } = Словари.Строкой<int>();
    public Словарь<string, int> смотрел_лестницу { get; } = Словари.Строкой<int>();

    // самая громкая злость в доме — одним числом на день. Считается с утра
    // и не пересчитывается внутри дня: это то, с чем дом проснулся
    public double злость_дома { get; set; }

    public List<string> состав_налёта { get; } = new();   // кто ходил в налёт этой ночью

    // чтобы запах не писался в журнал десять раз за день. Словарь, а не
    // запись: в прототипе это `Dict[str, Any]`, и пока в него не написали,
    // он пуст — а снимок сверяется поле в поле
    public Словарь<string, double> запах_журнал { get; } = Словари.Числа();
}

/// <summary>Кто-то положил вещь под чужую дверь; того, кто положил, не видел никто.</summary>
public sealed record Подброс(string кто, string что, int день);

/// <summary>Очереди на утро: ночью положили, утром разобрали (discoveries, report).</summary>
public sealed class Ожидает
{
    public List<(string вор, string кладовка)> вскрытые_кладовые { get; } = new();
    public Словарь<int, Подброс> подброшено { get; set; } = new();      // квартира -> что и кто положил
    public List<(string вор, string жертва)> пропажи { get; } = new();
    public Словарь<string, List<string>> не_простил { get; } =          // кто -> кого не простил
        Словари.Строкой<List<string>>();
    public Словарь<string, List<string>> сводка { get; set; } =         // жилец -> что заметил
        Словари.Строкой<List<string>>();
    public string? дежурил_вчера { get; set; }                          // чья была ночь по расписанию
}

/// <summary>Что назначено на какой день и в какой день что случилось.</summary>
public sealed class Календарь
{
    public Словарь<int, List<string>> события { get; } = new();     // день -> кандидаты
    public Словарь<int, (Режим режим, double сдвиг)> погода { get; } = new();
    public int последнее_громкое { get; set; } = -99;   // тишину считают отсюда
    public int последнее_собрание { get; set; } = -99;
    public int последний_налёт { get; set; } = -99;
    public int подъезд_заколочен { get; set; } = -99;   // дом заколотил подъезд общими силами
    public int укрепление_порыв { get; set; } = -99;    // дом кинулся укрепляться
    public List<int> происшествия_дни { get; } = new();
}

/// <summary>Дом и всё, что в нём происходит. Один подъезд пятиэтажки (GDD 25, «Ядро»).</summary>
public sealed class House
{
    public Rng rng { get; set; } = null!;
    public Баланс B { get; set; } = new();
    public int day { get; set; }
    public Словарь<string, NPC> people { get; } = Словари.Строкой<NPC>();
    public Словарь<int, Flat> flats { get; } = new();
    public Словарь<string, Кладовая> кладовые { get; } = Словари.Строкой<Кладовая>();

    // наблюдатели (hooks.py): линейки и проверки. Не состояние — в снимок не входят
    public Хуки hooks { get; set; } = new();

    // погода и инфраструктура
    public double outside { get; set; } = -8.0;
    public bool heating { get; set; } = true;
    public bool power_on { get; set; } = true;
    public bool water_on { get; set; } = true;
    public double network { get; set; } = 1.0;
    public bool банки { get; set; } = true;        // работает ли банк и приложение (GDD 18)
    public bool магазины { get; set; } = true;     // открыт ли ещё магазин на Заречной

    // Снег — не погода, а количество (GDD 3). Погода бывает и кончается,
    // а снег копится: он и есть причина, по которой улица на двадцатый день
    // не та же самая, что на первый.
    public double снег { get; set; }               // метры на дворе

    // среда
    public double scav_richness { get; set; } = 1.0;
    public Словарь<string, double> места { get; } = Словари.Числа();   // что ещё осталось где
    public int incidents { get; set; }
    public int? first_incident_day { get; set; }

    // состояние с временем жизни: на день, до утра, на всю метель (аудит §4)
    public Сутки сутки { get; set; } = new();
    public Ожидает ожидает { get; set; } = new();
    public Календарь календарь { get; set; } = new();

    // погода дня и эффекты событий
    public Режим режим { get; set; } = Режим.МЕТЕЛЬ;
    public Режим режим_вчера { get; set; } = Режим.МЕТЕЛЬ;
    public double температура_сдвиг { get; set; }  // событие сдвинуло температуру…
    public int температура_дней { get; set; }      // …на столько дней
    public double опасность_множитель { get; set; } = 1.0;  // событие сделало улицу опаснее…
    public int опасность_дней { get; set; }        // …на столько дней
    public double банкомат { get; set; }           // касса банкомата на сегодня

    // память дома длиннее суток. Ключ пары — два id, отсортированные ordinal:
    // так их складывает `tuple(sorted(...))` в прототипе
    public Словарь<(string, string), int> засады { get; } = new();      // пара -> день засады
    public Словарь<(string, string), int> обиды_дни { get; } = new();   // (кто, кого) -> день обиды
    public Словарь<(string, string), int> разрывы { get; } = new();     // пара -> день разрыва
    public HashSet<string> смерть_объявлена { get; } = new(StringComparer.Ordinal);

    // многодневные дела
    public Словарь<string, Заказ> заказы { get; } = Словари.Строкой<Заказ>();  // заказчик -> заказ
    public Словарь<string, string> мастер_занят { get; } = Словари.Строкой<string>();
    public Дежурство? дежурство { get; set; }          // расписание ночей (meeting)
    public Приговор? приговор_нужен { get; set; }      // кого дом должен судить

    // данные, а не состояние
    public List<РепликаБыта> реплики_быт { get; } = new();

    // вывод
    public IЖурнал journal { get; set; } = new ЗаглушкаЖурнала();
    public Словарь<string, double> stats { get; } = Словари.Числа();
    public List<string> chronicle { get; } = new();
    // поток событий для движка: то же, что в журнале, только записями (ADR-14)
    public List<Событие> события { get; } = new();

    /// <summary>
    /// Чего стоят деньги сегодня: 1.0 — как до метели, 0.0 — бумага (GDD 18).
    ///
    /// Пока магазин работает, деньги настоящие: за них дают товар. Дальше они
    /// держатся только на том, что кто-то у соседа их ещё берёт, и к назначенному
    /// дню не стоят ничего. Документ говорил «после дня 0 деньги бесполезны» —
    /// разом; на прототипе честнее вышло иначе.
    /// </summary>
    public double курс()
    {
        var b = B;
        double закрытие = b["день_закрытия_магазинов"];
        if (day <= закрытие)
            return 1.0;
        double всего = Math.Max(1.0, b["день_нуля_денег"] - закрытие);
        return Util.Clamp(1.0 - (day - закрытие) / всего, 0.0, 1.0);
    }

    /// <summary>Сколько ещё осталось в этом месте. 1.0 — как было до метели.</summary>
    public double богатство_места(string имя) => места.Взять(имя, 1.0);

    public List<NPC> alive()
    {
        var кто = new List<NPC>();
        foreach (var p in people.Значения)
            if (p.здесь())
                кто.Add(p);
        return кто;
    }

    public List<NPC> others(NPC npc)
    {
        var кто = new List<NPC>();
        foreach (var p in alive())
            if (!string.Equals(p.id, npc.id, StringComparison.Ordinal))
                кто.Add(p);
        return кто;
    }

    public NPC? get(string npc_id) => people.TryGetValue(npc_id, out var p) ? p : null;

    /// <summary>Жилец по id, если он жив и в доме; иначе null.</summary>
    public NPC? живой(string? npc_id)
    {
        if (npc_id is null)
            return null;
        var p = get(npc_id);
        return p is not null && p.здесь() ? p : null;
    }

    /// <summary>Утро дома: дневное состояние заводится заново, злость дома
    /// снимается один раз.</summary>
    public void новый_день()
    {
        сутки = new Сутки();
        double худшее = 0.0;
        foreach (var a in alive())
            foreach (var c in others(a))
                худшее = Math.Max(худшее, a.hate.Взять(c.id, 0.0));
        сутки.злость_дома = худшее;
    }

    // ---------- жильё ----------

    /// <summary>
    /// Чья это квартира: своя или того, к кому он переехал. То же самое, что
    /// «у кого человек физически находится». Переехавший к соседу спит, ест
    /// и шумит не у себя: его собственная квартира стоит пустая.
    /// </summary>
    public NPC хозяин_жилья(NPC npc)
    {
        if (!string.IsNullOrEmpty(npc.living_with))
        {
            var host = живой(npc.living_with);
            if (host is not null)
                return host;
        }
        return npc;
    }

    /// <summary>В какой квартире человек физически находится.</summary>
    public Flat where(NPC npc) => flats[хозяин_жилья(npc).apt];

    /// <summary>Стоят ли эти двое в одной комнате прямо сейчас.</summary>
    public bool под_одной_крышей(NPC a, NPC b_npc)
        => string.Equals(хозяин_жилья(a).id, хозяин_жилья(b_npc).id, StringComparison.Ordinal);

    /// <summary>
    /// Кто физически живёт в этой квартире: хозяин и его гости. К одной двери
    /// приходят за всем, что за ней лежит: пока налёт брал только у хозяина,
    /// переезд к соседу делал человека неприкосновенным.
    /// </summary>
    public List<NPC> household(NPC person)
    {
        var люди = new List<NPC> { person };
        foreach (var gid in person.guests.OrderBy(x => x, StringComparer.Ordinal))
        {
            var g = живой(gid);
            if (g is not null)
                люди.Add(g);
        }
        return люди;
    }

    /// <summary>Номера квартир, в которых кто-то живёт прямо сейчас.</summary>
    public HashSet<int> занятые()
    {
        var кв = new HashSet<int>();
        foreach (var p in people.Значения)
            if (p.здесь() && string.IsNullOrEmpty(p.living_with))
                кв.Add(p.apt);
        return кв;
    }

    /// <summary>
    /// Квартиры, в которых никто не живёт. Считается, а не хранится: пока
    /// список вёлся руками, одна и та же квартира успевала попасть в него
    /// дважды, а жилая — остаться в нём и уйти на доски из-под живого человека.
    /// </summary>
    public List<Flat> пустые()
    {
        var занято = занятые();
        return flats.Значения.OrderBy(f => f.apt).Where(f => !занято.Contains(f.apt)).ToList();
    }

    /// <summary>
    /// Квартиры, которые вот ЭТОТ человек считает пустыми (GDD 12.2, 12.3).
    /// Правда мира — <see cref="пустые"/>, и на ней стоит геометрия осады
    /// и расчёт тепла. А занимают и разбирают квартиру по представлению: пока
    /// человек не знает, что хозяин умер, чужая дверь для него остаётся чужой
    /// дверью, а не пустым углом с досками.
    /// </summary>
    public List<Flat> пустые_для(NPC npc)
        => пустые().Where(f => f.owner_died is null || npc.знает_о_смерти.Contains(f.owner_died))
                   .ToList();

    /// <summary>
    /// Откуда можно ломать эту квартиру (GDD 16, стадия стен). «стена» —
    /// соседняя дверь на той же площадке, «потолок» — квартира сверху,
    /// «пол» — снизу. Геометрия у дома настоящая, и это видно по номеру
    /// квартиры, а не по броску кубика.
    /// </summary>
    public List<Flat> соседние(Flat flat, string где)
    {
        int сдвиг = где switch
        {
            "стена" => 0,
            "потолок" => 1,
            "пол" => -1,
            _ => throw new KeyNotFoundException(где),
        };
        int этаж = flat.floor + сдвиг;
        return flats.Значения.OrderBy(f => f.apt)
                    .Where(f => f.apt != flat.apt && f.floor == этаж).ToList();
    }

    public int floor_gap(NPC a, NPC b) => Math.Abs(a.floor - b.floor);

    // ---------- кладовые ----------

    /// <summary>Живой человек, за квартирой которого эта кладовая числится.</summary>
    public NPC? хозяин_кладовой(Кладовая к)
        => flats.TryGetValue(к.apt, out var f) ? чей(f) : null;

    /// <summary>
    /// Может ли человек войти сюда, не срывая замка (GDD 12: жильё как
    /// имущество). Ключом не делятся: он не вещь, которую можно отдать штукой,
    /// а доступ ко всему сразу и навсегда. Поэтому обычной щедрости здесь
    /// нет — доступ дают только два положения, в которых хозяйство и так общее.
    /// </summary>
    public bool есть_ключ(NPC npc, Кладовая к)
    {
        if (к.вскрыта)
            return true;                    // замок сорван — заходят все, кто знает
        if (npc.ключи_кладовых.Contains(к.id))
            return true;
        var хозяин = хозяин_кладовой(к);
        if (хозяин is null || string.Equals(хозяин.id, npc.id, StringComparison.Ordinal)
            || !хозяин.ключи_кладовых.Contains(к.id))
            return false;
        bool под_крышей = string.Equals(npc.living_with, хозяин.id, StringComparison.Ordinal)
                       || string.Equals(хозяин.living_with, npc.id, StringComparison.Ordinal);
        return под_крышей || npc.allies.Contains(хозяин.id);
    }

    /// <summary>Куда этот человек может сходить за своим — без лома и без вины.</summary>
    public List<Кладовая> мои_кладовые(NPC npc)
        => кладовые.Значения.OrderBy(к => к.id, StringComparer.Ordinal)
                   .Where(к => есть_ключ(npc, к)).ToList();

    /// <summary>
    /// Знает ли он, что эта кладовка вообще есть. Подвал один на подъезд,
    /// гаражи стоят за домом рядами — что у соседа там своя дверь, в доме
    /// знают все. Что за ней лежит — не знает никто.
    /// </summary>
    public bool знает_кладовую(NPC npc, Кладовая к) => кладовые.Есть(к.id);

    /// <summary>
    /// Есть ли в этой квартире электричество (GDD 15, уровень 3). Либо в доме
    /// ещё не отключили свет, либо человек завёл свой генератор — ради этого
    /// он его и собирал, платя за это шумом на весь подъезд.
    /// </summary>
    public bool powered(NPC npc)
    {
        if (power_on)
            return true;
        return where(npc).shelter.Взять("питание", double.NaN) == day;
    }

    /// <summary>
    /// Сколько градусов от бывшего отопления дом ещё держит (GDD 15, 21).
    /// Панельная пятиэтажка — не палатка: остывшие батареи не выстуживают её
    /// за одну ночь, дом несколько дней отдаёт накопленное.
    /// </summary>
    public double остаток_отопления()
    {
        var b = B;
        if (heating)
            return b["отопление_градусов"];
        double прошло = day - b["день_отключения_отопления"] + 1;
        double доля = Util.Clamp(1.0 - прошло / Math.Max(1.0, b["остывание_дома_дней"]), 0.0, 1.0);
        return b["отопление_градусов"] * доля;
    }

    /// <summary>Температура в конкретной квартире. Считает стены, а не жильца —
    /// поэтому этим же можно оценить чужую и пустую (GDD 15).</summary>
    public double flat_temp(Flat flat, bool burning = false, bool powered = false)
    {
        var b = B;
        double t = outside + b["дом_базовый_прогрев"] + остаток_отопления();
        t += flat.shelter.Взять("утепление", 0.0) * b["утепление_градус_за_уровень"];
        if (burning && flat.shelter.Взять("буржуйка", 0.0) != 0.0)
            t += b["буржуйка_градусов"];
        if (powered && flat.shelter.Взять("обогреватель", 0.0) != 0.0)
            t += b["обогреватель_градусов"];
        // костёр посреди комнаты: греет хуже печки, дымит и может спалить дом
        if (flat.костёр == day)
            t += b["костёр_градусов"];
        // и то, что вынесли осадой: в пролом дует, и это уже не отопить
        t -= flat.потери_тепла(b);
        return t;
    }

    /// <summary>
    /// Живой человек, который считает эту квартиру своей. Он может в ней
    /// сейчас и не жить — переехал к соседу, — но она его, и занять её значит
    /// оставить его без угла.
    /// </summary>
    public NPC? чей(Flat flat)
    {
        foreach (var p in people.Значения)
            if (p.здесь() && p.apt == flat.apt)
                return p;
        return null;
    }

    /// <summary>
    /// Чего эта квартира стоит вот этому человеку. Три слагаемых, и все три
    /// он может оценить снаружи: сколько она держит тепла (видно по окнам
    /// и по дыму), крепка ли дверь (видно с площадки) и что в ней лежит.
    /// </summary>
    public double ценность_жилья(Flat flat, NPC для)
    {
        var b = B;
        // свет в доме кончился — значит, обогреватель в расчёт больше не идёт
        double тепло = flat_temp(flat, burning: true, powered: power_on);
        double v = (тепло - b["комфортная_температура"]) * b["жильё_вес_тепла"];
        double тревога = Math.Min(1.5, для.panic / 100.0 + 0.2 * (first_incident_day is null ? 0 : 1));
        v += flat.защита(b) * b["жильё_вес_защиты"] * (0.4 + тревога);
        v += Ресурсы.Вещи(flat.stock) * b["жильё_вес_запаса"] * (0.5 + для.t01("жадность"));
        // к квартире приписан погреб или гараж, и это часть её цены. Не запас
        // в нём — что там лежит, снаружи не видно, — а сам факт: своя дверь
        // в подвале, до которой можно дойти и в буран
        int свои = 0;
        foreach (var к in кладовые.Значения)
            if (к.apt == flat.apt && !к.вскрыта && !к.пусто())
                свои++;
        v += свои * b["жильё_вес_кладовой"] * (0.5 + для.t01("жадность"));
        // и свет. Генератор — единственное электричество в доме после того,
        // как встанет подстанция. Это не запас, который можно вынести,
        // а свойство самой квартиры
        if (flat.shelter.Взять("генератор", 0.0) != 0.0 && !power_on)
            v += b["жильё_вес_генератора"] * (0.6 + для.t01("жадность"));
        return v;
    }

    /// <summary>Температура в квартире (GDD 15: утепление, буржуйка, обогреватель).</summary>
    public double room_temp(NPC npc, bool? burning = null)
    {
        // гость греется хозяйской печкой — в этом весь смысл съезжаться
        var хозяин = хозяин_жилья(npc);
        var flat = flats[хозяин.apt];
        bool топит = burning ?? хозяин.burning;
        return flat_temp(flat, burning: топит, powered: powered(npc));
    }

    public void bump(string key, double n = 1) => stats[key] = stats.Взять(key, 0.0) + n;

    /// <summary>
    /// Записать крупное событие дома. Рядом со строкой журнала, а не вместо:
    /// строка остаётся тем, что читает человек, запись — тем, что читает движок.
    /// </summary>
    public Событие событие(ВидСобытия вид, string? кто = null, string? кому = null,
                           string? что = null, double сколько = 0.0, int? где = null)
    {
        var e = new Событие(день: day, вид: вид, кто: кто, кому: кому,
                            что: что, сколько: сколько, где: где);
        события.Add(e);
        hooks.Зов_событие(this, e);
        return e;
    }

    /// <summary>Строка в хронику дома — то, что попадёт в финальный отчёт.</summary>
    public void note(string text) => chronicle.Add($"день {day,2}: {text}");
}
