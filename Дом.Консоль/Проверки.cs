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

        using var ждём = Оракул.Json(СНИМОК_PY + """
        h = engine.Simulation(seed=1).h
        печать(h)
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

    /// <summary>
    /// Этап 1б: характер, психика, физиология и сутки ребёнка.
    ///
    /// Свежий дом эти четыре модуля почти не трогают: при работающем отоплении
    /// никто не мёрзнет, не болеет и не ранен, а значит ни одна ветка
    /// с броском `rng` не выполняется вовсе — и проверка была бы пустой.
    /// Поэтому дом ставится в положение двенадцатого дня без отопления, воды,
    /// света и связи, а жильцам раздаются травмы, болезни и провалившиеся
    /// шкалы — по одной и той же формуле на обеих сторонах.
    ///
    /// Сверяется не только состояние после двух суток, но и **положение
    /// в ленте случайности**: последним делом с обеих сторон берётся ещё одно
    /// число, и если порт бросил монету лишний раз или на раз меньше, оно
    /// разойдётся.
    /// </summary>
    public static List<string> Тело(Action<string> w)
    {
        if (!Оракул.Доступен)
        {
            w("  сверить не с чем: python не найден");
            return new List<string> { "сутки тела не сверены с прототипом" };
        }

        using var ждём = Оракул.Json(ФИКСТУРА_PY);

        var h = Собрать();
        Обстановка(h);
        var мерки = new JsonObject();
        for (int проход = 0; проход < 2; проход++)
        {
            h.календарь.последнее_громкое = проход == 0 ? 11 : 0;
            foreach (var p in h.alive())
            {
                Психика.горизонт(h, p);
                Психика.дрейф_нормальности(h, p);
            }
            int infra = (h.heating ? 0 : 1) + (h.water_on ? 0 : 1)
                      + (h.power_on ? 0 : 1) + (h.network <= 0 ? 1 : 0);
            foreach (var p in h.alive().ToList())
            {
                double room = Физиология.расход_и_тепло(h, p, infra);
                Дети.сутки(h, p, room);
                Физиология.износ(h, p, infra);
            }
        }
        foreach (var p in h.people.Значения)
        {
            var свои = new JsonObject();
            foreach (var key in Дом.Ядро.Каталог.COST.Ключи)
            {
                var (запрет, склонность) = Характер.мерка_поступка(p, key, h.B);
                свои[key] = new JsonArray(
                    Дом.Ядро.Снимок.Округлить(запрет),
                    Дом.Ядро.Снимок.Округлить(склонность),
                    Дом.Ядро.Снимок.Округлить(Характер.своя_мерка(p, key, h.B)),
                    Дом.Ядро.Снимок.Округлить(Характер.norm_gate(p, key, h.B)));
            }
            свои["причина_смерти"] = Физиология.причина_смерти(p);
            мерки[p.id] = свои;
        }

        var стало = Дом.Ядро.Снимок.Собранный(h);
        стало["мерки"] = мерки;
        стало["лента"] = Дом.Ядро.Снимок.Округлить(h.rng.Random());

        var плохо = new List<string>();
        using var мой = JsonDocument.Parse(стало.ToJsonString());
        Сравнение.Одинаково(ждём.RootElement, мой.RootElement, "сутки", плохо);
        if (плохо.Count > 0)
            foreach (var x in плохо)
                w("  " + x);
        else
            w("  сутки тела, психика, характер и ребёнок совпадают с прототипом: " +
              $"двое суток на {h.people.Count} жильцах, " +
              $"{Дом.Ядро.Каталог.COST.Count} мерок на каждого, лента сходится");
        return плохо;
    }

    /// <summary>
    /// Положение, в котором эти четыре модуля работают целиком: двенадцатый
    /// день без коммуналок, промёрзшие и больные жильцы. Ровно то же самое
    /// делает питоновская половина проверки — формула одна.
    /// </summary>
    private static void Обстановка(House h)
    {
        h.day = 12;
        h.heating = false;
        h.power_on = false;
        h.water_on = false;
        h.network = 0.0;
        h.outside = -28.0;
        int i = 0;
        foreach (var p in h.people.Значения)
        {
            p.warmth = 10.0 + (i * 7) % 45;
            p.satiety = 30.0 + (i * 11) % 60;
            p.hydration = 28.0 + (i * 13) % 55;
            p.rest = 20.0 + (i * 5) % 70;
            p.health = 80.0 + (i * 3) % 20;
            p.mood = 30.0 + (i * 9) % 60;
            p.panic = 10.0 + (i * 17) % 70;
            p.часы_работы = (i % 5) * 1.5;
            p.день_разговора = i % 3 == 0 ? 12 : -99;
            p.burning = i % 6 == 0;
            if (i % 4 == 1)
                p.injuries.Add("ушиб руки");
            if (i % 4 == 2)
            {
                p.injuries.Add("перелом ноги");
                p.injuries.Add("порез руки");
            }
            if (i % 5 == 3)
                p.sick = "простуда";
            int j = 0;
            foreach (var р in p.дети)
            {
                р.тепло = 20.0 + (i * 13 + j) % 50;
                р.сытость = 40.0 + (i * 7 + j) % 45;
                р.здоровье = 95.0;
                р.болен = (i + j) % 2 == 1 ? "простуда" : null;
                j++;
            }
            i++;
        }
    }

    /// <summary>
    /// Этап 1в, `social`: шум, запах, взгляд, слухи, ложь, слово, отношения,
    /// союзы, группы и цена вещи.
    ///
    /// Дом ставится в затишье двенадцатого дня — в буран половина модуля
    /// молчит, — с уже случившимися происшествиями и одним мёртвым жильцом
    /// (поле `alive`, а не через `conflict.умер`: тот ещё не переехал).
    /// Дальше сценарий проходит по всем парам и зовёт всё, что в модуле есть.
    /// </summary>
    public static List<string> Общество(Action<string> w)
    {
        if (!Оракул.Доступен)
        {
            w("  сверить не с чем: python не найден");
            return new List<string> { "social не сверен с прототипом" };
        }

        using var ждём = Оракул.Json(ОБЩЕСТВО_PY);

        var h = Собрать();
        ОбстановкаОбщества(h, out var мертвец);
        var живые = h.alive();

        foreach (var a in живые)
            foreach (var b2 in живые)
            {
                if (string.CompareOrdinal(a.id, b2.id) >= 0)
                    continue;
                Социальное.встретились(h, a, b2);
                Социальное.gossip(h, a, b2);
            }
        for (int i = 0; i < живые.Count; i++)
        {
            var a = живые[i];
            Социальное.observe(h, a, живые[(i + 1) % живые.Count]);
            Социальное.emit(h, a, 1 + i % 5,
                new[] { "готовка", "буржуйка", "ремонт", "разбор", "генератор" }[i % 5]);
            Социальное.smell(h, a, hot: i % 2 == 0);
        }
        for (int i = 0; i < живые.Count; i++)
        {
            var a = живые[i];
            var b2 = живые[(i + 3) % живые.Count];
            Социальное.обещать(h, a, b2, "отдать", "еда");
            if (i % 2 == 1)
                Социальное.сдержал(h, a, b2, "отдать", "еда");
            Социальное.обидели(h, b2, a, 20.0 + i, непрощаемо: i % 5 == 0);
            Социальное.загладил(h, a, b2, 8.0);
            Социальное.judge(h, a, "воровство", hate: 6.0, trust: -0.4,
                             участники: new List<NPC> { b2 });
            Социальное.видел(h, b2, 0.05, кто: a);
            Социальное.переступил(h, a, "разбор");
            Социальное.испугался(h, b2, a, 5.0);
            Социальное.увидел_оружие(h, b2, a);
            Социальное.держится(h, b2, a);
            Социальное.отдалились(b2, a.id, 0.3);
            Социальное.вошёл_в_квартиру(h, a, h.flats[b2.apt]);
        }
        Социальное.нашёл_тело(h, живые[0], мертвец);
        foreach (var a in живые.Skip(1).Take(3))
            Социальное.сообщить_о_смерти(h, живые[0], a, мертвец);
        Социальное.house_shock(h, panic: 3.0, mood: -2.0, note: "проверка");
        Социальное.register_incident(h, "проверка", null);
        Социальное.spread_panic(h);
        Социальное.alliance_check(h);
        Социальное.update_groups(h);
        Социальное.проверить_обещания(h);
        Социальное.daily_decay(h);

        var цены = new JsonObject();
        foreach (var a in h.people.Значения)
        {
            var свои = new JsonObject();
            foreach (var res in Ресурсы.ВСЕ)
                свои[res] = new JsonArray(
                    Дом.Ядро.Снимок.Округлить(Социальное.value_of(a, res, 2.0)),
                    Дом.Ядро.Снимок.Округлить(
                        Социальное.value_of(a, res, 2.0, глазами: живые[0])));
            свои["деньги_цена"] = Дом.Ядро.Снимок.Округлить(Социальное.цена_денег(a));
            свои["напряжение"] = Дом.Ядро.Снимок.Округлить(Социальное.напряжение_дома(h, a));
            свои["выгода"] = Дом.Ядро.Снимок.Округлить(
                Социальное.выгода_соседства(h, a, живые[1]));
            свои["теснота"] = Дом.Ядро.Снимок.Округлить(
                Социальное.теснота(h, a, живые[2], h.B));
            var дни = new JsonArray();
            foreach (var t in живые.Take(3))
                дни.Add(Дом.Ядро.Снимок.Округлить(Социальное.believed_days(a, t, "еда")));
            свои["дни"] = дни;
            цены[a.id] = свои;
        }

        var стало = Дом.Ядро.Снимок.Собранный(h);
        стало["цены"] = цены;
        стало["лента"] = Дом.Ядро.Снимок.Округлить(h.rng.Random());

        var плохо = new List<string>();
        using var мой = JsonDocument.Parse(стало.ToJsonString());
        Сравнение.Одинаково(ждём.RootElement, мой.RootElement, "общество", плохо);
        if (плохо.Count > 0)
            foreach (var x in плохо)
                w("  " + x);
        else
            w($"  шум, слухи, слово и отношения совпадают с прототипом: " +
              $"{живые.Count} живых, {живые.Count * (живые.Count - 1) / 2} пар, лента сходится");
        return плохо;
    }

    private static void ОбстановкаОбщества(House h, out NPC мертвец)
    {
        h.day = 12;
        h.режим = Режим.ЗАТИШЬЕ;
        h.календарь.последнее_громкое = 9;
        h.power_on = false;
        h.water_on = false;
        h.network = 0.0;
        h.first_incident_day = 8;
        h.календарь.происшествия_дни.Clear();
        h.календарь.происшествия_дни.AddRange(new[] { 8, 10, 11 });
        int i = 0;
        foreach (var p in h.people.Значения)
        {
            p.panic = 10.0 + (i * 13) % 60;
            p.mood = 30.0 + (i * 7) % 50;
            p.normalcy = 0.2 + ((i * 11) % 70) / 100.0;
            p.satiety = 20.0 + (i * 17) % 70;
            p.warmth = 15.0 + (i * 5) % 60;
            p.stock["еда"] = (i * 3) % 12;
            p.stock["топливо"] = (i * 5) % 9;
            p.burning = i % 3 == 0;
            // без знания о местах и без злости не срабатывают ни ложь
            // о местах, ни наговор — половина раздела «слово» осталась бы
            // непройденной
            p.места["двор"] = 0.2 + (i % 5) * 0.15;
            p.места["магазин на Заречной"] = 0.95 - (i % 4) * 0.12;
            p.места["гаражи"] = 0.5 + (i % 3) * 0.2;
            i++;
        }
        // и злость: наговор начинается с неё
        var все = h.people.Значения;
        for (int j = 0; j < все.Count; j++)
        {
            все[j].hate[все[(j + 4) % все.Count].id] = 50.0 + j;
            все[j].trust[все[(j + 2) % все.Count].id] = 7.0;
        }
        мертвец = h.people.Значения[6];
        мертвец.alive = false;
        мертвец.cause = "убит в драке";
        мертвец.died_day = 11;
    }

    private const string ОБЩЕСТВО_PY = СНИМОК_PY + """
        # Та же обстановка и тот же сценарий, что в Проверки.Общество.
        from house import social
        from house.model import Режим, RESOURCES

        h = engine.Simulation(seed=1).h
        h.day = 12
        h.режим = Режим.ЗАТИШЬЕ
        h.календарь.последнее_громкое = 9
        h.power_on = False
        h.water_on = False
        h.network = 0.0
        h.first_incident_day = 8
        h.календарь.происшествия_дни = [8, 10, 11]
        for i, p in enumerate(h.people.values()):
            p.panic = 10.0 + (i * 13) % 60
            p.mood = 30.0 + (i * 7) % 50
            p.normalcy = 0.2 + ((i * 11) % 70) / 100.0
            p.satiety = 20.0 + (i * 17) % 70
            p.warmth = 15.0 + (i * 5) % 60
            p.stock["еда"] = float((i * 3) % 12)
            p.stock["топливо"] = float((i * 5) % 9)
            p.burning = (i % 3 == 0)
            p.места["двор"] = 0.2 + (i % 5) * 0.15
            p.места["магазин на Заречной"] = 0.95 - (i % 4) * 0.12
            p.места["гаражи"] = 0.5 + (i % 3) * 0.2
        все = list(h.people.values())
        for j, p in enumerate(все):
            p.hate[все[(j + 4) % len(все)].id] = 50.0 + j
            p.trust[все[(j + 2) % len(все)].id] = 7.0
        мертвец = list(h.people.values())[6]
        мертвец.alive = False
        мертвец.cause = "убит в драке"
        мертвец.died_day = 11

        живые = h.alive()
        for a in живые:
            for b2 in живые:
                if a.id >= b2.id:
                    continue
                social.встретились(h, a, b2)
                social.gossip(h, a, b2)
        виды = ["готовка", "буржуйка", "ремонт", "разбор", "генератор"]
        for i, a in enumerate(живые):
            social.observe(h, a, живые[(i + 1) % len(живые)])
            social.emit(h, a, 1 + i % 5, виды[i % 5])
            social.smell(h, a, hot=(i % 2 == 0))
        for i, a in enumerate(живые):
            b2 = живые[(i + 3) % len(живые)]
            social.обещать(h, a, b2, "отдать", "еда")
            if i % 2 == 1:
                social.сдержал(h, a, b2, "отдать", "еда")
            social.обидели(h, b2, a, 20.0 + i, непрощаемо=(i % 5 == 0))
            social.загладил(h, a, b2, 8.0)
            social.judge(h, a, "воровство", hate=6.0, trust=-0.4, участники=[b2])
            social.видел(h, b2, 0.05, кто=a)
            social.переступил(h, a, "разбор")
            social.испугался(h, b2, a, 5.0)
            social.увидел_оружие(h, b2, a)
            social.держится(h, b2, a)
            social.отдалились(b2, a.id, 0.3)
            social.вошёл_в_квартиру(h, a, h.flats[b2.apt])
        social.нашёл_тело(h, живые[0], мертвец)
        for a in живые[1:4]:
            social.сообщить_о_смерти(h, живые[0], a, мертвец)
        social.house_shock(h, panic=3.0, mood=-2.0, note="проверка")
        social.register_incident(h, "проверка", None)
        social.spread_panic(h)
        social.alliance_check(h)
        social.update_groups(h)
        social.проверить_обещания(h)
        social.daily_decay(h)

        цены = {}
        for a in h.people.values():
            свои = {}
            for res in RESOURCES:
                свои[res] = [окр(social.value_of(a, res, 2.0)),
                             окр(social.value_of(a, res, 2.0, глазами=живые[0]))]
            свои["деньги_цена"] = окр(social.цена_денег(a))
            свои["напряжение"] = окр(social.напряжение_дома(h, a))
            свои["выгода"] = окр(social.выгода_соседства(h, a, живые[1]))
            свои["теснота"] = окр(social.теснота(h, a, живые[2], h.B))
            свои["дни"] = [окр(social.believed_days(a, t, "еда")) for t in живые[:3]]
            цены[a.id] = свои

        печать(h, цены=цены, лента=окр(h.rng.random()))
        """;

    /// <summary>
    /// Питоновская половина канонического снимка: те же семь правил, что
    /// в `Дом.Ядро/Данные/Снимок.cs`. Подставляется в начало каждого скрипта
    /// оракула; скрипт доводит дом до нужного состояния и зовёт `печать(h)`.
    /// </summary>
    private const string СНИМОК_PY = """
        # -*- coding: utf-8 -*-
        import dataclasses, json, sys
        from enum import Enum
        sys.path.insert(0, ".")
        from house import engine

        СКРЫТЬ = {
            "NPC": {"_h", "решающий"},
            "House": {"rng", "B", "journal", "people", "flats", "кладовые",
                      "реплики_быт", "hooks"},
        }
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

        def окр(v):
            r = round(v, 6)
            return 0.0 if r == 0.0 else r

        def знач(v, ссылкой=True, как_запись=False):
            if v is None:              return None
            if isinstance(v, bool):    return 1 if v else 0
            if isinstance(v, Enum):    return v.value
            if isinstance(v, str):     return v
            if isinstance(v, float):   return окр(v)
            if isinstance(v, int):     return v
            if isinstance(v, dict):
                пары = sorted(v.items(), key=lambda kv: ключ(kv[0])) if как_запись                        else list(v.items())
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
                return {f.name: знач(getattr(v, f.name), как_запись=f.name in КАК_ЗАПИСЬ)
                        for f in поля if f.name not in скрыть}
            return str(v)

        def снимок(h, **ещё):
            д = {
                "квартиры": {str(k): знач(v, ссылкой=False) for k, v in h.flats.items()},
                "кладовые": {k: знач(v, ссылкой=False) for k, v in h.кладовые.items()},
                "жильцы":   {k: знач(v, ссылкой=False) for k, v in h.people.items()},
                "дом":      знач(h, ссылкой=False),
            }
            д.update(ещё)
            return д

        def печать(h, **ещё):
            print(json.dumps(снимок(h, **ещё), ensure_ascii=False))

        """;

    private const string ФИКСТУРА_PY = СНИМОК_PY + """
        # Та же обстановка и тот же сценарий, что в Проверки.Тело.
        from house import psyche, physiology, child, character, catalog

        h = engine.Simulation(seed=1).h

        # --- обстановка: двенадцатый день без коммуналок ---
        h.day = 12
        h.heating = False
        h.power_on = False
        h.water_on = False
        h.network = 0.0
        h.outside = -28.0
        for i, p in enumerate(h.people.values()):
            p.warmth = 10.0 + (i * 7) % 45
            p.satiety = 30.0 + (i * 11) % 60
            p.hydration = 28.0 + (i * 13) % 55
            p.rest = 20.0 + (i * 5) % 70
            p.health = 80.0 + (i * 3) % 20
            p.mood = 30.0 + (i * 9) % 60
            p.panic = 10.0 + (i * 17) % 70
            p.часы_работы = (i % 5) * 1.5
            p.день_разговора = 12 if i % 3 == 0 else -99
            p.burning = (i % 6 == 0)
            if i % 4 == 1:
                p.injuries.append("ушиб руки")
            if i % 4 == 2:
                p.injuries.append("перелом ноги")
                p.injuries.append("порез руки")
            if i % 5 == 3:
                p.sick = "простуда"
            for j, р in enumerate(p.дети):
                р.тепло = 20.0 + (i * 13 + j) % 50
                р.сытость = 40.0 + (i * 7 + j) % 45
                р.здоровье = 95.0
                р.болен = "простуда" if (i + j) % 2 == 1 else None

        # --- двое суток ---
        for проход in range(2):
            h.календарь.последнее_громкое = 11 if проход == 0 else 0
            for p in h.alive():
                psyche.горизонт(h, p)
                psyche.дрейф_нормальности(h, p)
            infra = ((not h.heating) + (not h.water_on) + (not h.power_on) + (h.network <= 0))
            for p in list(h.alive()):
                room = physiology.расход_и_тепло(h, p, infra)
                child.сутки(h, p, room)
                physiology.износ(h, p, infra)

        мерки = {}
        for p in h.people.values():
            свои = {}
            for key in catalog.COST:
                з, с = character.мерка_поступка(p, key, h.B)
                свои[key] = [окр(з), окр(с),
                             окр(character.своя_мерка(p, key, h.B)),
                             окр(character.norm_gate(p, key, h.B))]
            свои["причина_смерти"] = physiology.причина_смерти(p)
            мерки[p.id] = свои

        печать(h, мерки=мерки, лента=окр(h.rng.random()))
        """;

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
