// Беседы героя с соседями (решение автора: «диалоги больше, чем соврать
// или сказать правду — про быт, про слухи»).
//
// Беседа — не новое действие дома. Это тот же «разговор» (`Действия`):
// доверие, сплетни, шаги знатоков — всё идёт, как шло. Тема решает только,
// что сосед скажет вслух. Кроме двух мест, где слово и есть дело:
//
//   · **слухи в метель** — не выдумка: разговор дома передаёт, что сосед
//     знает о чужих запасах (память «сказали»), и здесь это и звучит —
//     «говорят, у Толика еды полно». Что прозвучало, то герой и узнал;
//   · **предупредить о метели** (только в прологе): откуда он знает? Сосед
//     запоминает (подозрение пролога), поверивший идёт в магазин
//     и запасается — дом получит его таким в первый день.
//
// Слой без движка, как вся подача: реплики из `data/разговоры.json`,
// выбор строки — детерминированный бросок от зерна (мимо `h.rng`: слова
// не должны менять судьбу дома), род — по говорящему «{|а}» и по герою «[|а]».
//
// **Болезнь в доме** (просьба автора 01.10.2026: «диалоги, если началась
// эпидемия — соседи обеспокоены ситуацией»). Пока в доме болеют или от
// болезни уже умирали, у беседы есть тема «о болезни в доме», приветствие
// осторожное («на пороге постой» — своими словами для чужого, знакомого,
// своего и злого, вместо обычного), в быту кашляют за стенкой, в слухах —
// «Вектор». Каждый говорит только то, что знает сам (`ВестиБолезни`,
// `Эпидемия.Вести`): кого он знает больным, кто умер, болен ли он сам
// и видно ли по герою; у каждого жильца — свой взгляд на болезнь
// (медсестра — про повязку, Аркадий — про третий корпус, Толик — «из дома
// их»). Больной говорит через силу, тяжёлый — почти ничего.

using System.Text.Json;
using System.Text.RegularExpressions;
using Дом.Ядро;

namespace Дом.Экран;

/// <summary>Тема беседы: как зовётся, только ли до метели и при каком
/// условии есть вообще («болезнь» — пока в доме болеют).</summary>
public sealed record ТемаБеседы(string id, string имя, bool пролог, string? условие = null);

/// <summary>Что герой знал до разговора — чтобы после сказать, что нового
/// он услышал (слухи в метель).</summary>
public sealed record Слепок(int памяти);

public sealed class Беседы
{
    private readonly List<ТемаБеседы> _темы = new();
    private readonly Dictionary<string, List<string>> _приветствие = new(StringComparer.Ordinal);
    private readonly List<string> _быт_пролог = new();
    private readonly List<(string текст, string? условие)> _быт_метель = new();
    private readonly Dictionary<string, List<string>> _о_себе = new(StringComparer.Ordinal);
    private readonly List<string> _начала = new();
    private readonly Dictionary<string, List<string>> _о_ком = new(StringComparer.Ordinal);
    private readonly List<string> _ничего = new();
    private readonly Dictionary<string, string> _запасы = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ресурсы = new(StringComparer.Ordinal);
    private readonly List<string> _город_пролог = new();
    private readonly List<string> _город_метель = new();
    private readonly Dictionary<string, string> _город_события = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _метель = new(StringComparer.Ordinal);
    private readonly List<string> _прощание = new();
    // болезнь в доме: списки по смыслу и свой взгляд каждого жильца
    private readonly Dictionary<string, List<string>> _болезнь = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _болезнь_свои = new(StringComparer.Ordinal);

    /// <summary>Списки реплик о болезни — проверка сверяет, что все на месте.</summary>
    public static readonly IReadOnlyList<string> БОЛЕЗНЬ = new[]
    {
        "приветствие", "приветствие_больной", "тяжело", "герой_болен",
        "больные", "умерли", "о_герое", "нет_вестей", "сам_болен", "быт", "слухи",
    };

