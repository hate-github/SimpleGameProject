// Двоичный FBX без библиотек — ровно столько, сколько нужно проверке людей:
// какие сетки в файле, какие у них материалы и какого цвета. Массивы
// (вершины, веса, кривые) не распаковываются — пропускаются по длине.
//
// Устройство файла: заголовок в 27 байт, дальше дерево узлов. У узла —
// конец (смещение), число свойств, длина их списка, имя, свойства и дети
// до конца; с версии 7500 длины восьмибайтные. Свойство — буква типа
// и значение; массив — длина, сжат ли, длина в байтах и данные.

using System.Text;

namespace Дом.Консоль;

public sealed record УзелFbx(string имя, IReadOnlyList<object?> св, IReadOnlyList<УзелFbx> дети)
{
    public УзелFbx? Один(string имя_) => дети.FirstOrDefault(д => д.имя == имя_);
    public IEnumerable<УзелFbx> Все(string имя_) => дети.Where(д => д.имя == имя_);
}

/// <summary>Материал сетки: имя и цвет (линейный — как пишет его Blender).</summary>
public sealed record МатериалFbx(string имя, double r, double g, double b);

public sealed record СеткаFbx(string имя, IReadOnlyList<МатериалFbx> материалы);

public static class Fbx
{
    public static IReadOnlyList<УзелFbx> Читать(string путь)
    {
        var d = File.ReadAllBytes(путь);
        if (d.Length < 27 || Encoding.ASCII.GetString(d, 0, 20) != "Kaydara FBX Binary  ")
            throw new InvalidDataException($"{путь}: не двоичный FBX");
        bool широкий = BitConverter.ToUInt32(d, 23) >= 7500;
        int p = 27;
        var корни = new List<УзелFbx>();
        while (p + (широкий ? 25 : 13) <= d.Length)
        {
            var (узел, дальше) = Узел(d, p, широкий);
            if (узел is null)
                break;
            корни.Add(узел);
            p = дальше;
        }
        return корни;
    }

    /// <summary>Сетки файла с их материалами (по связям «материал → модель»).</summary>
    public static IReadOnlyList<СеткаFbx> Сетки(string путь)
    {
        var корни = Читать(путь);
        var объекты = корни.First(к => к.имя == "Objects");
        var связи = корни.First(к => к.имя == "Connections");
        static string Имя(УзелFbx у) => ((string)у.св[1]!).Split('\0')[0];
        var модели = объекты.Все("Model").Where(м => м.св[2] as string == "Mesh")
                            .ToDictionary(м => (long)м.св[0]!, Имя);
        var материалы = объекты.Все("Material").ToDictionary(м => (long)м.св[0]!, м =>
        {
            var P = м.Один("Properties70")?.Все("P").FirstOrDefault(P => P.св[0] as string == "DiffuseColor");
            return P is null
                ? new МатериалFbx(Имя(м), 0.8, 0.8, 0.8)
                : new МатериалFbx(Имя(м), (double)P.св[4]!, (double)P.св[5]!, (double)P.св[6]!);
        });
        var у_модели = модели.Keys.ToDictionary(к => к, _ => new List<МатериалFbx>());
        foreach (var c in связи.Все("C"))
            if (c.св[0] as string == "OO" && c.св[1] is long ребёнок && c.св[2] is long родитель
                && материалы.TryGetValue(ребёнок, out var мат) && у_модели.TryGetValue(родитель, out var список))
                список.Add(мат);
        return модели.Select(м => new СеткаFbx(м.Value, у_модели[м.Key])).ToList();
    }

    private static (УзелFbx? узел, int дальше) Узел(byte[] d, int p, bool широкий)
    {
        long конец, n;
        int длина_имени;
        if (широкий)
        {
            конец = (long)BitConverter.ToUInt64(d, p);
            n = (long)BitConverter.ToUInt64(d, p + 8);
            длина_имени = d[p + 24];
            p += 25;
        }
        else
        {
            конец = BitConverter.ToUInt32(d, p);
            n = BitConverter.ToUInt32(d, p + 4);
            длина_имени = d[p + 12];
            p += 13;
        }
        if (конец == 0)
            return (null, p);
        string имя = Encoding.UTF8.GetString(d, p, длина_имени);
        p += длина_имени;
        var св = new List<object?>();
        for (long i = 0; i < n; i++)
            св.Add(Свойство(d, ref p));
        var дети = new List<УзелFbx>();
        while (p < конец)
        {
            var (ребёнок, дальше) = Узел(d, p, широкий);
            p = дальше;
            if (ребёнок is null)
                break;
            дети.Add(ребёнок);
        }
        return (new УзелFbx(имя, св, дети), (int)конец);
    }

    private static object? Свойство(byte[] d, ref int p)
    {
        char t = (char)d[p++];
        switch (t)
        {
            case 'Y': { long v = BitConverter.ToInt16(d, p); p += 2; return v; }
            case 'C': { bool v = d[p] != 0; p += 1; return v; }
            case 'I': { long v = BitConverter.ToInt32(d, p); p += 4; return v; }
            case 'F': { double v = BitConverter.ToSingle(d, p); p += 4; return v; }
            case 'D': { double v = BitConverter.ToDouble(d, p); p += 8; return v; }
            case 'L': { long v = BitConverter.ToInt64(d, p); p += 8; return v; }
            case 'f' or 'd' or 'l' or 'i' or 'b':
                p += 12 + (int)BitConverter.ToUInt32(d, p + 8);      // массив проверке не нужен
                return null;
            case 'S' or 'R':
            {
                int длина = (int)BitConverter.ToUInt32(d, p);
                p += 4;
                object v = t == 'S' ? Encoding.UTF8.GetString(d, p, длина) : d[p..(p + длина)];
                p += длина;
                return v;
            }
            default:
                throw new InvalidDataException($"FBX: свойство типа «{t}» на {p - 1}");
        }
    }
}
