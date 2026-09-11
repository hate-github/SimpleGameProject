// Двор в редакторе: точки, которые двигают мышью.
//
// Дом строится по данным — `npcs.json` решает, где какая дверь, и двигать
// его руками нельзя: сдвинется расселение, разъедется всё. А двор по данным
// не строится, и до сих пор его вещи стояли числами прямо в `Мир.cs`:
// поправить дерево можно было только правкой кода и пересборкой.
//
// Теперь место каждой вещи живёт в сцене `двор.tscn`. В ней лежат точки
// (`Marker3D`), у которых значимо только три вещи — где, как повёрнута
// и какого размера. Чем точка станет, решает её **имя**:
//
//   · `погреб6`, `гараж13` — кладовая с таким `id` из данных дома;
//   · `дерево…` — мёртвое дерево (высота из `scale.Y`, какое из трёх —
//     из последней цифры имени);
//   · `сугроб…` — сугроб (размер из `scale`, прямо в метрах).
//
// Имена читаются по началу, а не по списку: точек можно добавлять сколько
// угодно, и все они появятся в игре. Сцены нет или она пуста — двор
// строится по запасной расстановке, той же, что была в коде.
//
// Этот скрипт — только для редактора. В игре он не делает ничего: двор
// строит `Мир`, читая ту же сцену. Здесь рисуется **справка** — серые
// коробки дома и очертания вещей, — потому что расставлять двор вслепую
// нельзя: надо видеть, где стена. Габариты дома приходят из `Мир.Габариты`,
// то есть из тех же констант, что и сам дом, и разъехаться с ним не могут.
//
// Справка в сцену не сохраняется: узлы заводятся без владельца, и Godot
// их не пишет. Файл остаётся коротким — одни точки.

using Godot;

namespace Дом.Годот;

[Tool]
public partial class Двор : Node3D
{
    /// <summary>Сколько этажей рисовать в справке. В игре их считают
    /// по данным; здесь — просто чтобы видеть высоту стены.</summary>
    [Export] public int Этажей { get; set; } = 5;

    /// <summary>Показывать ли справку. Мешает — выключите.</summary>
    [Export] public bool Справка { get; set; } = true;

    private Node3D? _справка;
    private string _слепок = "";

    public override void _Process(double _)
    {
        if (!Engine.IsEditorHint())
            return;
        string слепок = Слепок();
        if (слепок == _слепок)
            return;
        _слепок = слепок;
        Нарисовать();
    }

    /// <summary>Всё, от чего зависит картинка, одной строкой: пока она та же,
    /// перерисовывать нечего. Иначе справка пересобиралась бы каждый кадр.</summary>
    private string Слепок()
    {
        var сб = new System.Text.StringBuilder();
        сб.Append(Справка).Append('|').Append(Этажей).Append('|');
        foreach (var у in GetChildren())
            if (у is Node3D т && !ReferenceEquals(т, _справка))
                сб.Append(т.Name).Append(т.Transform).Append(';');
        return сб.ToString();
    }

    private void Нарисовать()
    {
        _справка?.QueueFree();
        _справка = null;
        if (!Справка)
            return;

        // без владельца — значит, в файл сцены не попадёт
        _справка = new Node3D { Name = "(справка)" };
        AddChild(_справка);

        // Дом — с обычным отсечением задних граней: камера редактора
        // открывается в начале координат, то есть внутри дома, и коробка
        // без отсечения закрывала бы собой весь вид. Изнутри её не видно,
        // снаружи видно.
        var дом = Краска(new Color(0.55f, 0.57f, 0.60f, 0.35f), внутри: false);
        foreach (var (размер, где) in Мир.Габариты(System.Math.Max(1, Этажей)))
            Кусок(дом, размер, где, 0);

        var кладовка = Краска(new Color(0.42f, 0.36f, 0.31f, 0.75f));
        var снег = Краска(new Color(0.72f, 0.76f, 0.79f, 0.75f));
        var ствол = Краска(new Color(0.34f, 0.33f, 0.30f, 0.85f));

        foreach (var у in GetChildren())
        {
            if (у is not Node3D т || ReferenceEquals(т, _справка))
                continue;
            string имя = т.Name.ToString();
            var где = т.Transform;
            float угол = Mathf.RadToDeg(где.Basis.GetEuler().Y);
            var м = где.Basis.Scale;

            if (имя.StartsWith("гараж", System.StringComparison.Ordinal))
                Кусок(кладовка, new Vector3(3.0f, 2.2f, 5.0f),
                      где.Origin + Vector3.Up * 1.1f, угол);
            else if (имя.StartsWith("погреб", System.StringComparison.Ordinal))
                Кусок(кладовка, new Vector3(2.2f, 1.5f, 2.6f),
                      где.Origin + Vector3.Up * 0.75f, угол);
            else if (имя.StartsWith("сугроб", System.StringComparison.Ordinal))
                Кусок(снег, м, где.Origin + Vector3.Up * (м.Y / 2), угол);
            else if (имя.StartsWith("дерево", System.StringComparison.Ordinal))
            {
                // дерево — моделью, а справка — столбом: точка ставится
                // по месту и высоте, а ветви и так видно в игре
                float высота = 4.2f * Mathf.Max(0.2f, м.Y);
                Кусок(ствол, new Vector3(0.35f, высота, 0.35f),
                      где.Origin + Vector3.Up * (высота / 2), угол);
            }
        }
    }

    private void Кусок(StandardMaterial3D краска, Vector3 размер, Vector3 где,
                       float угол)
        => _справка!.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = размер },
            MaterialOverride = краска,
            Position = где,
            RotationDegrees = new Vector3(0, угол, 0),
        });

    private static StandardMaterial3D Краска(Color цвет, bool внутри = true) => new()
    {
        AlbedoColor = цвет,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = внутри ? BaseMaterial3D.CullModeEnum.Disabled
                          : BaseMaterial3D.CullModeEnum.Back,
    };
}
