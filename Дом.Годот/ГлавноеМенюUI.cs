// Главное меню и экран нового человека (задание автора, пп. 19–20).
//
// Меню — первое, что видно: продолжить, новая игра, настройки, выход.
// «Продолжить» есть, только когда есть что продолжать: сохранённая жизнь
// (`user://жизнь.json`, пишется при каждом сне) или петля, у которой
// кончилась жизнь, но не кончился человек, — тогда начинается следующая
// жизнь того же героя. Ничего нет — кнопка погашена, и под ней сказано
// почему, а не спрятана: игрок должен видеть, что продолжения нет.
//
// Новая игра — экран нового человека. Имя даёт игрок; остальное задано
// (п. 20): мужчина, замкнутый, почти ни с кем не знаком, связей нет.
// Это не выбор, а описание — поэтому стоит строками, а не галочками:
// галочка, которую нельзя снять, обещает выбор, которого нет.
//
// Новая игра начинает и новую петлю: наследие прошлых жизней забывается.
// Если есть что забыть, экран говорит это до кнопки, а не после.
//
// Как и в настройках, кнопки берутся только мышью: фокус на кнопке делает
// пробел нажатием, и пробел, прожатый на экране между жизнями, начинал бы
// новую игру поверх старой. Имя набирается в поле, Enter — начать.

using Godot;

namespace Дом.Годот;

public partial class ГлавноеМенюUI : PanelContainer
{
    /// <summary>«Продолжить».</summary>
    public System.Action Продолжить { get; set; } = () => { };

    /// <summary>«Начать» на экране нового человека: имя уже в порядке.</summary>
    public System.Action<string> Начать { get; set; } = _ => { };

    /// <summary>«Настройки» — те же, что по Esc в игре.</summary>
    public System.Action Настройки { get; set; } = () => { };

    /// <summary>Что о герое известно до имени (`Герой.Шаблон`).</summary>
    public (int квартира, int этаж, int возраст, string роль)? Шаблон { get; set; }

    public bool открыт => Visible;

    private static readonly Color СВЕТЛО = new("#e8e2d0");
    private static readonly Color ТИХО = new("#8a8474");
    private static readonly Color БЕДА = new("#c86a5a");

    private VBoxContainer _меню = null!;
    private VBoxContainer _человек = null!;
    private Button _продолжить = null!;
    private Label _что_продолжить = null!;
    private Label _весть = null!;
    private LineEdit _имя = null!;
    private VBoxContainer _о_нём = null!;
    private Label _забудется = null!;
    private Label _не_годится = null!;

