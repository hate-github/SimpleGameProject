// Камера на пролёт над дверью героя (ГДД 15): серая коробочка под потолком
// у косяка, глазом на площадку, и мигающий красный огонёк. Своей
// модели нет — заглушка из двух коробок. Где висит — решает ядро
// (`Петля/Камера.cs`), мир только ставит и снимает.

using Godot;

namespace Дом.Годот;

public partial class Мир
{
    private Node3D? _камера;
    private int? _камера_кв;

    /// <summary>Повесить камеру над этой дверью — или снять (null).</summary>
    public void Камера_над(int? кв)
    {
        if (кв == _камера_кв && (кв is null || (_камера is not null && GodotObject.IsInstanceValid(_камера))))
            return;
        if (_камера is not null && GodotObject.IsInstanceValid(_камера))
            _камера.QueueFree();
        _камера = null;
        _камера_кв = кв;
        if (кв is not int номер || _двери.FirstOrDefault(д => д.квартира == номер) is not { } д)
            return;
        // своя створка стоит внутри квартиры, поэтому место берётся от «перед»:
        // проём на 0.9 м глубже, верх проёма — 2.15 м от пола
        var где = д.перед + new Vector3(-д.в_дом * 0.80f, ПРОЁМ_ВЕРХ + 0.14f, 0.62f);
        var камера = new Node3D { Name = $"камера{номер}" };
        камера.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.08f, 0.09f) },
            MaterialOverride = Материал(new Color("#b8b6ae"), 0.6f),
        });
        камера.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.028f, BottomRadius = 0.028f, Height = 0.03f },
            Position = new Vector3(д.в_дом * 0.09f, -0.01f, 0),
            RotationDegrees = new Vector3(0, 0, 90),
            MaterialOverride = Материал(new Color("#1a1a1c"), 0.2f),
        });
        var огонёк = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.008f, Height = 0.016f },
            Position = new Vector3(д.в_дом * 0.08f, 0.03f, 0.03f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color("#ff3020"),
            },
        };
        камера.AddChild(огонёк);
        AddChild(камера);
        камера.GlobalPosition = где;
        var мигает = огонёк.CreateTween().SetLoops();
        мигает.TweenCallback(Callable.From(() => огонёк.Visible = false)).SetDelay(0.9);
        мигает.TweenCallback(Callable.From(() => огонёк.Visible = true)).SetDelay(0.9);
        _камера = камера;
    }
}
