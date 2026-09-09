// Проверки порта — то же, что `check.py` в прототипе, и в том же порядке
// (навык golden-oracle): сначала генератор, потом данные, потом состояние
// по дням, потом сохранение, и только в самом конце текст.
//
// Пока перенесён `util`, здесь живут две первые: вектор генератора
// и граница движка. Остальные добавляются вместе со своими модулями.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// Справочник действий — против самого прототипа. Сверяются все
    /// одиннадцать таблиц, и не только состав, но и порядок: `COST` идёт
    /// в порядке записей, а это порядок вариантов в корзине и, значит,
    /// порядок жребия.
    /// </summary>
    public static List<string> Каталог(Action<string> w)
    {
        if (!Оракул.Доступен)
        {
            w("  сверить не с чем: python не найден");
            return new List<string> { "каталог не сверен с прототипом" };
        }

        using var ждём = Оракул.Json("""
            # -*- coding: utf-8 -*-
            import json, sys
            sys.path.insert(0, ".")
            from house import catalog as c

            пары = lambda d: [[k, v] for k, v in d.items()]
            имена = lambda x: (x if isinstance(x, str) else None)
            данные = {
                "COST": [[k, [v[0], v[1]]] for k, v in c.COST.items()],
                "НОРМА": пары(c.НОРМА),
                "ТЕГИ": [[k, list(v)] for k, v in c.ТЕГИ.items()],
                "СУД": [[k, [имена(x) for x in v]] for k, v in c.СУД.items()],
                "NOTABLE": пары(c.NOTABLE),
                "СТРОЙКА": пары(c.СТРОЙКА),
                "ПОСТРОЙКИ": [[k, [v[0], v[1], list(v[2]), v[3]]]
                              for k, v in c.ПОСТРОЙКИ.items()],
                "ВИЗИТЫ": sorted(c.ВИЗИТЫ),
                "РАБОТА": sorted(c.РАБОТА),
                "ОБЫЧНОЕ": sorted(c.ОБЫЧНОЕ),
                "ВЫХОД": sorted(c.ВЫХОД_НА_ПЛОЩАДКУ),
                "ЦЕННОСТИ": sorted(c.ЦЕННОСТИ),
                "ПУНКТИК_КЛЮЧИ": sorted(c.ПУНКТИК_КЛЮЧИ),
                "ВЕСА_КЛЮЧИ": sorted(c.ВЕСА_КЛЮЧИ),
            }
            print(json.dumps(данные, ensure_ascii=False))
            """);

        var стало = new JsonObject
        {
            ["COST"] = Пары(Дом.Ядро.Каталог.COST,
                            ш => (JsonNode?)new JsonArray(ш!.громкость, ш.вид)),
            ["НОРМА"] = Пары(Дом.Ядро.Каталог.НОРМА, v => (JsonNode?)JsonValue.Create(v)),
            ["ТЕГИ"] = Пары(Дом.Ядро.Каталог.ТЕГИ, Строки),
            ["СУД"] = Пары(Дом.Ядро.Каталог.СУД,
                           с => (JsonNode?)new JsonArray(с.ненависть, с.доверие)),
            ["NOTABLE"] = Пары(Дом.Ядро.Каталог.NOTABLE, v => (JsonNode?)JsonValue.Create(v)),
            ["СТРОЙКА"] = Пары(Дом.Ядро.Каталог.СТРОЙКА, v => (JsonNode?)JsonValue.Create(v)),
            ["ПОСТРОЙКИ"] = Пары(Дом.Ядро.Каталог.ПОСТРОЙКИ,
                                 п => (JsonNode?)new JsonArray(
                                     п.уровень, п.материалы, Строки(п.умения), п.потолок)),
            ["ВИЗИТЫ"] = Набор(Дом.Ядро.Каталог.ВИЗИТЫ),
            ["РАБОТА"] = Набор(Дом.Ядро.Каталог.РАБОТА),
            ["ОБЫЧНОЕ"] = Набор(Дом.Ядро.Каталог.ОБЫЧНОЕ),
            ["ВЫХОД"] = Набор(Дом.Ядро.Каталог.ВЫХОД_НА_ПЛОЩАДКУ),
            ["ЦЕННОСТИ"] = Набор(Дом.Ядро.Каталог.ЦЕННОСТИ),
            ["ПУНКТИК_КЛЮЧИ"] = Набор(Дом.Ядро.Каталог.ПУНКТИК_КЛЮЧИ),
            ["ВЕСА_КЛЮЧИ"] = Набор(Дом.Ядро.Каталог.ВЕСА_КЛЮЧИ),
        };

        var плохо = new List<string>();
        using var мой = JsonDocument.Parse(стало.ToJsonString());
        Сравнение.Одинаково(ждём.RootElement, мой.RootElement, "каталог", плохо);
        if (плохо.Count > 0)
            foreach (var x in плохо)
                w("  " + x);
        else
            w($"  справочник действий совпадает с прототипом: {Дом.Ядро.Каталог.ДЕЙСТВИЯ.Count} " +
              $"действий, {Дом.Ядро.Каталог.ПОСТУПКИ.Count} поступков, 14 таблиц");
        return плохо;
    }

    /// <summary>
    /// Главная приёмка этапа 1а: дом, собранный из `npcs.json`, поле в поле
    /// совпадает с питоновским. Сверяются квартиры, кладовые и жильцы целиком —
    /// каждое поле каждой записи, включая порядок ключей в словарях.
    ///
    /// Снимок считается по правилам `Дом.Ядро.Снимок`; ту же половину правил
    /// повторяет питоновский скрипт ниже. Это и есть та самая переписанная
    /// на JSON сериализация, о которой говорит `docs/ПОРТ.md`, — пока только
    /// для собранного дома, до первого дня.
    /// </summary>
    public static List<string> ДомИзДанных(Action<string> w)
    {
        if (!Оракул.Доступен)
        {
            w("  сверить не с чем: python не найден");
            return new List<string> { "сборка дома не сверена с прототипом" };
        }

        using var ждём = Оракул.Json("""
            # -*- coding: utf-8 -*-
            # Снимок собранного дома по правилам Дом.Ядро/Данные/Снимок.cs.
            # Скрипт ничего в прототипе не меняет: он его только читает.
            import dataclasses, json, sys
            from enum import Enum
            sys.path.insert(0, ".")
            from house import engine

            СКРЫТЬ = {"NPC": {"_h", "решающий"}}
            # в порте `values` — запись, а не словарь, и её поля сортируются
            # по имени (правило 7); поэтому здесь тоже
            КАК_ЗАПИСЬ = {"values"}

            def ключ(k):
                if isinstance(k, bool):   return "1" if k else "0"
                if isinstance(k, Enum):   return str(k.value)
                if isinstance(k, str):    return k
                if isinstance(k, tuple):  return ",".join(ключ(x) for x in k)
                if isinstance(k, float):
                    r = round(k, 6)
                    return repr(0.0 if r == 0.0 else r)
                return str(k)

            def знач(v, ссылкой=True, как_запись=False):
                if v is None:              return None
                if isinstance(v, bool):    return 1 if v else 0
                if isinstance(v, Enum):    return v.value
                if isinstance(v, str):     return v
                if isinstance(v, float):
                    r = round(v, 6)
                    return 0.0 if r == 0.0 else r
                if isinstance(v, int):     return v
                if isinstance(v, dict):
                    пары = sorted(v.items(), key=lambda kv: ключ(kv[0])) if как_запись \
                           else list(v.items())
                    return {ключ(k): знач(x) for k, x in пары}
                if isinstance(v, (set, frozenset)):
                    return [знач(x) for x in sorted(v, key=ключ)]
                if isinstance(v, (list, tuple)):
                    return [знач(x) for x in v]
                if dataclasses.is_dataclass(v):
                    id_ = getattr(v, "id", None)
                    if ссылкой and isinstance(id_, str):
                        return ["#", id_]
                    скрыть = СКРЫТЬ.get(type(v).__name__, set())
                    поля = sorted(dataclasses.fields(v), key=lambda f: f.name)
                    return {f.name: знач(getattr(v, f.name),
                                         как_запись=f.name in КАК_ЗАПИСЬ)
                            for f in поля if f.name not in скрыть}
                return str(v)

            h = engine.Simulation(seed=1).h
            print(json.dumps({
                "квартиры": {str(k): знач(v, ссылкой=False) for k, v in h.flats.items()},
                "кладовые": {k: знач(v, ссылкой=False) for k, v in h.кладовые.items()},
                "жильцы":   {k: знач(v, ссылкой=False) for k, v in h.people.items()},
            }, ensure_ascii=False))
            """);

        var h = Собрать();
        using var мой = JsonDocument.Parse(Дом.Ядро.Снимок.Собранный(h).ToJsonString());

        var плохо = new List<string>();
        Сравнение.Одинаково(ждём.RootElement, мой.RootElement, "дом", плохо);
        if (плохо.Count > 0)
            foreach (var x in плохо)
                w("  " + x);
        else
            w($"  дом из npcs.json совпадает с питоновским поле в поле: " +
              $"{h.flats.Count} квартир, {h.кладовые.Count} кладовых, {h.people.Count} жильцов");
        return плохо;
    }

    /// <summary>Собрать дом так же, как это делает `engine.Simulation.__init__`.</summary>
    public static House Собрать(long зерно = 1)
    {
        var данные = Схема.Прочитать(Пути.Данные);
        var h = new House { rng = new Rng(зерно), B = данные.Баланс };
        foreach (var в in данные.lines.Массив("быт").EnumerateArray())
            h.реплики_быт.Add(new РепликаБыта(
                в.Строка("текст"),
                в.Есть("занятие") ? в.Строка("занятие") : null,
                в.Есть("условие")
                    ? new Условие(в.GetProperty("условие").Строка("вид"),
                                  в.GetProperty("условие").Число("день"))
                    : null));
        Сборка.build_house(h, данные.npcs);
        return h;
    }

    private static JsonArray Пары<T>(Дом.Ядро.Словарь<string, T> т, Func<T, JsonNode?> как)
    {
        var а = new JsonArray();
        foreach (var (k, v) in т)          // порядок вставки — он тоже сверяется
            а.Add(new JsonArray(k, как(v)));
        return а;
    }

    private static JsonArray Набор(IReadOnlySet<string> s)
    {
        var а = new JsonArray();
        foreach (var x in s.OrderBy(x => x, StringComparer.Ordinal))
            а.Add(x);
        return а;
    }

    private static JsonNode? Строки(string[] xs)
    {
        var а = new JsonArray();
        foreach (var x in xs)
            а.Add(x);
        return а;
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
