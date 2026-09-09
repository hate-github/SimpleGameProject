// Перенос house/report.py, строки 13–162: журнал дня и панель скрытых шкал.
//
// Это последний этап переноса по `docs/ПОРТ.md`: текст переезжает, когда
// состояние сошлось по всем зёрнам. Оно сошлось — 719 отпечатков на двадцати
// четырёх зёрнах, — и теперь текст можно сверить не «по смыслу», а знак
// в знак: оракул печатает питоновский журнал, порт печатает свой, и они
// сравниваются побайтно.
//
// Вся арифметика вывода идёт через `Текст`: `.0f`, `.1f`, `:g` и выравнивание
// в Python и в .NET разные, и это ловушка 4 из спеки. Ни одного
// `ToString()` без правил здесь нет.

using System.Text;

namespace Дом.Ядро;

/// <summary>
/// Журнал: копит строки дня и печатает их разом. Чат жильцов живёт отдельно
/// (`Чат`): он не показывает дом, а меняет его.
/// </summary>
public sealed class Журнал : IЖурнал
{
    /// <summary>0 — только крупное, 1 — обычно, 2 — каждое действие.</summary>
    public int verbosity { get; }

    /// <summary>Печатать ли то, чего дом не видит: кто украл, кто подбросил.</summary>
    public bool secrets { get; }

    private readonly TextWriter поток;
    private readonly List<(int важность, string текст)> buf = new();

    /// <summary>
    /// Часы текущего хода: их ставит ход дня и снимает после. Так время
    /// попадает на каждую строку дня — и на само действие, и на то,
    /// что оно за собой потянуло, — а утро, ночь и события дома остаются
    /// без часов, потому что они не чей-то ход.
    /// </summary>
    public string? час { get; set; }

    public Журнал(int verbosity = 1, bool secrets = false, TextWriter? поток = null)
    {
        this.verbosity = verbosity;
        this.secrets = secrets;
        this.поток = поток ?? Console.Out;
    }

    // --- запись ---
    public void line(string текст, int заметность = 1) => line(текст, заметность, false);

    public void line(string текст, int заметность, bool hidden)
    {
        if (hidden && !secrets)
            return;
        buf.Add((заметность, час is not null ? $"{час} {текст}" : текст));
    }

    public void @event(string текст, bool scripted = false)
        => buf.Add((2, $"{(scripted ? "◆" : "◇")} {текст}"));

    public void secret(string текст)
    {
        if (secrets)
            buf.Add((1, $"    ⌁ {текст}"));
    }

    public void chat(string кто, string текст) => buf.Add((1, $"  [чат] {кто}: {текст}"));

    // --- вывод ---
    public void w(string s = "") => поток.Write(s + "\n");

    /// <summary>
    /// Заголовок дня: число, погода и то, что в доме отключено.
    ///
    /// Отдельно от <c>flush_day</c>, потому что живой журнал печатает его
    /// перед первой строкой дня, а не в конце: игрок должен видеть день,
    /// пока день идёт.
    /// </summary>
    public string шапка_дня(House h)
    {
        string weather = $"{Текст.Знак(h.outside, 0)}°C";
        var infra = new List<string>();
        if (!h.heating)
            infra.Add("без отопления");
        if (!h.water_on)
            infra.Add("без воды");
        if (!h.power_on)
            infra.Add("без света");
        if (h.network <= 0)
            infra.Add("без связи");
        string tail = infra.Count > 0 ? " · " + string.Join(", ", infra) : "";
        string режим = h.режим.Текст();
        return $"══ ДЕНЬ {h.day} · {режим} · {weather}{tail} "
               + new string('═', Math.Max(0, 40 - tail.Length - (режим.Length - 6)));
    }

    public void flush_day(House h)
    {
        w();
        w(шапка_дня(h));
        int порог = verbosity == 0 ? 2 : (verbosity == 1 ? 1 : 0);
        foreach (var (важность, текст) in buf)
            if (важность >= порог)
                w("  " + текст);
        buf.Clear();
    }

    /// <summary>
    /// Что заметили соседи (GDD 4.4).
    ///
    /// Главный инструмент обучения по документу: игрок должен понимать,
    /// чем живёт дом, до того как это его убьёт.
    /// </summary>
    public void сводка_дня(House h)
    {
        if (verbosity < 1)
            return;
        var сводка = h.ожидает.сводка;
        h.ожидает.сводка = Словари.Строкой<List<string>>();
        foreach (string pid in сводка.Ключи.OrderBy(x => x, StringComparer.Ordinal))
        {
            var p = h.people.Взять(pid, null);
            if (p is null || !p.здесь())
                continue;
            var строки = сводка.Взять(pid, null!).Take(3).ToList();
            if (строки.Count > 0)
                line($"{p.@short} за день заметил"
                     + (string.Equals(p.sex, "ж", StringComparison.Ordinal) ? "а" : "")
                     + ": " + string.Join("; ", строки), 1);
        }
    }

