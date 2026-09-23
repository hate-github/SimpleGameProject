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
// **Улицы заметает** (задание автора, п. 11): у каждой дороги за двор —
// уровень снега (`СостояниеУлицы`): по дню, со сдвигом дороги (дальнее
// заваливает раньше, двор — никогда) и не ниже того, что сказали события
// («большой снегопад» на четвёртый день). Что уровень значит — пройти ли
// без снегоступов и во сколько раз дольше дорога (`ПроходУлицы`). Это
// тоже свёртка дня и зерна, и считается она для героя: соседи ходят по
// своему снегу (`снег_дорога` дома), как ходили.
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

/// <summary>Видно ли на месте следы и ходы — вход, шахты: видны или замело.</summary>
public enum Следы { ВИДНЫ, ЗАМЕЛО }

/// <summary>Ступень рассказа знатока: с какого доверия к герою и дня, что
/// герой узнаёт, что тот говорит, и про снегоступы ли это.</summary>
public sealed record Ступень(double доверие, int с_дня, string? узнаёшь, string говорит, bool снегоступы);

/// <summary>Знаток: кто и что по ступеням рассказывает.</summary>
public sealed record Знаток(string кто, IReadOnlyList<Ступень> ступени);

/// <summary>Сколько снега на дороге: чисто, снег, глубокий снег, завалено
/// (задание: Clear, LightSnow, DeepSnow, Blocked).</summary>
public enum СостояниеУлицы { ЧИСТО, СНЕГ, ГЛУБОКИЙ_СНЕГ, ЗАВАЛЕНО }

/// <summary>Что уровень снега значит для идущего: пройти ли без снегоступов,
/// во сколько раз дольше дорога и во сколько — в снегоступах.</summary>
public sealed record ПроходУлицы(bool без_снегоступов, double часы, double на_снегоступах);

/// <summary>
/// Событие мира: с какого утра (<paramref name="день"/>), где, что это было
/// и что стало с местом. <paramref name="шанс"/> — случится ли вообще,
/// <paramref name="после"/> — только если случилось то событие.
/// </summary>
public sealed record СобытиеМира(string id, int день, string место, ВидСобытияМира вид,
                                 Здание? здание, double шанс = 1.0, string? после = null,
                                 СостояниеУлицы? улица = null, Следы? следы = null);

/// <summary>Поход героя за ворота: куда, чья дорога, часов в один конец
/// по чистой улице, что там и что надо знать, чтобы туда пойти.</summary>
public sealed record Поход(string id, string имя, string улица, double часы, string вид, string? знать);

/// <summary>Мир на это утро: каким стало каждое место и что случилось к нему.</summary>
public sealed record СостояниеМира(int день, IReadOnlyDictionary<string, Здание> здания,
                                   IReadOnlyList<СобытиеМира> случились)
{
    /// <summary>Каково здание в этом месте. Не тронутое событиями — цело.</summary>
    public Здание Здание(string место) => здания.TryGetValue(место, out var з) ? з : Ядро.Здание.ЦЕЛО;

    /// <summary>Видны ли на месте следы: последнее случившееся событие
    /// со следами решает; не было таких — видны.</summary>
    public Следы Следы(string место)
        => случились.LastOrDefault(э => э.следы is not null
                                        && string.Equals(э.место, место, StringComparison.Ordinal))?.следы
           ?? Ядро.Следы.ВИДНЫ;

    /// <summary>Что случилось ровно в этот день: для вести и звука.</summary>
    public IEnumerable<СобытиеМира> Сегодня => случились.Where(с => с.день == день);
}

/// <summary>Летопись: места и события из `data/мир.json` и мир на любой день.</summary>
public sealed class Летопись
{
    private readonly Dictionary<string, string> _места = new(StringComparer.Ordinal);
    private readonly List<СобытиеМира> _события = new();
    private readonly List<(int с_дня, СостояниеУлицы улица)> _по_дням = new();
    private readonly Dictionary<string, int> _сдвиг = new(StringComparer.Ordinal);
    private readonly Dictionary<СостояниеУлицы, ПроходУлицы> _проход = new();

    /// <summary>Место всех дорог за двором разом — для событий вроде снегопада.</summary>
    public const string ГОРОД = "город";

