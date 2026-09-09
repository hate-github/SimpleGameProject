// Перенос house/util.py. Мелкие утилиты: случайность с зерном, зажимы,
// взвешенный выбор, согласование глаголов по полу.
//
// Вся случайность в симуляции идёт через один объект Rng, созданный от зерна.
// Это значит: одно и то же зерно = один и тот же прогон, до последнего слова.
// (GDD 21: «Распределение случайных событий фиксируется зерном жизни».)
using System.Text;
using System.Text.RegularExpressions;

namespace Дом.Ядро;

/// <summary>Зажимы и приведение к долям — то же, что модульные функции util.py.</summary>
public static class Util
{
    /// <summary>Зажать значение в границы.</summary>
    public static double Clamp(double v, double lo = 0.0, double hi = 100.0)
        => v < lo ? lo : (v > hi ? hi : v);

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Привести значение к 0..1 внутри диапазона.</summary>
    public static double Norm(double v, double lo, double hi)
        => hi <= lo ? 0.0 : Clamp((v - lo) / (hi - lo), 0.0, 1.0);

    /// <summary>
    /// Сумма чисел с плавающей точкой — так, как её считает `sum()` в Python.
    ///
    /// И это не мелочь. С версии 3.12 `sum()` над float'ами суммирует
    /// с компенсацией Неймайера, а не слева направо: у четырнадцати долей
    /// близости наивное сложение даёт 3.4999999999999996, а `sum()` — ровно
    /// 3.5. Разница в два последних бита доезжает до оценки варианта
    /// и переворачивает округление шестого знака — на том и разошлись
    /// первые прогоны сбора.
    ///
    /// Правило простое: где в прототипе `sum(...)` по числам с точкой,
    /// здесь `Util.Sum`. Где `sum(...)` по целым — обычное сложение,
    /// целые точны и без компенсации.
    /// </summary>
    public static double Sum(IEnumerable<double> числа)
    {
        double итог = 0.0, c = 0.0;
        foreach (double x in числа)
        {
            double t = итог + x;
            if (Math.Abs(итог) >= Math.Abs(x))
                c += (итог - t) + x;
            else
                c += (x - t) + итог;
            итог = t;
        }
        return итог + c;
    }

    // Согласование глаголов по полу — чтобы в логе не было «Лида ходил».
    private static readonly Dictionary<string, string> _IRREGULAR =
        new(StringComparer.Ordinal)
        {
            ["зашёл"] = "зашла", ["ушёл"] = "ушла", ["нашёл"] = "нашла", ["слёг"] = "слегла",
            ["умер"] = "умерла", ["мёртв"] = "мертва", ["уверен"] = "уверена",
            ["вынес"] = "вынесла", ["унёс"] = "унесла", ["принёс"] = "принесла",
            ["занёс"] = "занесла", ["отнёс"] = "отнесла", ["снёс"] = "снесла",
            ["привёл"] = "привела", ["полез"] = "полезла", ["вышел"] = "вышла",
            ["дошёл"] = "дошла", ["замёрз"] = "замёрзла", ["смог"] = "смогла",
            ["обжёг"] = "обожгла", ["пошёл"] = "пошла",
        };

    private static readonly Regex _FORM = new(@"\{([^{}|]*)\|([^{}|]*)\}", RegexOptions.Compiled);

    /// <summary>
    /// Подставить род в строку из данных: «сидел{|а}» -> «сидел» / «сидела».
    /// Слева мужская форма, справа женская. Так реплики правятся в JSON
    /// без единой строчки кода.
    /// </summary>
    public static string Gform(string text, string sex)
        => _FORM.Replace(text, m => sex == "ж" ? m.Groups[2].Value : m.Groups[1].Value);

    /// <summary>Vb("ж", "ходил") -> "ходила", Vb("ж", "проснулся") -> "проснулась".</summary>
    public static string Vb(string sex, string word)
    {
        if (sex != "ж")
            return word;
        if (_IRREGULAR.TryGetValue(word, out var ж))
            return ж;
        if (word.EndsWith("лся", StringComparison.Ordinal))   // возвратные: -лся -> -лась
            return word[..^2] + "ась";
        return word + "а";
    }
}

/// <summary>
/// Свой поток случайности (PCG-XSH-RR): 64 бита состояния, 32 бита выхода.
///
/// Раньше в прототипе стоял вихрь Мерсенна из стандартной библиотеки Python.
/// Он не воспроизводится побитово ни в GDScript, ни в C#, а весь
/// <c>data/эталон.json</c> держится ровно на том, что зерно даёт ту же жизнь
/// до последнего слова. PCG32 — три строки арифметики по 64-битному
/// состоянию; всё считается на целых с явной маской, а не на float, —
/// поэтому результат не зависит ни от разрядности, ни от порядка байт.
///
/// Приёмка порта — <c>data/rng_вектор.json</c>: четыре зерна, первые 64 сырых
/// броска, sha256 первых 10 000 и производные (<c>Rint</c>, тасовка,
/// <c>Pick</c>). Производные важнее самих бросков: они ловят ошибку не
/// в генераторе, а в том, как из него берут число.
/// </summary>
public sealed class Rng
{
    private const ulong _МНОЖИТЕЛЬ = 6364136223846793005UL;
    private const ulong _ПРИБАВКА = 1442695040888963407UL;

