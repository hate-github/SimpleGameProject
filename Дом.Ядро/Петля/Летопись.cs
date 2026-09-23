// Летопись мира (задание автора, п. 9): события, привязанные к дню.
//
// Мир за стенами дома меняется по календарю: на седьмой день горит
// котельная, на девятый — дом напротив, соседний подъезд выгорает, и огонь
// дальше не идёт. Это не решения жильцов и не случай в доме, а погода
// другого рода, и потому живёт она не в доме, а здесь.
//
// Задание просит универсальную запись, а не «пожар в котельной» кодом:
// WorldEvent — trigger_day, location, event_type, consequences. Так она
// и устроена (`СобытиеМира`), а что где горит — `data/мир.json`.
//
// **Состояние мира — чистая функция дня и зерна** (`На`). Не наблюдатель,
// который что-то запоминает по ходу, а свёртка: всё, что случилось к этому
// утру. Поэтому его нечего сохранять и нечего чинить после перемотки
// (задание, п. 14: смерть отматывает назад) — мир на третий день тот же,
// сколько бы раз герой в него ни возвращался. Шанс события бросается
// своим генератором от зерна и имени события, мимо `h.rng`: дом его
// не чувствует, а одна и та же метель горит одинаково в каждой жизни.
//
// **Дом эти события пока не трогают** — только вид мира. Сгоревший подъезд
// в симуляции не значит ничего: соседи ходят туда вылазкой («соседние
// подъезды») как ходили. Событие, которое меняет то, что знает дом, —
// сдвиг эталона, и заводится оно вместе с этим решением, а не попутно.
// Поэтому загрузчик принимает только те последствия, у которых есть кому
// их применить; остальные — ошибка данных, а не молчаливая строка.

using System.Text;
using System.Text.Json;

namespace Дом.Ядро;

/// <summary>Что это было — для звука и картинки: пожар, обрушение, снег.</summary>
public enum ВидСобытияМира { ПОЖАР, РАЗРУШЕНИЕ, СНЕГОПАД }

/// <summary>Каково здание: цело, горит, сгорело, разрушено.</summary>
public enum Здание { ЦЕЛО, ГОРИТ, СГОРЕЛО, РАЗРУШЕНО }

/// <summary>
/// Событие мира: с какого утра (<paramref name="день"/>), где, что это было
/// и что стало с местом. <paramref name="шанс"/> — случится ли вообще,
/// <paramref name="после"/> — только если случилось то событие.
/// </summary>
public sealed record СобытиеМира(string id, int день, string место, ВидСобытияМира вид,
                                 Здание? здание, double шанс = 1.0, string? после = null);

/// <summary>Мир на это утро: каким стало каждое место и что случилось к нему.</summary>
public sealed record СостояниеМира(int день, IReadOnlyDictionary<string, Здание> здания,
                                   IReadOnlyList<СобытиеМира> случились)
{
    /// <summary>Каково здание в этом месте. Не тронутое событиями — цело.</summary>
    public Здание Здание(string место) => здания.TryGetValue(место, out var з) ? з : Ядро.Здание.ЦЕЛО;

    /// <summary>Что случилось ровно в этот день: для вести и звука.</summary>
    public IEnumerable<СобытиеМира> Сегодня => случились.Where(с => с.день == день);
}

/// <summary>Летопись: места и события из `data/мир.json` и мир на любой день.</summary>
public sealed class Летопись
{
    private readonly Dictionary<string, string> _места = new(StringComparer.Ordinal);
    private readonly List<СобытиеМира> _события = new();

    /// <summary>Места: id → как зовётся.</summary>
    public IReadOnlyDictionary<string, string> места => _места;

    /// <summary>События в порядке файла.</summary>
    public IReadOnlyList<СобытиеМира> события => _события;

    /// <summary>Пустая летопись: в мире ничего не случается.</summary>
    public static readonly Летопись ПУСТАЯ = new();

    /// <summary>
    /// Случится ли событие в мире с этим зерном. Шанс бросается своим
    /// генератором, заведённым от зерна и имени события, — не `h.rng`:
    /// дом броска не чувствует, а ответ в одном мире всегда один. Цепочка
    /// «после» проверяется здесь же: сгореть может только то, что горело.
    /// </summary>
    public bool Случится(СобытиеМира с, long зерно)
    {
        if (с.после is string раньше)
        {
            var то = _события.FirstOrDefault(x => string.Equals(x.id, раньше, StringComparison.Ordinal));
            if (то is null || то.день > с.день || !Случится(то, зерно))
                return false;
        }
        if (с.шанс >= 1.0)
            return true;
        if (с.шанс <= 0.0)
            return false;
        return new Rng(Зерно(зерно, с.id)).Chance(с.шанс);
    }

