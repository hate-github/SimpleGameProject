// Где лежат данные. `data/*.json` не копируются в порт: и Python, и C#
// читают одни и те же файлы из корня прототипа — тогда «байт в байт»
// выполняется само собой, а не проверкой.
namespace Дом.Консоль;

public static class Пути
{
    private static string? _корень;

    /// <summary>Корень прототипа — папка, в которой лежат `data` и `house`.</summary>
    public static string Корень => _корень ??= Найти();

    public static string Данные => Path.Combine(Корень, "data");

    public static string Файл(string имя) => Path.Combine(Данные, имя);

    private static string Найти()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (File.Exists(Path.Combine(d.FullName, "data", "balance.json")))
                return d.FullName;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException(
            "не найден корень прототипа: вверх от " + AppContext.BaseDirectory +
            " нет папки с data/balance.json");
    }
}
