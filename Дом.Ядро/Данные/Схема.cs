// Перенос house/schema.py: чтение JSON и проверка их против кода.
//
// Опечатка в id, эффекте, условии, пунктике или метке ценности — это правило,
// которое молча ничего не делает; здесь она поднимается при запуске, а не
// через сорок прогонов. Обратная проверка — ручек баланса, которые читает
// код, против balance.json — остаётся в `check.проверить_ручки` на стороне
// прототипа, пока порт не догонит.
using System.Text.Json;

namespace Дом.Ядро;

/// <summary>Все данные одной жизни: баланс, жильцы, события, реплики.</summary>
public sealed class Данные
{
    public required Баланс Баланс { get; init; }
    public required JsonDocument Жильцы { get; init; }
    public required JsonDocument События { get; init; }
    public required JsonDocument Реплики { get; init; }

    public JsonElement npcs => Жильцы.RootElement;
    public JsonElement events => События.RootElement;
    public JsonElement lines => Реплики.RootElement;
}

public static class Схема
{
    // Эффекты, которые умеет применять world.apply_effects. Всё остальное
    // в events.json — опечатка, и лучше узнать о ней при запуске, чем
    // не заметить никогда.
    public static readonly IReadOnlySet<string> ЭФФЕКТЫ = new HashSet<string>(StringComparer.Ordinal)
    {
        "паника", "настроение", "богатство", "связь", "температура", "опасность_вылазки",
        "укрепление_порыв", "болезнь_шанс", "кража_в_доме", "смерть_от_холода",
        "нормальность", "пункт_обогрева",
    };

    // Условия событий, которые умеет проверять world.условие_верно. Условие,
    // которого никто не понимает, тихо считалось бы невыполненным — а значит,
    // событие никогда бы не случилось и никто бы этого не заметил.
    public static readonly IReadOnlySet<string> УСЛОВИЯ = new HashSet<string>(StringComparer.Ordinal)
    {
        "жив_с_умением", "нет_происшествий", "было_происшествие", "все_в_тепле",
        "была_смерть", "есть_раненый", "пусто_снаружи", "есть_пустая_квартира",
        "холоднее", "мало_живых", "есть_тело", "режим", "подъезд_открыт",
        "отключено", "работает", "до_дня", "деньги_ничего_не_стоят",
    };

    public static readonly IReadOnlySet<string> ЧЕРТЫ = new HashSet<string>(StringComparer.Ordinal)
    {
        "жадность", "храбрость", "лояльность", "общительность", "вспыльчивость",
        "сообразительность",
    };

    /// <summary>Коммуналки, о которых умеют спрашивать условия (world.КОММУНАЛКИ).</summary>
    public static readonly IReadOnlySet<string> КОММУНАЛКИ = new HashSet<string>(StringComparer.Ordinal)
        { "отопление", "вода", "свет", "связь", "банк", "магазин" };

    /// <summary>Виды кладовых, которые понимает улица (street.КЛАДОВЫЕ_ВИДЫ).</summary>
    public static readonly IReadOnlySet<string> КЛАДОВЫЕ_ВИДЫ = new HashSet<string>(StringComparer.Ordinal)
        { "погреб", "гараж" };

    /// <summary>Прочитать все данные из папки и проверить их.</summary>
    public static Данные Прочитать(string каталог)
    {
        var баланс = ЧитатьJson(каталог, "balance.json");
        var данные = new Данные
        {
            Баланс = РазобратьБаланс(баланс.RootElement),
            Жильцы = ЧитатьJson(каталог, "npcs.json"),
            События = ЧитатьJson(каталог, "events.json"),
            Реплики = ЧитатьJson(каталог, "lines.json"),
        };
        Проверить(баланс.RootElement, данные.npcs, данные.events, данные.lines);
        return данные;
    }

    public static JsonDocument ЧитатьJson(string каталог, string имя)
    {
        using var поток = File.OpenRead(Path.Combine(каталог, имя));
        return JsonDocument.Parse(поток);
    }

