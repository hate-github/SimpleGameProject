// Вход консоли: пока — только проверки порта.
// Дальше сюда переедут прогон (`run.py`) и игра руками (`играть.py`).
using System.Globalization;

namespace Дом.Консоль;

public static class Программа
{
    public static int Main(string[] args)
    {
        // всё, что печатается и разбирается, — в инвариантной культуре:
        // «0.5», а не «0,5». Иначе журнал разойдётся с прототипом на числах,
        // не разойдясь ни в одном решении.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var что = args.Length > 0 ? args[0] : "проверить";
        return что switch
        {
            "проверить" => Проверить(),
            _ => Подсказка(что),
        };
    }

    private static int Проверить()
    {
        var плохо = new List<string>();
        void w(string s) => Console.WriteLine(s);

        w("генератор:");
        плохо.AddRange(Проверки.Генератор(w));
        w("вопросы игроку:");
        плохо.AddRange(Проверки.Вопросы(w));
        w("граница движка:");
        плохо.AddRange(Проверки.ГраницаДвижка(w));

        w("");
        if (плохо.Count > 0)
        {
            w($"не сошлось: {плохо.Count}");
            return 1;
        }
        w("всё сошлось");
        return 0;
    }

    private static int Подсказка(string что)
    {
        Console.Error.WriteLine($"неизвестная команда «{что}». Есть: проверить");
        return 2;
    }
}
