// Инструменты и замки (задание автора, п. 4): голыми руками замок не сорвать.
//
// До этого взлом был делом одних рук: подошёл к чужому погребу — бей.
// Теперь для взлома нужен инструмент, и не всякий подходит ко всякому замку:
// навесной берут болторезом или молотком, дверной — отмычкой или монтировкой,
// железную дверь — монтировкой, ящик — монтировкой или топором. Что чем
// берётся и во что обходится — таблица в `data/инструменты.json`, а не код:
// новый инструмент или новый замок — строка в данных.
//
// Инструмент решает три вещи, и все три — через правила, которые уже есть:
//   · сколько часов (доля от `кладовая_вскрытие_часы`);
//   · какой шанс (множитель к мерке работы на трудность замка);
//   · насколько громко (уровень шума дома — и значит, кто услышит
//     и спустится, `Взлом.услышали`).
// Одно покупается другим: болторез перекусывает навесной замок за двадцать
// минут и почти беззвучно, но лежит только в чужом гараже; молоток есть
// в любой квартире, но бьёт полтора часа и слышен на весь подвал.
//
// Снаряжение — только у игрока, как ноша и карман: у соседей его нет,
// в снимок и сейв дома оно не входит и умирает вместе с героем (ГДД 8:
// вещи не переносятся). Что лежит дома и что с собой — два списка: взять
// с собой можно у своей полки. Оружие жильца, которое заодно инструмент
// (топор), в счёт идёт само: оно и так при нём.

using System.Text.Json;

namespace Дом.Ядро;

/// <summary>Как инструмент берёт замок: доля часов, множитель шанса, уровень шума.</summary>
public sealed record Приём(double часы, double шанс, int шум);

/// <summary>Инструмент: как зовётся, чем («молотком»), что им делают
/// с замком, тяжёлый ли (не спрятать) и какие замки берёт.</summary>
public sealed record Инструмент(string id, string имя, string чем, string как, bool тяжёлый,
                                IReadOnlyDictionary<string, Приём> замки)
{
    public Приём? Берёт(string замок) => замки.TryGetValue(замок, out var п) ? п : null;
}

/// <summary>Каталог: замки, инструменты, что где лежит. Читается из
/// `data/инструменты.json` и проверяется при чтении: инструмент к замку,
/// которого нет, — опечатка, а не расширение.</summary>
public sealed class Инструменты
{
    private readonly Dictionary<string, string> _замки = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Инструмент> _все = new(StringComparer.Ordinal);
    private readonly Dictionary<ВидКладовой, string> _кладовые = new();
    private readonly Dictionary<Оружие, string> _оружие = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _дома = new(StringComparer.Ordinal);
    private readonly List<string> _верстак = new();

    /// <summary>Замки: id → как зовётся.</summary>
    public IReadOnlyDictionary<string, string> замки => _замки;

    /// <summary>Инструменты по id, в порядке файла.</summary>
    public IReadOnlyDictionary<string, Инструмент> все => _все;

    /// <summary>Что лежит на верстаке в гараже соседа.</summary>
    public IReadOnlyList<string> верстак => _верстак;

    /// <summary>Пустой каталог: инструментов нет ни у кого, и ломать нечем.</summary>
    public static readonly Инструменты НИКАКИХ = new();

    /// <summary>Какой замок на кладовой этого вида.</summary>
    public string? Замок(Кладовая к) => _кладовые.TryGetValue(к.вид, out var з) ? з : null;

    /// <summary>Что лежит дома у этого жильца в начале жизни.</summary>
    public IReadOnlyList<string> Дома(string кто)
        => _дома.TryGetValue(кто, out var с) ? с
         : _дома.TryGetValue("*", out var все) ? все
         : Array.Empty<string>();

    /// <summary>Каким инструментом оружие жильца заодно служит — или никаким.</summary>
    public string? Из_оружия(Оружие о) => _оружие.TryGetValue(о, out var id) ? id : null;

    /// <summary>Какие инструменты берут этот замок — в порядке файла.</summary>
    public IEnumerable<Инструмент> Берут(string замок)
        => _все.Values.Where(и => и.Берёт(замок) is not null);