    /// <summary>balance.json → <see cref="Баланс"/>: числа, строки, таблицы.</summary>
    public static Баланс РазобратьБаланс(JsonElement корень)
    {
        var b = new Баланс();
        foreach (var п in корень.EnumerateObject())
        {
            switch (п.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    b.Число(п.Name, п.Value.GetDouble());
                    break;
                case JsonValueKind.String:
                    b.Строку(п.Name, п.Value.GetString()!);
                    break;
                case JsonValueKind.Object:
                {
                    bool вложенный = false;
                    foreach (var в in п.Value.EnumerateObject())
                        if (в.Value.ValueKind == JsonValueKind.Object)
                            вложенный = true;
                    if (вложенный)
                    {
                        var т = Словари.Строкой<Словарь<string, double>>();
                        foreach (var в in п.Value.EnumerateObject())
                        {
                            var свой = Словари.Числа();
                            foreach (var x in в.Value.EnumerateObject())
                                свой[x.Name] = x.Value.GetDouble();
                            т[в.Name] = свой;
                        }
                        b.Таблицу2(п.Name, т);
                    }
                    else
                    {
                        var т = Словари.Числа();
                        foreach (var в in п.Value.EnumerateObject())
                            т[в.Name] = в.Value.GetDouble();
                        b.Таблицу(п.Name, т);
                    }
                    break;
                }
            }
        }
        return b;
    }

