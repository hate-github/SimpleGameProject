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
// И здесь же — инструменты (`Дом.Ядро.Снаряжение`): что лежит дома и что
// взято с собой. Голыми руками замок не сорвать, и молоток, чтобы сбить
// чужой замок, сперва берут с полки. По клавише кармана видно, что при себе.
//
// Открывает экран корневой узел, а не своя клавиша: он знает, не идёт ли
// долгое дело и не открыто ли другое окно (задание автора, п. 1: карман
// открывался посреди взлома и ложился поверх шкалы). Сам экран ловит
// только то, чем его закрывают.
//
// И главное, чего этот экран не прячет: **вещь исчезает из мира.** Кто
// стоит с героем в одной квартире, это видит, и экран говорит об этом
// прямо — до того, как игрок нажмёт, а не после.
//
// Еда здесь — вещи (docs/ЕДА_ПЛАН.md, этап 2): ячейками с иконками,
// снятыми с моделей автора, в трёх местах — на полке (у полки), в ноше
// и в кармане. Навёл — подпись; нажал — карточка (`КарточкаЕды`):
// модель крутится, рядом описание и дела — съесть, в карман, на полку.
// Прочие запасы — строками, как были: моделей у них пока нет.

using Godot;
using Дом.Экран;
using Дом.Ядро;

namespace Дом.Годот;

public partial class КарманUI : PanelContainer
{
    private VBoxContainer _шкаф = null!;
    private VBoxContainer _внутри = null!;
    private VBoxContainer _в_ноше = null!;
    private Label _шкаф_шапка = null!;
    private Label _внутри_шапка = null!;
    private Label _ноша_шапка = null!;
    private Label _наведено = null!;
    private ИконкиЕды _иконки = null!;
    private КарточкаЕды _карточка = null!;
    private Control _покров = null!;
    private ЯчейкаЕды? _открыта;
    private string _весть = "";
    private Label _заголовок = null!;
    private Label _свидетели = null!;
    private Label _где = null!;
    private VBoxContainer _мысли = null!;
    private VBoxContainer _инструменты = null!;
    private Button _закрыть = null!;
    private IReadOnlyList<(string что, System.Action сделать)> _дела =
        System.Array.Empty<(string, System.Action)>();

    /// <summary>Откуда брать. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    /// <summary>Каталог инструментов — чтобы звать их по имени.</summary>
    public System.Func<Инструменты> Каталог { get; set; } = () => Инструменты.НИКАКИХ;

    /// <summary>Ручки петли — сколько места в кармане занимает вещь.</summary>
    public System.Func<РучкиПетли?> Ручки { get; set; } = () => null;

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
        (_в_ноше, _ноша_шапка) = Колонка(две);
        (_внутри, _внутри_шапка) = Колонка(две);

        // что под мышью — строкой под ячейками: подсказка Godot на паузе
        // может и не прийти, а эта приходит всегда
        _наведено = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _наведено.AddThemeColorOverride("font_color", new Color("#d8b070"));
        столбец.AddChild(_наведено);

        _инструменты = new VBoxContainer();
        _инструменты.AddThemeConstantOverride("separation", 2);
        столбец.AddChild(_инструменты);

        _мысли = new VBoxContainer();
        _мысли.AddThemeConstantOverride("separation", 2);
        столбец.AddChild(_мысли);

        _закрыть = new Button();
        _закрыть.Pressed += Убрать;
        столбец.AddChild(_закрыть);

        _иконки = new ИконкиЕды();
        AddChild(_иконки);

