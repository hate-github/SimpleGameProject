// Оракул: спросить у самого прототипа, что должно получиться.
//
// Пока порт не сошёлся, источник правды о поведении — Python, и он лежит
// в двух шагах отсюда. Поэтому порт не сверяется с копией списка, набранной
// руками (её пришлось бы держать в согласии вручную, а значит рано или
// поздно не удержать), а спрашивает у прототипа и сравнивает.
//
// Это же понадобится главной приёмке этапа 1а: дом, собранный из `npcs.json`,
// поле в поле совпадает с питоновским.
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Дом.Консоль;

public static class Оракул
{
    private static string? _питон;

    /// <summary>Есть ли чем спрашивать. Нет питона — проверка не падает,
    /// а честно говорит, что сверить нечем.</summary>
    public static bool Доступен => Питон() is not null;

    /// <summary>
    /// Выполнить кусок питоновского кода в корне прототипа и разобрать то,
    /// что он напечатал, как JSON. Код печатает ровно одну строку JSON.
    /// </summary>
    public static JsonDocument Json(string код)
    {
        var питон = Питон() ?? throw new InvalidOperationException(
            "не найден python — сверять с прототипом нечем");

        // через файл, а не через -c: кириллица в аргументах командной строки
        // на Windows зависит от кодовой страницы, а файл читается как UTF-8
        var файл = Path.Combine(Path.GetTempPath(), $"оракул_{Guid.NewGuid():N}.py");
        File.WriteAllText(файл, код, new UTF8Encoding(false));
        try
        {
            var инфо = new ProcessStartInfo(питон, $"\"{файл}\"")
            {
                WorkingDirectory = Пути.Корень,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
            };
            инфо.Environment["PYTHONIOENCODING"] = "utf-8";
            using var п = Process.Start(инфо)
                ?? throw new InvalidOperationException("не удалось запустить python");
            string вывод = п.StandardOutput.ReadToEnd();
            string ошибки = п.StandardError.ReadToEnd();
            п.WaitForExit();
            if (п.ExitCode != 0)
                throw new InvalidOperationException(
                    $"прототип ответил кодом {п.ExitCode}:\n{ошибки.TrimEnd()}");
            return JsonDocument.Parse(вывод);
        }
        finally
        {
            try { File.Delete(файл); } catch { /* временный файл, не беда */ }
        }
    }

    private static string? Питон()
    {
        if (_питон is not null)
            return _питон;
        foreach (var имя in new[] { "python", "python3", "py" })
            if (Проверить(имя))
                return _питон = имя;
        return null;
    }

    private static bool Проверить(string имя)
    {
        try
        {
            var инфо = new ProcessStartInfo(имя, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var п = Process.Start(инфо);
            if (п is null)
                return false;
            п.WaitForExit(10000);
            return п.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
