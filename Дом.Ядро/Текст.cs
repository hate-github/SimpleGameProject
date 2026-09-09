// Числа строкой — так, как их пишет Python.
//
// Ловушка 4 из `docs/ПОРТ.md` в чистом виде, и она глубже, чем «другая
// культура». `f"{v:.2f}"` в Python округляет по ТОЧНОМУ двоичному значению
// и половину сводит к чётному. В .NET «F2» половину округляет от нуля.
// Два разных ответа на одном числе:
//
//     0.125  → Python «0.12», .NET «0.13»   (ровно половина, к чётному)
//     3.145  → Python «3.15», .NET «3.15»   (в double это чуть больше половины)
//
// И второй случай важнее первого: пока округление шло через `G17`, число
// выглядело ровной половиной, уходило к чётному и давало «3.14». Семнадцать
// значащих цифр однозначно называют double, но не равны ему — а решает
// именно точное значение. Поэтому здесь берутся настоящие цифры
// (формат «F» с большой точностью, с .NET Core 3.0 он их даёт)
// и округляются вручную.
//
// Журнал переезжает отдельным этапом, но строители объяснения решения
// приехали вместе со швом, и заводить в них эту разницу нельзя.
using System.Globalization;
using System.Text;

namespace Дом.Ядро;

/// <summary>Форматирование чисел для строк: правила Python, культура
/// инвариантная.</summary>
public static class Текст
{
    /// <summary>Сколько лишних цифр брать, чтобы отличить ровную половину
    /// от «чуть больше». Двоичная дробь либо обрывается в этих пределах,
    /// либо давно ушла от половины.</summary>
    private const int ЗАПАС = 25;

    /// <summary>`f"{v:.Nf}"`: N знаков после точки, половина — к чётному.</summary>
    public static string Ф(double v, int знаков)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            return v.ToString(CultureInfo.InvariantCulture);
        string точно = v.ToString("F" + (знаков + ЗАПАС).ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
        return КЧётному(точно, знаков);
    }

    /// <summary>`f"{v:+.Nf}"`: то же, но со знаком всегда.</summary>
    public static string Знак(double v, int знаков)
    {
        string s = Ф(v, знаков);
        return s.StartsWith('-') ? s : "+" + s;
    }

    /// <summary>
    /// `f"{v:g}"` — «3», «0.5», «1.25».
    ///
    /// Порог перехода в степень у «G6» и у питоновского `g` один и тот же
    /// (степень от −4 до 5 включительно печатается обычной записью),
    /// а вот буква разная: .NET пишет «6.21725E-15», Python — «6.21725e-15».
    /// Нашлось это на тридцатом дне сыгранной вручную жизни: остаток воды
    /// в шкафу к тому времени успевает стать 6.21725e-15, и раньше такого
    /// числа в журнале просто не встречалось.
    /// </summary>
    public static string G(double v)
        => v.ToString("G6", CultureInfo.InvariantCulture).Replace("E", "e",
                                                                 StringComparison.Ordinal);

    /// <summary>`round(v, N)` из Python: то же округление, что у `Ф`,
    /// но числом, а не строкой.</summary>
    public static double Округлить(double v, int знаков)
        => double.IsNaN(v) || double.IsInfinity(v)
           ? v
           : double.Parse(Ф(v, знаков), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>`str(v)` из Python: кратчайшая запись, но у целого числа
    /// с плавающей точкой остаётся «.0» — в C# его бы не было.</summary>
    public static string Repr(double v)
    {
        string s = v.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') || s.Contains('N') || s.Contains('I')
               ? s : s + ".0";
    }

    /// <summary>`f"{s:&lt;N}"`: дополнить пробелами справа.</summary>
    public static string Слева(string s, int ширина) => s.PadRight(ширина);

    /// <summary>`f"{v:N.Mf}"`: число с M знаками, дополненное слева до ширины N.</summary>
    public static string Справа(double v, int ширина, int знаков)
        => Ф(v, знаков).PadLeft(ширина);

    /// <summary>
    /// Обрезать десятичную запись до N знаков, округлив половину к чётному.
    ///
    /// Работает по цифрам, а не по числу: `decimal` держит 28 значащих цифр,
    /// а точная запись double бывает и длиннее — и обрезка как раз стёрла бы
    /// то, чем половина отличается от не-половины.
    /// </summary>
    private static string КЧётному(string запись, int знаков)
    {
        bool минус = запись.StartsWith('-');
        if (минус)
            запись = запись[1..];
        int точка = запись.IndexOf('.');
        string целое = точка < 0 ? запись : запись[..точка];
        string дробь = точка < 0 ? "" : запись[(точка + 1)..];

        string остаётся = дробь.Length <= знаков ? дробь.PadRight(знаков, '0') : дробь[..знаков];
        string хвост = дробь.Length <= знаков ? "" : дробь[знаков..];

        bool вверх;
        if (хвост.Length == 0 || хвост[0] < '5')
            вверх = false;
        else if (хвост[0] > '5')
            вверх = true;
        else if (хвост.AsSpan(1).IndexOfAnyExcept('0') >= 0)
            вверх = true;                       // больше половины
        else
        {
            // ровно половина — к чётному. Последняя оставленная цифра
            // при нуле знаков берётся из целой части
            char последняя = знаков > 0
                ? остаётся[знаков - 1]
                : (целое.Length > 0 ? целое[^1] : '0');
            вверх = (последняя - '0') % 2 != 0;
        }

        if (вверх)
            (целое, остаётся) = Прибавить(целое, остаётся);

        var б = new StringBuilder();
        if (минус)
            б.Append('-');
        б.Append(целое.Length > 0 ? целое : "0");
        if (знаков > 0)
            б.Append('.').Append(остаётся);
        return б.ToString();
    }

    /// <summary>Прибавить единицу в последний знак, перенося через точку.</summary>
    private static (string целое, string дробь) Прибавить(string целое, string дробь)
    {
        var ц = целое.ToCharArray();
        var д = дробь.ToCharArray();
        bool перенос = true;
        for (int i = д.Length - 1; i >= 0 && перенос; i--)
        {
            if (д[i] == '9') { д[i] = '0'; }
            else             { д[i]++; перенос = false; }
        }
        for (int i = ц.Length - 1; i >= 0 && перенос; i--)
        {
            if (ц[i] == '9') { ц[i] = '0'; }
            else             { ц[i]++; перенос = false; }
        }
        return (перенос ? "1" + new string(ц) : new string(ц), new string(д));
    }
}
