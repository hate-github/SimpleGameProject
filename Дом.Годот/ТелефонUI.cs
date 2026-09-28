// Телефон (ГДД 19). Четыре приложения: мысли, чат подъезда, заметки
// и сервисы — займы и доставка, пока город живёт обычной жизнью
// (до метели; `ДомУзел.Сервисы`).
//
// Мысли — это вопрос дня: что приходит в голову сделать, по весу, как дом
// его и собрал (порядок не трогается, вес показан). Мысли одного дела
// с разными целями — одной строкой (автор: «читать долго; нажимаешь
// говорить — там тебе дают варианты, с кем говорить»): «поговорить — с кем
// ▸ 6», нажал — раскрылись цели в том же порядке, по весу. Нажать мысль —
// не сделать её, а узнать, где она делается (`Дом.Экран.Места`): делают
// ногами, у места, клавишей действия. Выбранная мысль закрепляется
// строкой на экране, и у своего места клавиша делает именно её, а не ту,
// что дом взвесил выше. Раньше вопрос дня висел листом поверх подъезда;
// автор попросил убрать его в телефон — это догадки героя, а не приказ.
//
// Чат — общий чат жильцов: он уже есть в симуляции и уже что-то делает
// с домом. Реплика в нём — не украшение, а способ узнать, у кого что есть
// и кто кого боится.
//
// Заметки — то, что герой записал о людях и местах (ГДД 10) и что
// переносится между жизнями. Разница между вкладками принципиальная:
// чат живёт одну метель и обнуляется вместе с ней, заметки живут дольше
// героя. Поэтому у них разные источники: чат берётся из ленты сеанса,
// заметки — из наследия.
//
// Экран ничего не решает: он берёт готовые записи и кладёт их в столбик.
// Что сказано и кем — решено в `Дом.Ядро/Чат.cs`; что записано и о ком —
// в `Дом.Ядро/Петля`.
//
// Чего здесь нет и не будет: возможности написать самому. Игрок в чат
// не пишет — пока в симуляции нет действия «написать в чат», приделать
// поле ввода значило бы приделать кнопку, которая ничего не делает.
//
// **Телефон в руке** (просьба автора, 28.09.2026: «чтобы доставался телефон,
// а не вылезало окно»). Это не панель в углу, а сам телефон: корпус,
// экран, строка состояния с часами героя; по клавише он выезжает снизу
// справа — как из кармана в правую руку — и уезжает вниз, когда его убрали.
// Пока он в руке, в руке нет ничего другого (`ДомУзел` прячет предмет).
// Что «открыт» — отдельно от того, виден ли: убранный телефон ещё уезжает
// вниз, а мышь и время уже вернулись миру.

using Godot;
using Дом.Экран;
using Дом.Ядро;

namespace Дом.Годот;

public partial class ТелефонUI : PanelContainer
{
    private VBoxContainer _мысли = null!;
    private Label _мысли_шапка = null!;
    private Label _как = null!;
    private ВопросИгроку? _вопрос;
    private IReadOnlyList<string> _подсказки = System.Array.Empty<string>();
    private RichTextLabel _чат = null!;
    private RichTextLabel _заметки = null!;
    private VBoxContainer? _сервисы;
    private Label _сервисы_шапка = null!;

    /// <summary>Что во вкладке «сервисы»: шапка (деньги, долги, заказ) и строки;
    /// строка без дела — подпись раздела. Ставится корневым узлом.</summary>
    public System.Func<(string шапка, IReadOnlyList<СтрокаОкна> строки)>? Сервисы_дай { get; set; }
    private TabContainer _вкладки = null!;
    private Label _время = null!;
    private Tween? _ход;
    private int _показано;                  // сколько реплик чата уже на экране
    private int _заметок = -1;              // сколько заметок нарисовано
    private readonly HashSet<string> _раскрыто = new(System.StringComparer.Ordinal);   // какие дела раскрыты

    /// <summary>Как назвать цель мысли — соседа так, как его знает герой;
    /// null — как написал дом. Ставится корневым узлом.</summary>
    public System.Func<object?, string?>? Цель_словами { get; set; }

