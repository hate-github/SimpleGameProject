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
//
// **Руки** (задание автора, п. 22). Из того, что при герое, одно может
// быть в руках (`в_руках`): достал — держит, убрал — руки пусты, вещь
// при нём. Тяжёлое (монтировку, топор-инструмент) не спрятать: взятое,
// оно в руках, пока его не положат дома у полки, и второго тяжёлого
// не взять. Оружие жильца прячется всегда — даже топор у пояса. Что
// в руках, тем и ломают, если оно берёт этот замок (`Взлом.чем`).
// Каталог знает и то, что бывает в руках, кроме инструментов, — оружие
// (`предметы`), и имя ассета для моделей и анимаций автора.

using System.Text.Json;

namespace Дом.Ядро;

/// <summary>Как инструмент берёт замок: доля часов, множитель шанса, уровень шума.</summary>
public sealed record Приём(double часы, double шанс, int шум);

/// <summary>Инструмент: как зовётся, чем («молотком»), что им делают
/// с замком, тяжёлый ли (не спрятать), какие замки берёт и как зовётся
/// у моделей и анимаций автора (латиницей).</summary>
public sealed record Инструмент(string id, string имя, string чем, string как, bool тяжёлый,
                                IReadOnlyDictionary<string, Приём> замки, string ассет = "")
{
    public Приём? Берёт(string замок) => замки.TryGetValue(замок, out var п) ? п : null;
}

/// <summary>Что бывает при герое: инструмент, оружие, снегоступы. Имя, ассет
/// (латиницей — для модели и клипа), тяжёлое ли — не спрятать, и носят ли
/// на себе (снегоступы — на ногах: в руки их не берут).</summary>
public sealed record ВРуках(string id, string имя, string ассет, bool тяжёлый, bool носят = false);

