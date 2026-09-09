// Сравнение двух деревьев JSON: где именно разошлось.
//
// Хэш говорит «не сошлось», а разбирать приходится руками; здесь сразу
// названо поле. Числа сравниваются значением, а не записью: «1.0» Python
// и «1» C# — одно и то же число, и это ещё не расхождение поведения
// (ловушка 3 из `docs/ПОРТ.md`).
using System.Text.Json;

namespace Дом.Консоль;

public static class Сравнение
{
    /// <summary>Округление до шестого знака — то же, что в каноническом снимке.</summary>
    public const int ЗНАКОВ = 6;

    /// <summary>
    /// Сложить в <paramref name="расхождения"/> все места, где деревья
    /// не совпали, назвав путь до каждого. Возвращает true, если совпали.
    /// </summary>
    public static bool Одинаково(JsonElement ждём, JsonElement стало, string путь,
                                 List<string> расхождения, int предел = 25)
    {
        if (расхождения.Count >= предел)
            return false;

        if (ждём.ValueKind != стало.ValueKind
            && !(Число(ждём) && Число(стало)))
        {
            расхождения.Add($"{путь}: в прототипе {Кратко(ждём)}, в порте {Кратко(стало)}");
            return false;
        }

        switch (ждём.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var а = ждём.EnumerateObject().ToList();
                var б = стало.EnumerateObject().ToList();
                if (а.Count != б.Count)
                {
                    расхождения.Add($"{путь}: полей в прототипе {а.Count}, в порте {б.Count}");
                    return false;
                }
                bool всё = true;
                for (int i = 0; i < а.Count; i++)
                {
                    // порядок ключей значим: в прототипе это порядок вставки,
                    // и по нему идут обходы, внутри которых бывает бросок rng
                    if (!string.Equals(а[i].Name, б[i].Name, StringComparison.Ordinal))
                    {
                        расхождения.Add(
                            $"{путь}: ключ {i} — в прототипе «{а[i].Name}», в порте «{б[i].Name}»");
                        всё = false;
                        continue;
                    }
                    всё &= Одинаково(а[i].Value, б[i].Value, $"{путь}.{а[i].Name}",
                                     расхождения, предел);
                }
                return всё;
            }
            case JsonValueKind.Array:
            {
                var а = ждём.EnumerateArray().ToList();
                var б = стало.EnumerateArray().ToList();
                if (а.Count != б.Count)
                {
                    расхождения.Add($"{путь}: длина в прототипе {а.Count}, в порте {б.Count}");
                    return false;
                }
                bool всё = true;
                for (int i = 0; i < а.Count; i++)
                    всё &= Одинаково(а[i], б[i], $"{путь}[{i}]", расхождения, предел);
                return всё;
            }
            case JsonValueKind.String:
                if (!string.Equals(ждём.GetString(), стало.GetString(), StringComparison.Ordinal))
                {
                    расхождения.Add(
                        $"{путь}: в прототипе «{ждём.GetString()}», в порте «{стало.GetString()}»");
                    return false;
                }
                return true;
            case JsonValueKind.Number:
            {
                double а = Math.Round(ждём.GetDouble(), ЗНАКОВ);
                double б = Math.Round(стало.GetDouble(), ЗНАКОВ);
                if (а != б)
                {
                    расхождения.Add($"{путь}: в прототипе {а}, в порте {б}");
                    return false;
                }
                return true;
            }
            default:
                return true;    // true / false / null уже сверены по ValueKind
        }
    }

    private static bool Число(JsonElement e) => e.ValueKind == JsonValueKind.Number;

    private static string Кратко(JsonElement e)
    {
        string s = e.ToString();
        return s.Length > 60 ? s[..60] + "…" : s;
    }
}