        // карточка — поверх всего, на затемнении: нажал мимо — закрылась
        _покров = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _покров.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_покров);
        var тень = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        тень.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        тень.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                _карточка.Убрать();
        };
        _покров.AddChild(тень);
        var по_центру = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        по_центру.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _покров.AddChild(по_центру);
        _карточка = new КарточкаЕды();
        _карточка.Закрыта = () =>
        {
            _покров.Visible = false;
            _открыта = null;
        };
        по_центру.AddChild(_карточка);
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
        if (Сеанс.я.карман is null && Сеанс.я.снаряжение is null && !у_полки)
            return;                     // ни кармана, ни снаряжения — нечего и открывать
        this.у_полки = у_полки;
        _руки_весть = "";
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
        _наведено.Text = "";
        _весть = "";
        _карточка.Убрать();
        _дела = System.Array.Empty<(string, System.Action)>();
        Сменился();
    }

    private void Обновить()
    {
        var я = Сеанс!.я;
        var карман = я.карман;
        // еда вещами сводится с числами дома, пока он спит: экран открыт
        // только тогда
        var продукты = Продукты.У(я);
        продукты?.Сверить(Сеанс.дом, я);
        foreach (var столбец in new[] { _шкаф, _в_ноше, _внутри })
            foreach (var узел in столбец.GetChildren().Skip(1))
            {
                столбец.RemoveChild(узел);
                узел.QueueFree();
            }
        foreach (var столбец in new[] { _мысли, _инструменты })
            foreach (var узел in столбец.GetChildren())
            {
                столбец.RemoveChild(узел);
                узел.QueueFree();
            }
        Инструменты_героя();

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
                if (сколько <= 0.0 || (продукты is not null && р == Продукты.ЕДА))
                    continue;
                string строка = $"{р} {Текст.G(Текст.Округлить(сколько, 2))}";
                if (карман is null)
                    Надпись(_шкаф, строка);
                else
                    Ряд(_шкаф, строка + "  →", свободно: карман.свободно > 0,
                        одну: () => Положить(р, System.Math.Min(1.0, сколько)),
                        всё: () => Положить(р, сколько));
            }

        if (у_полки && продукты is not null)
            Сетка(_шкаф, продукты, МестоЕды.ПОЛКА);

        // ноша — что несут: смотреть и есть можно, выложить — у порога
        var ноша = я.ноша;
        _в_ноше.Visible = ноша is { пусто: false };
        if (ноша is { пусто: false, тара: Тара тара } && Ручки() is { } ручки_)
        {
            _ноша_шапка.Text = $"{тара.Где()} — {Текст.G(Текст.Округлить(ноша.занято, 1))} из {Текст.G(тара.мест(ручки_))}";
            foreach (var р in ноша.ресурсы.OrderBy(x => x, StringComparer.Ordinal))
                if (продукты is null || р != Продукты.ЕДА)
                    Надпись(_в_ноше, $"{р} {Текст.G(Текст.Округлить(ноша.Сколько(р), 2))}");
            if (продукты is not null)
                Сетка(_в_ноше, продукты, МестоЕды.НОША);
        }

        _внутри.Visible = карман is not null;
        _внутри_шапка.Text = у_полки ? "в кармане — на полку" : "в кармане";
        if (карман is not null)
        {
            foreach (var р in карман.ресурсы.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (продукты is not null && р == Продукты.ЕДА)
                    continue;
                string строка = $"{р} {Текст.G(Текст.Округлить(карман.Сколько(р), 2))}";
                if (у_полки)
                    Ряд(_внутри, "←  " + строка, свободно: true,
                        одну: () => Достать(р, 1.0),
                        всё: () => Достать(р, карман.Сколько(р)));
                else
                    Надпись(_внутри, строка);
            }
            if (продукты is not null)
                Сетка(_внутри, продукты, МестоЕды.КАРМАН);
            if (карман.ресурсы.Count == 0)
                Надпись(_внутри, "пусто");
        }

        // карточка открыта — показать её заново: вещь могла стать початой,
        // а стопка — меньше
        if (_открыта is { } была && продукты is not null)
        {
            var все = Продукты.МЕСТА.SelectMany(м => Инвентарь.Ячейки(продукты, м, Порция())).ToList();
            var та = все.FirstOrDefault(я_ => я_.номера.Contains(_карточка.номер ?? -1))
                     ?? все.FirstOrDefault(я_ => я_.где == была.где && я_.вид == была.вид && я_.состояние == была.состояние)
                     ?? все.FirstOrDefault(я_ => я_.где == была.где && я_.вид == была.вид);
            if (та is null)
            {
                _карточка.Убрать();
                _наведено.Text = _весть;
            }
            else
                Открыть(та);
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

    /// <summary>Инструменты: у полки — взять с собой и положить дома,
    /// по клавише кармана — только что при себе.</summary>
    // что ответили руки на последнее нажатие — строкой под списком
    private string _руки_весть = "";

    /// <summary>
    /// Что в руках и что при себе (задание автора, п. 22). По клавише
    /// кармана — взять в руки или убрать из рук; у полки — ещё взять
    /// с собой и положить дома. Тяжёлое, взятое с полки, сразу в руках,
    /// и второго тяжёлого не взять; спрятать тяжёлое нельзя, только
    /// положить дома.
    /// </summary>
    private void Инструменты_героя()
    {
        var я = Сеанс!.я;
        if (я.снаряжение is not { } с)
            return;
        var каталог = Каталог();
        string Имя(string id) => каталог.в_руках.TryGetValue(id, out var в) ? в.имя : id;
        var шапка = new Label { Text = "в руках и при себе" };
        шапка.AddThemeColorOverride("font_color", new Color("#6f6a5e"));
        _инструменты.AddChild(шапка);
        Надпись(_инструменты, с.в_руках is string держит
            ? $"в руках: {Имя(держит)}"
              + (с.Тяжёлое(держит, каталог) ? " — тяжёлое, не спрятать: положить можно дома у полки" : "")
            : "руки пусты");

        void Ряд_(string текст, string кнопка, System.Action сделать)
        {
            var ряд = new HBoxContainer();
            var надпись = new Label { Text = текст, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            надпись.AddThemeColorOverride("font_color", new Color("#c8c2b0"));
            ряд.AddChild(надпись);
            var к = new Button { Text = кнопка };
            к.Pressed += () =>
            {
                сделать();
                Обновить();
            };
            ряд.AddChild(к);
            _инструменты.AddChild(ряд);
        }

        if (!у_полки)
        {
            var можно = с.Можно_держать(я, каталог);
            if (можно.Count == 0)
                Надпись(_инструменты, "при себе ничего — голыми руками замок не сорвать; "
                                      + "взять инструменты можно дома, у полки");
            foreach (string id in можно)
            {
                string что = id;
                bool в_руках = с.в_руках == что;
                Ряд_(что == каталог.Оружие_в_руки(я.weapon) ? $"оружие: {Имя(что)}" : $"с собой: {Имя(что)}",
                     в_руках ? "убрать из рук" : "в руки",
                     () => _руки_весть = в_руках
                         ? с.Убрать(каталог) ?? $"убрал: {Имя(что)}"
                         : с.Достать(что, я, каталог) ?? $"в руках: {Имя(что)}");
            }
        }
        else
        {
            foreach (string id in с.дома.ToList())
            {
                string инструмент = id;
                Ряд_($"дома: {Имя(инструмент)}", "взять с собой", () =>
                {
                    if (с.Нельзя_взять(инструмент, каталог) is string нельзя)
                        _руки_весть = нельзя;
                    else if (с.Взять(инструмент, каталог))
                        _руки_весть = с.в_руках == инструмент
                            ? $"{Имя(инструмент)} — тяжёлое, в руках" : $"взял: {Имя(инструмент)}";
                });
            }
            foreach (string id in с.с_собой.ToList())
            {
                string инструмент = id;
                Ряд_($"с собой: {Имя(инструмент)}", "положить дома", () =>
                {
                    с.Положить(инструмент);
                    _руки_весть = $"положил: {Имя(инструмент)}";
                });
            }
            if (с.дома.Count == 0 && с.с_собой.Count == 0)
                Надпись(_инструменты, "инструментов нет");
        }
        // карман вне времени держит и вещи — их нет в мире, и отобрать их
        // нельзя (п. 17: из камеры выбираются тем, что было в кармане).
        // Прятать можно где угодно: вещь при себе, а не на полке
        if (я.карман is { } карман && Ручки() is { } ручки)
        {
            foreach (string id in с.с_собой.OrderBy(x => x, StringComparer.Ordinal).ToList())
            {
                string вещь = id;
                if (каталог.в_руках.TryGetValue(вещь, out var в) && в.носят)
                    continue;
                Ряд_($"при себе: {Имя(вещь)}", $"в карман ({Текст.G(Карман.Вес(вещь, каталог, ручки))})", () =>
                    _руки_весть = карман.Спрятать(я, вещь, каталог, ручки) ?? $"{Имя(вещь)} — в кармане, в мире её больше нет");
            }
            foreach (string id in карман.инструменты.ToList())
            {
                string вещь = id;
                Ряд_($"в кармане: {Имя(вещь)}", "вынуть", () =>
                    _руки_весть = карман.Вынуть(я, вещь, каталог) ?? $"вынул: {Имя(вещь)}");
            }
        }
        if (_руки_весть.Length > 0)
        {
            var весть = new Label { Text = _руки_весть };
            весть.AddThemeColorOverride("font_color", new Color("#d8b070"));
            _инструменты.AddChild(весть);
        }
    }

    // ------------------------------------------------------------ еда ячейками

    private double Порция() => Сеанс is null ? 45.0 : Действия.порция(Сеанс.дом, Сеанс.я, Сеанс.дом.B);

    /// <summary>Ячейки еды места: иконка с модели, «×2» у стопки, подпись
    /// при наведении, карточка по нажатию.</summary>
    private void Сетка(VBoxContainer куда, Продукты продукты, МестоЕды где)
    {
        var ячейки = Инвентарь.Ячейки(продукты, где, Порция());
        if (ячейки.Count == 0)
            return;
        var сетка = new GridContainer { Columns = 4 };
        сетка.AddThemeConstantOverride("h_separation", 6);
        сетка.AddThemeConstantOverride("v_separation", 6);
        foreach (var ячейка in ячейки)
        {
            var я_ = ячейка;
            var иконка = _иконки.Дай(я_.модель, я_.меши);
            var к = new Button
            {
                CustomMinimumSize = new Vector2(84, 92),
                Icon = иконка,
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top,
                Text = иконка is null ? я_.имя : (я_.штук > 1 ? $"×{я_.штук}" : " "),
                TooltipText = я_.подпись,
                ClipText = true,
            };
            if (я_.свежесть != Свежесть.СВЕЖАЯ)
                к.AddThemeColorOverride("font_color", new Color("#c86a5a"));
            к.MouseEntered += () => _наведено.Text = я_.подпись;
            к.MouseExited += () =>
            {
                if (_наведено.Text == я_.подпись)
                    _наведено.Text = "";
            };
            к.Pressed += () =>
            {
                _весть = "";
                Открыть(я_);
            };
            сетка.AddChild(к);
        }
        куда.AddChild(сетка);
    }

    /// <summary>Карточка стопки: дела — съесть всегда; у полки — ещё
    /// в карман с полки и на полку из кармана.</summary>
    private void Открыть(ЯчейкаЕды ячейка)
    {
        if (Сеанс is null || Продукты.У(Сеанс.я) is not { } продукты
            || продукты.Найти(ячейка.номера[0]) is not { } вещь)
            return;
        var дела = new List<(string, System.Action)> { ("съесть", () => Съесть(вещь)) };
        if (у_полки && ячейка.где == МестоЕды.ПОЛКА && Сеанс.я.карман is not null)
            дела.Add(("в карман", () => Переложить(вещь, МестоЕды.КАРМАН)));
        if (у_полки && ячейка.где == МестоЕды.КАРМАН)
            дела.Add(("на полку", () => Переложить(вещь, МестоЕды.ПОЛКА)));
        _открыта = ячейка;
        _покров.Visible = true;
        _карточка.Показать(ячейка, дела, _весть);
    }

    private void Съесть(Продукт вещь)
    {
        if (Сеанс is null || Продукты.У(Сеанс.я) is not { } продукты)
            return;
        string имя = продукты.Вид(вещь).имя;
        var с = продукты.Съесть(Сеанс.дом, Сеанс.я, вещь);
        _весть = с.почему ?? (с.отравился
            ? $"{имя}: съел — и сразу понял, что зря. Мутит; сытости вдвое меньше, пить хочется"
            : $"{имя}: съел {(Math.Abs(с.единиц - продукты.Вид(вещь).единиц) < 1e-6 ? "всё" : "сколько влезло")} — "
              + $"сытость +{Текст.G(Math.Round(с.сытости))}"
              + (с.настроения > 0.05 ? ", и на душе чуть легче" : ""));
        Обновить();
    }

    private void Переложить(Продукт вещь, МестоЕды куда)
    {
        if (Сеанс is null || Продукты.У(Сеанс.я) is not { } продукты)
            return;
        string имя = продукты.Вид(вещь).имя;
        string? почему = продукты.Переложить(Сеанс.дом, Сеанс.я, вещь, куда, out var видели);
        _весть = почему ?? (куда == МестоЕды.КАРМАН ? $"{имя} — в кармане, в мире её больше нет" : $"{имя} — на полке");
        _свидетели.Text = видели.Count == 0
            ? ""
            : "Это видели: " + string.Join(", ", видели.Select(
                  в => Сеанс.дом.get(в.кто)?.@short ?? в.кто))
              + " — вещь исчезла у них на глазах.";
        Обновить();
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

    /// <summary>Открытый экран закрывают клавиша кармана и Esc, у полки —
    /// и клавиша действия. Открывает его корневой узел. Повтор клавиши
    /// отбрасывается — зажатая иначе мигала бы экраном.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible)
            return;
        // Esc при карточке закрывает карточку, а не весь экран
        if (_карточка.Visible && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            _карточка.Убрать();
            AcceptEvent();
            return;
        }
        if (Управление.Нажато(e, Клавиши.КАРМАН)
            || e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            || (у_полки && Управление.Нажато(e, Клавиши.ДЕЙСТВИЕ)))
        {
            Убрать();
            AcceptEvent();
        }
    }
}
