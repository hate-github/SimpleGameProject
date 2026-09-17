// Настройки игрока (Esc в игре): управление, экран, звук.
//
// Здесь — что настраивается, какие значения законны и как это лежит
// на диске. Применяет их движок (`Дом.Годот/Настроить.cs`), и только он:
// как зовётся клавиша и что такое «полный экран», знает Godot. А разбор
// файла, умолчания и переназначение клавиш обходятся без движка и потому
// проверяются из консоли (раздел «настройки»): файл настроек ломается
// у живого человека, и чинить его приходится не глядя.
//
// Три правила.
//
//  · **Файл настроек игру не останавливает.** Наследие читается строго:
//    нет поля — сказать, какого, и не играть, иначе петля тихо обнулится.
//    Настройкам так нельзя: потерять громкость не беда, а не запустить
//    игру из-за неё — беда. Всё непонятное заменяется умолчанием,
//    и об этом остаётся замечание в логе.
//  · **Клавиша записана словом**: «W», «Space», «ПКМ». Имена клавиш —
//    те, что даёт сам Godot, и файл читается глазом, как и наследие.
//  · **Клавиша ловится по месту на клавиатуре, а не по букве**: на русской
//    раскладке W — это Ц, и ходьба от раскладки зависеть не должна.
//
// Две клавиши не отдаются: Esc открывает эти самые настройки (без него
// из переназначения не выйти), а цифры — номера вариантов в листе
// вопроса, и номер значит то же, что в консоли (ГДД 13).

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Дом.Экран;

/// <summary>Действие, которое вешается на клавишу: ключ в файле (он же
/// имя действия в движке), подпись на экране и клавиша по умолчанию.</summary>
public sealed record Привязка(string ключ, string подпись, string клавиша);

