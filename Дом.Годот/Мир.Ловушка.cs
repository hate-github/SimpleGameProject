// Ловушка у порога героя (ГДД 15, уровень 4): проволока поперёк проёма
// изнутри, на высоте щиколотки, на двух колышках. С площадки её не видно —
// она за порогом; виден только тот, кто входит. Своей модели нет — заглушка.
// Стоит ли она — решает ядро (`Петля/Ловушка.cs`), мир только ставит и снимает.

using Godot;

namespace Дом.Годот;

public partial class Мир
{
    private Node3D? _ловушка;
    private int? _ловушка_кв;

    /// <summary>Поставить проволоку у этой двери — или снять (null).</summary>
    public void Ловушка_у(int? кв)
    {
        if (кв == _ловушка_кв && (кв is null || (_ловушка is not null && GodotObject.IsInstanceValid(_ловушка))))
            return;
        if (_ловушка is not null && GodotObject.IsInstanceValid(_ловушка))
            _ловушка.QueueFree();
        _ловушка = null;
        _ловушка_кв = кв;
        if (кв is not int номер || _двери.FirstOrDefault(д => д.квартира == номер) is not { } д)
            return;
        // «перед» — точка на площадке в 0.9 м от проёма; порог изнутри — ещё
        // на треть метра дальше, в квартиру
        var где = д.перед + new Vector3(-д.в_дом * 1.25f, 0, 0);
        var ловушка = new Node3D { Name = $"ловушка{номер}" };
        var дерево = Материал(new Color("#5a4632"), 0.8f);
        foreach (float z in new[] { -0.52f, 0.52f })
            ловушка.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.2f, 0.04f) },
                Position = new Vector3(0, 0.1f, z),
                MaterialOverride = дерево,
            });
        ловушка.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.004f, Height = 1.04f },
            Position = new Vector3(0, 0.14f, 0),
            RotationDegrees = new Vector3(90, 0, 0),
            MaterialOverride = Материал(new Color("#9a9a96"), 0.3f),
        });
        AddChild(ловушка);
        ловушка.GlobalPosition = где;
        _ловушка = ловушка;
    }
}
