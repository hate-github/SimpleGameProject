// Верстак — пока заглушка.
//
// Крафта в доме нет: ни рецептов, ни того, из чего их считать. Заводить
// рецепты здесь, в экране, значило бы придумать механику мимо дома,
// а у этого проекта механика живёт в ядре и сверяется числом. Поэтому
// панель открывается и честно говорит, что делать тут пока нечего.
// Когда крафт появится в ядре, сюда придёт список того, что можно
// собрать из запасов героя.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class ВерстакUI : PanelContainer
{
    public bool открыт => Visible;

    /// <summary>Закрыли — корневому узлу надо вернуть мышь и ходьбу.</summary>
    public System.Action Закрыт { get; set; } = () => { };

    public override void _Ready()
    {
        Visible = false;
        // во весь экран: SetAnchorsPreset в дереве оставил бы нулевой прямоугольник
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel",
            new StyleBoxFlat { BgColor = new Color(0.07f, 0.065f, 0.06f, 0.92f) });

        var середина = new CenterContainer();
        середина.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(середина);

        var столбец = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        столбец.AddThemeConstantOverride("separation", 14);
        середина.AddChild(столбец);

        var заголовок = new Label
        {
            Text = "ВЕРСТАК",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        заголовок.AddThemeFontSizeOverride("font_size", 26);
        заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        столбец.AddChild(заголовок);

        var текст = new Label
        {
            Text = "Тиски, ящик с инструментом, обрезки железа.\n"
                   + "Собирать здесь пока нечего — крафт появится позже.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        текст.AddThemeColorOverride("font_color", new Color("#b8b0a0"));
        столбец.AddChild(текст);

        var закрыть = new Button { Text = "Отойти  [Esc]" };
        закрыть.Pressed += Убрать;
        столбец.AddChild(закрыть);
    }

    public void Показать() => Visible = true;

    public void Убрать()
    {
        if (!Visible)
            return;
        Visible = false;
        Закрыт();
    }

    /// <summary>Отойти — Esc или та же клавиша, которой подошли.</summary>
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