    /// <summary>Сдвиг дороги, при котором её не заметает вовсе: двор,
    /// соседние подъезды.</summary>
    public const int НЕ_ЗАМЕТАЕТ = -3;

    /// <summary>Места: id → как зовётся.</summary>
    public IReadOnlyDictionary<string, string> места => _места;

    private readonly List<Поход> _походы = new();

    /// <summary>Куда герой ходит сам, за ворота двора, — в порядке файла.</summary>
    public IReadOnlyList<Поход> походы => _походы;

    private readonly List<Знаток> _знатоки = new();

    /// <summary>Кто из жильцов что знает о мире и рассказывает по ступеням.</summary>
    public IReadOnlyList<Знаток> знатоки => _знатоки;

    /// <summary>Бункер: сколько часов искать и шанс найти, пока видно.</summary>
    public (double часов, double найти) бункер { get; private set; }

    /// <summary>Заражение с «Вектора-3»: стадии, урон, передача, промзона.</summary>
    public ДанныеЗаражения заражение { get; private set; } = ДанныеЗаражения.НИКАКОГО;

    /// <summary>Снегоступы в мире: кто их делает и как к ним тянутся.</summary>
    public ДанныеСнегоступов снегоступы { get; private set; } = ДанныеСнегоступов.НИКАКИХ;

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

    /// <summary>
    /// Сколько снега на дороге к этому месту в этот день: уровень дня
    /// со сдвигом дороги, не ниже того, что сказали случившиеся к утру
    /// события (своё место или весь «город»). Дорога, которой нет в данных, —
    /// со сдвигом ноль; двор и соседние подъезды (`НЕ_ЗАМЕТАЕТ`) — всегда
    /// чисто: это не улица.
    /// </summary>
    public СостояниеУлицы Улица(string место, int день, long зерно)
    {
        int сдвиг = _сдвиг.TryGetValue(место, out var с) ? с : 0;
        if (сдвиг <= НЕ_ЗАМЕТАЕТ || _по_дням.Count == 0)
            return СостояниеУлицы.ЧИСТО;
        int уровень = 0;
        foreach (var (с_дня, улица) in _по_дням)
            if (с_дня <= день)
                уровень = (int)улица;
        уровень += сдвиг;
        foreach (var э in _события)
            if (э.улица is СостояниеУлицы не_ниже && э.день <= день
                && (string.Equals(э.место, место, StringComparison.Ordinal)
                    || string.Equals(э.место, ГОРОД, StringComparison.Ordinal))
                && Случится(э, зерно))
                уровень = Math.Max(уровень, (int)не_ниже);
        return (СостояниеУлицы)Math.Clamp(уровень, 0, (int)СостояниеУлицы.ЗАВАЛЕНО);
    }

    /// <summary>Что этот уровень снега значит для идущего.</summary>
    public ПроходУлицы Проход(СостояниеУлицы у)
        => _проход.TryGetValue(у, out var п) ? п : new ПроходУлицы(true, 1.0, 1.0);

    /// <summary>Часы дороги по такому снегу: множитель — в снегоступах или без.</summary>
    public double Часы_дороги(СостояниеУлицы у, bool снегоступы)
    {
        var п = Проход(у);
        return снегоступы && у == СостояниеУлицы.ЗАВАЛЕНО ? п.на_снегоступах : п.часы;
    }

    /// <summary>Пройти ли сегодня к этому месту — без снегоступов или в них.</summary>
    public bool Пройти(string место, int день, long зерно, bool снегоступы)
        => снегоступы || Проход(Улица(место, день, зерно)).без_снегоступов;