    /// <summary>Списки, разложенные по отношению к герою (чужой, знакомый,
    /// свой, злой), как обычное приветствие.</summary>
    public static bool По_отношению(string список) => список is "приветствие" or "герой_болен";

    /// <summary>Отношения, по которым раскладывается приветствие.</summary>
    public IReadOnlyCollection<string> отношения => _приветствие.Keys;

    /// <summary>У кого из жильцов есть свой взгляд на болезнь.</summary>
    public IReadOnlyCollection<string> болезнь_свои => _болезнь_свои.Keys;

    /// <summary>Реплики о болезни одного списка — для проверки; у списка
    /// по отношению — одного отношения или, без него, всех разом.</summary>
    public IReadOnlyList<string> Болезнь(string список, string? отношение = null)
        => !По_отношению(список) ? _болезнь[список]
         : отношение is not null ? _болезнь[список + "." + отношение]
         : _приветствие.Keys.SelectMany(к => _болезнь[список + "." + к]).ToList();

    public IReadOnlyList<ТемаБеседы> темы => _темы;

    /// <summary>О ком у беседы есть свои слова — проверка сверяет с жильцами.</summary>
    public IReadOnlyCollection<string> знает_о_себе => _о_себе.Keys;
    public IReadOnlyCollection<string> знает_о_ком => _о_ком.Keys;

    public static Беседы Прочитать(string каталог)
    {
        var б = new Беседы();
        using var поток = File.OpenRead(Path.Combine(каталог, "разговоры.json"));
        using var док = JsonDocument.Parse(поток);
        var к = док.RootElement;
        List<string> Список(JsonElement э) => э.EnumerateArray().Select(x => x.GetString()!).ToList();
        foreach (var т in к.GetProperty("темы").EnumerateArray())
            б._темы.Add(new ТемаБеседы(т.GetProperty("id").GetString()!, т.GetProperty("имя").GetString()!,
                                       т.TryGetProperty("пролог", out var п) && п.GetBoolean(),
                                       т.TryGetProperty("условие", out var у) ? у.GetString() : null));
        foreach (var x in к.GetProperty("приветствие").EnumerateObject())
            б._приветствие[x.Name] = Список(x.Value);
        var быт = к.GetProperty("быт");
        б._быт_пролог.AddRange(Список(быт.GetProperty("пролог")));
        foreach (var x in быт.GetProperty("метель").EnumerateArray())
            б._быт_метель.Add(x.ValueKind == JsonValueKind.String
                ? (x.GetString()!, null)
                : (x.GetProperty("текст").GetString()!, x.GetProperty("условие").GetString()));
        foreach (var x in к.GetProperty("о_себе").EnumerateObject())
            б._о_себе[x.Name] = Список(x.Value);
        var сл = к.GetProperty("слухи");
        б._начала.AddRange(Список(сл.GetProperty("начала")));
        foreach (var x in сл.GetProperty("о_ком").EnumerateObject())
            б._о_ком[x.Name] = Список(x.Value);
        б._ничего.AddRange(Список(сл.GetProperty("ничего")));
        foreach (var x in сл.GetProperty("запасы").EnumerateObject())
            б._запасы[x.Name] = x.Value.GetString()!;
        foreach (var x in сл.GetProperty("ресурсы").EnumerateObject())
            б._ресурсы[x.Name] = x.Value.GetString()!;
        var г = к.GetProperty("город");
        б._город_пролог.AddRange(Список(г.GetProperty("пролог")));
        б._город_метель.AddRange(Список(г.GetProperty("метель")));
        foreach (var имя in new[] { "горит", "сгорело", "рухнуло", "замело" })
            б._город_события[имя] = г.GetProperty(имя).GetString()!;
        foreach (var x in к.GetProperty("метель").EnumerateObject())
            б._метель[x.Name] = Список(x.Value);
        б._прощание.AddRange(Список(к.GetProperty("прощание")));
        var бол = к.GetProperty("болезнь");
        foreach (var имя in БОЛЕЗНЬ)
            if (По_отношению(имя))
                foreach (var отношение in б._приветствие.Keys)
                    б._болезнь[имя + "." + отношение] = Список(бол.GetProperty(имя).GetProperty(отношение));
            else
                б._болезнь[имя] = Список(бол.GetProperty(имя));
        foreach (var x in бол.GetProperty("свои").EnumerateObject())
            б._болезнь_свои[x.Name] = Список(x.Value);
        return б;
    }

