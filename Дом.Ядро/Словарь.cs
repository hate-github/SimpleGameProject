// Словарь с порядком вставки — то, чем в прототипе является обычный `dict`.
//
// В Python порядок вставки гарантирован языком, и 132 места в домене обходят
// `.values()` / `.items()`. Внутри такого обхода бывает бросок `rng` и запись
// в журнал, а значит порядок обхода — часть поведения, а не подробность
// реализации. `Dictionary<K,V>` в C# порядка не обещает и меняет его
// от удалений, поэтому домен пользуется этим типом, а не им.
//
// Это вторая из четырёх ловушек `docs/ПОРТ.md`. Первая (сортировка русских
// строк) закрыта тем, что всякое сравнение строк в ядре — `Ordinal`.
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Дом.Ядро;

/// <summary>
/// Отображение с порядком вставки: обход идёт в том порядке, в каком ключи
/// добавлялись. Переприсваивание значения позицию не меняет, удаление
/// её освобождает, повторная вставка ставит ключ в конец — ровно как `dict`.
/// </summary>
public sealed class Словарь<K, V> : IEnumerable<KeyValuePair<K, V>> where K : notnull
{
    private readonly Dictionary<K, V> _значения;
    private readonly List<K> _порядок;

    public Словарь(IEqualityComparer<K>? сравнение = null)
    {
        _значения = new Dictionary<K, V>(сравнение);
        _порядок = new List<K>();
    }

    public Словарь(Словарь<K, V> откуда) : this(откуда._значения.Comparer)
    {
        foreach (var (k, v) in откуда)
            this[k] = v;
    }

    public int Count => _порядок.Count;

    public V this[K ключ]
    {
        get => _значения[ключ];
        set
        {
            if (!_значения.ContainsKey(ключ))
                _порядок.Add(ключ);
            _значения[ключ] = value;
        }
    }

    /// <summary>`d.get(ключ, умолчание)` — умолчание здесь часто значимо.</summary>
    public V Взять(K ключ, V умолчание)
        => _значения.TryGetValue(ключ, out var v) ? v : умолчание;

    public bool Есть(K ключ) => _значения.ContainsKey(ключ);

    public bool TryGetValue(K ключ, [MaybeNullWhen(false)] out V значение)
        => _значения.TryGetValue(ключ, out значение);

    public bool Убрать(K ключ)
    {
        if (!_значения.Remove(ключ))
            return false;
        _порядок.Remove(ключ);
        return true;
    }

    public void Очистить()
    {
        _значения.Clear();
        _порядок.Clear();
    }

    /// <summary>Ключи в порядке вставки. Копия: обход переживает правку.</summary>
    public List<K> Ключи => new(_порядок);

    /// <summary>Значения в порядке вставки.</summary>
    public List<V> Значения
    {
        get
        {
            var v = new List<V>(_порядок.Count);
            foreach (var k in _порядок)
                v.Add(_значения[k]);
            return v;
        }
    }

    public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
    {
        foreach (var k in _порядок)
            yield return new KeyValuePair<K, V>(k, _значения[k]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Короткие имена для самых частых видов словарей домена.</summary>
public static class Словари
{
    /// <summary>Ключ — строка (ресурс, id жильца): сравнение всегда ordinal.</summary>
    public static Словарь<string, T> Строкой<T>() => new(StringComparer.Ordinal);

    /// <summary>Шкаф, оценки чужого шкафа, счётчики: строка → число.</summary>
    public static Словарь<string, double> Числа() => Строкой<double>();
}
