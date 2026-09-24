// Настройки (Esc): управление, экран, звук.
//
// Открываются поверх всего и ставят игру на паузу. Дом и так ждёт ответа,
// а вот шкала взлома, створка гаража и снег за окном без паузы шли бы
// дальше, пока игрок ищет нужную клавишу. Метель и фон при этом звучат:
// их громкость крутят здесь же, и слышать, что крутишь, полезнее тишины.
//
// Всё применяется сразу — кнопки «применить» нет: что видно и слышно,
// то и записано (`user://настройки.json`). Клавиши, режим окна и галочки
// пишутся на диск в ту же минуту, полоски громкости — когда меню
// закрывают: иначе каждое движение ползунка было бы записью файла.
//
// Кнопки — мышью, фокуса они не берут. Фокус на кнопке превращает пробел
// в нажатие, и открыв настройки по Esc, можно было бы пробелом выйти
// из игры — ловушка та же, что у слова пророка в `ПереходUI`.
//
// Переназначение ловит следующее нажатие целиком, до интерфейса
// (`_Input`): иначе щелчок мышью, который игрок хочет назначить,
// достался бы кнопке под курсором. Esc — отмена, не клавиша.

using Godot;
using Дом.Экран;
using Дом.Ядро;

namespace Дом.Годот;

public partial class НастройкиUI : PanelContainer
{
    /// <summary>Что показывать и менять. Ставится корневым узлом.</summary>
    public Настройки Настройки { get; set; } = new();

    /// <summary>Меню закрыли — вернуть мышь и ходьбу.</summary>
    public System.Action Закрыт { get; set; } = () => { };

    /// <summary>Поменялось то, что держат сами узлы: мышь героя
    /// и подсказки с именем клавиши.</summary>
    public System.Action Применено { get; set; } = () => { };

    /// <summary>Как выйти в главное меню — или null: меню уже на экране
    /// (настройки открыты из него), выходить некуда.</summary>
    public System.Func<System.Action?> В_меню { get; set; } = () => null;

    public bool открыт => Visible;

    private static readonly Color СВЕТЛО = new("#e8e2d0");
    private static readonly Color ТИХО = new("#8a8474");
    private static readonly Color БЕДА = new("#c86a5a");

    private TabContainer _вкладки = null!;
    private Label _весть = null!;
    private readonly Dictionary<string, Button> _клавиши = new(StringComparer.Ordinal);
    private string? _ждёт;                  // какое действие ждёт клавишу
    private HSlider _мышь = null!;
    private Label _мышь_число = null!;
    private CheckBox _инверсия = null!;
    private OptionButton _режим = null!;
    private OptionButton _разрешение = null!;
    private Label _разрешение_что = null!;
    private CheckBox _синхронизация = null!;
    private Label _встроено = null!;
    private IReadOnlyList<Размер> _размеры = System.Array.Empty<Размер>();
    private readonly Dictionary<string, (HSlider полоса, Label число)> _громкость =
        new(StringComparer.Ordinal);
    private bool _не_записано;               // полоски двигали, а файл ещё старый
    private Button _в_меню = null!;
    private Label _выход = null!;

    public override void _Ready()
    {
        Visible = false;
        // живёт и на паузе: пауза — это оно само
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        // во весь экран: SetAnchorsPreset в дереве оставил бы нулевой прямоугольник
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel",
            new StyleBoxFlat { BgColor = new Color(0.07f, 0.065f, 0.06f, 0.94f) });

        var середина = new CenterContainer();
        середина.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(середина);

        var столбец = new VBoxContainer { CustomMinimumSize = new Vector2(880, 0) };
        столбец.AddThemeConstantOverride("separation", 12);
        середина.AddChild(столбец);