    public override void _Ready()
    {
        Visible = false;
        // живёт и на паузе: настройки поверх него ставят игру на паузу
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        // во весь экран: SetAnchorsPreset в дереве оставил бы нулевой прямоугольник
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("#12110f") });

        var середина = new CenterContainer();
        середина.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(середина);
        var оба = new VBoxContainer();
        середина.AddChild(оба);

        // ---- меню ----
        _меню = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        _меню.AddThemeConstantOverride("separation", 10);
        оба.AddChild(_меню);

        var имя_игры = new Label { Text = "ОПЯТЬ ЗДЕСЬ", HorizontalAlignment = HorizontalAlignment.Center };
        имя_игры.AddThemeFontSizeOverride("font_size", 44);
        имя_игры.AddThemeColorOverride("font_color", СВЕТЛО);
        _меню.AddChild(имя_игры);
        var под = Тихо("подъезд на пятнадцать соседей, метель на тридцать дней");
        под.HorizontalAlignment = HorizontalAlignment.Center;
        _меню.AddChild(под);
        _меню.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        _продолжить = Кнопка("Продолжить", () => Продолжить());
        _меню.AddChild(_продолжить);
        _что_продолжить = Тихо("");
        _что_продолжить.HorizontalAlignment = HorizontalAlignment.Center;
        _меню.AddChild(_что_продолжить);
        _меню.AddChild(Кнопка("Новая игра", К_человеку));
        _меню.AddChild(Кнопка("Настройки", () => Настройки()));
        _меню.AddChild(Кнопка("Выход", () => GetTree().Quit()));

        _весть = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 26),
        };
        _весть.AddThemeColorOverride("font_color", БЕДА);
        _меню.AddChild(_весть);

        // ---- новый человек ----
        _человек = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0), Visible = false };
        _человек.AddThemeConstantOverride("separation", 10);
        оба.AddChild(_человек);

        var заголовок = new Label { Text = "НОВЫЙ ЧЕЛОВЕК", HorizontalAlignment = HorizontalAlignment.Center };
        заголовок.AddThemeFontSizeOverride("font_size", 30);
        заголовок.AddThemeColorOverride("font_color", СВЕТЛО);
        _человек.AddChild(заголовок);
        _человек.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        var строка_имени = new HBoxContainer();
        строка_имени.AddThemeConstantOverride("separation", 12);
        _человек.AddChild(строка_имени);
        строка_имени.AddChild(Светло("Имя", 110));
        _имя = new LineEdit
        {
            PlaceholderText = "как его зовут",
            MaxLength = Дом.Ядро.Герой.ИМЯ_ДЛИННЕЕ,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _имя.TextSubmitted += _ => Готово();
        _имя.TextChanged += _ => _не_годится.Text = "";
        строка_имени.AddChild(_имя);

        var строка_пола = new HBoxContainer();
        строка_пола.AddThemeConstantOverride("separation", 12);
        _человек.AddChild(строка_пола);
        строка_пола.AddChild(Светло("Пол", 110));
        строка_пола.AddChild(Светло("мужчина", 0));

        _о_нём = new VBoxContainer();
        _о_нём.AddThemeConstantOverride("separation", 4);
        _человек.AddChild(_о_нём);

        _забудется = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _забудется.AddThemeColorOverride("font_color", БЕДА);
        _человек.AddChild(_забудется);

        _не_годится = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 24),
        };
        _не_годится.AddThemeColorOverride("font_color", БЕДА);
        _человек.AddChild(_не_годится);

        var низ = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        низ.AddThemeConstantOverride("separation", 16);
        _человек.AddChild(низ);
        низ.AddChild(Кнопка("Начать", Готово));
        низ.AddChild(Кнопка("Назад", К_меню));
    }

    /// <summary>
    /// Показать меню. <paramref name="продолжить"/> — что продолжится
    /// («Андрей · метель, день 3») или null: продолжать нечего.
    /// <paramref name="забудется"/> — что новая игра сотрёт, или null.
    /// </summary>
    public void Показать(string? продолжить, string? забудется, string весть = "")
    {
        _продолжить.Disabled = продолжить is null;
        _что_продолжить.Text = продолжить ?? "продолжать нечего — начни новую игру";
        _забудется.Text = забудется is null ? "" : $"Прошлая петля забудется: {забудется}.";
        _забудется.Visible = забудется is not null;
        _весть.Text = весть;
        К_меню();
        Visible = true;
    }

    /// <summary>Сразу экран нового человека — петля оборвалась
    /// «новым человеком».</summary>
    public void Показать_человека(string? забудется)
    {
        Показать(null, забудется);
        К_человеку();
    }

    public void Убрать() => Visible = false;

    /// <summary>Esc на экране нового человека — назад в меню. Истина —
    /// если Esc ушёл сюда.</summary>
    public bool Esc()
    {
        if (!Visible || !_человек.Visible)
            return false;
        К_меню();
        return true;
    }

    private void К_меню()
    {
        _человек.Visible = false;
        _меню.Visible = true;
    }

    private void К_человеку()
    {
        _весть.Text = "";
        _меню.Visible = false;
        _человек.Visible = true;
        foreach (var узел in _о_нём.GetChildren())
            узел.QueueFree();
        if (Шаблон is { } ш)
            _о_нём.AddChild(Тихо($"{ш.возраст} {Штук(ш.возраст, "год", "года", "лет")}, "
                                  + $"квартира {ш.квартира}, {ш.этаж}-й этаж. {ш.роль}."));
        _о_нём.AddChild(Тихо("Замкнутый: сам к соседям не тянется, и разговоры даются ему тяжелее, чем другим."));
        _о_нём.AddChild(Тихо("Почти ни с кем в доме не знаком: соседи знают его в лицо, не больше — "
                              + "ни доверия, ни обид."));
        _о_нём.AddChild(Тихо("Связей никаких: ни родни рядом, ни друга в подъезде — рассчитывать не на кого."));
        _не_годится.Text = "";
        // фокус в поле — отложенно: в кадре, где узел стал видимым, он не берётся
        _имя.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Готово()
    {
        if (Дом.Ядро.Герой.Имя(_имя.Text, out var почему) is not string имя)
        {
            _не_годится.Text = почему ?? "";
            return;
        }
        _имя.Text = имя;
        Начать(имя);
    }

    /// <summary>«34 года», «21 год», «40 лет».</summary>
    public static string Штук(int n, string один, string два, string пять)
        => (n % 100) is >= 11 and <= 14 ? пять
           : (n % 10) switch { 1 => один, 2 or 3 or 4 => два, _ => пять };

    private static Button Кнопка(string текст, System.Action что)
    {
        var к = new Button
        {
            Text = текст,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(260, 40),
        };
        к.Pressed += что;
        return к;
    }

    private static Label Светло(string текст, float ширина)
    {
        var л = new Label { Text = текст, CustomMinimumSize = new Vector2(ширина, 0) };
        л.AddThemeColorOverride("font_color", СВЕТЛО);
        return л;
    }

    private static Label Тихо(string текст)
    {
        var л = new Label { Text = текст, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        л.AddThemeFontSizeOverride("font_size", 14);
        л.AddThemeColorOverride("font_color", ТИХО);
        return л;
    }
}
