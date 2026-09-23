// Окна похода: ворота в город (куда пойти) и прилавок магазина (что купить).
//
// Решают не окна. Куда можно и сколько часов — `Округа.Нельзя`
// и `Округа.Часы_дороги`; цена, склад и кошелёк — `Магазин`. Окно
// показывает строки, которые ему дали, и зовёт то, что ему дали. Как
// верстак: закрывается клавишей действия или Esc.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

/// <summary>Строка окна: что написано, можно ли нажать и что при нажатии.</summary>
public sealed record СтрокаОкна(string текст, bool можно, System.Action? сделать);

public partial class ПоходUI : PanelContainer
{
    private Label _заголовок = null!, _текст = null!, _весть = null!;
    private VBoxContainer _строки = null!;
    private System.Func<IReadOnlyList<СтрокаОкна>> _строки_дай = () => System.Array.Empty<СтрокаОкна>();
    private System.Func<string> _текст_дай = () => "";

    public bool открыт => Visible;

    /// <summary>Закрыли — корневому узлу надо вернуть мышь и ходьбу.</summary>
    public System.Action Закрыт { get; set; } = () => { };

    public override void _Ready()
    {
        Visible = false;
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel",
            new StyleBoxFlat { BgColor = new Color(0.07f, 0.065f, 0.06f, 0.92f) });
        var середина = new CenterContainer();
        середина.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(середина);
        var столбец = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        столбец.AddThemeConstantOverride("separation", 12);
        середина.AddChild(столбец);

        _заголовок = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _заголовок.AddThemeFontSizeOverride("font_size", 26);
        _заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        столбец.AddChild(_заголовок);

        _текст = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _текст.AddThemeColorOverride("font_color", new Color("#b8b0a0"));
        столбец.AddChild(_текст);

        _строки = new VBoxContainer();
        _строки.AddThemeConstantOverride("separation", 4);
        столбец.AddChild(_строки);

        _весть = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _весть.AddThemeColorOverride("font_color", new Color("#d8b070"));
        столбец.AddChild(_весть);

        var закрыть = new Button { Text = "Отойти  [Esc]" };
        закрыть.Pressed += Убрать;
        столбец.AddChild(закрыть);
    }

    /// <summary>Показать окно: заголовок, строки (их просят заново после
    /// каждого нажатия — склад и кошелёк меняются) и текст над ними.</summary>
    public void Показать(string заголовок, System.Func<string> текст,
                         System.Func<IReadOnlyList<СтрокаОкна>> строки)
    {
        _заголовок.Text = заголовок;
        _текст_дай = текст;
        _строки_дай = строки;
        _весть.Text = "";
        Visible = true;
        Разложить();
    }

    /// <summary>Сказать под строками, чем кончилось нажатие.</summary>
    public void Весть(string что) => _весть.Text = что;

    private void Разложить()
    {
        foreach (var узел in _строки.GetChildren())
        {
            _строки.RemoveChild(узел);
            узел.QueueFree();
        }
        _текст.Text = _текст_дай();
        foreach (var с in _строки_дай())
        {
            if (с.сделать is null)
            {
                var надпись = new Label { Text = с.текст };
                надпись.AddThemeColorOverride("font_color", new Color("#8a8474"));
                _строки.AddChild(надпись);
                continue;
            }
            var к = new Button { Text = с.текст, Alignment = HorizontalAlignment.Left, Disabled = !с.можно };
            var дело = с.сделать;
            к.Pressed += () =>
            {
                дело();
                if (Visible)
                    Разложить();
            };
            _строки.AddChild(к);
        }
    }

    public void Убрать()
    {
        if (!Visible)
            return;
        Visible = false;
        Закрыт();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible)
            return;
        if (Управление.Нажато(e, Клавиши.ДЕЙСТВИЕ)
            || e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Убрать();
            AcceptEvent();
        }
    }
}
