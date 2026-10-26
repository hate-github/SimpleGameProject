// Где движок берёт данные.
//
// Своё, а не консольное: `Дом.Консоль.Пути` живут в консоли, и тянуть их
// сюда значило бы ссылку вбок между двумя приложениями. Каждое приложение
// само знает, где лежит его установка, — ядру это по-прежнему неизвестно
// (`Прогон` берёт каталог первым аргументом, и не случайно).
//
// Консоль ищет `data/balance.json` вверх от своего двоичного файла. Движку
// так нельзя: у запущенной игры рабочая папка — это папка Godot, а не папка
// проекта. Поэтому из редактора отсчёт идёт от `res://` — того единственного
// места, которое движок знает про себя точно.
//
// У собранной игры `res://` — это .pck, и путь к нему движок отдаёт пустым.
// Первая сборка этого не знала: `data` искалась от рабочей папки, игра
// заводилась двойным щелчком (рабочая папка — её собственная) и не заводилась
// с ярлыка. А проверочный запуск шёл из `prototype/`, где `data` тоже лежит, —
// и находил не ту папку, молча. Поэтому у собранной игры отсчёт — от exe.
//
// Данные лежат рядом с прототипом (`../data`), потому что Python и C#
// читают одни и те же файлы: так «байт в байт» выполняется само собой,
// а не проверкой. Собранная игра носит копию рядом с exe (`собрать.ps1`):
// внутрь `res://` их не кладут — `System.IO` из .pck не читает.
//
// **Песочница проб** (27.10.2026). Игру умеет водить MCP-сервер (Godot
// Runtime MCP): он вписывает в `project.godot` свой мост `McpBridge`
// и запускает игру сам. Такая игра не должна коснуться сохранений автора
// ни при каких настройках. Обычно это решено снаружи: сервер запущен
// с APPDATA, уведённым в `Опять здесь/песочница`, и `user://` смотрит туда.
// А если APPDATA не дошёл и `user://` оказался настоящей папкой автора,
// файлы игры уходят во временную песочницу. Отличить «водит MCP» можно
// только по мосту в настройках проекта: у игры автора его нет никогда,
// а забытый мост краснит консоль (раздел «проект движка»).

using Godot;

namespace Дом.Годот;

public static class Пути
{
    private static string? _данные;

    /// <summary>Папка с `balance.json`, `npcs.json` и прочими.</summary>
    public static string Данные => _данные ??= Найти();

    /// <summary>Игру водит MCP-сервер: в настройках проекта есть его мост.
    /// У игры автора моста нет никогда.</summary>
    public static bool Песочница => ProjectSettings.HasSetting("autoload/McpBridge");

    private static string? _папка;

    /// <summary>Куда игра пишет свои файлы: `user://`, а когда игру водит
    /// MCP, — песочница, и никогда не папка автора.</summary>
    public static string Папка => _папка ??= Выбрать_папку();

    private static string Выбрать_папку()
    {
        string user = ProjectSettings.GlobalizePath("user://");
        if (!Песочница)
            return user;
        // настоящую папку автора спрашиваем у системы, а не у APPDATA:
        // APPDATA песочница как раз подменяет
        string автора = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        if (!Внутри(user, автора))
        {
            GD.Print($"[пути] песочница MCP: файлы игры — в {user}");
            return user;
        }
        string запасная = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "опять-здесь-песочница");
        System.IO.Directory.CreateDirectory(запасная);
        GD.PrintErr($"[пути] игру водит MCP, а user:// — папка автора ({user}); файлы игры — в {запасная}");
        return запасная;
    }

    /// <summary>Лежит ли путь внутри папки (без учёта регистра, как в Windows).</summary>
    private static bool Внутри(string путь, string папка)
    {
        static string Ровно(string п)
            => System.IO.Path.GetFullPath(п).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
        return Ровно(путь).StartsWith(Ровно(папка), System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Файл наследия (ГДД 24): одно сохранение на петлю, рядом с игрой.
    ///
    /// `user://` — то место, куда движок пускает игру писать на любой
    /// машине; класть сейв в папку проекта нельзя, у собранной игры её
    /// может не быть вовсе. Под MCP — песочница (`Папка`).
    /// </summary>
    public static string Наследие => System.IO.Path.Combine(Папка, "наследие.json");

    public static bool ЕстьНаследие => System.IO.File.Exists(Наследие);

    /// <summary>Файл настроек (Esc): рядом с наследием, но отдельно —
    /// настройки не часть петли и не обнуляются вместе с ней.</summary>
    public static string Настройки => System.IO.Path.Combine(Папка, "настройки.json");

    /// <summary>Кем и в каком мире идёт петля (`ИграГероя`): пишется
    /// новой игрой, живёт всю петлю — после смерти герой тот же.</summary>
    public static string Игра => System.IO.Path.Combine(Папка, "игра.json");

    public static bool ЕстьИгра => System.IO.File.Exists(Игра);

    /// <summary>
    /// Сохранение текущей жизни (`СохранениеИгры`, ГДД 24): одно,
    /// автоматическое, перезаписывается при каждом сне. Жизнь кончилась —
    /// файла нет: загрузиться раньше смерти нельзя.
    /// </summary>
    public static string Жизнь => System.IO.Path.Combine(Папка, "жизнь.json");

    public static bool ЕстьЖизнь => System.IO.File.Exists(Жизнь);

    private static string Найти()
    {
        string корень = OS.HasFeature("editor")
            ? ProjectSettings.GlobalizePath("res://")
            : System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        foreach (string куда in new[] { "data", "../data", "../../data" })
        {
            string путь = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(корень, куда));
            if (System.IO.File.Exists(System.IO.Path.Combine(путь, "balance.json")))
                return путь;
        }
        throw new System.IO.DirectoryNotFoundException(
            $"не найден каталог данных: рядом с {корень} нет data/balance.json");
    }
}