    private static СостояниеУлицы Уровень(string имя, string где) => имя switch
    {
        "чисто" => СостояниеУлицы.ЧИСТО,
        "снег" => СостояниеУлицы.СНЕГ,
        "глубокий_снег" => СостояниеУлицы.ГЛУБОКИЙ_СНЕГ,
        "завалено" => СостояниеУлицы.ЗАВАЛЕНО,
        _ => throw new InvalidDataException($"мир.json: {где} — улицы «{имя}» не бывает"),
    };

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
            СостояниеУлицы? улица = null;
            Следы? следы = null;
            foreach (var п in э.GetProperty("последствия").EnumerateObject())
            {
                if (п.Name == "улица")
                {
                    улица = Уровень(п.Value.GetString()!, $"«{id}»");
                    continue;
                }
                if (п.Name == "следы")
                {
                    следы = п.Value.GetString() switch
                    {
                        "видны" => Ядро.Следы.ВИДНЫ,
                        "замело" => Ядро.Следы.ЗАМЕЛО,
                        var х => throw new InvalidDataException($"мир.json: «{id}» — следов «{х}» не бывает"),
                    };
                    continue;
                }
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
            л._события.Add(new СобытиеМира(id, день, место, вид, здание, шанс, после, улица, следы));
        }
        if (корень.TryGetProperty("улицы", out var у))
        {
            foreach (var x in у.GetProperty("по_дням").EnumerateArray())
                л._по_дням.Add((x.GetProperty("с_дня").GetInt32(),
                               Уровень(x.GetProperty("улица").GetString()!, "по_дням")));
            л._по_дням.Sort((а, б) => а.с_дня.CompareTo(б.с_дня));
            foreach (var x in у.GetProperty("сдвиг").EnumerateObject())
                л._сдвиг[x.Name] = x.Value.GetInt32();
            foreach (var x in у.GetProperty("состояния").EnumerateObject())
            {
                double часы = x.Value.GetProperty("часы").GetDouble();
                л._проход[Уровень(x.Name, "состояния")] = new ПроходУлицы(
                    x.Value.GetProperty("без_снегоступов").GetBoolean(), часы,
                    x.Value.TryGetProperty("на_снегоступах", out var н) ? н.GetDouble() : часы);
            }
            foreach (var уровень in Enum.GetValues<СостояниеУлицы>())
                if (!л._проход.ContainsKey(уровень))
                    throw new InvalidDataException($"мир.json: у улицы «{уровень}» не сказано, проходима ли она");
        }
        if (корень.TryGetProperty("походы", out var пх))
            foreach (var x in пх.EnumerateArray())
            {
                var п = new Поход(x.GetProperty("id").GetString()!, x.GetProperty("имя").GetString()!,
                                  x.GetProperty("улица").GetString()!, x.GetProperty("часы").GetDouble(),
                                  x.GetProperty("вид").GetString()!,
                                  x.TryGetProperty("знать", out var з) ? з.GetString() : null);
                if (п.часы <= 0 || п.вид is not ("магазин" or "промзона" or "бункер"))
                    throw new InvalidDataException($"мир.json: поход «{п.id}» — часы больше нуля, вид — магазин, промзона или бункер");
                л._походы.Add(п);
            }
        if (корень.TryGetProperty("знатоки", out var зн))
            foreach (var x in зн.EnumerateArray())
                л._знатоки.Add(new Знаток(x.GetProperty("кто").GetString()!,
                    x.GetProperty("ступени").EnumerateArray().Select(с => new Ступень(
                        с.GetProperty("доверие").GetDouble(),
                        с.TryGetProperty("с_дня", out var д) ? д.GetInt32() : 1,
                        с.TryGetProperty("узнаёшь", out var у2) ? у2.GetString() : null,
                        с.GetProperty("говорит").GetString()!,
                        с.TryGetProperty("снегоступы", out var с2) && с2.GetBoolean())).ToList()));
        if (корень.TryGetProperty("бункер", out var бн))
            л.бункер = (бн.GetProperty("часов").GetDouble(), бн.GetProperty("найти").GetDouble());
        if (корень.TryGetProperty("заражение", out var зр))
            л.заражение = ДанныеЗаражения.Прочитать(зр);
        if (корень.TryGetProperty("снегоступы", out var сн))
            л.снегоступы = new ДанныеСнегоступов(
                сн.GetProperty("мастера").EnumerateArray()
                 .Select(x => new МастерСнегоступов(x.GetProperty("кто").GetString()!,
                                                    x.GetProperty("с_дня").GetInt32())).ToList(),
                сн.GetProperty("тяга").GetDouble(),
                сн.GetProperty("тяга_по_завалу").GetDouble());
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
