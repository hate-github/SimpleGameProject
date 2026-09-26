// Меш из модели автора по имени варианта — один на всех: мир ставит им
// еду на полки и урны во двор (`Мир.Модели.cs`), инвентарь снимает иконки
// и крутит модель в карточке (`ИконкиЕды`, `КарточкаЕды`).
//
// В файлах автора варианты одного предмета лежат в одной точке
// («beef unopen fresh», «half mold») — меш берётся по части имени, без
// учёта регистра (`модели/ЧИТАЙ.md`). Сцена модели разворачивается один
// раз на (файл, вариант): банок на полках магазина полторы сотни.
//
// Вывернутую деталь (грани внутрь — так пришли урна, скамейка, картошка)
// загрузчик узнаёт сам и рисует с обеих сторон: односторонне у неё
// видна дальняя стенка сквозь ближнюю. Правило то же, что у витрины
// (`Изнанка`), — одно на двоих, чтобы витрина и игра не спорили.

using Godot;

namespace Дом.Годот;

public static class МоделиАвтора
{
    public sealed record Найдено(Mesh меш, Aabb рамка, string имя);

    private static readonly Dictionary<(string, string, bool), Найдено?> _меши = new();

    /// <param name="двусторонне">Рисовать грани с обеих сторон — для
    /// вывернутых моделей (витрина: «НАИЗНАНКУ» — скамейка и урны):
    /// с обеих сторон Godot у обратной грани сам переворачивает нормаль,
    /// и свет ложится правильно. Копия меша — одна на все такие вещи.</param>
    public static Найдено? Меш(string путь, string вариант, bool двусторонне = false)
    {
        if (_меши.TryGetValue((путь, вариант, двусторонне), out var есть))
            return есть;
        Найдено? найдено = null;
        var сцена = ResourceLoader.Exists(путь) ? GD.Load<PackedScene>(путь) : null;
        if (сцена is not null)
        {
            var образец = сцена.Instantiate<Node3D>();
            var меши = new List<MeshInstance3D>();
            Найти(образец, меши);
            var м = меши.FirstOrDefault(x => x.Name.ToString().Contains(вариант, StringComparison.OrdinalIgnoreCase));
            if (м?.Mesh is { } меш)
            {
                if (двусторонне || Изнанка(Треугольники(меш), меш.GetAabb().GetCenter(), Знак_наружу).вывернута)
                {
                    var копия = (Mesh)меш.Duplicate();
                    for (int п = 0; п < копия.GetSurfaceCount(); п++)
                        if (м.GetActiveMaterial(п) is BaseMaterial3D мат)
                        {
                            var с_обеих = (BaseMaterial3D)мат.Duplicate();
                            с_обеих.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                            копия.SurfaceSetMaterial(п, с_обеих);
                        }
                    меш = копия;
                }
                найдено = new Найдено(меш, меш.GetAabb(), м.Name.ToString());
            }
            образец.Free();
        }
        _меши[(путь, вариант, двусторонне)] = найдено;
        return найдено;
    }

    /// <summary>Первый из вариантов, который есть в файле.</summary>
    public static Найдено? Первый(string путь, IEnumerable<string> варианты)
    {
        foreach (string в in варианты)
            if (Меш(путь, в) is { } н)
                return н;
        return null;
    }

    /// <summary>
    /// Узел с мешем, стоящим «как в мире»: Z-вверх модели → Y-вверх,
    /// середина над началом координат, низ на нуле, большая сторона —
    /// <paramref name="размер"/>.
    /// </summary>
    public static Node3D Узел(Найдено н, float размер)
    {
        var р = н.рамка;
        float больше = Math.Max(р.Size.X, Math.Max(р.Size.Y, р.Size.Z));
        float масштаб = больше > 1e-6f ? размер / больше : 1f;
        var середина = р.GetCenter();
        var узел = new Node3D { RotationDegrees = new Vector3(-90, 0, 0), Scale = Vector3.One * масштаб };
        узел.AddChild(new MeshInstance3D
        {
            Mesh = н.меш,
            Position = new Vector3(-середина.X, -середина.Y, -р.Position.Z),
        });
        return узел;
    }

    // ------------------------------------------------------------ изнанка

    private static int? _знак;

    /// <summary>Знак «наружу» в этом движке — по кубу Godot.</summary>
    public static int Знак_наружу => _знак ??= Знак_куба();