    public static Инструменты Прочитать(string каталог)
    {
        var к = new Инструменты();
        using var поток = File.OpenRead(Path.Combine(каталог, "инструменты.json"));
        using var док = JsonDocument.Parse(поток);
        var корень = док.RootElement;

        foreach (var з in корень.GetProperty("замки").EnumerateObject())
            к._замки[з.Name] = з.Value.GetString()!;

        foreach (var и in корень.GetProperty("инструменты").EnumerateObject())
        {
            var в = и.Value;
            var приёмы = new Dictionary<string, Приём>(StringComparer.Ordinal);
            foreach (var з in в.GetProperty("замки").EnumerateObject())
            {
                if (!к._замки.ContainsKey(з.Name))
                    throw new InvalidDataException(
                        $"инструменты.json: «{и.Name}» берёт замок «{з.Name}», которого нет");
                var п = new Приём(з.Value.GetProperty("часы").GetDouble(),
                                  з.Value.GetProperty("шанс").GetDouble(),
                                  з.Value.GetProperty("шум").GetInt32());
                if (п.часы <= 0 || п.шанс <= 0 || п.шум < 0 || п.шум > 5)
                    throw new InvalidDataException(
                        $"инструменты.json: «{и.Name}» на «{з.Name}» — часы и шанс больше нуля, "
                        + "шум от нуля до пяти");
                приёмы[з.Name] = п;
            }
            к._все[и.Name] = new Инструмент(и.Name, в.GetProperty("имя").GetString()!,
                                            в.GetProperty("чем").GetString()!,
                                            в.GetProperty("как").GetString()!,
                                            в.GetProperty("тяжёлый").GetBoolean(), приёмы);
        }

        foreach (var п in корень.GetProperty("кладовые").EnumerateObject())
        {
            var вид = Слова.Кладовая(п.Name);
            string замок = п.Value.GetString()!;
            if (!к._замки.ContainsKey(замок))
                throw new InvalidDataException($"инструменты.json: на {п.Name} замок «{замок}», которого нет");
            к._кладовые[вид] = замок;
        }
        foreach (var п in корень.GetProperty("оружие").EnumerateObject())
        {
            if (!Enum.TryParse<Оружие>(п.Name, out var о))
                throw new InvalidDataException($"инструменты.json: оружия «{п.Name}» нет");
            к._оружие[о] = Известный(к, п.Value.GetString()!, "оружие");
        }
        foreach (var п in корень.GetProperty("дома").EnumerateObject())
            к._дома[п.Name] = п.Value.EnumerateArray()
                .Select(x => Известный(к, x.GetString()!, "дома")).ToList();
        foreach (var x in корень.GetProperty("верстак").EnumerateArray())
            к._верстак.Add(Известный(к, x.GetString()!, "верстак"));
        return к;
    }

    private static string Известный(Инструменты к, string id, string где)
        => к._все.ContainsKey(id)
           ? id
           : throw new InvalidDataException($"инструменты.json: в «{где}» инструмент «{id}», которого нет");
}

/// <summary>
/// Что у героя из инструментов: что при нём и что лежит дома. Только
/// у игрока — как ноша; мимо снимка и сейва.
/// </summary>
public sealed class Снаряжение
{
    private readonly SortedSet<string> _с_собой = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _дома = new(StringComparer.Ordinal);

    public Снаряжение(IEnumerable<string>? дома = null)
    {
        foreach (var id in дома ?? Array.Empty<string>())
            _дома.Add(id);
    }

    public IReadOnlyCollection<string> с_собой => _с_собой;
    public IReadOnlyCollection<string> дома => _дома;

    /// <summary>Взять из дома с собой. false — дома такого нет.</summary>
    public bool Взять(string id) => _дома.Remove(id) && _с_собой.Add(id);

    /// <summary>Положить дома. false — с собой такого нет.</summary>
    public bool Положить(string id) => _с_собой.Remove(id) && _дома.Add(id);

    /// <summary>Подобрать на месте (с верстака) — сразу с собой.</summary>
    public bool Подобрать(string id) => _с_собой.Add(id);

    /// <summary>Что при герое, считая оружие, которое заодно инструмент.</summary>
    public IEnumerable<string> При_себе(NPC я, Инструменты каталог)
    {
        foreach (var id in _с_собой)
            yield return id;
        if (каталог.Из_оружия(я.weapon) is string из_оружия && !_с_собой.Contains(из_оружия))
            yield return из_оружия;
    }

    public override string ToString()
        => _с_собой.Count == 0 ? "ничего" : string.Join(", ", _с_собой);
}
