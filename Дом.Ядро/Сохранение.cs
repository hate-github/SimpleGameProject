// Перенос house/сохранение.py: снимок, из которого прогон продолжается
// бит в бит.
//
// Канонический снимок (`Снимок.cs`) сворачивает состояние в строку для
// сравнения — из неё дом не поднять: ссылки уже свёрнуты в id, а лента
// случайности не снимается вовсе, потому что для сравнения она не нужна.
// Здесь наоборот: всё, что нужно, чтобы продолжить с того же места,
// и ничего сверх.
//
// Что не сохраняется и почему: ручки и реплики — это данные, а не состояние,
// они читаются с диска; журнал — текст, он уже напечатан; наблюдатели — те,
// кто смотрит на дом, а не часть дома. Лента случайности сохраняется вся:
// без неё продолженный прогон разойдётся с непрерывным на первом же броске.
//
// Формат — тот же, что у прототипа, тег в тег: `#вид`, `#мн`, `#кортеж`,
// `#словарь`, `#запись`. Не ради переносимости сейвов между языками,
// а ради проверки: два сохранения одного дома сравниваются полем в поле,
// и забытое поле видно сразу.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Дом.Ядро;

public static class Сохранение
{
    public const int ВЕРСИЯ = 1;

    /// <summary>
    /// Поля дома, которые не сохраняются: данные, вывод и наблюдатели. Люди,
    /// квартиры и кладовые идут своими разделами, потому что восстанавливаются
    /// в уже собранные объекты.
    /// </summary>
    private static readonly HashSet<string> НЕ_СОСТОЯНИЕ = new(StringComparer.Ordinal)
    {
        "rng", "B", "journal", "people", "flats", "кладовые", "реплики_быт", "hooks",
    };

    /// <summary>
    /// Кем восстанавливать записи и перечисления: имя — сам тип. Собирается
    /// из сборки, а не пишется списком, чтобы новая запись не забылась.
    /// </summary>
    private static readonly Dictionary<string, Type> СПРАВОЧНИК = Собрать();

