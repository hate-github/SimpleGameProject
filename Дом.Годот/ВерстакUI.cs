// Верстак в гараже соседа: инструмент, который можно унести.
//
// Крафта в доме нет: ни рецептов, ни того, из чего их считать. Заводить
// рецепты здесь, в экране, значило бы придумать механику мимо дома,
// а у этого проекта механика живёт в ядре и сверяется числом. Поэтому
// собирать на верстаке по-прежнему нечего.
//
// Зато на нём лежит инструмент (`data/инструменты.json`, «верстак»):
// монтировка и болторез. Голыми руками замок не сорвать, и болторез
// с чужого верстака — единственный способ перекусить навесной замок
// тихо. Взять — значит унести чужое; взятое сразу при герое.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class ВерстакUI : PanelContainer
{
    private VBoxContainer _лежат = null!;
    private Label _пусто = null!;
    private IReadOnlyList<(string id, string имя)> _что = System.Array.Empty<(string, string)>();
    private System.Action<string>? _взять;

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
                   + "Собирать здесь нечего — а инструмент унести можно. Это чужое.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        текст.AddThemeColorOverride("font_color", new Color("#b8b0a0"));
        столбец.AddChild(текст);

        _лежат = new VBoxContainer();
        _лежат.AddThemeConstantOverride("separation", 4);
        столбец.AddChild(_лежат);

        _пусто = new Label
        {
            Text = "инструмента на верстаке больше нет",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _пусто.AddThemeColorOverride("font_color", new Color("#8a8474"));
        столбец.AddChild(_пусто);

        var закрыть = new Button { Text = "Отойти  [Esc]" };
        закрыть.Pressed += Убрать;
        столбец.AddChild(закрыть);
    }

    /// <summary>Показать верстак: что на нём лежит и чем это взять.</summary>
    public void Показать(IReadOnlyList<(string id, string имя)> лежат, System.Action<string> взять)
    {
        _что = лежат;
        _взять = взять;
        Visible = true;
        Разложить();
    }

    private void Разложить()
    {
        foreach (var узел in _лежат.GetChildren())
        {
            _лежат.RemoveChild(узел);
            узел.QueueFree();
        }
        foreach (var (id, имя) in _что)
        {
            string инструмент = id;
            var к = new Button { Text = $"взять: {имя}", Alignment = HorizontalAlignment.Left };
            к.Pressed += () =>
            {
                _взять?.Invoke(инструмент);
                _что = _что.Where(x => x.id != инструмент).ToList();
                Разложить();
            };
            _лежат.AddChild(к);
        }
        _пусто.Visible = _что.Count == 0;
    }

    public void Убрать()
    {
        if (!Visible)
            return;
        Visible = false;
        _взять = null;
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
