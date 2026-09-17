// Карман вне времени (ГДД 9): открывается своей клавишей (по умолчанию I),
// время стоит — «в инвентаре и меню время стоит».
//
// «Время стоит» — два условия, и оба держит этот экран:
//   · открыться он может только пока дом спит на вопросе: в любой другой
//     момент поток дома считает, и соседи проживали бы часы при открытом
//     кармане, а экран лез бы в шкаф из-под руки у считающего;
//   · пока он открыт, мир на паузе, как под настройками: герой не идёт,
//     шкала взлома и обыска не бежит, створки не двигаются.
// Первая версия не держала ни того, ни другого: карман ловил клавишу сам,
// корневой узел об этом не знал, и герой ходил с открытым карманом.
//
// Перекладывать можно только **у своей полки** — там, где шкаф квартиры
// стоит в мире (`Мир.Вещи`, полка показывает запасы коробками). По клавише
// кармана он только показывает, что в нём лежит: раньше с экрана кармана
// можно было положить банку из домашнего шкафа, стоя в подвале.
// Найденное в погребе и гараже ложится в карман прямо из ящика —
// это окно находки, а не этот экран.
//
// У полки экран — ещё и место мыслей: быт, одежда, генератор делаются
// отсюда кнопками, как у печи клавишей. Выбранная в телефоне мысль
// помечена.
//
// И главное, чего этот экран не прячет: **вещь исчезает из мира.** Кто
// стоит с героем в одной квартире, это видит, и экран говорит об этом
// прямо — до того, как игрок нажмёт, а не после.

using Godot;
using Дом.Экран;
using Дом.Ядро;

namespace Дом.Годот;

public partial class КарманUI : PanelContainer
{
    private VBoxContainer _шкаф = null!;
    private VBoxContainer _внутри = null!;
    private Label _шкаф_шапка = null!;
    private Label _внутри_шапка = null!;
    private Label _заголовок = null!;
    private Label _свидетели = null!;
    private Label _где = null!;
    private VBoxContainer _мысли = null!;
    private Button _закрыть = null!;
    private IReadOnlyList<(string что, System.Action сделать)> _дела =
        System.Array.Empty<(string, System.Action)>();

    /// <summary>Откуда брать. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    /// <summary>Можно ли открыться: дом спит на вопросе.</summary>
    public System.Func<bool> МожноОткрыть { get; set; } = () => false;

    /// <summary>Сказать герою, почему не открылось.</summary>
    public System.Action<string> Сказать { get; set; } = _ => { };

    /// <summary>Открылся или закрылся: корневому узлу — отпустить мышь,
    /// остановить героя, показать полку заново.</summary>
    public System.Action Сменился { get; set; } = () => { };

    /// <summary>Открыт ли сейчас.</summary>
    public bool открыт => Visible;

    /// <summary>Открыт ли у полки: только тогда вещи перекладываются.</summary>
    public bool у_полки { get; private set; }

    public override void _Ready()
    {
        Visible = false;
        // живёт и на паузе: пока он открыт, время стоит, а кнопки нажимаются
        ProcessMode = ProcessModeEnum.Always;
        // во весь экран: SetAnchorsPreset в дереве оставил бы нулевой прямоугольник
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var фон = new StyleBoxFlat { BgColor = new Color("#12110f") };
        AddThemeStyleboxOverride("panel", фон);

        var середина = new CenterContainer();
        середина.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(середина);

        var столбец = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        столбец.AddThemeConstantOverride("separation", 8);
        середина.AddChild(столбец);

        _заголовок = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        _заголовок.AddThemeFontSizeOverride("font_size", 18);
        столбец.AddChild(_заголовок);

        _где = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _где.AddThemeColorOverride("font_color", new Color("#a8a294"));
        столбец.AddChild(_где);

        _свидетели = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _свидетели.AddThemeColorOverride("font_color", new Color("#c86a5a"));
        столбец.AddChild(_свидетели);

        var две = new HBoxContainer();
        две.AddThemeConstantOverride("separation", 24);
        столбец.AddChild(две);

        (_шкаф, _шкаф_шапка) = Колонка(две);
        (_внутри, _внутри_шапка) = Колонка(две);

        _мысли = new VBoxContainer();
        _мысли.AddThemeConstantOverride("separation", 2);
        столбец.AddChild(_мысли);

        _закрыть = new Button();
        _закрыть.Pressed += Убрать;
        столбец.AddChild(_закрыть);
    }

