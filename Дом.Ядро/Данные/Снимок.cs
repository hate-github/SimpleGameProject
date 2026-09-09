// Канонический снимок состояния — в JSON, а не питоновским `repr`.
//
// `docs/ПОРТ.md` этого прямо и просит: «Разумнее переписать сериализацию
// на JSON с теми же правилами (сортировка, округление, сворачивание ссылок)
// и в Python, и в порте одновременно». Здесь — половина порта; вторая
// половина живёт в скрипте, которым `Дом.Консоль` спрашивает прототип
// (`Проверки.Дом`), и правила у них одни:
//
//  1. перечисление сворачивается в свою строку;
//  2. ссылка на объект с полем `id` — в ["#", id];
//  3. число с плавающей точкой округляется до шестого знака, −0.0 → 0.0;
//  4. **булево пишется числом 1/0.** Это единственное правило, которого
//     не было в питоновском снимке, и оно нужно потому, что `убежище`
//     держит и уровни (целые), и «есть ли буржуйка» (булево), а в Python
//     `True == 1` и вся арифметика от этого не зависит. Различать их
//     в отпечатке значило бы ловить разницу представления вместо разницы
//     поведения;
//  5. множество сортируется (ordinal) — порядок множества в Python зависит
//     от хэшей и не воспроизводим;
//  6. словарь **сохраняет порядок вставки** и не сортируется. Питоновский
//     снимок здесь сортировал; это правило строже: порядок обхода словаря
//     в домене — часть поведения, и расхождение в нём должно быть видно,
//     а не скрыто сортировкой;
//  7. поля записи сортируются по имени: у Python и C# порядок объявления
//     читается по-разному, и сортировка снимает этот вопрос.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Дом.Ядро;

public static class Снимок
{
    /// <summary>
    /// Что в снимок не входит: вычисляемые свойства и то, что не состояние.
    /// В Python их и не было бы — `dataclasses.fields` возвращает поля,
    /// а не `@property`; в C# и то и другое выглядит одинаково, поэтому
    /// список ведётся руками. Новое вычисляемое свойство — строка сюда,
    /// иначе снимок разойдётся с питоновским на пустом месте.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> НЕ_СОСТОЯНИЕ =
        new(StringComparer.Ordinal)
        {
            ["NPC"] = new(StringComparer.Ordinal) { "shelter", "решающий" },
            ["Flat"] = new(StringComparer.Ordinal) { "id" },
            ["Кладовая"] = new(StringComparer.Ordinal) { "имя", "имя_род" },
            ["Взгляд"] = new(StringComparer.Ordinal) { "сытость", "целость", "теплота" },
        };

    /// <summary>
    /// Дом, собранный из данных: квартиры, кладовые и жильцы поле в поле.
    /// Поля самого <c>House</c> сюда пока не входят — их заполняет не сборка,
    /// а движок дня, и они приедут вместе с ним.
    /// </summary>
    public static JsonObject Собранный(House h)
    {
        var квартиры = new JsonObject();
        foreach (var (apt, f) in h.flats)
            квартиры[apt.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Значение(f);
        var кладовые = new JsonObject();
        foreach (var (id, к) in h.кладовые)
            кладовые[id] = Значение(к);
        var жильцы = new JsonObject();
        foreach (var (id, p) in h.people)
            жильцы[id] = Значение(p);
        return new JsonObject
        {
            ["квартиры"] = квартиры,
            ["кладовые"] = кладовые,
            ["жильцы"] = жильцы,
        };
    }

    /// <summary>Любое значение состояния по правилам снимка.</summary>
    public static JsonNode? Значение(object? v, bool ссылкой = false)
    {
        switch (v)
        {
            case null:
                return null;
            case bool b:
                return JsonValue.Create(b ? 1 : 0);          // правило 4
            case string s:
                return JsonValue.Create(s);
            case double d:
                return JsonValue.Create(Округлить(d));       // правило 3
            case float f:
                return JsonValue.Create(Округлить(f));
            case int or long or short or byte:
                return JsonValue.Create(Convert.ToInt64(v));
            case Enum e:
                return JsonValue.Create(Строкой(e));         // правило 1
        }

        var тип = v.GetType();

        // Словарь<K,V> — порядок вставки (правило 6)
        if (тип.IsGenericType && тип.GetGenericTypeDefinition() == typeof(Словарь<,>))
        {
            var о = new JsonObject();
            foreach (var пара in (IEnumerable)v)
            {
                var т = пара.GetType();
                object? ключ = т.GetProperty("Key")!.GetValue(пара);
                object? знач = т.GetProperty("Value")!.GetValue(пара);
                о[Ключом(ключ)] = Значение(знач, ссылкой: true);
            }
            return о;
        }

        // множество — сортируется (правило 5)
        if (тип.IsGenericType && тип.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            var элементы = new List<object?>();
            foreach (var x in (IEnumerable)v)
                элементы.Add(x);
            var а = new JsonArray();
            foreach (var x in элементы.OrderBy(Ключом, StringComparer.Ordinal))
                а.Add(Значение(x, ссылкой: true));
            return а;
        }

        if (v is IEnumerable список && тип != typeof(string))
        {
            var а = new JsonArray();
            foreach (var x in список)
                а.Add(Значение(x, ссылкой: true));
            return а;
        }

        // ссылка на объект с id — сворачивается (правило 2)
        var поле_id = тип.GetProperty("id", BindingFlags.Public | BindingFlags.Instance);
        if (ссылкой && поле_id is not null && поле_id.PropertyType == typeof(string))
            return new JsonArray("#", (string?)поле_id.GetValue(v));

        // запись состояния: поля по именам, отсортированным ordinal (правило 7)
        var запись = new JsonObject();
        НЕ_СОСТОЯНИЕ.TryGetValue(тип.Name, out var лишние);
        foreach (var п in тип.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(п => п.GetIndexParameters().Length == 0)
                             .Where(п => лишние is null || !лишние.Contains(п.Name))
                             .OrderBy(п => п.Name, StringComparer.Ordinal))
            запись[п.Name] = Значение(п.GetValue(v), ссылкой: true);
        return запись;
    }

    /// <summary>Округление до шестого знака; −0.0 приводится к 0.0.</summary>
    public static double Округлить(double v)
    {
        double r = Math.Round(v, 6);
        return r == 0.0 ? 0.0 : r;
    }

    private static string Строкой(Enum e) => e switch
    {
        Оружие о => о.Текст(),
        ВидКладовой к => к.Текст(),
        Режим р => р.Текст(),
        ВидСобытия с => с.Текст(),
        Вид в => в.Текст(),
        Ночь н => н.Текст(),
        Вопрос в => Вопросы.Текст(в),
        _ => e.ToString(),
    };

    /// <summary>Ключ словаря или элемент множества — строкой, как его пишет
    /// Python: строка как есть, число десятичным, пара — через запятую.</summary>
    private static string Ключом(object? ключ) => ключ switch
    {
        null => "",
        string s => s,
        Enum e => Строкой(e),
        double d => Округлить(d).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ITuple т => string.Join(",", Enumerable.Range(0, т.Length).Select(i => Ключом(т[i]))),
        _ => Convert.ToString(ключ, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };
}
