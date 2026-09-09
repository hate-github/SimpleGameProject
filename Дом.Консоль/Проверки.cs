// Проверки порта — то же, что `check.py` в прототипе, и в том же порядке
// (навык golden-oracle): сначала генератор, потом данные, потом состояние
// по дням, потом сохранение, и только в самом конце текст.
//
// Пока перенесён `util`, здесь живут две первые: вектор генератора
// и граница движка. Остальные добавляются вместе со своими модулями.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Дом.Ядро;

namespace Дом.Консоль;

public static class Проверки
{
    /// <summary>
    /// Генератор случайности против файла-вектора (`data/rng_вектор.json`).
    ///
    /// Вектор — это приёмка переноса: порт обязан воспроизвести те же числа
    /// бит в бит, иначе `data/эталон.json` на нём не сойдётся и переносить
    /// дальше бессмысленно. Производные (`Rint`, тасовка, `Pick`) важнее
    /// самих бросков: они ловят ошибку не в генераторе, а в том, как из него
    /// берут число.
    /// </summary>
    public static List<string> Генератор(Action<string> w)
    {
        using var поток = File.OpenRead(Пути.Файл("rng_вектор.json"));
        using var док = JsonDocument.Parse(поток);
        var зёрна = док.RootElement.GetProperty("зёрна");

        var плохо = new List<string>();
        var список = new List<(long зерно, JsonElement ждём)>();
        foreach (var п in зёрна.EnumerateObject())
            список.Add((long.Parse(п.Name), п.Value));
        список.Sort((a, b) => a.зерно.CompareTo(b.зерно));

        foreach (var (зерно, ждём) in список)
        {
            var r = new Rng(зерно);
            var броски = new uint[10000];
            for (int i = 0; i < броски.Length; i++)
                броски[i] = r.Бросок();

            var rц = new Rng(зерно);
            var rп = new Rng(зерно);
            var азбука = "абвгде".ToCharArray();

            var rint = new int[64];
            for (int i = 0; i < 64; i++)
                rint[i] = rц.Rint(1, 10);

            var pick = new StringBuilder();
            for (int i = 0; i < 32; i++)
                pick.Append(rп.Pick(азбука));

            Сверить(плохо, зерно, "броски_первые_64", ждём,
                ЧислаСтрокой(броски.Take(64).Select(x => (long)x)));
            Сверить(плохо, зерно, "броски_10000_sha256_16", ждём,
                Sha256_16(string.Join(",", броски)));
            Сверить(плохо, зерно, "rint_1_10_первые_64", ждём,
                ЧислаСтрокой(rint.Select(x => (long)x)));
            Сверить(плохо, зерно, "тасовка_0_9", ждём,
                ЧислаСтрокой(new Rng(зерно).Shuffled(Enumerable.Range(0, 10)).Select(x => (long)x)));
            Сверить(плохо, зерно, "pick_из_абвгде_32", ждём, pick.ToString());
        }

        if (плохо.Count > 0)
            foreach (var x in плохо)
                w("  " + x);
        else
            w($"  генератор сходится с вектором на {список.Count} зёрнах " +
              "(10 000 бросков на каждом)");
        return плохо;
    }

    /// <summary>
    /// Граница движка: в `Дом.Ядро` не должно быть ни одного упоминания
    /// Godot. Проверка дешёвая и стоит здесь потому, что нарушить границу
    /// легче всего случайно — одной строкой `using` (навык godot-boundary).
    /// </summary>
    public static List<string> ГраницаДвижка(Action<string> w)
    {
        var ядро = Path.Combine(Пути.Корень, "Дом.Ядро");
        var плохо = new List<string>();
        int файлов = 0;
        foreach (var файл in Directory.EnumerateFiles(ядро, "*.cs", SearchOption.AllDirectories))
        {
            if (файл.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                файл.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            файлов++;
            var строки = File.ReadAllLines(файл);
            for (int i = 0; i < строки.Length; i++)
            {
                var s = строки[i];
                if (s.Contains("using Godot", StringComparison.Ordinal) ||
                    s.Contains("Godot.", StringComparison.Ordinal))
                    плохо.Add($"ядро знает о Godot: {Path.GetFileName(файл)}:{i + 1}: {s.Trim()}");
            }
        }
        if (плохо.Count > 0)
            foreach (var x in плохо)
                w("  " + x);
        else
            w($"  ядро о Godot не знает ({файлов} файлов, ноль упоминаний)");
        return плохо;
    }

    /// <summary>
    /// Контракт вопросов игроку — против самого прототипа. Читается
    /// `house/decision.py`, а не копия списка: порт контракт воспроизводит,
    /// а не переписывает. Сверяются и состав, и порядок, и текст.
    /// </summary>
    public static List<string> Вопросы(Action<string> w)
    {
        var путь = Path.Combine(Пути.Корень, "house", "decision.py");
        var текст = File.ReadAllText(путь);
        var начало = текст.IndexOf("class Вопрос(StrEnum):", StringComparison.Ordinal);
        var конец = текст.IndexOf("\nclass ", начало + 1, StringComparison.Ordinal);
        var тело = конец > 0 ? текст[начало..конец] : текст[начало..];

        var из_python = new List<(string имя, string текст)>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(
                     тело, @"^\s{4}([А-ЯЁA-Z_]+)\s*=\s*""([^""]*)""",
                     System.Text.RegularExpressions.RegexOptions.Multiline))
            из_python.Add((m.Groups[1].Value, m.Groups[2].Value));

        var из_csharp = Дом.Ядро.Вопросы.Все
            .Select(п => (имя: п.Key.ToString(), текст: п.Value)).ToList();

        var плохо = new List<string>();
        if (из_python.Count == 0)
            плохо.Add("не удалось прочитать decision.Вопрос из прототипа");
        else if (из_python.Count != из_csharp.Count)
            плохо.Add($"вопросов в прототипе {из_python.Count}, в порте {из_csharp.Count}");
        else
            for (int i = 0; i < из_python.Count; i++)
                if (из_python[i] != из_csharp[i])
                    плохо.Add($"вопрос {i}: в прототипе {из_python[i]}, в порте {из_csharp[i]}");

        if (плохо.Count > 0)
            foreach (var x in плохо)
                w("  " + x);
        else
            w($"  вопросы игроку совпадают с прототипом: {из_python.Count} членов, " +
              "имя в имя и текст в текст");
        return плохо;
    }

    private static void Сверить(List<string> плохо, long зерно, string имя,
                                JsonElement ждём, string стало)
    {
        var было = ЖдёмСтрокой(ждём.GetProperty(имя));
        if (было != стало)
            плохо.Add($"генератор разошёлся с вектором: зерно {зерно}, «{имя}»");
    }

    private static string ЖдёмСтрокой(JsonElement e)
        => e.ValueKind == JsonValueKind.Array
            ? string.Join(",", e.EnumerateArray().Select(x => x.GetInt64().ToString()))
            : e.GetString() ?? "";

    private static string ЧислаСтрокой(IEnumerable<long> числа) => string.Join(",", числа);

    private static string Sha256_16(string s)
    {
        var хэш = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(хэш).ToLowerInvariant()[..16];
    }
}