    /// <summary>
    /// Проверить данные при загрузке. Без этого опечатка в id тихо стирает
    /// стартовые отношения, дубль id стирает жильца, а несуществующий эффект
    /// события не делает ничего и никто не замечает.
    /// </summary>
    public static void Проверить(JsonElement balance, JsonElement npcs,
                                 JsonElement events, JsonElement lines)
    {
        var bad = new List<string>();

        // характер в данных: пунктики и веса черт. Опечатка здесь — молча
        // ничего не делающее правило, то есть ровно тот случай, который
        // в симуляции никак иначе не заметить
        var пунктики = balance.Объект("пунктики");
        foreach (var п in пунктики.EnumerateObject())
            foreach (var сдвиг in п.Value.EnumerateObject())
                if (!Каталог.ПУНКТИК_КЛЮЧИ.Contains(сдвиг.Name))
                    bad.Add($"пунктик «{п.Name}» сдвигает «{сдвиг.Name}», а такого места в коде нет");
        foreach (var решение in balance.Объект("веса_черт").EnumerateObject())
        {
            if (!Каталог.ВЕСА_КЛЮЧИ.Contains(решение.Name))
                bad.Add($"веса_черт: решение «{решение.Name}» никто не спрашивает");
            foreach (var черта in решение.Value.EnumerateObject())
                if (!ЧЕРТЫ.Contains(черта.Name))
                    bad.Add($"веса_черт[{решение.Name}]: нет такой черты «{черта.Name}»");
        }

        // часы и лимиты в balance.json — таблицы по ключу действия, как
        // и записи каталога. Действие без часов упало бы на первом же ходу,
        // а часы без действия — ручка, которую никто не читает
        var действия = new HashSet<string>(Каталог.COST.Ключи, StringComparer.Ordinal);
        var часы = new HashSet<string>(StringComparer.Ordinal);
        foreach (var п in balance.Объект("часы_действий").EnumerateObject())
            часы.Add(п.Name);
        foreach (var ключ in часы.Except(действия).OrderBy(x => x, StringComparer.Ordinal))
            bad.Add($"часы_действий: «{ключ}» — такого действия в catalog нет");
        foreach (var ключ in действия.Except(часы).OrderBy(x => x, StringComparer.Ordinal))
            bad.Add($"часы_действий: у действия «{ключ}» нет часов");
        foreach (var таблица in new[] { "лимит_в_день", "лимит_на_пару_в_день" })
        {
            var свои = new HashSet<string>(StringComparer.Ordinal);
            foreach (var п in balance.Объект(таблица).EnumerateObject())
                свои.Add(п.Name);
            foreach (var ключ in свои.Except(действия).OrderBy(x => x, StringComparer.Ordinal))
                bad.Add($"{таблица}: «{ключ}» — такого действия в catalog нет");
        }

        var жильцы = npcs.Массив("жильцы").EnumerateArray().ToList();
        var ids = жильцы.Select(d => d.Строка("id")).ToList();
        var dupes = ids.Where(i => ids.Count(x => string.Equals(x, i, StringComparison.Ordinal)) > 1)
                       .Distinct(StringComparer.Ordinal)
                       .OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (dupes.Count > 0)
            bad.Add("повторяющиеся id жильцов: " + string.Join(", ", dupes));
        var known = new HashSet<string>(ids, StringComparer.Ordinal);

        // занятия, которые вообще бывают в строках быта — для проверки привычек
        var занятия = new HashSet<string>(StringComparer.Ordinal);
        foreach (var в in lines.Массив("быт").EnumerateArray())
        {
            var з = в.Строка("занятие");
            if (в.ValueKind == JsonValueKind.Object && з.Length > 0)
                занятия.Add(з);
        }

        foreach (var d in жильцы)
        {
            string id = d.Строка("id");
            foreach (var поле in new[] { "доверие_старт", "ненависть_старт", "осведомлённость_старт" })
                foreach (var п in d.Объект(поле).EnumerateObject())
                    if (!known.Contains(п.Name))
                        bad.Add($"{id}.{поле} ссылается на несуществующего «{п.Name}»");

            var свои_черты = new HashSet<string>(StringComparer.Ordinal);
            foreach (var т in d.Объект("черты").EnumerateObject())
            {
                свои_черты.Add(т.Name);
                double v = т.Value.GetDouble();
                if (!(0 <= v && v <= 10))
                    bad.Add($"{id}: черта {т.Name} = {Ч(v)}, а должна быть 0..10");
            }
            var нет = ЧЕРТЫ.Except(свои_черты).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (нет.Count > 0)
                bad.Add($"{id}: нет черт {string.Join(", ", нет)}");

            string оружие = d.Строка("оружие", "нет");
            if (!Слова.ЕстьОружие(оружие))
                bad.Add($"{id}: неизвестное оружие «{оружие}»");
            string решает = d.Строка("решает", "softmax");
            if (!Решающие.ИМЕНА.Contains(решает))
                bad.Add($"{id}: неизвестный решающий «{решает}» (есть: " +
                        string.Join(", ", Решающие.ИМЕНА.OrderBy(x => x, StringComparer.Ordinal)) + ")");

            double пол = d.Число("нормальность_пол", 0.1);
            if (!(0.0 <= пол && пол <= 0.9))
                bad.Add($"{id}: нормальность_пол = {Ч(пол)}, а должен быть 0..0.9");
            double скорость = d.Число("нормальность_скорость", 1.0);
            if (!(0.2 <= скорость && скорость <= 3.0))
                bad.Add($"{id}: нормальность_скорость = {Ч(скорость)}, а должна быть 0.2..3.0");

            foreach (var п in d.Строки("пунктики"))
                if (!пунктики.Есть(п))
                    bad.Add($"{id}: пунктика «{п}» нет в balance.json");

            // ценности — те же данные, что и пунктики, и та же беда: метка,
            // которую никто не ставит поступкам и никто не судит, лежит
            // в файле и не делает ничего
            foreach (var поле in new[] { "ценит", "не_терпит" })
                foreach (var метка in d.Объект("ценности").Строки(поле))
                    if (!Каталог.ЦЕННОСТИ.Contains(метка))
                        bad.Add($"{id}.ценности.{поле}: метку «{метка}» " +
                                "никто не ставит поступкам и никто не судит");

            // сутки жильца: во сколько встаёт и чем занимает своё время.
            // Опечатка в занятии — привычка, которой не соответствует ни одна
            // строка быта: человек будет «предпочитать» то, чего в доме
            // не бывает, и разницы не увидит никто
            double подъём = d.Число("подъём", 8.0);
            if (!(5.0 <= подъём && подъём <= 12.0))
                bad.Add($"{id}: подъём в {Ч(подъём)} — а бывает с 5 до 12");
            foreach (var п in d.Объект("привычки").EnumerateObject())
            {
                if (п.Name is not ("утро" or "день" or "вечер" or "ночь"))
                    bad.Add($"{id}.привычки: нет такого отрезка дня «{п.Name}»");
                string занятие = п.Value.GetString() ?? "";
                if (!занятия.Contains(занятие))
                    bad.Add($"{id}.привычки: занятия «{занятие}» нет " +
                            "ни в одной строке быта (lines.json)");
            }

            // то, что подъезд знал о нём до метели (сборка): опечатка
            // в названии ресурса — знание, которого ни у кого не появится
            var слава = d.Объект("известен_дому");
            foreach (var ключ in слава.EnumerateObject())
                if (ключ.Name is not ("осведомлённость" or "запас"))
                    bad.Add($"{id}.известен_дому: «{ключ.Name}» никто не читает");
            foreach (var res in слава.Объект("запас").EnumerateObject())
                if (!Ресурсы.ВСЕ.Contains(res.Name, StringComparer.Ordinal))
                    bad.Add($"{id}.известен_дому.запас: нет ресурса «{res.Name}»");
            double осв = слава.Число("осведомлённость", 0.0);
            if (!(0.0 <= осв && осв <= 100.0))
                bad.Add($"{id}.известен_дому: осведомлённость вне 0..100");

            // деньги: наличные лежат в запасах, счёт — отдельно (GDD 18)
            if (d.Число("счёт", 0.0) < 0 || d.Объект("запасы").Число("деньги", 0) < 0)
                bad.Add($"{id}: деньги в минусе");
        }

        var apts = жильцы.Select(d => d.Целое("кв")).ToList();
        apts.AddRange(npcs.Массив("пустые_квартиры").EnumerateArray().Select(f => f.Целое("кв")));
        if (apts.Count != apts.Distinct().Count())
            bad.Add("две квартиры с одним номером в npcs.json");

        // кладовые: по той же причине, по какой проверяются пунктики
        // и ценности. Опечатка в виде («пoгреб» с латинской «o») дала бы
        // кладовку, в которую никто никогда не сходит
        var кладовые = npcs.Массив("кладовые").EnumerateArray().ToList();
        var кл_ids = кладовые.Select(k => k.Строка("id")).ToList();
        if (кл_ids.Count != кл_ids.Distinct(StringComparer.Ordinal).Count())
            bad.Add("две кладовые с одним id в npcs.json");
        foreach (var k in кладовые)
        {
            string имя = k.Строка("id");
            if (имя.Length == 0)
                имя = "?";
            string вид = k.Строка("вид");
            if (!КЛАДОВЫЕ_ВИДЫ.Contains(вид))
                bad.Add($"кладовая «{имя}»: вид «{вид}» никто не понимает (известны: " +
                        string.Join(", ", КЛАДОВЫЕ_ВИДЫ.OrderBy(x => x, StringComparer.Ordinal)) + ")");
            if (!apts.Contains(k.Целое("кв", -1)))
                bad.Add($"кладовая «{имя}» приписана к кв.{k.Целое("кв", -1)}, которой нет в доме");
            foreach (var w in k.Строки("оружие"))
                if (!Слова.ЕстьОружие(w))
                    bad.Add($"кладовая «{имя}»: неизвестное оружие «{w}»");
        }

        var сл_ids = events.Массив("случайные").EnumerateArray()
                           .Select(ev => ev.Строка("id")).ToList();
        var повторы = сл_ids.Where(i => сл_ids.Count(x => string.Equals(x, i, StringComparison.Ordinal)) > 1)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (повторы.Count > 0)
            bad.Add("повторяющиеся id событий: " + string.Join(", ", повторы));
        foreach (var group in new[] { "скриптовые", "случайные" })
            foreach (var ev in events.Массив(group).EnumerateArray())
            {
                string имя = ev.Строка("id");
                if (имя.Length == 0)
                    имя = "день " + ev.Целое("день");
                foreach (var k in ev.Объект("эффекты").EnumerateObject())
                    if (!ЭФФЕКТЫ.Contains(k.Name))
                        bad.Add($"событие «{имя}»: эффект «{k.Name}» никто не применяет");
                foreach (var поле in new[] { "условие", "отменяется_если" })
                    if (ev.Есть(поле))
                        ПроверитьУсловие(ev.GetProperty(поле), $"событие «{имя}»", bad);
                if (string.Equals(group, "случайные", StringComparison.Ordinal)
                    && ev.Массив("окно").GetArrayLength() == 0)
                    bad.Add($"событие «{имя}»: нет окна дней");
            }

        // реплики: та же проверка, что и у событий. Реплика с условием-опечаткой
        // просто исчезла бы из чата, и понять это по логу невозможно
        foreach (var (раздел, варианты) in Разделы(lines))
        {
            int i = 0;
            foreach (var в in варианты.EnumerateArray())
            {
                string где = $"реплика {раздел}[{i++}]";
                if (в.ValueKind == JsonValueKind.String)
                    continue;
                if (в.ValueKind != JsonValueKind.Object || в.Строка("текст").Length == 0)
                {
                    bad.Add($"{где}: ни строка, ни {{текст, условие}}");
                    continue;
                }
                if (в.Есть("условие"))
                    ПроверитьУсловие(в.GetProperty("условие"), где, bad);
            }
        }

        if (bad.Count > 0)
            throw new InvalidOperationException(
                "Данные не в порядке:\n  · " + string.Join("\n  · ", bad));
    }

