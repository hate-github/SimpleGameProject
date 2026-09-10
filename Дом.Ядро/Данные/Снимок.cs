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
using System.Text;
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
            ["NPC"] = new(StringComparer.Ordinal)
            {
                // `умения` и `карман` — то же, что `решающий`: приставлено
                // к жильцу снаружи и в мире не существует. У соседей их нет
                // вовсе, и прогон без игрока даёт ровно тот же снимок,
                // что и раньше
                "shelter", "решающий", "умения", "карман",
            },
            ["Flat"] = new(StringComparer.Ordinal) { "id" },
            ["Кладовая"] = new(StringComparer.Ordinal) { "имя", "имя_род" },
            ["Взгляд"] = new(StringComparer.Ordinal) { "сытость", "целость", "теплота" },
            // тот же список, что в docs/ПОРТ.md: генератор, ручки, вывод,
            // зрители и три больших словаря, которые печатаются отдельно
            ["House"] = new(StringComparer.Ordinal)
            {
                "rng", "B", "journal", "people", "flats", "кладовые", "реплики_быт", "hooks",
            },
        };

    /// <summary>
    /// Дом целиком: квартиры, кладовые, жильцы и поля самого <c>House</c> —
    /// всё поле в поле. Что из <c>House</c> не входит, перечислено
    /// в <see cref="НЕ_СОСТОЯНИЕ"/> и совпадает со списком из docs/ПОРТ.md.
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
            ["дом"] = Значение(h),
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

        // кортеж — массивом: в Python это tuple, и печатается он списком
        if (v is ITuple кортеж)
        {
            var а = new JsonArray();
            for (int i = 0; i < кортеж.Length; i++)
                а.Add(Значение(кортеж[i], ссылкой: true));
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

    /// <summary>
    /// −0.0 приводится к 0.0; больше ничего.
    ///
    /// Раньше здесь стояло округление до шестого знака — и оно само создавало
    /// расхождения. Оценка `1.5513325` лежит ровно на половине, и Python
    /// с C# решают её в разные стороны при бит в бит одинаковом числе: три
    /// варианта корзины разошлись именно так. Округление переехало
    /// в сравнение (`Сравнение.Одинаково`), где половина не решается вовсе —
    /// числа сходятся с допуском.
    /// </summary>
    public static double Округлить(double v) => v == 0.0 ? 0.0 : v;

    internal static string Строкой(Enum e) => e switch
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
    internal static string Ключом(object? ключ) => ключ switch
    {
        null => "",
        string s => s,
        Enum e => Строкой(e),
        double d => Округлить(d).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ITuple т => string.Join(",", Enumerable.Range(0, т.Length).Select(i => Ключом(т[i]))),
        _ => Convert.ToString(ключ, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>
    /// Восьмое правило: как снимок превращается в одну строку.
    ///
    /// Нужно затем, что тридцать дней на двадцати четырёх зёрнах снимками
    /// через оракул не пролезут — по дням идут отпечатки. А чтобы равенство
    /// отпечатков значило равенство состояния, текст должен быть один
    /// на оба языка, и `ToJsonString` тут не годится: .NET печатает
    /// целое 1.0 как «1», Python — как «1.0», и разделители у них разные.
    ///
    /// Правила: разделители без пробелов; порядок ключей — как в снимке;
    /// **всякое** число печатается как число с точкой (кратчайшая запись,
    /// что round-trip). Целое и дробное этим склеиваются нарочно: сверка
    /// чисел и так идёт по значению, а не по записи (ловушка 3).
    /// </summary>
    public static string Текстом(JsonNode? узел)
    {
        var б = new StringBuilder();
        Пиши(узел, б);
        return б.ToString();
    }

    private static void Пиши(JsonNode? узел, StringBuilder б)
    {
        switch (узел)
        {
            case null:
                б.Append("null");
                return;
            case JsonObject о:
            {
                б.Append('{');
                bool первый = true;
                foreach (var (ключ, знач) in о)
                {
                    if (!первый)
                        б.Append(',');
                    первый = false;
                    Строка(ключ, б);
                    б.Append(':');
                    Пиши(знач, б);
                }
                б.Append('}');
                return;
            }
            case JsonArray а:
            {
                б.Append('[');
                for (int i = 0; i < а.Count; i++)
                {
                    if (i > 0)
                        б.Append(',');
                    Пиши(а[i], б);
                }
                б.Append(']');
                return;
            }
        }
        var значение = узел.AsValue();
        if (значение.TryGetValue<string>(out var s))
        {
            Строка(s, б);
            return;
        }
        if (значение.TryGetValue<bool>(out var флаг))
        {
            б.Append(флаг ? "true" : "false");
            return;
        }
        // всё числовое — одним видом. Кратчайшая запись, что читается обратно
        // в то же число, и всегда с точкой: так печатает `repr` в Python.
        // Целое кладут в узел то как long, то как int, дробное как double —
        // спрашиваем по очереди, а не гадаем
        double число;
        if (значение.TryGetValue<long>(out long ц))
            число = ц;
        else if (значение.TryGetValue<int>(out int ц32))
            число = ц32;
        else
            число = значение.GetValue<double>();
        б.Append(Текст.Repr(число).ToLowerInvariant());
    }

    /// <summary>Строка так, как её пишет `json.dumps(..., ensure_ascii=False)`:
    /// экранируются только кавычка, слеш и управляющие.</summary>
    private static void Строка(string s, StringBuilder б)
    {
        б.Append('"');
        foreach (char c in s)
            switch (c)
            {
                case '"':  б.Append("\\\""); break;
                case '\\': б.Append("\\\\"); break;
                case '\n': б.Append("\\n"); break;
                case '\r': б.Append("\\r"); break;
                case '\t': б.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        б.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    else
                        б.Append(c);
                    break;
            }
        б.Append('"');
    }
}