    /// <summary>Темы, о которых сейчас можно говорить: до метели — все,
    /// в метель — без «предупредить»; с условием — только при нём.</summary>
    public IEnumerable<ТемаБеседы> Темы(bool пролог) => Темы(пролог, болезнь: false);

    /// <summary>То же, и «о болезни в доме» — пока в доме болезнь (`Эпидемия.В_доме`).</summary>
    public IEnumerable<ТемаБеседы> Темы(bool пролог, bool болезнь)
        => _темы.Where(т => (пролог || !т.пролог)
                            && (т.условие is null || т.условие == "болезнь" && болезнь && !пролог));

    // ------------------------------------------------------------ слова

    private static readonly Regex _ГЕРОЙ = new(@"\[([^\[\]|]*)\|([^\[\]|]*)\]", RegexOptions.Compiled);

    /// <summary>Род: «{|а}» — по говорящему, «[|а]» — по герою; «{я}» — имя героя.</summary>
    public static string Слова(string текст, NPC говорит, NPC я)
    {
        текст = текст.Replace("{я}", я.@short, StringComparison.Ordinal);
        текст = _ГЕРОЙ.Replace(текст, м => я.sex == "ж" ? м.Groups[2].Value : м.Groups[1].Value);
        return Util.Gform(текст, говорит.sex);
    }

    /// <summary>Строка из списка — бросок от зерна, говорящего, темы и того,
    /// в который раз о ней речь; мимо `h.rng`.</summary>
    private static T Одна<T>(IReadOnlyList<T> из, long зерно, string повод, string кто, int раз)
        => из[(int)Math.Min(из.Count - 1, Math.Floor(Округа.Бросок(зерно, повод, кто, раз) * из.Count))];