    /// <summary>Пары (путь, список реплик) по всем разделам lines.json
    /// (checks._разделы).</summary>
    public static IEnumerable<(string раздел, JsonElement варианты)> Разделы(JsonElement lines)
    {
        foreach (var ключ in lines.EnumerateObject().OrderBy(п => п.Name, StringComparer.Ordinal))
        {
            if (ключ.Value.ValueKind == JsonValueKind.Array)
                yield return (ключ.Name, ключ.Value);
            else if (ключ.Value.ValueKind == JsonValueKind.Object)
                foreach (var под in ключ.Value.EnumerateObject()
                                              .OrderBy(п => п.Name, StringComparer.Ordinal))
                    if (под.Value.ValueKind == JsonValueKind.Array)
                        yield return ($"{ключ.Name}.{под.Name}", под.Value);
        }
    }

    /// <summary>
    /// Условие события или реплики: вид известен и поля на месте. Условие
    /// с опечаткой считается невыполненным и потому никогда не срабатывает
    /// молча — ровно тот случай, который в симуляции не заметить.
    /// </summary>
    private static void ПроверитьУсловие(JsonElement условие, string где, List<string> bad)
    {
        if (условие.ValueKind == JsonValueKind.Array)
        {
            foreach (var у in условие.EnumerateArray())
                ПроверитьУсловие(у, где, bad);
            return;
        }
        string вид = условие.Строка("вид");
        if (!УСЛОВИЯ.Contains(вид))
        {
            bad.Add($"{где}: условие «{(условие.Есть("вид") ? вид : "None")}» никто не проверяет");
            return;
        }
        if ((вид is "отключено" or "работает") && !КОММУНАЛКИ.Contains(условие.Строка("что")))
            bad.Add($"{где}: «{вид}» без понятного «что» " +
                    $"({(условие.Есть("что") ? условие.Строка("что") : "None")})");
        if (вид == "до_дня"
            && !(условие.Есть("день")
                 && условие.GetProperty("день").ValueKind == JsonValueKind.Number
                 && условие.GetProperty("день").TryGetInt32(out _)))
            bad.Add($"{где}: «до_дня» без номера дня");
    }

    /// <summary>Число так, как его печатает Python: «10» вместо «10.0»
    /// у целых значений, иначе тексты ошибок разошлись бы на пустом месте.</summary>
    private static string Ч(double v)
        => v == Math.Floor(v) && Math.Abs(v) < 1e15
           ? ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture)
           : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