    /// <summary>
    /// Мир на утро этого дня: все события, чей день наступил и которые
    /// случились, свёрнуты по порядку дня, а в один день — по порядку файла.
    /// </summary>
    public СостояниеМира На(int день, long зерно)
    {
        var здания = new Dictionary<string, Здание>(StringComparer.Ordinal);
        var случились = new List<СобытиеМира>();
        foreach (var с in _события.Select((с, i) => (с, i))
                                  .OrderBy(x => x.с.день).ThenBy(x => x.i).Select(x => x.с))
        {
            if (с.день > день || !Случится(с, зерно))
                continue;
            случились.Add(с);
            if (с.здание is Здание з)
                здания[с.место] = з;
        }
        return new СостояниеМира(день, здания, случились);
    }

    /// <summary>Зерно генератора события: FNV-1a по зерну мира и имени
    /// события. Не `string.GetHashCode` — он в .NET свой в каждом процессе,
    /// и мир горел бы по-разному от запуска к запуску.</summary>
    private static long Зерно(long зерно, string id)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in BitConverter.GetBytes(зерно).Concat(Encoding.UTF8.GetBytes(id)))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return (long)(h & 0x7fffffffffffffffUL);
    }

    public static Летопись Прочитать(string каталог)
    {
        var л = new Летопись();
        using var поток = File.OpenRead(Path.Combine(каталог, "мир.json"));
        using var док = JsonDocument.Parse(поток);
        var корень = док.RootElement;
        foreach (var м in корень.GetProperty("места").EnumerateObject())
            л._места[м.Name] = м.Value.GetString()!;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var э in корень.GetProperty("события").EnumerateArray())
        {
            string id = э.GetProperty("id").GetString()!;
            if (!ids.Add(id))
                throw new InvalidDataException($"мир.json: событие «{id}» дважды");
            int день = э.GetProperty("день").GetInt32();
            string место = э.GetProperty("место").GetString()!;
            if (!л._места.ContainsKey(место))
                throw new InvalidDataException($"мир.json: «{id}» — место «{место}», которого нет");
            var вид = э.GetProperty("вид").GetString() switch
            {
                "пожар" => ВидСобытияМира.ПОЖАР,
                "разрушение" => ВидСобытияМира.РАЗРУШЕНИЕ,
                "снегопад" => ВидСобытияМира.СНЕГОПАД,
                var х => throw new InvalidDataException($"мир.json: «{id}» — вида «{х}» нет"),
            };
            Здание? здание = null;
            foreach (var п in э.GetProperty("последствия").EnumerateObject())
            {
                if (п.Name != "здание")
                    throw new InvalidDataException(
                        $"мир.json: «{id}» — последствие «{п.Name}» некому применить: "
                        + "заводится вместе с тем, кто его применяет");
                здание = п.Value.GetString() switch
                {
                    "цело" => Здание.ЦЕЛО,
                    "горит" => Здание.ГОРИТ,
                    "сгорело" => Здание.СГОРЕЛО,
                    "разрушено" => Здание.РАЗРУШЕНО,
                    var х => throw new InvalidDataException($"мир.json: «{id}» — здание «{х}» не бывает"),
                };
            }
            double шанс = э.TryGetProperty("шанс", out var ш) ? ш.GetDouble() : 1.0;
            if (шанс < 0.0 || шанс > 1.0)
                throw new InvalidDataException($"мир.json: «{id}» — шанс {шанс} не от нуля до единицы");
            string? после = э.TryGetProperty("после", out var по) ? по.GetString() : null;
            л._события.Add(new СобытиеМира(id, день, место, вид, здание, шанс, после));
        }
        foreach (var с in л._события)
            if (с.после is string раньше
                && л._события.FirstOrDefault(x => string.Equals(x.id, раньше, StringComparison.Ordinal))
                   is not { } то)
                throw new InvalidDataException($"мир.json: «{с.id}» — после «{раньше}», которого нет");
            else if (с.после is not null
                     && л._события.First(x => string.Equals(x.id, с.после, StringComparison.Ordinal)).день > с.день)
                throw new InvalidDataException($"мир.json: «{с.id}» раньше того, после чего идёт");
        return л;
    }
}