/// <summary>Что видно в руках у соседа: оружие наголо — за какими делами,
/// с какой нормальности его носят открыто всегда и что берут в руки
/// для какой работы (null — пустые руки).</summary>
public sealed record РукиСоседей(IReadOnlySet<string> оружие_наголо, double открыто_ниже,
                                 IReadOnlyDictionary<string, string?> дело);

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
    private readonly Dictionary<string, ВРуках> _в_руках = new(StringComparer.Ordinal);
    private РукиСоседей _соседи = new(new HashSet<string>(StringComparer.Ordinal), 0.0,
                                      new Dictionary<string, string?>(StringComparer.Ordinal));

    /// <summary>Замки: id → как зовётся.</summary>
    public IReadOnlyDictionary<string, string> замки => _замки;

    /// <summary>Инструменты по id, в порядке файла.</summary>
    public IReadOnlyDictionary<string, Инструмент> все => _все;

    /// <summary>Что лежит на верстаке в гараже соседа.</summary>
    public IReadOnlyList<string> верстак => _верстак;

    /// <summary>Всё, что бывает в руках: инструменты и оружие, по id.</summary>
    public IReadOnlyDictionary<string, ВРуках> в_руках => _в_руках;

    /// <summary>Что видно в руках у соседей.</summary>
    public РукиСоседей соседи => _соседи;

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
    public string? Из_оружия(Оружие о)
        => _оружие.TryGetValue(о, out var id) && _все.ContainsKey(id) ? id : null;

    /// <summary>Какое это оружие жильца — по id предмета; не оружие — null.</summary>
    public Оружие? Оружие_из(string id)
    {
        foreach (var (о, и) in _оружие)
            if (string.Equals(и, id, StringComparison.Ordinal))
                return о;
        return null;
    }

    /// <summary>Что в руках у того, кто достал это оружие, — или ничего (НЕТ).</summary>
    public string? Оружие_в_руки(Оружие о) => _оружие.TryGetValue(о, out var id) ? id : null;

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
            var инструмент = new Инструмент(и.Name, в.GetProperty("имя").GetString()!,
                                            в.GetProperty("чем").GetString()!,
                                            в.GetProperty("как").GetString()!,
                                            в.GetProperty("тяжёлый").GetBoolean(), приёмы,
                                            Ассет(в, и.Name));
            к._все[и.Name] = инструмент;
            к._в_руках[и.Name] = new ВРуках(и.Name, инструмент.имя, инструмент.ассет, инструмент.тяжёлый);
        }
        foreach (var п in корень.GetProperty("предметы").EnumerateObject())
        {
            if (к._в_руках.ContainsKey(п.Name))
                throw new InvalidDataException($"инструменты.json: «{п.Name}» — и инструмент, и предмет");
            к._в_руках[п.Name] = new ВРуках(п.Name, п.Value.GetProperty("имя").GetString()!,
                                            Ассет(п.Value, п.Name), false,
                                            п.Value.TryGetProperty("носят", out var н) && н.GetBoolean());
        }
        var ассеты = new HashSet<string>(StringComparer.Ordinal);
        foreach (var в in к._в_руках.Values)
            if (!ассеты.Add(в.ассет))
                throw new InvalidDataException($"инструменты.json: ассет «{в.ассет}» у двух предметов");

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
            if (!Enum.TryParse<Оружие>(п.Name, out var о) || о == Оружие.НЕТ)
                throw new InvalidDataException($"инструменты.json: оружия «{п.Name}» нет");
            string id = п.Value.GetString()!;
            if (!к._в_руках.ContainsKey(id))
                throw new InvalidDataException($"инструменты.json: оружие {п.Name} — предмет «{id}», которого нет");
            к._оружие[о] = id;
        }
        foreach (var о in Enum.GetValues<Оружие>())
            if (о != Оружие.НЕТ && !к._оружие.ContainsKey(о))
                throw new InvalidDataException($"инструменты.json: у оружия {о} нет предмета в руках");
        var рс = корень.GetProperty("руки_соседей");
        var наголо = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in рс.GetProperty("оружие_наголо").EnumerateArray())
            наголо.Add(x.GetString()!);
        var дело = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var п in рс.GetProperty("дело").EnumerateObject())
        {
            string? id = п.Value.ValueKind == JsonValueKind.Null ? null : п.Value.GetString();
            if (id is not null && !к._в_руках.ContainsKey(id))
                throw new InvalidDataException($"инструменты.json: для «{п.Name}» — предмет «{id}», которого нет");
            дело[п.Name] = id;
        }
        к._соседи = new РукиСоседей(наголо, рс.GetProperty("открыто_ниже").GetDouble(), дело);
        foreach (var п in корень.GetProperty("дома").EnumerateObject())
            к._дома[п.Name] = п.Value.EnumerateArray()
                .Select(x => Известный(к, x.GetString()!, "дома")).ToList();
        foreach (var x in корень.GetProperty("верстак").EnumerateArray())
            к._верстак.Add(Известный(к, x.GetString()!, "верстак"));
        return к;
    }

    /// <summary>Имя ассета: латиница и цифры, без пробелов — это имя файла
    /// модели и клипа анимации.</summary>
    private static string Ассет(JsonElement в, string id)
    {
        string а = в.TryGetProperty("ассет", out var x) ? x.GetString() ?? "" : "";
        if (а.Length == 0 || !а.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
            throw new InvalidDataException($"инструменты.json: у «{id}» ассет «{а}» — нужна латиница без пробелов");
        return а;
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

    /// <summary>Что в руках — id предмета (инструмент или оружие), или
    /// null: руки пусты. Остальное, что при герое, — спрятано.</summary>
    public string? в_руках { get; private set; }

    /// <summary>Тяжёлое ли это — не спрятать: взятый тяжёлый инструмент.
    /// Оружие жильца (даже топор у пояса) прячется всегда.</summary>
    public bool Тяжёлое(string id, Инструменты каталог)
        => _с_собой.Contains(id) && каталог.все.TryGetValue(id, out var и) && и.тяжёлый;

    /// <summary>Почему этого не взять — или null, если можно: руки заняты
    /// другим тяжёлым, а это тоже тяжёлое.</summary>
    public string? Нельзя_взять(string id, Инструменты каталог)
    {
        bool тяжёлое = каталог.все.TryGetValue(id, out var и) && и.тяжёлый;
        if (тяжёлое && в_руках is string держит && Тяжёлое(держит, каталог) && держит != id)
            return $"руки заняты: {Имя(держит, каталог)} — сначала положить дома у полки";
        return null;
    }

    /// <summary>Взять из дома с собой. false — дома такого нет или руки
    /// заняты тяжёлым. Тяжёлое берут в руки.</summary>
    public bool Взять(string id, Инструменты? каталог = null)
    {
        if (каталог is not null && Нельзя_взять(id, каталог) is not null)
            return false;
        if (!(_дома.Remove(id) && _с_собой.Add(id)))
            return false;
        Взяли(id, каталог);
        return true;
    }

    /// <summary>При себе ли это (в руках, спрятано или надето).</summary>
    public bool Есть(string id) => _с_собой.Contains(id);

    /// <summary>Лишиться насовсем — отняли: ни с собой, ни дома.</summary>
    public bool Положить_насовсем(string id)
    {
        if (!_с_собой.Remove(id))
            return false;
        if (в_руках == id)
            в_руках = null;
        return true;
    }

    /// <summary>Положить дома. false — с собой такого нет. Было в руках —
    /// руки пусты.</summary>
    public bool Положить(string id)
    {
        if (!(_с_собой.Remove(id) && _дома.Add(id)))
            return false;
        if (в_руках == id)
            в_руках = null;
        return true;
    }

    /// <summary>Подобрать на месте (с верстака) — сразу с собой; тяжёлое —
    /// в руки. false — уже при себе или руки заняты тяжёлым.</summary>
    public bool Подобрать(string id, Инструменты? каталог = null)
    {
        if (каталог is not null && Нельзя_взять(id, каталог) is not null)
            return false;
        if (!_с_собой.Add(id))
            return false;
        Взяли(id, каталог);
        return true;
    }

    private void Взяли(string id, Инструменты? каталог)
    {
        if (каталог is not null && Тяжёлое(id, каталог))
            в_руках = id;
    }

    /// <summary>Что можно держать: всё, что при герое, и его оружие —
    /// в порядке: инструменты по имени, потом оружие.</summary>
    public IReadOnlyList<string> Можно_держать(NPC я, Инструменты каталог)
    {
        // надетое (снегоступы) в руки не берут
        var @out = _с_собой.Where(id => !(каталог.в_руках.TryGetValue(id, out var в) && в.носят)).ToList();
        if (каталог.Оружие_в_руки(я.weapon) is string оружие && !@out.Contains(оружие))
            @out.Add(оружие);
        return @out;
    }

    /// <summary>Достать в руки. null — достал; иначе — почему нет: этого
    /// при себе нет или руки заняты тяжёлым.</summary>
    public string? Достать(string id, NPC я, Инструменты каталог)
    {
        if (!Можно_держать(я, каталог).Contains(id))
            return $"{Имя(id, каталог)} — не при себе";
        if (в_руках is string держит && держит != id && Тяжёлое(держит, каталог))
            return $"{Имя(держит, каталог)} не спрятать — только положить дома у полки";
        в_руках = id;
        return null;
    }

    /// <summary>Убрать из рук: вещь при герое, руки пусты. null — убрал;
    /// иначе — почему нет (тяжёлое не спрятать).</summary>
    public string? Убрать(Инструменты каталог)
    {
        if (в_руках is not string держит)
            return null;
        if (Тяжёлое(держит, каталог))
            return $"{Имя(держит, каталог)} не спрятать — только положить дома у полки";
        в_руках = null;
        return null;
    }

    /// <summary>Следующее по кругу: пусто → первое → второе → … → пусто.
    /// Тяжёлое в руках — ничего не меняется, и сказано почему.</summary>
    public string? Следующее(NPC я, Инструменты каталог)
    {
        var можно = Можно_держать(я, каталог);
        if (в_руках is string держит && Тяжёлое(держит, каталог))
            return $"{Имя(держит, каталог)} не спрятать — только положить дома у полки";
        if (можно.Count == 0)
            return "в руки взять нечего";
        int i = в_руках is null ? -1 : можно.ToList().IndexOf(в_руках);
        в_руках = i + 1 < можно.Count ? можно[i + 1] : null;
        return null;
    }

    /// <summary>Проверить руки после того, как мир поменялся: то, чего
    /// больше нет при себе (оружие отобрали), из рук выпадает.</summary>
    public void Сверить(NPC я, Инструменты каталог)
    {
        if (в_руках is string держит && !Можно_держать(я, каталог).Contains(держит))
            в_руках = null;
    }

    private static string Имя(string id, Инструменты каталог)
        => каталог.в_руках.TryGetValue(id, out var в) ? в.имя : id;

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