    private ulong _состояние;

    /// <summary>Зерно, от которого запущен поток (нужно ветвлению и сейву).</summary>
    public long Зерно { get; }

    public Rng(long зерно)
    {
        Зерно = зерно;
        // засев как в эталонной реализации PCG: провернуть, добавить зерно,
        // провернуть ещё раз — чтобы соседние зёрна не давали похожие ленты
        _состояние = 0UL;
        Бросок();
        unchecked { _состояние += (ulong)зерно; }
        Бросок();
    }

    /// <summary>Следующие 32 бита: сдвиг-исключающее-или, потом поворот.</summary>
    public uint Бросок()
    {
        ulong было = _состояние;
        unchecked { _состояние = было * _МНОЖИТЕЛЬ + _ПРИБАВКА; }
        uint сдвинутое = (uint)((((было >> 18) ^ было) >> 27) & 0xFFFFFFFFUL);
        int поворот = (int)(было >> 59);
        return unchecked((сдвинутое >> поворот) | (сдвинутое << ((-поворот) & 31)));
    }

    /// <summary>0.0 ≤ x &lt; 1.0 — тридцать два бита точности, ровно один бросок.</summary>
    public double Random() => Бросок() / 4294967296.0;

    /// <summary>
    /// Отдельный поток случайности от того же зерна (GDD 21): мир получает
    /// свою ленту, чтобы календарь погоды не съезжал от действий игрока.
    /// crc32, а не хэш строки: встроенный хэш зависел бы от запуска.
    /// </summary>
    public Rng Branch(string tag)
    {
        uint crc = Crc32(Encoding.UTF8.GetBytes(tag));
        uint от_зерна = unchecked((uint)((ulong)Зерно * 2654435761UL));
        return new Rng(crc ^ от_зерна);
    }

    public bool Chance(double p) => Random() < p;

    public double Uni(double a, double b) => a + (b - a) * Random();

    /// <summary>
    /// a…b включительно. Остаток от деления, а не отбраковка: диапазоны здесь
    /// короткие (дни, штуки), перекос меньше миллионной доли, зато число
    /// бросков на вызов ровно одно — и лента переносится один в один.
    /// </summary>
    public int Rint(int a, int b)
    {
        if (b <= a)
            return a;
        return a + (int)(Бросок() % (uint)(b - a + 1));
    }

    /// <summary>Один из списка. На пустом списке бросок не делается — как в Python.</summary>
    public T? Pick<T>(IReadOnlyList<T> seq)
        => seq.Count == 0 ? default : seq[(int)(Бросок() % (uint)seq.Count)];

    /// <summary>Тасовка Фишера — Йетса: тот же порядок в любом языке.</summary>
    public List<T> Shuffled<T>(IEnumerable<T> seq)
    {
        var s = new List<T>(seq);
        for (int i = s.Count - 1; i > 0; i--)
        {
            int j = (int)(Бросок() % (uint)(i + 1));
            (s[i], s[j]) = (s[j], s[i]);
        }
        return s;
    }

    /// <summary>pairs: [(объект, вес)] -> объект. Веса не обязаны быть нормированы.</summary>
    public T? Weighted<T>(IReadOnlyList<(T, double)> pairs)
    {
        var веса = new double[pairs.Count];
        double total = 0.0;
        for (int i = 0; i < pairs.Count; i++)
        {
            веса[i] = Math.Max(0.0, pairs[i].Item2);
            total += веса[i];
        }
        if (total <= 0)                       // ни одного броска, как в Python
            return pairs.Count > 0 ? pairs[0].Item1 : default;
        double x = Random() * total;
        double acc = 0.0;
        for (int i = 0; i < pairs.Count; i++)
        {
            acc += веса[i];
            if (x <= acc)
                return pairs[i].Item1;
        }
        return pairs[^1].Item1;
    }

    /// <summary>
    /// options: [(объект, оценка)] -> объект. Мягкий выбор: обычно берётся
    /// лучшее, но иногда — второе или третье. temp (температура) растёт
    /// от паники: чем страшнее, тем безрассуднее выбор.
    /// </summary>
    public T? SoftmaxPick<T>(IReadOnlyList<(T, double)> options, double temp = 1.0)
    {
        if (options.Count == 0)
            return default;
        temp = Math.Max(0.08, temp);
        double best = options[0].Item2;
        for (int i = 1; i < options.Count; i++)
            if (options[i].Item2 > best)
                best = options[i].Item2;
        var weights = new (T, double)[options.Count];
        for (int i = 0; i < options.Count; i++)
            weights[i] = (options[i].Item1, Math.Exp((options[i].Item2 - best) / temp));
        return Weighted<T>(weights);
    }

    // crc32 как в zlib (полином 0xEDB88320, отражённый): своя таблица, чтобы
    // у ядра не было ни одной внешней зависимости — как и у прототипа.
    private static readonly uint[] _CRC_ТАБЛИЦА = ПостроитьТаблицу();

    private static uint[] ПостроитьТаблицу()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static uint Crc32(ReadOnlySpan<byte> данные)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in данные)
            c = _CRC_ТАБЛИЦА[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