    /// <summary>Скрытые шкалы — для дизайнера, не для игрока.</summary>
    public void panel(House h)
    {
        if (verbosity < 1)
            return;
        w("  " + new string('┄', 74));
        foreach (var p in h.people.Значения.OrderBy(x => x.apt))
        {
            if (!p.alive)
            {
                w($"  {Текст.Слева(p.@short, 8)} кв{Текст.Слева(Ц(p.apt), 3)} † "
                  + $"{p.cause} (день {p.died_day})");
                continue;
            }
            if (p.exiled)
            {
                string как = p.ушёл
                    ? Util.Vb(p.sex, "ушёл") + " к пункту обогрева"
                    : Util.Vb(p.sex, "изгнан");
                w($"  {Текст.Слева(p.@short, 8)} кв{Текст.Слева(Ц(p.apt), 3)} — "
                  + $"{как} (день {p.died_day})");
                continue;
            }
            var st = p.stock;
            // злость и осведомлённость показываются про тех, кто ещё в подъезде:
            // «злость→Игорь 98» на третий день после похорон Игоря — правда
            // модели (злость оседает медленно), но панель — это срез дома
            // сегодня, а не список того, что человек в себе носит
            var здесь = new HashSet<string>(h.others(p).Select(o => o.id),
                                            StringComparer.Ordinal);
            var top_hate = Наибольший(p.hate.Ключи
                .Where(k => p.hate.Взять(k, 0.0) > 25 && здесь.Contains(k))
                .Select(k => (k, p.hate.Взять(k, 0.0))));
            var top_aware = Наибольший(p.сведения.Ключи
                .Where(k => p.сведения.Взять(k, null!).aware > 45 && здесь.Contains(k))
                .Select(k => (k, p.сведения.Взять(k, null!).aware)));
            var marks = new List<string>();
            if (top_hate is not null)
                marks.Add($"злость→{h.people[top_hate.Value.Item1].@short} "
                          + $"{(int)top_hate.Value.Item2}");
            if (top_aware is not null)
                marks.Add($"знает о {h.people[top_aware.Value.Item1].@short} "
                          + $"{(int)top_aware.Value.Item2}");
            var страшный = p.самый_страшный(среди: здесь, порог: 20.0);
            if (страшный is not null)
                marks.Add($"боится {h.people[страшный.Value.Item1].@short} "
                          + $"{(int)страшный.Value.Item2}");
            var дыры = h.where(p).дыры;
            if (дыры.Count > 0)
                marks.Add("дыры: " + string.Join("+",
                    дыры.Ключи.OrderBy(x => x, StringComparer.Ordinal)));
            // шахта обмёрзла: то, чего человек про свою квартиру не знает,
            // а дизайнеру видеть надо — иначе угар выглядит как случайность
            double вент = h.where(p).вентиляция;
            if (вент < 0.5)
                marks.Add($"вытяжка {Текст.Ф(вент, 2)}");
            if (p.allies.Count > 0)
                marks.Add("союз: " + string.Join("+",
                    p.allies.OrderBy(x => x, StringComparer.Ordinal)
                            .Select(a => h.people[a].@short)));
            if (!string.IsNullOrEmpty(p.group))
                marks.Add(p.group!);
            if (p.injuries.Count > 0)
                marks.Add(string.Join("/", p.injuries));
            if (!string.IsNullOrEmpty(p.sick))
                marks.Add(p.sick!);
            foreach (var р in p.дети)
                marks.Add($"{р.имя}: сыт{(int)р.сытость} тепл{(int)р.тепло}"
                          + $" здор{(int)р.здоровье}"
                          + (р.болен is not null ? $" ({р.болен})" : ""));
            w($"  {Текст.Слева(p.@short, 8)} кв{Текст.Слева(Ц(p.apt), 3)}"
              + $" сыт{Ц((int)p.satiety, 3)} вод{Ц((int)p.hydration, 3)}"
              + $" тепл{Ц((int)p.warmth, 3)} сон{Ц((int)p.rest, 3)}"
              + $" настр{Ц((int)p.mood, 3)} здор{Ц((int)p.health, 3)}"
              + $" паника{Ц((int)p.panic, 3)}"
              + $" │ еда {Текст.Справа(st.Взять("еда", 0.0), 4, 1)}"
              + $" топл {Текст.Справа(st.Взять("топливо", 0.0), 4, 1)}"
              + $" мат {Текст.Справа(st.Взять("материалы", 0.0), 3, 0)}"
              + $" лек {Текст.Справа(st.Взять("лекарства", 0.0), 2, 0)}"
              + $" нал {Текст.Справа(st.Взять("деньги", 0.0), 3, 0)}"
              + (h.банки && p.счёт != 0 ? $"+{Текст.G(p.счёт)}" : "")
              + (marks.Count > 0 ? " │ " + string.Join(", ", marks) : ""));
        }
    }

    /// <summary>Первый из наибольших по второму полю — как `max` в Python.</summary>
    private static (string, double)? Наибольший(IEnumerable<(string, double)> пары)
    {
        (string, double)? лучший = null;
        foreach (var п in пары)
            if (лучший is null || п.Item2 > лучший.Value.Item2)
                лучший = п;
        return лучший;
    }

    internal static string Ц(int v)
        => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static string Ц(int v, int ширина) => Ц(v).PadLeft(ширина);
}
