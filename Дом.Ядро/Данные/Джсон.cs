// Мелкие обращения к JSON — те же, что `d.get(ключ, умолчание)` в Python.
//
// Данные читаются как есть, а не разбираются в типизированные записи:
// перенос сверяется с прототипом построчно, и чем ближе обращение к данным
// к питоновскому, тем меньше мест, где можно молча разойтись. Типы появятся
// после того, как эталон сойдётся.
using System.Text.Json;

namespace Дом.Ядро;

public static class Джсон
{
    public static bool Есть(this JsonElement e, string ключ)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ключ, out _);

    /// <summary>Поле-объект или «пусто» — как `d.get(ключ) or {}`.</summary>
    public static JsonElement Объект(this JsonElement e, string ключ)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ключ, out var v)
           && v.ValueKind == JsonValueKind.Object ? v : Пусто;

    /// <summary>Поле-массив или пустой — как `d.get(ключ) or []`.</summary>
    public static JsonElement Массив(this JsonElement e, string ключ)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ключ, out var v)
           && v.ValueKind == JsonValueKind.Array ? v : ПустойМассив;

    public static string Строка(this JsonElement e, string ключ, string умолчание = "")
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ключ, out var v)
           && v.ValueKind == JsonValueKind.String ? v.GetString()! : умолчание;

    public static double Число(this JsonElement e, string ключ, double умолчание = 0.0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ключ, out var v)
           && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : умолчание;

    public static int Целое(this JsonElement e, string ключ, int умолчание = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ключ, out var v)
           && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : умолчание;

    public static bool Флаг(this JsonElement e, string ключ, bool умолчание = false)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(ключ, out var v))
            return умолчание;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.GetDouble() != 0.0,
            _ => умолчание,
        };
    }

    /// <summary>Список строк из поля-массива.</summary>
    public static List<string> Строки(this JsonElement e, string ключ)
    {
        var с = new List<string>();
        foreach (var x in e.Массив(ключ).EnumerateArray())
            if (x.ValueKind == JsonValueKind.String)
                с.Add(x.GetString()!);
        return с;
    }

    /// <summary>Словарь «строка → число» из поля-объекта, в порядке файла.</summary>
    public static Словарь<string, double> Числа(this JsonElement e, string ключ)
    {
        var с = Словари.Числа();
        foreach (var п in e.Объект(ключ).EnumerateObject())
            if (п.Value.ValueKind == JsonValueKind.Number)
                с[п.Name] = п.Value.GetDouble();
        return с;
    }

    /// <summary>Словарь «строка → строка» из поля-объекта, в порядке файла.</summary>
    public static Словарь<string, string> Строкой(this JsonElement e, string ключ)
    {
        var с = Словари.Строкой<string>();
        foreach (var п in e.Объект(ключ).EnumerateObject())
            if (п.Value.ValueKind == JsonValueKind.String)
                с[п.Name] = п.Value.GetString()!;
        return с;
    }

    private static readonly JsonElement Пусто = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly JsonElement ПустойМассив = JsonDocument.Parse("[]").RootElement.Clone();
}