    /// <summary>Как дело звучит строкой-группой: «поговорить — с кем».</summary>
    private static readonly Dictionary<string, string> ГРУППЫ = new(System.StringComparer.Ordinal)
    {
        ["разговор"] = "поговорить — с кем",
        ["попросить"] = "попросить — у кого",
        ["поделиться"] = "поделиться — с кем",
        ["вернуть"] = "вернуть долг — кому",
        ["обмен"] = "поменяться — с кем",
        ["лечить"] = "полечить — кого",
        ["проведать"] = "проведать — кого",
        ["шепнуть"] = "шепнуть — кому",
        ["подбросить"] = "подбросить — кому",
        ["подкараулить"] = "подкараулить — кого",
        ["позвать"] = "позвать — кого",
        ["отдать_на_ночь"] = "отдать ребёнка на ночь — кому",
        ["отнять"] = "отнять — у кого",
        ["кража_днём"] = "украсть днём — у кого",
        ["кража"] = "украсть — у кого",
        ["обобрать"] = "обобрать — кого",
        ["убить_соседа"] = "убить — кого",
        ["налёт"] = "налёт — на кого",
        ["наблюдение"] = "понаблюдать — за кем",
        ["переехать"] = "переехать — к кому",
        ["выгнать"] = "выгнать — кого",
        ["занять"] = "занять квартиру — какую",
        ["разбор"] = "разобрать — что",
        ["тело"] = "тело — чьё",
        ["вынести"] = "вынести — что",
        ["вылазка"] = "вылазка — куда",
        ["вылазка_двор"] = "вылазка во двор — куда",
        ["вылазка_чужое"] = "вылазка — к чужому",
        ["кладовая"] = "к кладовой — к какой",
        ["вскрыть_кладовую"] = "вскрыть кладовую — какую",
    };

    /// <summary>Откуда брать чат. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    /// <summary>Откуда брать заметки: они переживают жизнь.</summary>
    public Наследие? Наследие { get; set; }

    /// <summary>Мысль выбрана: её номер в вопросе и строка «где». Корень
    /// закрепляет её на экране.</summary>
    public System.Action<int, string>? Выбрано { get; set; }

    /// <summary>Часы героя для строки состояния. Ставится корневым узлом.</summary>
    public System.Func<string>? Часы_дай { get; set; }

    /// <summary>Телефон убран и уехал вниз — корню: вернуть мышь миру.</summary>
    public System.Action? Спрятан { get; set; }

    /// <summary>Телефон в руке. Уезжающий вниз — уже не в руке.</summary>
    public bool открыт { get; private set; }

    /// <summary>Размер телефона — по экрану 1280×800: в правой руке, не на весь экран.</summary>
    public static readonly Vector2 РАЗМЕР = new(340, 640);

    private const float ЕХАТЬ = 0.22f;       // секунд — выехать или уехать