/// <summary>Размер в точках — окна или картинки мира.</summary>
public readonly record struct Размер(int ширина, int высота)
{
    public override string ToString() => $"{ширина}×{высота}";

    /// <summary>«1280×800» (или с латинской x) — или ничего.</summary>
    public static Размер? Разобрать(string? текст)
    {
        if (текст is null)
            return null;
        var части = текст.Split('×', 'x', 'X');
        if (части.Length != 2
            || !int.TryParse(части[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int ш)
            || !int.TryParse(части[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int в))
            return null;
        return new Размер(ш, в);
    }
}

/// <summary>Что вешается на клавиши и какие клавиши не отдаются.</summary>
public static class Клавиши
{
    public const string ВПЕРЁД = "вперёд";
    public const string НАЗАД = "назад";
    public const string ВЛЕВО = "влево";
    public const string ВПРАВО = "вправо";
    public const string ДЕЙСТВИЕ = "действие";
    public const string СПИСОК = "список";
    public const string СОСТОЯНИЕ = "состояние";
    public const string ЖУРНАЛ = "журнал";
    public const string ТЕЛЕФОН = "телефон";
    public const string ПОДЪЕЗД = "подъезд";
    public const string КАРТА = "карта";
    public const string КАРМАН = "карман";

    /// <summary>Все действия — в том порядке, в каком они стоят на экране.
    /// Клавиши по умолчанию — те, на которых игра жила до настроек.</summary>
    public static readonly IReadOnlyList<Привязка> ВСЕ = new Привязка[]
    {
        new(ВПЕРЁД, "вперёд", "W"),
        new(НАЗАД, "назад", "S"),
        new(ВЛЕВО, "влево", "A"),
        new(ВПРАВО, "вправо", "D"),
        new(ДЕЙСТВИЕ, "постучать, взять, бросить дело", "E"),
        new(СПИСОК, "весь список вариантов", "Space"),
        new(СОСТОЯНИЕ, "состояние", "Q"),
        new(ЖУРНАЛ, "журнал", "J"),
        new(ТЕЛЕФОН, "телефон", "T"),
        new(ПОДЪЕЗД, "план подъезда", "P"),
        new(КАРТА, "карта", "M"),
        new(КАРМАН, "карман", "I"),
    };

    /// <summary>Кнопки мыши — словом, как их зовут игроки: левая, правая,
    /// колесо и две боковые.</summary>
    public static readonly IReadOnlyList<string> МЫШЬ = new[] { "ЛКМ", "ПКМ", "СКМ", "Мышь 4", "Мышь 5" };

    public const string ESC = "Escape";

    public static bool Мышью(string клавиша) => МЫШЬ.Contains(клавиша, StringComparer.Ordinal);

    /// <summary>Не отдаются: Esc открывает настройки, цифры — номера
    /// вариантов (и в верхнем ряду, и на цифровом блоке).</summary>
    public static bool Занята(string клавиша)
        => клавиша == ESC
           || (клавиша.Length == 1 && клавиша[0] is >= '0' and <= '9')
           || (клавиша.Length == 4 && клавиша.StartsWith("Kp ", StringComparison.Ordinal)
               && клавиша[3] is >= '0' and <= '9');

    public static Привязка? Какая(string ключ)
        => ВСЕ.FirstOrDefault(п => string.Equals(п.ключ, ключ, StringComparison.Ordinal));

    /// <summary>Как клавиша выглядит на экране. Имена — движка («Space»,
    /// «BracketLeft»); те, что человек читает иначе, названы по-людски.</summary>
    public static string Подпись(string клавиша) => клавиша switch
    {
        "" => "—",
        "Space" => "Пробел",
        ESC => "Esc",
        "Up" => "↑",
        "Down" => "↓",
        "Left" => "←",
        "Right" => "→",
        "CapsLock" => "Caps Lock",
        "Windows" => "Win",
        "QuoteLeft" => "`",
        "Minus" => "-",
        "Equal" => "=",
        "BracketLeft" => "[",
        "BracketRight" => "]",
        "Semicolon" => ";",
        "Apostrophe" => "'",
        "BackSlash" => "\\",
        "Comma" => ",",
        "Period" => ".",
        "Slash" => "/",
        _ when клавиша.StartsWith("Kp ", StringComparison.Ordinal) => "цифр. " + клавиша[3..],
        _ => клавиша,
    };
}

/// <summary>Канал громкости: ключ в файле, подпись и что в нём звучит.</summary>
public sealed record Канал(string ключ, string подпись, string что);

public static class Громкость
{
    public const string ОБЩАЯ = "общая";
    public const string МЕТЕЛЬ = "метель";
    public const string ШАГИ = "шаги";
    public const string ФОН = "фон";

    public static readonly IReadOnlyList<Канал> ВСЕ = new Канал[]
    {
        new(ОБЩАЯ, "общая громкость", "всё сразу"),
        new(МЕТЕЛЬ, "метель", "ветер за стенами и во дворе"),
        new(ШАГИ, "шаги и вещи", "шаги, ворота, замок, коробки, шум у дверей"),
        new(ФОН, "фон", "лампы, печь, генератор"),
    };

    /// <summary>Ниже этого — тишина, а не «очень тихо».</summary>
    public const double ТИШЕ_НЕКУДА = 0.005;
    public const double ТИШИНА_ДБ = -80;

    /// <summary>Доля громкости в децибелах: половина — минус шесть.
    /// Движок складывает децибелы, а игрок двигает долю.</summary>
    public static double Децибелы(double доля)
        => доля <= ТИШЕ_НЕКУДА ? ТИШИНА_ДБ : 20 * Math.Log10(Math.Min(1.0, доля));
}

/// <summary>Режим окна: ключ в файле и подпись.</summary>
public sealed record РежимОкна(string ключ, string подпись);

/// <summary>
/// Какие размеры предлагать.
///
/// В окне «разрешение» — это размер окна, и предлагаются только те, что
/// помещаются на рабочий стол вместе с рамкой. На весь экран размер окна
/// задаёт сам экран (Godot видеорежим не меняет), и «разрешение» там —
/// размер картинки мира: доля экрана, растянутая до него. Надписи при этом
/// рисуются в полный размер и остаются чёткими.
/// </summary>
public static class Разрешения
{
    public const string ОКНО = "окно";
    public const string ПОЛНЫЙ = "полный";
    public const string БЕЗ_РАМКИ = "без рамки";

    public static readonly IReadOnlyList<РежимОкна> РЕЖИМЫ = new РежимОкна[]
    {
        new(ОКНО, "окно"),
        new(ПОЛНЫЙ, "полный экран"),
        new(БЕЗ_РАМКИ, "без рамки на весь экран"),
    };

    /// <summary>Обычные размеры окна, 16:9 и 16:10, от малого к большому.</summary>
    public static readonly IReadOnlyList<Размер> ОКНА = new Размер[]
    {
        new(1024, 576), new(1152, 648), new(1280, 720), new(1280, 800),
        new(1366, 768), new(1440, 900), new(1600, 900), new(1680, 1050),
        new(1920, 1080), new(1920, 1200), new(2560, 1440), new(2560, 1600),
        new(3200, 1800), new(3840, 2160),
    };

    /// <summary>Рамка окна Windows: заголовок и края. Окно, у которого
    /// заголовок уехал за край стола, уже не закрыть и не перетащить.</summary>
    public const int РАМКА_ШИРИНА = 16;
    public const int РАМКА_ВЫСОТА = 40;

    /// <summary>Доли экрана для картинки мира на весь экран.</summary>
    public static readonly IReadOnlyList<double> ДОЛИ = new[] { 1.0, 5.0 / 6.0, 0.75, 2.0 / 3.0, 0.5 };

    /// <summary>Размеры окна, которые помещаются на рабочий стол
    /// (<paramref name="стол"/> — экран без панели задач).</summary>
    public static IReadOnlyList<Размер> Для_окна(Размер стол)
    {
        var влезло = ОКНА.Where(р => р.ширина + РАМКА_ШИРИНА <= стол.ширина
                                     && р.высота + РАМКА_ВЫСОТА <= стол.высота).ToList();
        if (влезло.Count == 0)
            влезло.Add(new Размер(Math.Max(320, стол.ширина - РАМКА_ШИРИНА),
                                  Math.Max(200, стол.высота - РАМКА_ВЫСОТА)));
        return влезло;
    }

    /// <summary>Размеры картинки на весь экран — по доле на строку.</summary>
    public static IReadOnlyList<Размер> Для_экрана(Размер экран)
    {
        if (экран.ширина <= 0 || экран.высота <= 0)
            экран = Настройки.ОКНО;
        return ДОЛИ.Select(д => new Размер(Чётное(экран.ширина * д), Чётное(экран.высота * д)))
                   .ToList();
    }

    private static int Чётное(double x) => (int)Math.Round(x / 2, MidpointRounding.AwayFromZero) * 2;

    /// <summary>Ближайший к желаемому размер из списка: по сумме
    /// расхождений сторон; при равенстве — первый.</summary>
    public static int Ближайший(IReadOnlyList<Размер> список, Размер хочу)
    {
        int лучший = 0;
        int разница = int.MaxValue;
        for (int i = 0; i < список.Count; i++)
        {
            int р = Math.Abs(список[i].ширина - хочу.ширина) + Math.Abs(список[i].высота - хочу.высота);
            if (р < разница)
                (лучший, разница) = (i, р);
        }
        return лучший;
    }

    public static int Ближайшая_доля(double доля)
    {
        int лучший = 0;
        for (int i = 1; i < ДОЛИ.Count; i++)
            if (Math.Abs(ДОЛИ[i] - доля) < Math.Abs(ДОЛИ[лучший] - доля))
                лучший = i;
        return лучший;
    }
}

/// <summary>Чем кончилось переназначение: принято ли, у какого действия
/// клавиша была (оно получило прежнюю клавишу этого) и почему нет.</summary>
public sealed record Назначение(bool принято, string? обмен, string? почему);

public sealed class Настройки
{
    public const int ВЕРСИЯ = 1;

    public const double МЫШЬ_ОТ = 0.2;
    public const double МЫШЬ_ДО = 3.0;
    public const double ЧЁТКОСТЬ_ОТ = 0.5;

    /// <summary>Окно по умолчанию — как в `project.godot`.</summary>
    public static readonly Размер ОКНО = new(1280, 800);

    private readonly Dictionary<string, string> _клавиши = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _громкость = new(StringComparer.Ordinal);

    /// <summary>Множитель к обычной чувствительности мыши.</summary>
    public double чувствительность { get; set; } = 1.0;
    public bool инверсия { get; set; }

    /// <summary>Один из ключей <see cref="Разрешения.РЕЖИМЫ"/>.</summary>
    public string режим { get; set; } = Разрешения.ОКНО;
    public Размер окно { get; set; } = ОКНО;

    /// <summary>Доля экрана для картинки мира на весь экран.</summary>
    public double чёткость { get; set; } = 1.0;
    public bool синхронизация { get; set; } = true;

    public Настройки()
    {
        СброситьУправление();
        СброситьЭкран();
        СброситьЗвук();
    }

    /// <summary>На весь экран — с рамкой или без.</summary>
    public bool полный => режим != Разрешения.ОКНО;

    public string Клавиша(string действие)
        => _клавиши.TryGetValue(действие, out var к) ? к : "";

    public double Уровень(string канал)
        => _громкость.TryGetValue(канал, out var д) ? д : 1.0;

    public void Уровень(string канал, double доля)
    {
        if (Громкость.ВСЕ.Any(к => к.ключ == канал))
            _громкость[канал] = Math.Clamp(Math.Round(доля, 3), 0.0, 1.0);
    }

    public void СброситьУправление()
    {
        _клавиши.Clear();
        foreach (var п in Клавиши.ВСЕ)
            _клавиши[п.ключ] = п.клавиша;
        чувствительность = 1.0;
        инверсия = false;
    }

    public void СброситьЭкран()
    {
        режим = Разрешения.ОКНО;
        окно = ОКНО;
        чёткость = 1.0;
        синхронизация = true;
    }

    public void СброситьЗвук()
    {
        foreach (var к in Громкость.ВСЕ)
            _громкость[к.ключ] = 1.0;
    }

    /// <summary>
    /// Повесить действие на клавишу. Клавиша другого действия
    /// не отнимается молча: тот получает прежнюю клавишу этого, и оба
    /// остаются при клавишах. Esc и цифры не отдаются вовсе.
    /// </summary>
    public Назначение Назначить(string действие, string клавиша)
    {
        if (Клавиши.Какая(действие) is null)
            return new(false, null, $"нет действия «{действие}»");
        if (string.IsNullOrEmpty(клавиша))
            return new(false, null, "клавиша не названа");
        if (Клавиши.Занята(клавиша))
            return new(false, null, клавиша == Клавиши.ESC
                ? "Esc открывает настройки — его не отдать"
                : "цифры — номера вариантов, их не отдать");
        string было = Клавиша(действие);
        if (было == клавиша)
            return new(true, null, null);
        string? чья = Клавиши.ВСЕ.Select(п => п.ключ)
                                 .FirstOrDefault(к => к != действие && Клавиша(к) == клавиша);
        _клавиши[действие] = клавиша;
        if (чья is not null)
            _клавиши[чья] = было;
        return new(true, чья, null);
    }

    /// <summary>
    /// Привести клавиши в порядок: у каждого действия своя клавиша или
    /// никакой, занятые игрой и неизвестные — прочь.
    ///
    /// Выбор игрока важнее умолчаний: сперва расставляются клавиши
    /// из файла (кто выше в списке, тот и держит спорную), потом
    /// недостающие получают своё умолчание — если оно свободно; если нет,
    /// действие остаётся без клавиши и на экране видно прочерком.
    /// Действие, которого в файле не было вовсе (появилось позже файла),
    /// молча получает умолчание: поле, пришедшее позже, обязано иметь
    /// осмысленное умолчание — правило то же, что у наследия.
    ///
    /// <paramref name="известна"/> отвечает, знает ли движок такую
    /// клавишу; без него имена не судятся.
    /// </summary>
    public List<string> Починить(Func<string, bool>? известна = null)
    {
        var замечания = new List<string>();
        var держат = new Dictionary<string, string>(StringComparer.Ordinal);   // клавиша → действие
        var без = new List<Привязка>();
        foreach (var п in Клавиши.ВСЕ)
        {
            if (!_клавиши.TryGetValue(п.ключ, out var к))
            {
                без.Add(п);
                continue;
            }
            if (к.Length == 0)
                continue;                    // без клавиши — выбор игрока
            string? беда = Клавиши.Занята(к) ? "эту клавишу держит игра"
                : известна is not null && !Клавиши.Мышью(к) && !известна(к) ? "такой клавиши нет"
                : держат.TryGetValue(к, out var у) ? $"клавиша уже у «{Клавиши.Какая(у)!.подпись}»"
                : null;
            if (беда is null)
            {
                держат[к] = п.ключ;
                continue;
            }
            замечания.Add($"«{п.подпись}» = {к}: {беда}");
            _клавиши.Remove(п.ключ);
            без.Add(п);
        }
        foreach (var п in без)
        {
            if (держат.TryGetValue(п.клавиша, out var у))
            {
                _клавиши[п.ключ] = "";
                замечания.Add($"«{п.подпись}» осталось без клавиши: "
                              + $"{п.клавиша} уже у «{Клавиши.Какая(у)!.подпись}»");
                continue;
            }
            _клавиши[п.ключ] = п.клавиша;
            держат[п.клавиша] = п.ключ;
        }
        foreach (var лишний in _клавиши.Keys.Where(к => Клавиши.Какая(к) is null).ToList())
            _клавиши.Remove(лишний);
        return замечания;
    }

    // ------------------------------------------------------------ файл

    private static readonly JsonSerializerOptions КАК_ПИСАТЬ = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,     // кириллица остаётся читаемой
    };

    public string ВJson()
    {
        var клавиши = new JsonObject();
        foreach (var п in Клавиши.ВСЕ)
            клавиши[п.ключ] = Клавиша(п.ключ);
        var громкость = new JsonObject();
        foreach (var к in Громкость.ВСЕ)
            громкость[к.ключ] = Уровень(к.ключ);
        return new JsonObject
        {
            ["версия"] = ВЕРСИЯ,
            ["управление"] = new JsonObject
            {
                ["клавиши"] = клавиши,
                ["чувствительность"] = чувствительность,
                ["инверсия"] = инверсия,
            },
            ["экран"] = new JsonObject
            {
                ["режим"] = режим,
                ["окно"] = окно.ToString(),
                ["чёткость"] = чёткость,
                ["синхронизация"] = синхронизация,
            },
            ["громкость"] = громкость,
        }.ToJsonString(КАК_ПИСАТЬ);
    }

    /// <summary>
    /// Прочитать файл настроек. Не бросает: что не разобралось, то
    /// по умолчанию, а в <paramref name="замечания"/> — что и почему.
    /// </summary>
    public static Настройки ИзJson(string текст, List<string> замечания)
    {
        try
        {
            return Разобрать(текст, замечания);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
                                      or ArgumentException or FormatException)
        {
            замечания.Add($"файл не читается ({e.GetType().Name}: {e.Message}) — всё по умолчанию");
            return new Настройки();
        }
    }

    private static Настройки Разобрать(string текст, List<string> замечания)
    {
        var н = new Настройки();
        if (JsonNode.Parse(текст) is not JsonObject о)
        {
            замечания.Add("в файле не настройки — всё по умолчанию");
            return н;
        }

        double? версия = Число(о, "версия", замечания);
        if (версия is null)
            замечания.Add("у файла нет версии — читаю как первую");
        else if (версия > ВЕРСИЯ)
            замечания.Add($"файл версии {Строкой(версия.Value)}, а игра знает до {ВЕРСИЯ} — "
                          + "читаю, что понимаю");

        if (Раздел(о, "управление", замечания) is JsonObject у)
        {
            if (Раздел(у, "клавиши", замечания) is JsonObject клавиши)
            {
                н._клавиши.Clear();
                foreach (var (действие, узел) in клавиши)
                {
                    if (Клавиши.Какая(действие) is null)
                        замечания.Add($"нет действия «{действие}» — пропущено");
                    else if (узел is JsonValue з && з.TryGetValue(out string? клавиша))
                        н._клавиши[действие] = клавиша.Trim();
                    else
                        замечания.Add($"«{действие}» = {узел?.ToJsonString() ?? "null"}: не клавиша");
                }
            }
            if (Число(у, "чувствительность", замечания) is double ч)
                н.чувствительность = В_пределах(ч, МЫШЬ_ОТ, МЫШЬ_ДО, "чувствительность", замечания);
            if (Да_нет(у, "инверсия", замечания) is bool и)
                н.инверсия = и;
        }
        замечания.AddRange(н.Починить());

        if (Раздел(о, "экран", замечания) is JsonObject э)
        {
            if (Строка(э, "режим", замечания) is string р)
            {
                if (Разрешения.РЕЖИМЫ.Any(x => x.ключ == р))
                    н.режим = р;
                else
                    замечания.Add($"режим «{р}» неизвестен — окно");
            }
            if (Строка(э, "окно", замечания) is string окно)
            {
                if (Размер.Разобрать(окно) is Размер р2 && р2.ширина is >= 320 and <= 16384
                                                        && р2.высота is >= 200 and <= 16384)
                    н.окно = р2;
                else
                    замечания.Add($"окно «{окно}» — не размер, взято {ОКНО}");
            }
            if (Число(э, "чёткость", замечания) is double д)
                н.чёткость = В_пределах(д, ЧЁТКОСТЬ_ОТ, 1.0, "чёткость", замечания);
            if (Да_нет(э, "синхронизация", замечания) is bool с)
                н.синхронизация = с;
        }

        if (Раздел(о, "громкость", замечания) is JsonObject г)
            foreach (var (канал, _) in г)
            {
                if (!Громкость.ВСЕ.Any(к => к.ключ == канал))
                    замечания.Add($"нет канала «{канал}» — пропущено");
                else if (Число(г, канал, замечания) is double доля)
                    н._громкость[канал] = В_пределах(доля, 0.0, 1.0, канал, замечания);
            }

        return н;
    }

    private static JsonObject? Раздел(JsonObject о, string имя, List<string> замечания)
    {
        var узел = о[имя];
        if (узел is null or JsonObject)
            return узел as JsonObject;
        замечания.Add($"«{имя}» — не раздел, взято по умолчанию");
        return null;
    }

    private static double? Число(JsonObject о, string имя, List<string> замечания)
    {
        var узел = о[имя];
        if (узел is null)
            return null;
        if (узел is JsonValue з && з.GetValueKind() == JsonValueKind.Number
            && з.TryGetValue(out double д) && double.IsFinite(д))
            return д;
        замечания.Add($"«{имя}» = {узел.ToJsonString()}: не число — по умолчанию");
        return null;
    }

    private static bool? Да_нет(JsonObject о, string имя, List<string> замечания)
    {
        var узел = о[имя];
        if (узел is null)
            return null;
        if (узел is JsonValue з && з.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            return з.GetValue<bool>();
        замечания.Add($"«{имя}» = {узел.ToJsonString()}: не да и не нет — по умолчанию");
        return null;
    }

    private static string? Строка(JsonObject о, string имя, List<string> замечания)
    {
        var узел = о[имя];
        if (узел is null)
            return null;
        if (узел is JsonValue з && з.GetValueKind() == JsonValueKind.String)
            return з.GetValue<string>();
        замечания.Add($"«{имя}» = {узел.ToJsonString()}: не строка — по умолчанию");
        return null;
    }

    private static double В_пределах(double д, double от, double до, string имя, List<string> замечания)
    {
        double в = Math.Clamp(д, от, до);
        if (в != д)
            замечания.Add($"«{имя}» = {Строкой(д)} — вне {Строкой(от)}…{Строкой(до)}, взято {Строкой(в)}");
        return в;
    }

    private static string Строкой(double д) => д.ToString(CultureInfo.InvariantCulture);
}
