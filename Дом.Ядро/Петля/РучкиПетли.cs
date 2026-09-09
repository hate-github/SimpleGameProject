// Ручки петли — из `data/петля.json`.
//
// Свой файл, а не `balance.json`, по двум причинам. Первая: `balance.json`
// сверяется с `house/*.py` в обе стороны, и ручка, которую не читает
// ни один питоновский файл, там же и покраснеет — а петли в прототипе нет
// и не будет. Вторая: петля появилась после того, как порт сошёлся,
// и трогать данные, по которым считается эталон, нельзя.
//
// Свой файл — своя двусторонняя сверка: раздел «петля» в проверке красит
// и мёртвую ручку, и чтение отсутствующей. Ручки читаются через
// индексатор со строковым литералом (`ручки["смерть_своя"]`) именно
// затем — чтобы их можно было найти в тексте, как это делает
// `check.проверить_ручки` в прототипе.

using System.Text.Json;

namespace Дом.Ядро;

public sealed class РучкиПетли
{
    private readonly Словарь<string, double> _числа = Словари.Числа();

    private РучкиПетли()
    {
    }

    /// <summary>Ручка. Нет такой — исключение, а не ноль: молчаливое
    /// умолчание в балансе прячет опечатку до первого замера.</summary>
    public double this[string ключ]
        => _числа.TryGetValue(ключ, out var v)
           ? v
           : throw new KeyNotFoundException($"в петля.json нет ручки «{ключ}»");

    /// <summary>Все ключи в порядке файла — для сверки на мёртвые ручки.
    /// Пояснения (ключи с «_» в начале) не в счёт: это проза.</summary>
    public IEnumerable<string> Ключи => _числа.Ключи;

    public bool Есть(string ключ) => _числа.Есть(ключ);

    public static РучкиПетли Прочитать(string каталог)
    {
        var р = new РучкиПетли();
        using var поток = File.OpenRead(Path.Combine(каталог, "петля.json"));
        using var док = JsonDocument.Parse(поток);
        foreach (var п in док.RootElement.EnumerateObject())
        {
            if (п.Name.StartsWith('_'))
                continue;                       // пояснение, а не ручка
            if (п.Value.ValueKind != JsonValueKind.Number)
                throw new InvalidDataException(
                    $"петля.json: ручка «{п.Name}» не число");
            р._числа[п.Name] = п.Value.GetDouble();
        }
        return р;
    }
}
