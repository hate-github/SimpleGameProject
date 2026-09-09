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
            "схема" => Схема_(args.Length > 1 ? args[1] : Пути.Данные),
            _ => Подсказка(что),
        };
    }

    private static int Проверить()
    {
        var плохо = new List<string>();
        void w(string s) => Console.WriteLine(s);

        w("генератор:");
        плохо.AddRange(Проверки.Генератор(w));
        w("справочник действий:");
        плохо.AddRange(Проверки.Каталог(w));
        w("вопросы игроку:");
        плохо.AddRange(Проверки.Вопросы(w));
        w("дом из данных:");
        плохо.AddRange(Проверки.ДомИзДанных(w));
        w("сутки тела:");
        плохо.AddRange(Проверки.Тело(w));
        w("общество:");
        плохо.AddRange(Проверки.Общество(w));
        w("мир и улица:");
        плохо.AddRange(Проверки.МирИУлица(w));
        w("сбор вариантов:");
        плохо.AddRange(Проверки.Сбор(w));
        w("разбор решения:");
        плохо.AddRange(Проверки.РазборРешения(w));
        w("конфликт:");
        плохо.AddRange(Проверки.Конфликты(w));
        w("ночь и чат:");
        плохо.AddRange(Проверки.НочьИЧат(w));
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

    /// <summary>
    /// Прогнать проверку данных над указанной папкой. Нужна не только для
    /// чужих данных: ею же проверяется, что сама проверка не пустая — берётся
    /// копия `data/` с нарочной опечаткой, и она обязана покраснеть.
    /// </summary>
    private static int Схема_(string каталог)
    {
        try
        {
            Дом.Ядро.Схема.Прочитать(каталог);
            Console.WriteLine($"данные в «{каталог}» в порядке");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }

    private static int Подсказка(string что)
    {
        Console.Error.WriteLine($"неизвестная команда «{что}». Есть: проверить, схема [папка]");
        return 2;
    }
}