        var заголовок = new Label
        {
            Text = "НАСТРОЙКИ",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        заголовок.AddThemeFontSizeOverride("font_size", 26);
        заголовок.AddThemeColorOverride("font_color", СВЕТЛО);
        столбец.AddChild(заголовок);

        _вкладки = new TabContainer { CustomMinimumSize = new Vector2(880, 450) };
        столбец.AddChild(_вкладки);
        Вкладка(Вкладка_управления(), "Управление");
        Вкладка(Вкладка_экрана(), "Экран");
        Вкладка(Вкладка_звука(), "Звук");

        _весть = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 26),
        };
        столбец.AddChild(_весть);

        var низ = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        низ.AddThemeConstantOverride("separation", 16);
        столбец.AddChild(низ);
        низ.AddChild(Кнопка("Продолжить  [Esc]", Закрыть));
        низ.AddChild(Кнопка("Сбросить вкладку", Сбросить));
        _в_меню = Кнопка("В главное меню", () => В_меню()?.Invoke());
        низ.AddChild(_в_меню);
        низ.AddChild(Кнопка("Выйти из игры", Выйти));

        // жизнь сохраняется при каждом сне (ГДД 24) — выход посреди дня
        // отнимает только этот день
        _выход = Тихо("выйти можно когда угодно: жизнь сохраняется при каждом сне "
                      + "и продолжится с последнего утра");
        _выход.HorizontalAlignment = HorizontalAlignment.Center;
        столбец.AddChild(_выход);
    }

    // ------------------------------------------------------------ открыть и закрыть

    public void Открыть()
    {
        if (Visible)
            return;
        Visible = true;
        _ждёт = null;
        Сказать("", ТИХО);
        _в_меню.Visible = В_меню() is not null;
        _выход.Visible = _в_меню.Visible;
        Разложить();
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Закрыть()
    {
        if (!Visible)
            return;
        Не_ждать();
        Visible = false;
        GetTree().Paused = false;
        Записать_если_надо();
        Закрыт();
    }

    private void Выйти()
    {
        Записать_если_надо();
        GetTree().Quit();
    }

    public override void _ExitTree() => Записать_если_надо();

    private void Записать()
    {
        Настроить.Записать(Настройки);
        _не_записано = false;
    }

    private void Записать_если_надо()
    {
        if (_не_записано)
            Записать();
    }

    // ------------------------------------------------------------ ввод

    /// <summary>Esc закрывает меню — если не ждём клавишу (тогда он отмена).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _ждёт is not null)
            return;
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Закрыть();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Ждём клавишу: следующее нажатие — её, и дальше оно не идёт.</summary>
    public override void _Input(InputEvent e)
    {
        if (!Visible || _ждёт is null)
            return;
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Не_ждать();
            Сказать("отменено", ТИХО);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is not (InputEventKey or InputEventMouseButton))
            return;
        GetViewport().SetInputAsHandled();
        if (Управление.Клавиша(e) is not string клавиша)
            return;                 // отпускание, повтор, колесо — не ответ

        string действие = _ждёт;
        _ждёт = null;
        var итог = Настройки.Назначить(действие, клавиша);
        string кто = Клавиши.Какая(действие)!.подпись;
        string имя = Клавиши.Подпись(клавиша);
        if (!итог.принято)
            Сказать($"{имя}: {итог.почему}", БЕДА);
        else if (итог.обмен is string чья)
            Сказать($"«{кто}» — {имя}; «{Клавиши.Какая(чья)!.подпись}» получило "
                    + Клавиши.Подпись(Настройки.Клавиша(чья)), СВЕТЛО);
        else
            Сказать($"«{кто}» — {имя}", СВЕТЛО);
        Управление.Применить(Настройки);
        Записать();
        Разложить_клавиши();
        Применено();
    }

    private void Ждать(string действие)
    {
        Не_ждать();
        _ждёт = действие;
        _клавиши[действие].Text = "нажмите…";
        Сказать($"«{Клавиши.Какая(действие)!.подпись}»: нажмите клавишу или кнопку мыши; "
                + "Esc — отмена", СВЕТЛО);
    }

    private void Не_ждать()
    {
        if (_ждёт is null)
            return;
        _ждёт = null;
        Разложить_клавиши();
    }

    // ------------------------------------------------------------ вкладки

    private void Вкладка(Control что, string имя)
    {
        что.Name = имя;
        _вкладки.AddChild(что);
        _вкладки.SetTabTitle(_вкладки.GetTabCount() - 1, имя);
    }

    private Control Вкладка_управления()
    {
        var прокрутка = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var столбец = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        столбец.AddThemeConstantOverride("separation", 8);
        прокрутка.AddChild(столбец);

        столбец.AddChild(Тихо("щёлкните по клавише справа и нажмите новую; "
                              + "занятая другим действием поменяется с ним местами"));

        // две пары в строку: в одну колонку двенадцать действий и мышь
        // не помещаются во вкладку, и карман уезжал под прокрутку
        var сетка = new GridContainer { Columns = 4 };
        сетка.AddThemeConstantOverride("h_separation", 16);
        сетка.AddThemeConstantOverride("v_separation", 6);
        столбец.AddChild(сетка);
        foreach (var п in Клавиши.ВСЕ)
        {
            сетка.AddChild(Подпись(п.подпись, 262));
            string ключ = п.ключ;               // замыкание берёт копию
            var кнопка = Кнопка("", () => Ждать(ключ));
            кнопка.CustomMinimumSize = new Vector2(150, 0);
            _клавиши[ключ] = кнопка;
            сетка.AddChild(кнопка);
        }
        столбец.AddChild(Тихо("не переназначаются: Esc — настройки, 0–9 — номер варианта, "
                              + "мышь — взгляд"));

        столбец.AddChild(new HSeparator());
        var строка = new HBoxContainer();
        строка.AddThemeConstantOverride("separation", 12);
        строка.AddChild(Подпись("чувствительность мыши", 262));
        _мышь = Полоса(Настройки.МЫШЬ_ОТ, Настройки.МЫШЬ_ДО, 0.05, 240);
        _мышь.ValueChanged += з =>
        {
            Настройки.чувствительность = System.Math.Round(з, 2);
            _мышь_число.Text = "×" + Текст.Ф(Настройки.чувствительность, 2);
            _не_записано = true;
            Применено();
        };
        строка.AddChild(_мышь);
        _мышь_число = Подпись("", 70);
        строка.AddChild(_мышь_число);
        столбец.AddChild(строка);

        _инверсия = Галочка("инвертировать мышь по вертикали");
        _инверсия.Toggled += да =>
        {
            Настройки.инверсия = да;
            Записать();
            Применено();
        };
        столбец.AddChild(_инверсия);
        return прокрутка;
    }

    private Control Вкладка_экрана()
    {
        var столбец = new VBoxContainer();
        столбец.AddThemeConstantOverride("separation", 10);

        var сетка = new GridContainer { Columns = 2 };
        сетка.AddThemeConstantOverride("h_separation", 24);
        сетка.AddThemeConstantOverride("v_separation", 10);
        столбец.AddChild(сетка);

        сетка.AddChild(Подпись("режим", 300));
        _режим = new OptionButton
        {
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(340, 0),
        };
        foreach (var р in Разрешения.РЕЖИМЫ)
            _режим.AddItem(р.подпись);
        _режим.ItemSelected += i =>
        {
            Настройки.режим = Разрешения.РЕЖИМЫ[(int)i].ключ;
            Окно_изменилось();
        };
        сетка.AddChild(_режим);

        сетка.AddChild(Подпись("разрешение", 300));
        _разрешение = new OptionButton
        {
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(340, 0),
        };
        _разрешение.ItemSelected += i => Выбрать_размер((int)i);
        сетка.AddChild(_разрешение);

        сетка.AddChild(new Control());
        _разрешение_что = Тихо("");
        сетка.AddChild(_разрешение_что);

        сетка.AddChild(Подпись("вертикальная синхронизация", 300));
        _синхронизация = Галочка("кадр без разрывов");
        _синхронизация.Toggled += да =>
        {
            Настройки.синхронизация = да;
            Окно_изменилось();
        };
        сетка.AddChild(_синхронизация);

        _встроено = Тихо("игра встроена в окно редактора: режим и размер окна меняются "
                         + "только при отдельном запуске");
        _встроено.AddThemeColorOverride("font_color", БЕДА);
        столбец.AddChild(_встроено);
        return столбец;
    }

    private Control Вкладка_звука()
    {
        var сетка = new GridContainer { Columns = 3 };
        сетка.AddThemeConstantOverride("h_separation", 20);
        сетка.AddThemeConstantOverride("v_separation", 14);
        foreach (var к in Громкость.ВСЕ)
        {
            var имя = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
            имя.AddThemeConstantOverride("separation", 0);
            имя.AddChild(Подпись(к.подпись, 330));
            имя.AddChild(Тихо(к.что));
            сетка.AddChild(имя);

            var полоса = Полоса(0.0, 1.0, 0.01, 300);
            var число = Подпись("", 60);
            string ключ = к.ключ;
            полоса.ValueChanged += з =>
            {
                Настройки.Уровень(ключ, з);
                число.Text = Проценты(Настройки.Уровень(ключ));
                Шины.Применить(Настройки);
                _не_записано = true;
            };
            сетка.AddChild(полоса);
            сетка.AddChild(число);
            _громкость[ключ] = (полоса, число);
        }
        return сетка;
    }

    // ------------------------------------------------------------ раскладка

    /// <summary>Поставить все виджеты по настройкам — без их сигналов:
    /// раскладка не изменение.</summary>
    private void Разложить()
    {
        Разложить_клавиши();
        _мышь.SetValueNoSignal(Настройки.чувствительность);
        _мышь_число.Text = "×" + Текст.Ф(Настройки.чувствительность, 2);
        _инверсия.SetPressedNoSignal(Настройки.инверсия);
        foreach (var к in Громкость.ВСЕ)
        {
            var (полоса, число) = _громкость[к.ключ];
            полоса.SetValueNoSignal(Настройки.Уровень(к.ключ));
            число.Text = Проценты(Настройки.Уровень(к.ключ));
        }
        Разложить_экран();
    }

    private void Разложить_клавиши()
    {
        foreach (var п in Клавиши.ВСЕ)
            _клавиши[п.ключ].Text = Клавиши.Подпись(Настройки.Клавиша(п.ключ));
    }

    /// <summary>
    /// Вкладка экрана. Список размеров зависит от режима: в окне — окна,
    /// которые помещаются на стол; на весь экран — доли экрана для картинки
    /// мира. Поэтому при смене режима он собирается заново.
    /// </summary>
    private void Разложить_экран()
    {
        int режим = 0;
        for (int i = 0; i < Разрешения.РЕЖИМЫ.Count; i++)
            if (Разрешения.РЕЖИМЫ[i].ключ == Настройки.режим)
                режим = i;
        _режим.Select(режим);

        _разрешение.Clear();
        int выбран;
        if (Настройки.полный)
        {
            _размеры = Разрешения.Для_экрана(Настроить.Экран());
            выбран = Разрешения.Ближайшая_доля(Настройки.чёткость);
            _разрешение_что.Text = "картинка мира; надписи остаются чёткими";
        }
        else
        {
            _размеры = Разрешения.Для_окна(Настроить.Стол());
            выбран = Разрешения.Ближайший(_размеры, Настройки.окно);
            _разрешение_что.Text = "размер окна";
        }
        foreach (var р in _размеры)
            _разрешение.AddItem(р.ToString());
        _разрешение.Select(выбран);
        _синхронизация.SetPressedNoSignal(Настройки.синхронизация);

        bool встроено = Настроить.Встроено;
        _режим.Disabled = встроено;
        _разрешение.Disabled = встроено;
        _встроено.Visible = встроено;
    }

    private void Выбрать_размер(int i)
    {
        if (i < 0 || i >= _размеры.Count)
            return;
        if (Настройки.полный)
            Настройки.чёткость = Разрешения.ДОЛИ[i];
        else
            Настройки.окно = _размеры[i];
        Окно_изменилось();
    }

    private void Окно_изменилось()
    {
        Настроить.Окно(Настройки, GetViewport());
        Записать();
        // режим мог сменить и экран, и список размеров — собрать вкладку
        // после того, как окно встало
        Callable.From(Разложить_экран).CallDeferred();
    }

    private void Сбросить()
    {
        Не_ждать();
        switch (_вкладки.CurrentTab)
        {
            case 0:
                Настройки.СброситьУправление();
                Управление.Применить(Настройки);
                break;
            case 1:
                Настройки.СброситьЭкран();
                Настроить.Окно(Настройки, GetViewport());
                break;
            default:
                Настройки.СброситьЗвук();
                Шины.Применить(Настройки);
                break;
        }
        Записать();
        Разложить();
        Применено();
        Сказать("вкладка — как по умолчанию", ТИХО);
    }

    // ------------------------------------------------------------ мелочи

    private void Сказать(string что, Color цвет)
    {
        _весть.Text = что;
        _весть.AddThemeColorOverride("font_color", цвет);
    }

    private static string Проценты(double доля) => $"{(int)System.Math.Round(доля * 100)}%";

    private static Button Кнопка(string текст, System.Action что)
    {
        var к = new Button { Text = текст, FocusMode = FocusModeEnum.None };
        к.Pressed += что;
        return к;
    }

    /// <summary>Ползунок со своим желобом: у темы по умолчанию незаполненная
    /// часть тёмная и на фоне меню не видна — полоска казалась короче,
    /// чем есть, и где у неё край, было не понять.</summary>
    private static HSlider Полоса(double от, double до, double шаг, float ширина)
    {
        var п = new HSlider
        {
            MinValue = от,
            MaxValue = до,
            Step = шаг,
            CustomMinimumSize = new Vector2(ширина, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
        };
        п.AddThemeStyleboxOverride("slider", new StyleBoxFlat
        {
            BgColor = new Color("#4a473f"),
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
        });
        return п;
    }

    /// <summary>Галочка с видимым пустым квадратом: у темы по умолчанию
    /// он тёмный, и невыбранная галочка на фоне меню читалась просто
    /// надписью.</summary>
    private static CheckBox Галочка(string текст)
    {
        const int N = 16;
        var картинка = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        var цвет = new Color("#8a8474");
        for (int i = 2; i < N - 2; i++)
        {
            картинка.SetPixel(i, 2, цвет);
            картинка.SetPixel(i, N - 3, цвет);
            картинка.SetPixel(2, i, цвет);
            картинка.SetPixel(N - 3, i, цвет);
        }
        var г = new CheckBox { Text = текст, FocusMode = FocusModeEnum.None };
        г.AddThemeIconOverride("unchecked", ImageTexture.CreateFromImage(картинка));
        return г;
    }

    private static Label Подпись(string текст, float ширина)
    {
        var л = new Label
        {
            Text = текст,
            CustomMinimumSize = new Vector2(ширина, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        л.AddThemeColorOverride("font_color", СВЕТЛО);
        return л;
    }

    private static Label Тихо(string текст)
    {
        var л = new Label
        {
            Text = текст,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        л.AddThemeFontSizeOverride("font_size", 13);
        л.AddThemeColorOverride("font_color", ТИХО);
        return л;
    }
}
