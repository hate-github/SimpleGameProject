// Долгое дело: взлом, обыск — то, что делается не мгновенно.
//
// Под прицелом слово с бегущими точками («взлом.», «взлом..», «взлом...»)
// и шкала. Дело идёт, пока герой смотрит на ту же вещь; отвёл взгляд,
// нажал E ещё раз или открыл экран — дело прервано, и ничего не случилось.
//
// Итог дела меняет дом, а менять его можно, только пока он спит на вопросе.
// Поэтому, пока дело идёт, ответить на вопрос нельзя (`ДомУзел.Ответить`):
// дом не проснётся посреди взлома, и итог ляжет в спящий дом.

using Godot;

namespace Дом.Годот;

/// <summary>Что делается долго: слово для шкалы, сколько секунд, что
/// сделать в конце, что — если прервали, и что — пока идёт (прошло секунд).</summary>
public sealed record Долгое(string слово, double секунд, System.Action готово,
                            System.Action? прервано = null,
                            System.Action<double>? идёт = null);

public partial class ДелоUI : Control
{
    private Label _слово = null!;
    private ColorRect _фон = null!;
    private ColorRect _шкала = null!;
    private Долгое? _дело;
    private Предмет? _цель;
    private double _прошло;
    private double _без_взгляда;

    private const double ТОЧКИ = 0.3;       // как часто прибавляется точка
    private const double ТЕРПЕНИЕ = 0.25;   // сколько можно не смотреть на вещь

    public bool занят => _дело is not null;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        _слово = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _слово.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        AddChild(_слово);
        _фон = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.5f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_фон);
        _шкала = new ColorRect
        {
            Color = new Color("#d8c89a"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_шкала);
    }

    /// <summary>Разложить по окну: под прицелом, над подсказкой.</summary>
    public void Разложить(Vector2 окно)
    {
        const float ШИРИНА = 180;
        Position = new Vector2(окно.X / 2 - ШИРИНА / 2, окно.Y * 0.5f + 12);
        Size = new Vector2(ШИРИНА, 40);
        _слово.Position = Vector2.Zero;
        _слово.Size = new Vector2(ШИРИНА, 22);
        _фон.Position = new Vector2(30, 26);
        _фон.Size = new Vector2(ШИРИНА - 60, 5);
        _шкала.Position = _фон.Position;
        _шкала.Size = new Vector2(0, 5);
    }

    public void Начать(Долгое дело, Предмет цель)
    {
        Прервать();
        _дело = дело;
        _цель = цель;
        _прошло = 0;
        _без_взгляда = 0;
        Visible = true;
        Показать();
    }

    /// <summary>Бросить дело: ничего не случилось.</summary>
    public void Прервать()
    {
        if (_дело is null)
            return;
        var д = _дело;
        Закончить();
        д.прервано?.Invoke();
    }

    /// <summary>
    /// Кадр дела. <paramref name="можно"/> — ходить и смотреть можно,
    /// и дом спит на вопросе; иначе дело прерывается.
    /// </summary>
    public void Кадр(double delta, Предмет? под_прицелом, bool можно)
    {
        if (_дело is null)
            return;
        if (!можно)
        {
            Прервать();
            return;
        }
        if (ReferenceEquals(под_прицелом, _цель))
            _без_взгляда = 0;
        else if ((_без_взгляда += delta) > ТЕРПЕНИЕ)
        {
            Прервать();
            return;
        }

        _прошло += delta;
        _дело.идёт?.Invoke(_прошло);
        if (_дело is null)
            return;                 // пока шло, его могли прервать
        if (_прошло >= _дело.секунд)
        {
            var д = _дело;
            Закончить();
            д.готово();
            return;
        }
        Показать();
    }

    private void Закончить()
    {
        _дело = null;
        _цель = null;
        Visible = false;
    }

    private void Показать()
    {
        if (_дело is null)
            return;
        int точек = 1 + (int)(_прошло / ТОЧКИ) % 3;
        _слово.Text = _дело.слово + new string('.', точек);
        float доля = (float)System.Math.Clamp(_прошло / _дело.секунд, 0, 1);
        _шкала.Size = new Vector2(_фон.Size.X * доля, _фон.Size.Y);
    }
}
