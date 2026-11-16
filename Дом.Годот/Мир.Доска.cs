// Доска объявлений у входа в подъезд (импровизация 17.11.2026, автор: «чтобы
// игра ощущалась живее»). Что на ней висит, считает `Дом.Экран.Беседы.Объявления`
// — объявления о мене, о краже, о смерти, совет медика, бумажка с осени, — а мир
// только вешает доску: на задней стене, справа от проёма (створка входной двери
// открывается наружу, и стена изнутри свободна), с областью для прицела.
// Через пиксельный фильтр мелкие буквы не прочесть — текст целиком в подсказке
// и в окне доски (узел, `ДомУзел.Доска.cs`).

using Godot;

namespace Дом.Годот;

public partial class Мир
{
    /// <summary>Доска объявлений — подсказку и действие ставит узел.</summary>
    public Предмет? Доска { get; private set; }

    private Label3D? _доска_текст;

    private void Доска_объявлений()
    {
        float пол_ширины = ПРОЁМ_ШИРИНА / 2;
        var г = _дом?.Гнездо("выход")?.Origin ?? new Vector3(0, 0, ЗАД);
        var корень = new Node3D
        {
            Name = "доска_объявлений",
            Position = new Vector3(г.X + пол_ширины + 0.48f, г.Y + 1.45f, ЗАД + 0.12f),
        };
        AddChild(корень);
        корень.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.5f, 0.02f) },
            MaterialOverride = Материал(new Color("#6e5638"), 0.95f),
        });
        _доска_текст = new Label3D
        {
            FontSize = 26,
            PixelSize = 0.0014f,
            Position = new Vector3(0, 0, 0.015f),
            Width = 460,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color("#e8e2d0"),
            OutlineSize = 0,
            Shaded = false,
            DoubleSided = false,
        };
        корень.AddChild(_доска_текст);
        Доска = Предмет.Вокруг(корень, new Aabb(new Vector3(-0.35f, -0.25f, -0.01f), new Vector3(0.7f, 0.5f, 0.03f)),
                               0.15f, "доска");
    }

    /// <summary>Что висит на доске — буквами, по листку на объявление.</summary>
    public void Объявления(IReadOnlyList<string> что)
    {
        if (_доска_текст is not null && GodotObject.IsInstanceValid(_доска_текст))
            _доска_текст.Text = string.Join("\n\n", что.Take(4));
    }
}