    public override void _Ready()
    {
        Visible = false;
        Size = РАЗМЕР;
        CustomMinimumSize = РАЗМЕР;
        // шрифт экрана мельче, чем у окон: телефон узкий, а строк много
        Theme = new Theme { DefaultFontSize = 14 };
        // корпус: тёмный, скруглённый; сверху динамик, снизу полоска —
        // рисует `_Draw` в полях корпуса
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("#131313"),
            BorderColor = new Color("#34322e"),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 34, CornerRadiusTopRight = 34,
            CornerRadiusBottomLeft = 34, CornerRadiusBottomRight = 34,
            ContentMarginLeft = 11, ContentMarginRight = 11,
            ContentMarginTop = 30, ContentMarginBottom = 26,
            ShadowColor = new Color(0, 0, 0, 0.55f),
            ShadowSize = 14,
        });
        var экран = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        экран.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("#1c1b19"),
            CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 8,
        });
        AddChild(экран);
        var столбец = new VBoxContainer();
        столбец.AddThemeConstantOverride("separation", 4);
        экран.AddChild(столбец);

        // строка состояния: часы героя слева, сеть и заряд справа
        var состояние = new HBoxContainer();
        _время = new Label();
        _время.AddThemeColorOverride("font_color", new Color("#e0dac8"));
        _время.AddThemeFontSizeOverride("font_size", 13);
        состояние.AddChild(_время);
        состояние.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var сеть = new Label { Text = "▂▄▆  ▮▮▮▯" };
        сеть.AddThemeColorOverride("font_color", new Color("#8e897c"));
        сеть.AddThemeFontSizeOverride("font_size", 11);
        состояние.AddChild(сеть);
        столбец.AddChild(состояние);

        _вкладки = new TabContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        столбец.AddChild(_вкладки);

        Мысли_страница();
        _чат = Страница("чат");
        _заметки = Страница("заметки");
        Сервисы_страница();
    }

    private void Сервисы_страница()
    {
        var прокрутка = new ScrollContainer
        {
            Name = "сервисы",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _вкладки.AddChild(прокрутка);
        var столбец = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        столбец.AddThemeConstantOverride("separation", 6);
        прокрутка.AddChild(столбец);
        _сервисы_шапка = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _сервисы_шапка.AddThemeColorOverride("font_color", new Color("#e8d8a8"));
        столбец.AddChild(_сервисы_шапка);
        _сервисы = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _сервисы.AddThemeConstantOverride("separation", 2);
        столбец.AddChild(_сервисы);
    }

    /// <summary>Перерисовать «сервисы»: после каждого нажатия — деньги
    /// и долги меняются.</summary>
    public void Сервисы_обновить()
    {
        if (_сервисы is null)
            return;
        foreach (var узел in _сервисы.GetChildren())
        {
            _сервисы.RemoveChild(узел);
            узел.QueueFree();
        }
        if (Сервисы_дай?.Invoke() is not { } дано)
        {
            _сервисы_шапка.Text = "сервисов нет";
            return;
        }
        _сервисы_шапка.Text = дано.шапка;
        foreach (var с in дано.строки)
        {
            if (с.сделать is not { } сделать)
            {
                var подпись = new Label { Text = с.текст, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                подпись.AddThemeColorOverride("font_color", new Color("#a8a294"));
                _сервисы.AddChild(подпись);
                continue;
            }
            var кнопка = new Button
            {
                Text = с.текст,
                Disabled = !с.можно,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            кнопка.Pressed += () =>
            {
                сделать();
                Сервисы_обновить();
            };
            _сервисы.AddChild(кнопка);
        }
    }

    private void Мысли_страница()
    {
        var столбец = new VBoxContainer { Name = "мысли" };
        столбец.AddThemeConstantOverride("separation", 6);
        _вкладки.AddChild(столбец);

        _мысли_шапка = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _мысли_шапка.AddThemeColorOverride("font_color", new Color("#a8a294"));
        столбец.AddChild(_мысли_шапка);

        var прокрутка = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        столбец.AddChild(прокрутка);
        _мысли = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _мысли.AddThemeConstantOverride("separation", 2);
        прокрутка.AddChild(_мысли);

        _как = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 46),
        };
        _как.AddThemeColorOverride("font_color", new Color("#e8d8a8"));
        столбец.AddChild(_как);
        Мысли(null, System.Array.Empty<string>());
    }

    /// <summary>
    /// Разложить мысли вопроса дня. <paramref name="как"/> — где делается
    /// каждая, в том же порядке. Другой вопрос или никакого — мыслей нет:
    /// дом либо считает, либо спрашивает то, на что отвечают сейчас.
    /// </summary>
    public void Мысли(ВопросИгроку? в, IReadOnlyList<string> как)
    {
        _вопрос = в;
        _подсказки = как;
        foreach (var узел in _мысли.GetChildren())
        {
            _мысли.RemoveChild(узел);
            узел.QueueFree();
        }
        _как.Text = "";
        if (в is null)
        {
            _мысли_шапка.Text = "время идёт — мысли придут, когда будет пора решать";
            return;
        }
        if (в.вопрос is not (Вопрос.ЧТО_ДЕЛАТЬ or Вопрос.НОЧЬ))
        {
            _мысли_шапка.Text = "сейчас не до раздумий — ответь на то, что спрашивают";
            return;
        }
        // ночь — тоже мысли (`ДомУзел.Сон`): где лечь и чем заняться до утра
        _мысли_шапка.Text = в.вопрос == Вопрос.НОЧЬ
            ? "ночь — спать ложатся в постель; нажми мысль, чтобы понять, где это"
            : в.заголовок.Split('\n')[0] + " — нажми мысль, чтобы понять, где это делается";
        // дела — в порядке их лучшей мысли; мысли внутри — как были, по весу
        var дела = new List<(string ключ, List<int> номера)>();
        for (int i = 0; i < в.варианты.Count; i++)
        {
            string ключ = в.варианты[i].ключ;
            int д = ключ.Length == 0 ? -1 : дела.FindIndex(x => x.ключ == ключ);
            if (д < 0)
                дела.Add((ключ, new List<int> { i }));
            else
                дела[д].номера.Add(i);
        }
        foreach (var (ключ, номера) in дела)
        {
            if (номера.Count == 1)
            {
                Мысль(номера[0], Подпись(в.варианты[номера[0]]), 0);
                continue;
            }
            bool раскрыто = _раскрыто.Contains(ключ);
            string вес = Разобрать(в.варианты[номера[0]].строка).вес;
            var дело = new Button
            {
                Text = $"{(раскрыто ? "▾" : "▸")} {Группа(ключ)} · {номера.Count}"
                       + (вес.Length > 0 ? $"   · до {вес}" : ""),
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            дело.AddThemeColorOverride("font_color", new Color("#e8d8a8"));
            дело.Pressed += () =>
            {
                if (!_раскрыто.Remove(ключ))
                    _раскрыто.Add(ключ);
                Мысли(_вопрос, _подсказки);
            };
            _мысли.AddChild(дело);
            if (раскрыто)
                foreach (int i in номера)
                    Мысль(i, "→ " + Цель(в.варианты[i]), 1);
        }
    }

    /// <summary>Кнопка мысли: подпись, вес, отступ под делом.</summary>
    private void Мысль(int номер, string подпись, int отступ)
    {
        string вес = _вопрос is null ? "" : Разобрать(_вопрос.варианты[номер].строка).вес;
        var кнопка = new Button
        {
            Text = new string(' ', 6 * отступ) + (вес.Length > 0 ? $"{подпись}   · {вес}" : подпись),
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
        };
        кнопка.Pressed += () => Выбрать(номер);
        _мысли.AddChild(кнопка);
    }

    private static string Группа(string ключ)
        => ГРУППЫ.TryGetValue(ключ, out var з) ? з : ключ.Replace('_', ' ');

    /// <summary>Мысль строкой: «дело → цель»; цель-соседа — как его знает герой.</summary>
    private string Подпись(ВариантИгроку в)
    {
        if (в.цель is not null && Цель_словами?.Invoke(в.цель) is string цель)
            return $"{в.ключ.Replace('_', ' ')} → {цель}";
        return Разобрать(в.строка).что.Replace('_', ' ');
    }

    /// <summary>Только цель мысли — под строкой дела.</summary>
    private string Цель(ВариантИгроку в)
    {
        if (в.цель is not null && Цель_словами?.Invoke(в.цель) is string цель)
            return цель;
        string что = Разобрать(в.строка).что;
        int i = что.IndexOf("→ ", System.StringComparison.Ordinal);
        return i < 0 ? что : что[(i + 2)..];
    }

    /// <summary>Открыть телефон на мыслях.</summary>
    public void К_мыслям() => _вкладки.CurrentTab = 0;

    /// <summary>Открыта ли вкладка мыслей.</summary>
    public bool на_мыслях => _вкладки.CurrentTab == 0;

    /// <summary>Достать телефон: выезжает снизу в правую руку.</summary>
    public void Достать()
    {
        открыт = true;
        _ход?.Kill();
        var экран = GetViewportRect().Size;
        Size = РАЗМЕР;
        float x = экран.X - РАЗМЕР.X - Mathf.Max(24f, экран.X * 0.09f);
        float в_руке = экран.Y - РАЗМЕР.Y - 14f;
        if (!Visible)
            Position = new Vector2(x, экран.Y + 16f);
        Position = Position with { X = x };
        Visible = true;
        Часы();
        _ход = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _ход.TweenProperty(this, "position:y", в_руке, ЕХАТЬ);
    }

    /// <summary>Убрать телефон: уезжает вниз; <paramref name="сразу"/> —
    /// без дороги (другой экран поверх).</summary>
    public void Спрятать(bool сразу = false)
    {
        if (!открыт && !Visible)
            return;
        открыт = false;
        _ход?.Kill();
        if (сразу)
        {
            Visible = false;
            Спрятан?.Invoke();
            return;
        }
        var экран = GetViewportRect().Size;
        _ход = CreateTween().SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
        _ход.TweenProperty(this, "position:y", экран.Y + 16f, ЕХАТЬ);
        _ход.TweenCallback(Callable.From(() =>
        {
            if (!открыт)
                Visible = false;
            Спрятан?.Invoke();
        }));
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        Часы();
        // Подпись с переносом строк, пока её ширина ещё ноль, просит высоту
        // в строку на слово, и корпус вырастает под неё — а сам потом не
        // сжимается (проба: 1808 точек вместо 640). Каждый кадр — свой
        // размер; меньше настоящего минимума Godot всё равно не даст
        if (Size != РАЗМЕР)
            Size = РАЗМЕР;
    }

    private void Часы() => _время.Text = Часы_дай?.Invoke() ?? "";

    /// <summary>Динамик сверху и полоска снизу — в полях корпуса.</summary>
    public override void _Draw()
    {
        var тёмное = new Color("#2a2826");
        var динамик = new StyleBoxFlat { BgColor = тёмное };
        динамик.SetCornerRadiusAll(3);
        динамик.Draw(GetCanvasItem(), new Rect2(Size.X / 2 - 34, 13, 68, 6));
        var полоска = new StyleBoxFlat { BgColor = new Color("#4a4740") };
        полоска.SetCornerRadiusAll(2);
        полоска.Draw(GetCanvasItem(), new Rect2(Size.X / 2 - 44, Size.Y - 15, 88, 4));
    }

    private void Выбрать(int i)
    {
        if (_вопрос is null || i >= _вопрос.варианты.Count)
            return;
        string что = Подпись(_вопрос.варианты[i]);
        string где = i < _подсказки.Count ? _подсказки[i] : "";
        string почему = Почему(_вопрос.варианты[i].строка);
        _как.Text = $"{что}: {где} — {Управление.В_скобках(Клавиши.ДЕЙСТВИЕ)}"
                    + (почему.Length > 0 ? $"\nвес {почему}" : "");
        Выбрано?.Invoke(i, $"{что} — {где}");
    }

    /// <summary>Строка варианта — «что», вес и разложение через пробелы;
    /// мысли нужны первые два.</summary>
    private static (string что, string вес) Разобрать(string строка)
    {
        var части = строка.Split("  ", System.StringSplitOptions.RemoveEmptyEntries
                                       | System.StringSplitOptions.TrimEntries);
        return (части.Length > 0 ? части[0] : строка, части.Length > 1 ? части[1] : "");
    }

    /// <summary>Из чего сложился вес — то, что стоит после «=».</summary>
    private static string Почему(string строка)
    {
        int i = строка.IndexOf("= ", System.StringComparison.Ordinal);
        return i < 0 ? "" : строка[(i + 2)..].Trim();
    }


    private RichTextLabel Страница(string имя)
    {
        var прокрутка = new ScrollContainer
        {
            Name = имя,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _вкладки.AddChild(прокрутка);
        var текст = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        прокрутка.AddChild(текст);
        return текст;
    }

    /// <summary>
    /// Дописать то, что пришло с прошлого раза.
    ///
    /// Именно дописать, а не перерисовать: перерисовка каждый кадр теряет
    /// место прокрутки под рукой у читающего.
    /// </summary>
    public void Обновить()
    {
        Сервисы_обновить();
        if (Сеанс is not null)
        {
            var чат = Сеанс.Вида(ВидСтроки.ЧАТ);
            if (чат.Count < _показано)      // новая жизнь — новая метель
            {
                _чат.Clear();
                _показано = 0;
            }
            for (; _показано < чат.Count; _показано++)
            {
                var с = чат[_показано];
                // «  [чат] Толик: замёрз» → «Толик: замёрз»: скобки нужны
                // консоли, чтобы строка не терялась среди прочих, а здесь
                // весь экран и так про чат
                string текст = с.текст.Replace("  [чат] ", "",
                                               System.StringComparison.Ordinal);
                _чат.AppendText($"[color=#6f6a5e]день {с.день}[/color]  "
                                + $"{Экранировать(текст)}\n");
            }
        }

        // Заметки перерисовываются целиком, а не дописываются. Дописывать
        // их нельзя: список обрезается с начала (`записей_помнить`), и после
        // обрезки счётчик показывал бы уже на другие записи — молча
        // и незаметно. Их две сотни, и рисуются они не каждый кадр,
        // а когда дом задал вопрос
        if (Наследие is not null && Наследие.записи.Count != _заметок)
        {
            _заметки.Clear();
            foreach (var з in Наследие.записи)
                _заметки.AppendText(
                    $"[color=#6f6a5e]жизнь {з.жизнь}, день {з.день}[/color]  "
                    + $"{Экранировать(з.текст)}\n");
            _заметок = Наследие.записи.Count;
        }
    }

    /// <summary>Квадратная скобка в BBCode — начало тега, а в тексте она
    /// встречается.</summary>
    private static string Экранировать(string s)
        => s.Replace("[", "[lb]", System.StringComparison.Ordinal);
}