    private static (VBoxContainer, Label) Колонка(HBoxContainer куда)
    {
        var столбец = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        куда.AddChild(столбец);
        var l = new Label();
        l.AddThemeColorOverride("font_color", new Color("#6f6a5e"));
        столбец.AddChild(l);
        return (столбец, l);
    }

    /// <summary>
    /// Открыть. <paramref name="у_полки"/> — открыт у своей полки: тогда
    /// вещи перекладываются, а <paramref name="дела"/> — мысли, которые
    /// делаются у полки (что и как сделать), ложатся кнопками.
    /// </summary>
    public void Показать(bool у_полки = false,
                         IReadOnlyList<(string что, System.Action сделать)>? дела = null)
    {
        // на паузе, которую держит не он (настройки), не открывается:
        // закрываясь, он снял бы чужую паузу
        if (Visible || Сеанс is null || GetTree().Paused)
            return;
        if (!МожноОткрыть())
        {
            Сказать("дом ещё считает ход — подожди");
            return;
        }
        if (Сеанс.я.карман is null && !у_полки)
            return;                     // кармана нет — нечего и открывать
        this.у_полки = у_полки;
        _дела = дела ?? System.Array.Empty<(string, System.Action)>();
        Visible = true;
        GetTree().Paused = true;
        // клавишу назначает игрок — кнопка зовёт её по имени
        _закрыть.Text = у_полки
            ? $"отойти от полки  ({Управление.Имя(Клавиши.ДЕЙСТВИЕ)} или Esc)"
            : $"закрыть  ({Управление.Имя(Клавиши.КАРМАН)} или Esc)";
        Обновить();
        Сменился();
    }

    public void Убрать()
    {
        if (!Visible)
            return;
        Visible = false;
        GetTree().Paused = false;
        _свидетели.Text = "";
        _дела = System.Array.Empty<(string, System.Action)>();
        Сменился();
    }

