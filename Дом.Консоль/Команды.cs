// Перенос run.py и играть.py: прогон и игра руками.
//
// Обе команды — тонкие: разобрать ключи, завести дом, отдать его ядру.
// Всё, что решает, лежит в `Дом.Ядро`, и это видно по тому, сколько здесь
// строк на команду.

using System.Globalization;
using Дом.Ядро;

namespace Дом.Консоль;

public static class Команды
{
    /// <summary>
    /// Один прогон с журналом.
    ///
    ///   Дом.Консоль прогон                    — 30 дней, случайное зерно
    ///   Дом.Консоль прогон --зерно 7          — повторить тот же прогон
    ///   Дом.Консоль прогон --подробно         — каждое действие каждого
    ///   Дом.Консоль прогон --секреты          — то, чего дом не знает
    ///   Дом.Консоль прогон --дней 15 --тихо   — только крупные события
    ///   Дом.Консоль прогон --файл лог.txt     — записать в файл
    /// </summary>
    public static int Прогон_(Ключи к)
    {
        long зерно = к.Число("зерно") is long з ? з : Случайное();
        int дней = (int)(к.Число("дней") ?? 30);
        int подробность = к.Есть("подробно") ? 2 : (к.Есть("тихо") ? 0 : 1);

        var писало = new StringWriter { NewLine = "\n" };
        var журнал = new Журнал(подробность, к.Есть("секреты"), писало);
        var прогон = new Прогон(Пути.Данные, seed: зерно, days: дней, журнал: журнал);
        var h = прогон.run();
        Отчёт.final_report(h, дней, зерно);

        string текст = писало.ToString();
        string? файл = к.Строка("файл");
        if (файл is not null)
        {
            File.WriteAllText(файл, текст, new System.Text.UTF8Encoding(false));
            Печать($"Записано в {файл} ({текст.Split('\n').Length - 1} строк). "
                   + $"Зерно: {зерно}");
        }
        else
            Console.Out.Write(текст);
        return 0;
    }

    /// <summary>
    /// Прожить метель за одного из жильцов.
    ///
    ///   Дом.Консоль играть                    — за Оксану, случайное зерно
    ///   Дом.Консоль играть --кто аркадий      — за деда из первой квартиры
    ///   Дом.Консоль играть --зерно 42 --дней 30
    ///   Дом.Консоль играть --кто список       — кто вообще живёт в доме
    /// </summary>
    public static int Играть(Ключи к, TextReader? ввод = null, TextWriter? вывод = null)
    {
        // ввод открывается явно в UTF-8: `Console.In` разбирает его кодовой
        // страницей консоли, и «я», «т», «всё» приходят кашей — а «?» и цифры
        // проходят. Ошибка тем и неприятна, что наполовину работает
        ввод ??= new StreamReader(Console.OpenStandardInput(),
                                  new System.Text.UTF8Encoding(false));
        var поток = вывод ?? new StreamWriter(Console.OpenStandardOutput())
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        void П(string s = "") => поток.Write(s + "\n");

        long зерно = к.Число("зерно") is long з ? з : Случайное();
        int дней = (int)(к.Число("дней") ?? 30);
        string кто = к.Строка("кто") ?? "оксана";

        var прогон = new Прогон(Пути.Данные, seed: зерно, days: дней);
        var h = прогон.h;

        if (string.Equals(кто, "список", StringComparison.Ordinal) || !h.people.Есть(кто))
        {
            if (!string.Equals(кто, "список", StringComparison.Ordinal))
                П($"Нет такого жильца: {кто}");
            П("В доме живут:");
            foreach (var p in h.people.Значения.OrderBy(x => x.apt))
                П($"  {Текст.Слева(p.id, 9)} кв{Текст.Слева(Ч(p.apt), 3)} "
                  + $"{p.name}, {p.age} — {p.role}");
            поток.Flush();
            return 1;
        }

        // живой журнал вместо копящего — и в него же пишет всё остальное
        var живой = new ЖивойЖурнал(к.Есть("подробно") ? 2 : 1, secrets: false,
                                    поток: поток) { дом = h };
        h.journal = живой;

        // и один жилец из пятнадцати решает не мягким выбором, а вопросом
        // человеку. Правила при этом те же: шов ровно в том месте,
        // где NPC зовёт решающего
        var я = h.people[кто];
        я.решающий = new Человек(Игра.спросить(h, кто, живой, ввод, поток));

        П($"Зерно {зерно}. Вы — {я.name}, кв.{я.apt}: {я.role}.");
        П("Ответ — номер варианта. «всё» — весь список, «?» — дом, "
          + "«я» — состояние, «т» — что знаю о соседях, «в» — выход.");
        try
        {
            прогон.run();
        }
        catch (ВыходИзИгры)
        {
            поток.Flush();
            return 0;
        }
        П();
        Отчёт.final_report(h, дней, зерно, s => П(s));
        П();
        П(я.здесь()
          ? $"{я.@short} дожил"
            + (string.Equals(я.sex, "ж", StringComparison.Ordinal) ? "а" : "")
            + " до конца метели."
          : $"{я.@short}: {я.cause} (день {я.died_day}).");
        поток.Flush();
        return 0;
    }

    /// <summary>Зерно, если его не назвали. Тот же диапазон, что в прототипе.</summary>
    private static long Случайное() => System.Random.Shared.NextInt64(1, 1_000_000);

    private static void Печать(string s) => Console.Out.Write(s + "\n");

    private static string Ч(int v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Ключи командной строки: `--имя значение` и `--имя` как флаг. Своё, потому
/// что тянуть разборщик ради шести ключей — дороже, чем написать.
/// </summary>
public sealed class Ключи
{
    private readonly Dictionary<string, string?> _ключи = new(StringComparer.Ordinal);

    public Ключи(IReadOnlyList<string> args, int с = 1)
    {
        for (int i = с; i < args.Count; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            string имя = args[i][2..];
            bool есть_значение = i + 1 < args.Count
                                 && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            _ключи[имя] = есть_значение ? args[++i] : null;
        }
    }

    public bool Есть(string имя) => _ключи.ContainsKey(имя);

    public string? Строка(string имя)
        => _ключи.TryGetValue(имя, out var v) ? v : null;

    public long? Число(string имя)
        => _ключи.TryGetValue(имя, out var v) && v is not null
           && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)
           ? n : null;
}
