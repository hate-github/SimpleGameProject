// Меш из модели автора по имени варианта — один на всех: мир ставит им
// еду на полки и урны во двор (`Мир.Модели.cs`), инвентарь снимает иконки
// и крутит модель в карточке (`ИконкиЕды`, `КарточкаЕды`).
//
// В файлах автора варианты одного предмета лежат в одной точке
// («beef unopen fresh», «half mold») — меш берётся по части имени, без
// учёта регистра (`модели/ЧИТАЙ.md`). Сцена модели разворачивается один
// раз на (файл, вариант): банок на полках магазина полторы сотни.

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
                if (двусторонне)
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

    public static void Найти(Node где, List<MeshInstance3D> куда)
    {
        if (где is MeshInstance3D м)
            куда.Add(м);
        foreach (var д in где.GetChildren())
            Найти(д, куда);
    }
}
