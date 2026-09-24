// Экран между жизнями: монолог на старте и разбор на смерти.
//
// Один узел на оба, потому что это одна и та же вещь: метель остановилась,
// на весь экран несколько строк, и дальше идут только когда игрок сам
// нажмёт. Разводить их в два узла значило бы дважды написать одно и то же
// и потом дважды чинить.
//
// Отдельно — поле для слова (ГДД 11, точка невозврата). Оно появляется
// ровно один раз за петлю и ровно на одном экране, поэтому живёт здесь же,
// а не в своём.
//
// И ряды с кнопками — очки навыков (ГДД 8.2): тратит их игрок, между
// жизнями, на этом же экране. Долго их было негде тратить вовсе: монолог
// писал «очков навыков: N», а ни одна кнопка не звала `Наследие.Поднять`,
// и навыки в игре не росли. Кнопки берутся только мышью — пробел и Enter
// здесь значат «дальше».
//
// Чего этот экран не делает: не показывает рассудок числом. Ни на смерти,
// ни на старте — нигде. Он показывает, из чего рассудок сложился («видел
// расправу», «был один»), и этого достаточно: игрок должен заметить,
// что с героем стало, а не прочитать цифру.

using Godot;

namespace Дом.Годот;

public partial class ПереходUI : PanelContainer
{
    private VBoxContainer _столбец = null!;
    private Label _заголовок = null!;
    private VBoxContainer _строки = null!;
    private VBoxContainer _ряды = null!;
    private System.Func<IReadOnlyList<(string текст, string? кнопка, bool можно, System.Action? сделать)>>? _ряды_дай;
    private LineEdit _поле = null!;
    private Button _дальше = null!;
    private System.Action<string>? _ответ;

    public override void _Ready()
    {
        Visible = false;
        // во весь экран: SetAnchorsPreset в дереве оставил бы нулевой прямоугольник
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Непрозрачная подложка. Без неё сквозь монолог просвечивает лента
        // прошлой метели — а это ровно то, чего герой уже не видит: для него
        // сейчас утро первого дня
        var фон = new StyleBoxFlat { BgColor = new Color("#12110f") };
        AddThemeStyleboxOverride("panel", фон);

        var середина = new CenterContainer();
        середина.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(середина);

        _столбец = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        середина.AddChild(_столбец);

        _заголовок = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        _столбец.AddChild(_заголовок);

        _строки = new VBoxContainer();
        _столбец.AddChild(_строки);

        _ряды = new VBoxContainer();
        _ряды.AddThemeConstantOverride("separation", 4);
        _столбец.AddChild(_ряды);

        _поле = new LineEdit
        {
            Visible = false,
            PlaceholderText = "слово",
            Alignment = HorizontalAlignment.Center,
        };
        _поле.TextSubmitted += с => Дальше(с);
        _столбец.AddChild(_поле);

        _дальше = new Button { Text = "дальше" };
        _дальше.Pressed += () => Дальше(_поле.Visible ? _поле.Text : "");
        _столбец.AddChild(_дальше);
    }

    /// <summary>Показать экран. <paramref name="спросить_слово"/> —
    /// только для точки невозврата.</summary>
    public void Показать(string заголовок, IEnumerable<string> строки,
                         System.Action<string> ответ, bool спросить_слово = false,
                         System.Func<IReadOnlyList<(string текст, string? кнопка, bool можно, System.Action? сделать)>>? ряды = null)
    {
        _ряды_дай = ряды;
        Ряды();
        _заголовок.Text = заголовок;
        foreach (var узел in _строки.GetChildren())
            узел.QueueFree();
        foreach (var с in строки)
        {
            var l = new Label
            {
                Text = с,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            l.AddThemeColorOverride("font_color", new Color("#a8a294"));
            _строки.AddChild(l);
        }
        _поле.Visible = спросить_слово;
        _поле.Text = "";
        _ответ = ответ;
        Visible = true;

        // Пока спрашивают слово, кнопку нельзя брать клавишей: фокус после
        // показа встаёт не сразу, и пробел уходил на кнопку — вместе с тем,
        // что успело набраться в поле. Так «нирвана» включалась сама,
        // без единого осмысленного нажатия. Мышью кнопка работает по-прежнему
        _дальше.FocusMode = спросить_слово ? Control.FocusModeEnum.None
                                           : Control.FocusModeEnum.All;
        // и фокус ставится отложенно: в том же кадре, где узел только стал
        // видимым, он не берётся
        (спросить_слово ? (Control)_поле : _дальше).CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Убрать()
    {
        Visible = false;
        _ответ = null;
        _ряды_дай = null;
    }

    /// <summary>Ряды с кнопками — заново после каждого нажатия: очки
    /// и уровни поменялись.</summary>
    private void Ряды()
    {
        foreach (var узел in _ряды.GetChildren())
        {
            _ряды.RemoveChild(узел);
            узел.QueueFree();
        }
        if (_ряды_дай is null)
            return;
        foreach (var (текст, кнопка, можно, сделать) in _ряды_дай())
        {
            var ряд = new HBoxContainer();
            ряд.AddThemeConstantOverride("separation", 12);
            var надпись = new Label
            {
                Text = текст,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            надпись.AddThemeColorOverride("font_color", new Color(кнопка is null ? "#e8e2d0" : "#c8c2b0"));
            ряд.AddChild(надпись);
            if (кнопка is not null)
            {
                var к = new Button
                {
                    Text = кнопка,
                    Disabled = !можно,
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(150, 0),
                };
                var что = сделать;
                к.Pressed += () =>
                {
                    что?.Invoke();
                    Ряды();
                };
                ряд.AddChild(к);
            }
            _ряды.AddChild(ряд);
        }
    }

    private void Дальше(string слово)
    {
        var кому = _ответ;
        Убрать();
        кому?.Invoke(слово.Trim());
    }

    /// <summary>
    /// Пробел или Enter — дальше. Цифры нарочно не годятся: этот экран
    /// стоит между жизнями, а цифры на нём означали бы ровно то же, что
    /// в вопросе, — и первый же зажатый «0» пролистал бы и смерть тоже.
    /// </summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _поле.Visible || e is not InputEventKey к
            || !к.Pressed || к.Echo)
            return;
        if (к.Keycode is Key.Space or Key.Enter or Key.KpEnter)   // клавиша: «дальше» между жизнями — не действие игры
        {
            Дальше("");
            AcceptEvent();
        }
    }
}
