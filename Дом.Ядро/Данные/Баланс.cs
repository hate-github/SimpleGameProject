// 987 ручек баланса. Ни одно число поведения в коде не сидит — это правило
// прототипа, и оно переезжает вместе с ним: `data/balance.json` остаётся
// источником настройки и в движке, а не превращается в `.tres`.
// Двусторонняя проверка («нет мёртвых ручек, код не читает несуществующих»)
// остаётся на стороне Python, пока порт не догонит.
//
// Значения бывают четырёх видов: число (891 ключ), строка (79), таблица
// «строка → число» (15) и таблица таблиц (2: `веса_черт`, `пунктики`).

namespace Дом.Ядро;

public sealed class Баланс
{
    private readonly Словарь<string, double> _числа = Словари.Числа();
    private readonly Словарь<string, string> _строки = Словари.Строкой<string>();
    private readonly Словарь<string, Словарь<string, double>> _таблицы =
        Словари.Строкой<Словарь<string, double>>();
    private readonly Словарь<string, Словарь<string, Словарь<string, double>>> _таблицы2 =
        Словари.Строкой<Словарь<string, Словарь<string, double>>>();

    public void Число(string ключ, double значение) => _числа[ключ] = значение;
    public void Строку(string ключ, string значение) => _строки[ключ] = значение;
    public void Таблицу(string ключ, Словарь<string, double> значение) => _таблицы[ключ] = значение;
    public void Таблицу2(string ключ, Словарь<string, Словарь<string, double>> значение)
        => _таблицы2[ключ] = значение;

    /// <summary>Ручка-число. Нет такой — исключение: в Python это `KeyError`,
    /// и молчать здесь нельзя ровно по той же причине.</summary>
    public double this[string ключ]
        => _числа.TryGetValue(ключ, out var v) ? v : throw Нет(ключ);

    public string Строка(string ключ)
        => _строки.TryGetValue(ключ, out var v) ? v : throw Нет(ключ);

    public Словарь<string, double> Таблица(string ключ)
        => _таблицы.TryGetValue(ключ, out var v) ? v : throw Нет(ключ);

    public Словарь<string, Словарь<string, double>> Таблица2(string ключ)
        => _таблицы2.TryGetValue(ключ, out var v) ? v : throw Нет(ключ);

    public bool Есть(string ключ)
        => _числа.Есть(ключ) || _строки.Есть(ключ) || _таблицы.Есть(ключ) || _таблицы2.Есть(ключ);

    /// <summary>Все ключи в порядке файла — для проверки ручек и для отчёта.</summary>
    public IEnumerable<string> Ключи
    {
        get
        {
            foreach (var k in _числа.Ключи) yield return k;
            foreach (var k in _строки.Ключи) yield return k;
            foreach (var k in _таблицы.Ключи) yield return k;
            foreach (var k in _таблицы2.Ключи) yield return k;
        }
    }

    private static KeyNotFoundException Нет(string ключ)
        => new($"в balance.json нет ручки «{ключ}»");
}