    /// <summary>
    /// Какой знак у «наружу» в этом движке: берётся обычный куб Godot,
    /// у которого грани заведомо смотрят наружу, и считается, согласен ли
    /// с его нормалями порядок вершин при плюсе. Так правило не зависит
    /// от того, по часовой или против лицо в этой версии движка.
    /// </summary>
    public static int Знак_куба()
    {
        var м = new BoxMesh().GetMeshArrays();
        var точки = м[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var нормали = м[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var номера = м[(int)Mesh.ArrayType.Index].AsInt32Array();
        int за = 0, против = 0;
        for (int i = 0; i + 2 < номера.Length; i += 3)
        {
            var (A, B, C) = (точки[номера[i]], точки[номера[i + 1]], точки[номера[i + 2]]);
            float d = (B - A).Cross(C - A).Dot(нормали[номера[i]]);
            if (d > 0) за++;
            else if (d < 0) против++;
        }
        return за >= против ? 1 : -1;
    }

    /// <summary>Треугольники меша по всем поверхностям.</summary>
    public static List<(Vector3 A, Vector3 B, Vector3 C)> Треугольники(Mesh меш)
    {
        var тр = new List<(Vector3, Vector3, Vector3)>();
        for (int s = 0; s < меш.GetSurfaceCount(); s++)
        {
            var массивы = меш.SurfaceGetArrays(s);
            var точки = массивы[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var номера = массивы[(int)Mesh.ArrayType.Index].AsInt32Array();
            int n = номера.Length > 0 ? номера.Length : точки.Length;
            for (int i = 0; i + 2 < n; i += 3)
                тр.Add(номера.Length > 0
                    ? (точки[номера[i]], точки[номера[i + 1]], точки[номера[i + 2]])
                    : (точки[i], точки[i + 1], точки[i + 2]));
        }
        return тр;
    }

    /// <summary>
    /// Наизнанку ли деталь. У замкнутой (каждое ребро — ровно у двух граней)
    /// объём по граням от начала координат не зависит, и его знак — ответ.
    /// У открытой (листва, плоскость, труба без донышка) объём ничего
    /// не значит; там считается, какая доля граней смотрит к середине
    /// детали, — у целой её меньше половины, у вывернутой больше 0.6.
    /// </summary>
    public static (bool вывернута, bool замкнута, double доля) Изнанка(
        IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> тр, Vector3 середина, int знак)
    {
        double объём = 0;
        int к_центру = 0, считано = 0;
        foreach (var (A, B, C) in тр)
        {
            var (a, b, c) = (A - середина, B - середина, C - середина);
            объём += a.Dot(b.Cross(c)) / 6.0;
            var лицо = (B - A).Cross(C - A) * знак;
            if (лицо.LengthSquared() < 1e-12f)
                continue;
            считано++;
            if (лицо.Dot((A + B + C) / 3f - середина) < 0)
                к_центру++;
        }
        bool замкнута = Замкнута(тр);
        double доля = считано == 0 ? 0 : к_центру / (double)считано;
        return (замкнута ? объём * знак < 0 : доля > 0.6, замкнута, доля);
    }

    /// <summary>Замкнута ли деталь: каждое ребро — ровно у двух граней.
    /// Вершины сваривают по месту: импорт режет их по швам текстуры.</summary>
    public static bool Замкнута(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> тр)
    {
        if (тр.Count == 0)
            return false;
        (long, long, long) Ключ(Vector3 v) => ((long)Math.Round(v.X * 1e4),
                                               (long)Math.Round(v.Y * 1e4),
                                               (long)Math.Round(v.Z * 1e4));
        var рёбра = new Dictionary<((long, long, long), (long, long, long)), int>();
        void Ребро(Vector3 p, Vector3 q)
        {
            var (кп, кq) = (Ключ(p), Ключ(q));
            var к = кп.CompareTo(кq) <= 0 ? (кп, кq) : (кq, кп);
            рёбра[к] = рёбра.GetValueOrDefault(к) + 1;
        }
        foreach (var (A, B, C) in тр)
        {
            Ребро(A, B);
            Ребро(B, C);
            Ребро(C, A);
        }
        return рёбра.Values.All(n => n == 2);
    }

    public static void Найти(Node где, List<MeshInstance3D> куда)
    {
        if (где is MeshInstance3D м)
            куда.Add(м);
        foreach (var д in где.GetChildren())
            Найти(д, куда);
    }
}