    private static Dictionary<string, Type> Собрать()
    {
        var д = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var т in typeof(House).Assembly.GetTypes())
            if (т.IsPublic && (т.IsEnum || Записью(т)))
                д[т.Name] = т;
        return д;
    }

    /// <summary>
    /// Запись модели — то, что в прототипе `dataclass`: класс с одними
    /// свойствами и без своей логики восстановления. Дом, жилец, квартира
    /// и кладовая записями не считаются: они восстанавливаются в уже
    /// собранные объекты, своим разделом.
    /// </summary>
    private static bool Записью(Type т)
        => т.IsClass && т.Namespace == "Дом.Ядро" && !т.IsAbstract && !т.IsGenericType
           && т != typeof(House) && т != typeof(NPC) && т != typeof(Flat)
           && т != typeof(Кладовая)
           && (т.GetConstructor(Type.EmptyTypes) is not null || Позиционная(т) is not null);

    /// <summary>Конструктор записи-рекорда: у неё нет пустого, зато есть
    /// один, где имена параметров совпадают с именами свойств.</summary>
    private static ConstructorInfo? Позиционная(Type т)
    {
        foreach (var к in т.GetConstructors())
        {
            var пар = к.GetParameters();
            if (пар.Length == 0 || (пар.Length == 1 && пар[0].ParameterType == т))
                continue;
            if (пар.All(п => т.GetProperty(п.Name!) is not null))
                return к;
        }
        return null;
    }

    // ---------------------------------------------------------------- туда

    /// <summary>Значение любого поля дома — в то, что переживёт запись в JSON.
    ///
    /// Множества сортируются: их порядок зависит от того, как легли хэши,
    /// и без сортировки один и тот же дом сохранялся бы каждый раз по-разному.
    /// </summary>
    public static JsonNode? _в_json(object? x)
    {
        switch (x)
        {
            case null:
                return null;
            case bool b:
                return JsonValue.Create(b);
            case string s:
                return JsonValue.Create(s);
            case Enum e:
                return new JsonObject
                {
                    ["#вид"] = e.GetType().Name,
                    ["знач"] = Снимок.Строкой(e),
                };
            case int or long or short or byte:
                return JsonValue.Create(Convert.ToInt64(x));
            case double or float:
                return JsonValue.Create(Convert.ToDouble(x));
        }

        var тип = x.GetType();

        // множество — списком, отсортированным как в прототипе (по repr)
        if (тип.IsGenericType && тип.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            var элементы = ((IEnumerable)x).Cast<object>().ToList();
            var а = new JsonArray();
            foreach (var v in элементы.OrderBy(Снимок.Ключом, StringComparer.Ordinal))
                а.Add(_в_json(v));
            return new JsonObject { ["#мн"] = а };
        }

        // словарь списком пар: ключом бывает и число, и кортеж, а JSON знает
        // только строки
        if (тип.IsGenericType && тип.GetGenericTypeDefinition() == typeof(Словарь<,>))
        {
            var пары = new JsonArray();
            foreach (var пара in (IEnumerable)x)
            {
                var т = пара.GetType();
                пары.Add(new JsonArray(
                    _в_json(т.GetProperty("Key")!.GetValue(пара)),
                    _в_json(т.GetProperty("Value")!.GetValue(пара))));
            }
            return new JsonObject { ["#словарь"] = пары };
        }

        if (x is ITuple кортеж)
        {
            var а = new JsonArray();
            for (int i = 0; i < кортеж.Length; i++)
                а.Add(_в_json(кортеж[i]));
            return new JsonObject { ["#кортеж"] = а };
        }

        if (x is IEnumerable список)
        {
            var а = new JsonArray();
            foreach (var v in список)
                а.Add(_в_json(v));
            return а;
        }

        // ценности в прототипе — не запись, а кусок `npcs.json` как есть:
        // словарь с описанием и двумя списками. Порт разобрал его в запись —
        // типизировать лучше, — но в сейве он пишется словарём, как там,
        // и в том же порядке ключей
        if (x is Ценности ц)
            return new JsonObject
            {
                ["#словарь"] = new JsonArray(
                    new JsonArray(JsonValue.Create("описание"), JsonValue.Create(ц.описание)),
                    new JsonArray(JsonValue.Create("ценит"), _в_json(ц.ценит)),
                    new JsonArray(JsonValue.Create("не_терпит"), _в_json(ц.не_терпит))),
            };

        if (Записью(тип))
            return new JsonObject
            {
                ["#запись"] = тип.Name,
                ["поля"] = Поля(x, тип),
            };

        throw new InvalidOperationException($"нечем сохранить {тип.Name}: {x}");
    }

    /// <summary>Все свойства объекта, кроме названных.</summary>
    private static JsonObject Поля(object объект, Type тип,
                                   IReadOnlySet<string>? кроме = null)
    {
        var о = new JsonObject();
        foreach (var св in Свойства(тип))
        {
            if (кроме is not null && кроме.Contains(св.Name))
                continue;
            о[св.Name] = _в_json(св.GetValue(объект));
        }
        return о;
    }

    /// <summary>Свойства, которые вообще есть смысл сохранять: с чтением
    /// и либо с записью, либо ссылкой на изменяемое.</summary>
    private static IEnumerable<PropertyInfo> Свойства(Type тип)
        => тип.GetProperties(BindingFlags.Public | BindingFlags.Instance)
              .Where(св => св.CanRead && св.GetIndexParameters().Length == 0
                           && (св.CanWrite || Изменяемое(св.PropertyType)));

    private static bool Изменяемое(Type т)
        => t_словарь(т) || (т.IsGenericType
                            && (т.GetGenericTypeDefinition() == typeof(HashSet<>)
                                || т.GetGenericTypeDefinition() == typeof(List<>)));

    private static bool t_словарь(Type т)
        => т.IsGenericType && т.GetGenericTypeDefinition() == typeof(Словарь<,>);

    /// <summary>Как зовут того, кто за него решает.</summary>
    public static string имя_решающего(NPC p) => p.решающий switch
    {
        Скрипт => Решающие.СКРИПТ,
        Человек => Решающие.ЧЕЛОВЕК,
        _ => Решающие.СОФТМАКС,
    };

    /// <summary>Весь дом объектом, годным для записи в JSON.</summary>
    public static JsonObject сохранить(House h)
    {
        var люди = new JsonObject();
        foreach (string pid in h.people.Ключи)
        {
            var p = h.people.Взять(pid, null!);
            var поля = Поля(p, typeof(NPC), НЕ_У_ЖИЛЬЦА);
            поля["решающий"] = имя_решающего(p);
            люди[pid] = поля;
        }
        var квартиры = new JsonObject();
        foreach (int apt in h.flats.Ключи)
            квартиры[apt.ToString(System.Globalization.CultureInfo.InvariantCulture)]
                = Поля(h.flats.Взять(apt, null!), typeof(Flat));
        var кладовые = new JsonObject();
        foreach (string kid in h.кладовые.Ключи)
            кладовые[kid] = Поля(h.кладовые.Взять(kid, null!), typeof(Кладовая));
        return new JsonObject
        {
            ["версия"] = ВЕРСИЯ,
            ["день"] = h.day,
            // лента случайности: зерно и то, где она сейчас стоит. Без второго
            // продолженный прогон пойдёт другой жизнью с первого же броска
            ["случайность"] = new JsonObject
            {
                ["зерно"] = h.rng.Зерно,
                ["состояние"] = h.rng.Состояние,
            },
            ["люди"] = люди,
            ["квартиры"] = квартиры,
            ["кладовые"] = кладовые,
            ["мир"] = Поля(h, typeof(House), НЕ_СОСТОЯНИЕ),
        };
    }

    /// <summary>
    /// Что у жильца не его состояние.
    ///
    /// `решающий` — не поле, а имя: кто за него решает, пишется отдельно.
    /// `shelter` — вовсе не его: это окно в квартиру, где он сейчас живёт,
    /// и сохранять его значило бы записать чужие стены дважды, а при загрузке
    /// переписать ими квартиру хозяина. В прототипе это свойство, и в `vars`
    /// оно не попадает; в порте — свойство только для чтения, и его пришлось
    /// назвать вслух.
    ///
    /// Что список полон, стережёт не глаз, а сверка: набор полей сейва
    /// сравнивается с питоновским, и лишнее поле краснеет сразу.
    /// </summary>
    private static readonly HashSet<string> НЕ_У_ЖИЛЬЦА = new(StringComparer.Ordinal)
    {
        "решающий", "shelter",
    };

    // ---------------------------------------------------------------- обратно

    /// <summary>Обратно. Записи и перечисления поднимаются по имени типа.</summary>
    public static object? _из_json(JsonNode? x, Type ждём)
    {
        if (x is null)
            return null;
        if (x is JsonArray массив)
        {
            var элемент = ждём.IsGenericType ? ждём.GetGenericArguments()[0] : typeof(object);
            var список = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(элемент))!;
            foreach (var v in массив)
                список.Add(_из_json(v, элемент));
            return список;
        }
        if (x is JsonObject о)
        {
            if (о.ContainsKey("#вид"))
                return Слова.Значением(СПРАВОЧНИК[о["#вид"]!.GetValue<string>()],
                                       о["знач"]!.GetValue<string>());
            if (о.ContainsKey("#мн"))
            {
                var элемент = ждём.GetGenericArguments()[0];
                var мн = Создать(ждём);
                var добавить = ждём.GetMethod("Add")!;
                foreach (var v in о["#мн"]!.AsArray())
                    добавить.Invoke(мн, new[] { _из_json(v, элемент) });
                return мн;
            }
            if (о.ContainsKey("#кортеж"))
            {
                var части = ждём.GetGenericArguments();
                var значения = о["#кортеж"]!.AsArray()
                    .Select((v, i) => _из_json(v, части[i])).ToArray();
                return Activator.CreateInstance(ждём, значения);
            }
            if (о.ContainsKey("#словарь") && ждём == typeof(Ценности))
            {
                var ц = new Ценности();
                foreach (var пара in о["#словарь"]!.AsArray())
                {
                    var а = пара!.AsArray();
                    string к = а[0]!.GetValue<string>();
                    if (string.Equals(к, "описание", StringComparison.Ordinal))
                        ц.описание = а[1]!.GetValue<string>();
                    else if (string.Equals(к, "ценит", StringComparison.Ordinal))
                        ц.ценит.AddRange(а[1]!.AsArray().Select(v => v!.GetValue<string>()));
                    else
                        ц.не_терпит.AddRange(а[1]!.AsArray().Select(v => v!.GetValue<string>()));
                }
                return ц;
            }
            if (о.ContainsKey("#словарь"))
            {
                var ключ_т = ждём.GetGenericArguments()[0];
                var знач_т = ждём.GetGenericArguments()[1];
                var сл = Создать(ждём);
                var уложить = ждём.GetProperty("Item")!;
                foreach (var пара in о["#словарь"]!.AsArray())
                {
                    var а = пара!.AsArray();
                    уложить.SetValue(сл, _из_json(а[1], знач_т),
                                     new[] { _из_json(а[0], ключ_т) });
                }
                return сл;
            }
            if (о.ContainsKey("#запись"))
            {
                var тип = СПРАВОЧНИК[о["#запись"]!.GetValue<string>()];
                var поля = о["поля"]!.AsObject();
                var пустой = тип.GetConstructor(Type.EmptyTypes);
                if (пустой is not null)
                {
                    var объект = пустой.Invoke(null)!;
                    Заполнить(объект, тип, поля);
                    return объект;
                }
                // запись-рекорд: собирается разом, поимённо. То же, что делает
                // прототип, когда конструктор не принимает часть полей
                var к = Позиционная(тип)!;
                var значения = к.GetParameters()
                    .Select(п => _из_json(поля[п.Name!], п.ParameterType)).ToArray();
                return к.Invoke(значения);
            }
            throw new InvalidOperationException("непонятная запись в снимке");
        }
        return Простое(x.AsValue(), ждём);
    }

    /// <summary>Число, строка или флаг — с приведением к тому типу,
    /// который ждёт поле.</summary>
    private static object? Простое(JsonValue v, Type ждём)
    {
        var цель = Nullable.GetUnderlyingType(ждём) ?? ждём;
        // поле, объявленное как «что угодно»: подброшенное под дверь — словарь
        // со строками и числом, как в прототипе. Возвращаем то, чем оно
        // и записано
        if (цель == typeof(object))
        {
            if (v.TryGetValue<string>(out var стр)) return стр;
            if (v.TryGetValue<bool>(out bool фл))   return фл;
            if (v.TryGetValue<long>(out long ц))    return ц;
            return v.GetValue<double>();
        }
        if (цель == typeof(string))
            return v.GetValue<string>();
        if (цель == typeof(bool))
            return v.TryGetValue<bool>(out bool b) ? b : v.GetValue<long>() != 0;
        if (цель == typeof(double) || цель == typeof(float))
            return v.TryGetValue<double>(out double d) ? d : (double)v.GetValue<long>();
        if (цель == typeof(int))
            return (int)v.GetValue<long>();
        if (цель == typeof(long))
            return v.GetValue<long>();
        if (цель == typeof(ulong))
            return v.TryGetValue<ulong>(out ulong u) ? u : (ulong)v.GetValue<long>();
        if (цель.IsEnum)
            return Слова.Значением(цель, v.GetValue<string>());
        return v.GetValue<string>();
    }

    /// <summary>Переписать поля объекта из снимка. Изменяемое (словарь,
    /// множество, список) наполняется на месте: ссылки на него ниоткуда
    /// не рвутся.</summary>
    private static void Заполнить(object объект, Type тип, JsonObject поля,
                                  IReadOnlySet<string>? кроме = null)
    {
        var по_имени = Свойства(тип).ToDictionary(св => св.Name, StringComparer.Ordinal);
        foreach (var (имя, знач) in поля)
        {
            if (кроме is not null && кроме.Contains(имя))
                continue;
            if (!по_имени.TryGetValue(имя, out var св))
                continue;
            if (св.CanWrite)
            {
                св.SetValue(объект, _из_json(знач, св.PropertyType));
                continue;
            }
            // только для чтения — значит, наполняем то, что уже стоит,
            // а не собираем новое: у словаря есть своё сравнение ключей,
            // и пересобранный потерял бы его
            Наполнить(св.GetValue(объект)!, знач);
        }
    }

    /// <summary>
    /// Завести пустой контейнер. `Activator` не годится: у словаря
    /// конструктор с одним необязательным параметром — сравнением ключей, —
    /// и пустого у него нет вовсе. Сравнение по умолчанию для строки
    /// и так порядковое, так что поведение то же.
    /// </summary>
    private static object Создать(Type т)
    {
        var пустой = т.GetConstructor(Type.EmptyTypes);
        if (пустой is not null)
            return пустой.Invoke(null);
        var к = т.GetConstructors()
                 .First(x => x.GetParameters().All(п => п.HasDefaultValue));
        return к.Invoke(к.GetParameters().Select(п => п.DefaultValue).ToArray());
    }

    /// <summary>
    /// Опустошить и наполнить заново то, что нельзя присвоить: словарь,
    /// множество, список. Читается прямо из снимка, а не через промежуточный
    /// объект: словарь надо наполнить тот самый, со своим сравнением ключей.
    /// </summary>
    private static void Наполнить(object куда, JsonNode? данные)
    {
        var т = куда.GetType();
        т.GetMethod("Clear", Type.EmptyTypes)?.Invoke(куда, null);
        т.GetMethod("Очистить", Type.EmptyTypes)?.Invoke(куда, null);
        if (данные is null)
            return;
        if (t_словарь(т))
        {
            var ключ_т = т.GetGenericArguments()[0];
            var знач_т = т.GetGenericArguments()[1];
            var уложить = т.GetProperty("Item")!;
            foreach (var пара in данные["#словарь"]!.AsArray())
            {
                var а = пара!.AsArray();
                уложить.SetValue(куда, _из_json(а[1], знач_т),
                                 new[] { _из_json(а[0], ключ_т) });
            }
            return;
        }
        var элемент = т.GetGenericArguments()[0];
        var добавить = т.GetMethod("Add", new[] { элемент })!;
        var список = данные is JsonObject о && о.ContainsKey("#мн")
                     ? о["#мн"]!.AsArray() : данные.AsArray();
        foreach (var v in список)
            добавить.Invoke(куда, new[] { _из_json(v, элемент) });
    }

    /// <summary>
    /// Заполнить уже собранный дом состоянием из снимка.
    ///
    /// Дом берётся собранным (прогон строит его из тех же данных), а поля
    /// переписываются: людей, квартир и кладовых в прогоне не прибавляется
    /// и не убывает — умерший остаётся в списке, — поэтому пересобирать
    /// объекты незачем, и ссылки на них ниоткуда не рвутся.
    /// </summary>
    public static House загрузить(JsonObject данные, House h)
    {
        if (данные["версия"]?.GetValue<int>() != ВЕРСИЯ)
            throw new InvalidOperationException(
                $"снимок версии {данные["версия"]}, а читаем {ВЕРСИЯ}");
        h.rng.Зерно = данные["случайность"]!["зерно"]!.GetValue<long>();
        h.rng.Состояние = данные["случайность"]!["состояние"]!.GetValue<ulong>();
        foreach (var (pid, поля) in данные["люди"]!.AsObject())
        {
            var p = h.people.Взять(pid, null!);
            var о = поля!.AsObject();
            Заполнить(p, typeof(NPC), о, НЕ_У_ЖИЛЬЦА);
            p.решающий = Решающие.Создать(о["решающий"]!.GetValue<string>());
        }
        foreach (var (apt, поля) in данные["квартиры"]!.AsObject())
            Заполнить(h.flats.Взять(
                int.Parse(apt, System.Globalization.CultureInfo.InvariantCulture), null!),
                typeof(Flat), поля!.AsObject());
        foreach (var (kid, поля) in данные["кладовые"]!.AsObject())
            Заполнить(h.кладовые.Взять(kid, null!), typeof(Кладовая), поля!.AsObject());
        Заполнить(h, typeof(House), данные["мир"]!.AsObject(), НЕ_СОСТОЯНИЕ);
        return h;
    }
}