    private static string СЗаглавной(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Как сосед встречает героя: злой, чужой, знакомый, свой.
    /// В болезнь (<paramref name="в"/> — что он о ней знает): тяжёлый —
    /// почти ничего, больной — через силу; здоровый — по тому же отношению,
    /// но на пороге и с оглядкой, а больного героя видно сразу.</summary>
    public string Приветствие(NPC я, NPC с, long зерно, int раз, ВестиБолезни? в = null)
    {
        if (в?.сам == Стадия.ТЯЖЕЛО)
            return Слова(Одна(_болезнь["тяжело"], зерно, "тяжело", с.id, раз), с, я);
        if (в?.сам == Стадия.БОЛЕН)
            return Слова(Одна(_болезнь["приветствие_больной"], зерно, "больной", с.id, раз), с, я);
        double доверие = с.trust.Взять(я.id, 3.0), злоба = с.hate.Взять(я.id, 0.0);
        string кто = злоба > 30 || доверие < 1.5 ? "злой"
                   : доверие >= 4.5 ? "свой"
                   : доверие >= 3.1 ? "знакомый"
                   : "чужой";
        if (в is { герой_болен: true })
            return Слова(Одна(_болезнь["герой_болен." + кто], зерно, "герой_болен", с.id, раз), с, я);
        if (в is { в_доме: true })
            return Слова(Одна(_болезнь["приветствие." + кто], зерно, "осторожно", с.id, раз), с, я);
        return Слова(Одна(_приветствие[кто], зерно, "привет", с.id, раз), с, я);
    }

    public string Прощание(NPC я, NPC с, long зерно, int раз)
        => Слова(Одна(_прощание, зерно, "пока", с.id, раз), с, я);

    /// <summary>
    /// Что сосед отвечает на тему. <paramref name="до"/> — что герой знал до
    /// разговора: слухи в метель — то, что разговор дома ему передал.
    /// </summary>
    public string Ответ(House h, NPC я, NPC с, string тема, bool пролог, Слепок до,
                        Летопись? л, long зерно, int раз, ВестиБолезни? в = null)
    {
        // тяжёлый на любую тему — одно: потом, дышать нечем
        if (в?.сам == Стадия.ТЯЖЕЛО)
            return Слова(Одна(_болезнь["тяжело"], зерно, "тяжело", с.id, раз + 1), с, я);
        switch (тема)
        {
            case "быт":
                if (пролог)
                    return Слова(Одна(_быт_пролог, зерно, "быт", с.id, раз), с, я);
                var можно = _быт_метель.Where(р => Условие(h, р.условие)).Select(р => р.текст).ToList();
                if (в is { в_доме: true })
                    можно.AddRange(_болезнь["быт"]);
                return Слова(Одна(можно, зерно, "быт", с.id, раз + h.day * 7), с, я);
            case "болезнь":
                return О_болезни(я, с, в ?? ВестиБолезни.НЕТ, зерно, раз);
            case "о_себе":
                if (!_о_себе.TryGetValue(с.id, out var о))
                    return Слова("Да что про меня говорить.", с, я);
                bool откровенно = с.trust.Взять(я.id, 3.0) >= 3.3 && о.Count > 1;
                return Слова(о[откровенно ? 1 : 0], с, я);
            case "слухи":
                return Слухи(h, я, с, пролог, до, зерно, раз, в);
            case "город":
                return Город(h, я, с, пролог, л, зерно, раз);
            default:
                return Слова("…", с, я);
        }
    }

    private static bool Условие(House h, string? у) => у switch
    {
        null => true,
        "без_отопления" => !h.heating,
        "без_воды" => !h.water_on,
        "без_света" => !h.power_on,
        "без_связи" => h.network <= 0,
        _ => false,
    };

    /// <summary>
    /// Слухи: в метель — что разговор дома герою передал (память «сказали»
    /// о чужих запасах), словами, с его же оценкой; нет нового — то, что
    /// в доме знают друг о друге. Злой сосед не делится ничем.
    /// </summary>
    private string Слухи(House h, NPC я, NPC с, bool пролог, Слепок до, long зерно, int раз,
                         ВестиБолезни? в = null)
    {
        if (с.hate.Взять(я.id, 0.0) > 30 || с.trust.Взять(я.id, 3.0) < 1.5)
            return Слова(Одна(_ничего, зерно, "ничего", с.id, раз), с, я);
        var строки = new List<string>();
        // в болезнь первым делом говорят о ней
        if (!пролог && в is { в_доме: true })
            строки.Add(Одна(_начала, зерно, "начало", с.id + "болезнь", раз)
                       + Одна(_болезнь["слухи"], зерно, "слух_болезнь", с.id, раз));
        if (!пролог)
        {
            // что нового герой узнал в этом разговоре — о ком и о чём
            var новое = я.memory.Skip(до.памяти)
                .Where(м => м.вид == Вид.СКАЗАЛИ && м.кто is string && м.что is string)
                .GroupBy(м => м.кто!, StringComparer.Ordinal)
                .OrderBy(г => г.Key, StringComparer.Ordinal)
                .Take(2);
            foreach (var г in новое)
            {
                if (h.get(г.Key) is not NPC о || string.Equals(о.id, я.id, StringComparison.Ordinal))
                    continue;
                var части = new List<string>();
                foreach (var р in г.Select(м => м.что!).Distinct(StringComparer.Ordinal)
                                   .OrderBy(р => р, StringComparer.Ordinal))
                {
                    if (!_ресурсы.TryGetValue(р, out var что))
                        continue;
                    double сколько = я.believed(о.id, р);
                    string уровень = сколько >= 8 ? "много" : сколько >= 3 ? "есть" : "мало";
                    части.Add(_запасы[уровень].Replace("{кого}", "у " + о.form("gen"), StringComparison.Ordinal)
                                               .Replace("{что}", что, StringComparison.Ordinal));
                }
                if (части.Count == 0)
                    continue;
                // «у Толика еды ещё есть, лекарств почти нет, а дров полно»
                string у = "у " + о.form("gen") + " ";
                var хвосты = части.Select((ч, i) => i == 0 ? ч : ч.Replace(у, "", StringComparison.Ordinal)).ToList();
                string сказано = хвосты.Count == 1 ? хвосты[0]
                    : string.Join(", ", хвосты.Take(хвосты.Count - 1)) + ", а " + хвосты[^1];
                строки.Add(Одна(_начала, зерно, "начало", с.id, раз + строки.Count) + сказано + ".");
            }
        }
        if (строки.Count == 0 || строки.Count == 1 && в is { в_доме: true })
        {
            // то, что в доме знают друг о друге, — о ком-то третьем
            int сколько = с.trust.Взять(я.id, 3.0) >= 3.2 ? 2 : 1;
            var о_ком = _о_ком.Keys
                .Where(к => !string.Equals(к, с.id, StringComparison.Ordinal)
                            && !string.Equals(к, я.id, StringComparison.Ordinal)
                            && h.get(к) is { alive: true })
                .OrderBy(к => Округа.Бросок(зерно, "о_ком", с.id + к, раз))
                .Take(сколько);
            foreach (var к in о_ком)
                строки.Add(Одна(_начала, зерно, "начало", с.id + к, раз)
                           + Одна(_о_ком[к], зерно, "факт", с.id + к, раз));
        }
        if (строки.Count == 0)
            return Слова(Одна(_ничего, зерно, "ничего", с.id, раз), с, я);
        return Слова(СЗаглавной(string.Join(" ", строки)), с, я);
    }

    /// <summary>
    /// О болезни в доме — то, что говорящий знает сам. Больной — о себе.
    /// Здоровый — сперва о больном герое, если его видно (болезнь в дом
    /// чаще всего приносит он сам, из промзоны), потом один факт (кого знает
    /// больным; нет таких — кто умер; не знает ничего — что говорят) и, если
    /// есть, свой взгляд на болезнь.
    /// </summary>
    private string О_болезни(NPC я, NPC с, ВестиБолезни в, long зерно, int раз)
    {
        if (в.сам == Стадия.БОЛЕН)
            return Слова(Подставить(Одна(_болезнь["сам_болен"], зерно, "сам_болен", с.id, раз), null, в, зерно, с, раз), с, я);
        var части = new List<string>();
        if (в.герой_болен)
            части.Add(Одна(_болезнь["о_герое"], зерно, "о_герое", с.id, раз));
        if (в.больные.Count > 0)
        {
            var кто = Одна(в.больные, зерно, "больной", с.id, раз);
            части.Add(Подставить(Одна(_болезнь["больные"], зерно, "о_больном", с.id, раз), кто, в, зерно, с, раз));
        }
        if (в.умерли.Count > 0 && части.Count < 2)
            части.Add(Подставить(Одна(_болезнь["умерли"], зерно, "о_мёртвых", с.id, раз), null, в, зерно, с, раз));
        if (части.Count == 0)
            части.Add(Одна(_болезнь["нет_вестей"], зерно, "нет_вестей", с.id, раз));
        if (_болезнь_свои.TryGetValue(с.id, out var свои))
            части.Add(Одна(свои, зерно, "свой_взгляд", с.id, раз));
        return Слова(string.Join(" ", части), с, я);
    }

    /// <summary>Подставить больного ({кто}, {кого}, {кому}) и умерших
    /// ({умерших} — скольких, {умер} — кто).</summary>
    private static string Подставить(string текст, NPC? больной, ВестиБолезни в, long зерно, NPC с, int раз)
    {
        if (больной is not null)
            текст = текст.Replace("{кого}", больной.form("gen"), StringComparison.Ordinal)
                         .Replace("{кому}", больной.form("dat"), StringComparison.Ordinal)
                         .Replace("{кто}", больной.@short, StringComparison.Ordinal);
        if (в.умерли.Count > 0)
            текст = текст.Replace("{умерших}", Эпидемия.Скольких(в.умерли.Count), StringComparison.Ordinal)
                         .Replace("{умер}", Одна(в.умерли, зерно, "умерший", с.id, раз).@short, StringComparison.Ordinal);
        return текст;
    }

    /// <summary>Город: до метели — новости и очереди; в метель — что
    /// в городе случилось по летописи (горит, сгорело, замело) и слова.</summary>
    private string Город(House h, NPC я, NPC с, bool пролог, Летопись? л, long зерно, int раз)
    {
        if (пролог || л is null)
            return Слова(Одна(_город_пролог, зерно, "город", с.id, раз), с, я);
        var факты = new List<string>();
        var мир = л.На(h.day, зерно);
        foreach (var (место, здание) in мир.здания.OrderBy(п => п.Key, StringComparer.Ordinal))
        {
            string ключ = здание switch
            {
                Здание.ГОРИТ => "горит",
                Здание.СГОРЕЛО => "сгорело",
                Здание.РАЗРУШЕНО => "рухнуло",
                _ => "",
            };
            if (ключ.Length > 0 && л.места.TryGetValue(место, out var имя))
                факты.Add(_город_события[ключ].Replace("{место}", имя, StringComparison.Ordinal));
        }
        if (л.Улица(Округа.ДОРОГА, h.day, зерно) == СостояниеУлицы.ЗАВАЛЕНО)
            факты.Add(_город_события["замело"].Replace("{место}", "магазину", StringComparison.Ordinal));
        if (факты.Count > 0)
            return Слова(СЗаглавной(Одна(_начала, зерно, "начало", с.id, раз)
                                    + Одна(факты, зерно, "город", с.id, раз) + "."), с, я);
        return Слова(Одна(_город_метель, зерно, "город", с.id, раз + h.day * 7), с, я);
    }

    /// <summary>
    /// Предупредить о метели (только в прологе). Откуда он знает? Сосед
    /// запоминает — подозрение пролога, поверил он или нет. Поверил
    /// (знал заранее, хозяйственный, запасливый или доверяет) — сегодня же
    /// идёт в магазин: еды и воды у него прибавится, полка опустеет.
    /// </summary>
    public string Предупредить(House h, NPC я, NPC с, Пролог п, Магазин? м, long зерно, int раз)
    {
        if (п.Предупреждён(с.id))
            return Слова(Одна(_метель["уже"], зерно, "уже", с.id, раз), с, я);
        bool знал = с.пунктики.Contains("знал_заранее");
        bool верит = знал
                     || с.пунктики.Contains("хозяйственный") || с.пунктики.Contains("запасливый")
                     || с.trust.Взять(я.id, 3.0) >= 3.4;
        п.Предупредил(с.id, верит ? 0.5 : 0.3);
        if (знал)
            return Слова(Одна(_метель["знал_заранее"], зерно, "знал", с.id, раз), с, я);
        if (!верит)
            return Слова(Одна(_метель["не_верит"], зерно, "не_верит", с.id, раз), с, я);
        с.stock["еда"] = с.stock.Взять("еда", 0.0) + 3;
        с.stock["вода"] = с.stock.Взять("вода", 0.0) + 3;
        м?.Истощить(h, 6);
        return Слова(Одна(_метель["верит"], зерно, "верит", с.id, раз), с, я);
    }
}