    private void Обновить()
    {
        var я = Сеанс!.я;
        var карман = я.карман;
        foreach (var столбец in new[] { _шкаф, _внутри })
            foreach (var узел in столбец.GetChildren().Skip(1))
            {
                столбец.RemoveChild(узел);
                узел.QueueFree();
            }
        foreach (var узел in _мысли.GetChildren())
        {
            _мысли.RemoveChild(узел);
            узел.QueueFree();
        }

        string место = карман is null
            ? ""
            : $"карман вне времени — {Текст.Ф(карман.занято, 1)} из {Текст.Ф(карман.предел, 1)}";
        _заголовок.Text = у_полки
            ? "ПОЛКА — ШКАФ КВАРТИРЫ" + (место.Length > 0 ? $"   ·   {место}" : "")
            : место;
        _где.Text = у_полки
            ? "время стоит; положенное в карман исчезает из мира — кто рядом, тот видит"
            : $"время стоит. Выложить и взять — дома, у своей полки ({Управление.В_скобках(Клавиши.ДЕЙСТВИЕ)})";

        // шкаф — только у полки: это его место в мире
        _шкаф.Visible = у_полки;
        _шкаф_шапка.Text = карман is null ? "на полке" : "на полке — в карман";
        if (у_полки)
            foreach (var р in я.stock.Ключи.OrderBy(x => x, StringComparer.Ordinal))
            {
                double сколько = я.stock.Взять(р, 0.0);
                if (сколько <= 0.0)
                    continue;
                string строка = $"{р} {Текст.G(Текст.Округлить(сколько, 2))}";
                if (карман is null)
                    Надпись(_шкаф, строка);
                else
                    Ряд(_шкаф, строка + "  →", свободно: карман.свободно > 0,
                        одну: () => Положить(р, System.Math.Min(1.0, сколько)),
                        всё: () => Положить(р, сколько));
            }

        _внутри.Visible = карман is not null;
        _внутри_шапка.Text = у_полки ? "в кармане — на полку" : "в кармане";
        if (карман is not null)
        {
            foreach (var р in карман.ресурсы.OrderBy(x => x, StringComparer.Ordinal))
            {
                string строка = $"{р} {Текст.G(Текст.Округлить(карман.Сколько(р), 2))}";
                if (у_полки)
                    Ряд(_внутри, "←  " + строка, свободно: true,
                        одну: () => Достать(р, 1.0),
                        всё: () => Достать(р, карман.Сколько(р)));
                else
                    Надпись(_внутри, строка);
            }
            if (карман.ресурсы.Count == 0)
                Надпись(_внутри, "пусто");
        }

        // мысли, которые делаются у полки
        if (у_полки && _дела.Count > 0)
        {
            var шапка = new Label { Text = "здесь же можно:" };
            шапка.AddThemeColorOverride("font_color", new Color("#6f6a5e"));
            _мысли.AddChild(шапка);
            foreach (var (что, сделать) in _дела)
            {
                var к = new Button { Text = что, Alignment = HorizontalAlignment.Left };
                var дело = сделать;
                // сначала закрыться: пока экран открыт, дому не отвечают
                к.Pressed += () =>
                {
                    Убрать();
                    дело();
                };
                _мысли.AddChild(к);
            }
        }
    }

    private static void Надпись(VBoxContainer куда, string текст)
    {
        var l = new Label { Text = текст };
        l.AddThemeColorOverride("font_color", new Color("#c8c2b0"));
        куда.AddChild(l);
    }

    /// <summary>Строка с двумя кнопками: переложить одну и переложить всё.</summary>
    private static void Ряд(VBoxContainer куда, string текст, bool свободно,
                            System.Action одну, System.Action всё)
    {
        var ряд = new HBoxContainer();
        var к = new Button
        {
            Text = текст,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Disabled = !свободно,
        };
        к.Pressed += одну;
        ряд.AddChild(к);
        var в = new Button { Text = "всё", Disabled = !свободно };
        в.Pressed += всё;
        ряд.AddChild(в);
        куда.AddChild(ряд);
    }

    private void Положить(string ресурс, double сколько)
    {
        if (!у_полки || Сеанс?.я.карман is not { } карман)
            return;
        var видели = карман.Положить(Сеанс.дом, Сеанс.я, ресурс, сколько);
        _свидетели.Text = видели.Count == 0
            ? ""
            : "Это видели: " + string.Join(", ", видели.Select(
                  в => Сеанс.дом.get(в.кто)?.@short ?? в.кто))
              + " — вещь исчезла у них на глазах.";
        Обновить();
    }

    private void Достать(string ресурс, double сколько)
    {
        if (!у_полки || Сеанс?.я.карман is not { } карман)
            return;
        карман.Достать(Сеанс.я, ресурс, сколько);
        Обновить();
    }

    /// <summary>Клавиша кармана открывает и закрывает, Esc закрывает,
    /// у полки закрывает и клавиша действия. Повтор клавиши отбрасывается —
    /// зажатая иначе мигала бы экраном.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (Управление.Нажато(e, Клавиши.КАРМАН))
        {
            if (Visible)
                Убрать();
            else
                Показать();
            AcceptEvent();
        }
        else if (Visible && (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
                             || (у_полки && Управление.Нажато(e, Клавиши.ДЕЙСТВИЕ))))
        {
            Убрать();
            AcceptEvent();
        }
    }
}
