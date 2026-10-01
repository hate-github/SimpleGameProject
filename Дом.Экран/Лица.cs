// Лица соседей в окне разговора (просьба автора 01.10.2026: «при диалоге
// открывалось окно, как на скриншоте, но в стиле игры»).
//
// Картинки — сетка автора `референсы/лица.png` (нейросеть, 4 × 4),
// нарезанная в `Дом.Годот/лица/NN.png`; кто на каком лице — `data/лица.json`,
// номером ячейки. Здесь — чтение этой таблицы без движка: её сверяет консоль
// (раздел «лица»: у каждого жильца лицо есть, номер один на человека, файл
// лежит), а движок по ней грузит картинку (`Дом.Годот.ПортретUI`).
//
// Лицо — не знание: какое у соседа лицо, видно всякому, кто стоит у его
// двери, даже если имени герой ещё не знает.

using System.Text.Json;

namespace Дом.Экран;

public static class Лица
{
    /// <summary>Сколько ячеек в сетке автора: номер лица — от одного до стольки.</summary>
    public const int ЯЧЕЕК = 16;

    /// <summary>Кто на каком лице: id жильца (и «продавщица») → номер ячейки.</summary>
    public static IReadOnlyDictionary<string, int> Прочитать(string каталог)
    {
        var лица = new Dictionary<string, int>(StringComparer.Ordinal);
        string путь = Path.Combine(каталог, "лица.json");
        if (!File.Exists(путь))
            return лица;
        using var поток = File.OpenRead(путь);
        using var док = JsonDocument.Parse(поток);
        foreach (var п in док.RootElement.GetProperty("лица").EnumerateObject())
            лица[п.Name] = п.Value.GetInt32();
        return лица;
    }

    /// <summary>Файл лица в папке `лица` движка: «01.png».</summary>
    public static string Файл(int номер) => $"{номер:00}.png";
}
