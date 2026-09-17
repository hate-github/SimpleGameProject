// Что нашлось в коробке, ящике или на полке — и куда это положить (ГДД 7, 9).
//
// Найденное не переносится само: игрок видит, что нашлось, и решает,
// в чём нести. Карман вне времени — тихо, но мало и вещь исчезает
// из мира; пакеты, сумка, рюкзак — ноша (`Дом.Ядро.Ноша`), и чем громче
// тара, тем больше в неё влезает. Начатую тару на другую не меняют:
// другие кнопки гаснут и говорят почему. «Оставить» — всё обратно
// туда, где лежало; часы на обыск всё равно ушли.
//
// Окно небольшое и посередине: мир за ним виден, но ходить, пока оно
// открыто, нельзя — мышь нужна кнопкам. Esc — то же, что «оставить».

using Godot;
using Дом.Ядро;

namespace Дом.Годот;

public partial class НаходкаUI : PanelContainer
{
    private Label _заголовок = null!;
    private Label _что = null!;
    private Label _часы = null!;
    private VBoxContainer _кнопки = null!;
    private Label _почему = null!;
    private System.Action<Тара?>? _взять;
    private System.Action? _оставить;

    public bool открыт => Visible;

    private static readonly Color СВЕТЛО = new("#e8e2d0");
    private static readonly Color ТИХО = new("#a8a294");

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.065f, 0.06f, 0.93f),
            BorderColor = new Color("#8a8474"),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 14,
            ContentMarginBottom = 14,
        });

        var столбец = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        столбец.AddThemeConstantOverride("separation", 8);
        AddChild(столбец);

        _заголовок = new Label { Text = "В КОРОБКЕ", HorizontalAlignment = HorizontalAlignment.Center };
        _заголовок.AddThemeFontSizeOverride("font_size", 20);
        _заголовок.AddThemeColorOverride("font_color", СВЕТЛО);
        столбец.AddChild(_заголовок);

        _что = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _что.AddThemeFontSizeOverride("font_size", 17);
        _что.AddThemeColorOverride("font_color", new Color("#e8d8a8"));
        столбец.AddChild(_что);

        _часы = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _часы.AddThemeFontSizeOverride("font_size", 13);
        _часы.AddThemeColorOverride("font_color", ТИХО);
        столбец.AddChild(_часы);

        _кнопки = new VBoxContainer();
        _кнопки.AddThemeConstantOverride("separation", 4);
        столбец.AddChild(_кнопки);

        _почему = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _почему.AddThemeFontSizeOverride("font_size", 13);
        _почему.AddThemeColorOverride("font_color", new Color("#c8a07a"));
        столбец.AddChild(_почему);
    }

    /// <summary>
    /// Показать находку. <paramref name="взять"/> зовётся с тарой или
    /// с null — «в карман вне времени»; <paramref name="оставить"/> —
    /// если игрок не взял ничего. <paramref name="где"/> — где нашлось:
    /// «в коробке», «в ящике», «на полке».
    /// </summary>
    public void Показать(Добыча д, Ноша ноша, Карман? карман, РучкиПетли ручки,
                         System.Action<Тара?> взять, System.Action оставить,
                         string где = "в коробке")
    {
        _взять = взять;
        _оставить = оставить;
        _заголовок.Text = где.ToUpperInvariant();
        double всего = д.вещи.Ключи.Sum(р => д.вещи[р]);
        _что.Text = string.Join(" · ", д.вещи.Ключи
                        .OrderBy(р => р, System.StringComparer.Ordinal)
                        .Select(р => $"{р} {Текст.G(Текст.Округлить(д.вещи[р], 1))}"))
                    + (д.надето.Count > 0 ? "\nсразу надето: " + string.Join(", ", д.надето) : "");
        _часы.Text = $"на обыск ушло {Текст.G(Текст.Округлить(д.часы, 1))} ч";

        foreach (var узел in _кнопки.GetChildren())
        {
            _кнопки.RemoveChild(узел);
            узел.QueueFree();
        }
        var почему = new List<string>();

        // карман вне времени — первым: тихо, но мало, и только у того,
        // у кого есть наследие
        if (карман is not null)
        {
            var к = Кнопка($"в карман вне времени — свободно {Текст.G(Текст.Округлить(карман.свободно, 1))} "
                           + $"из {Текст.G(Текст.Округлить(карман.предел, 1))}, беззвучно",
                           () => Выбрать(null));
            к.Disabled = карман.свободно <= 0;
            if (к.Disabled)
                почему.Add("карман вне времени полон");
        }

        foreach (var т in Тары.ВСЕ)
        {
            string шум = т.шум(ручки) switch
            {
                >= 3 => "шуршит на весь подъезд",
                2 => "слышно, как с вылазки",
                _ => "почти не слышно",
            };
            double свободно = ноша.свободно(т, ручки);
            string места = ноша.тара == т
                ? $"свободно {Текст.G(Текст.Округлить(свободно, 1))} из {Текст.G(т.мест(ручки))}"
                : $"{Текст.G(т.мест(ручки))} места";
            var к = Кнопка($"{т.Куда()} — {места}, {шум}", () => Выбрать(т));
            к.Disabled = свободно <= 0;
            if (!ноша.Можно(т) && ноша.тара is Тара начата)
                почему.Add($"{начата.Текст()} уже начат{(начата == Тара.СУМКА ? "а" : "")} — "
                           + "другую тару не взять, пока не донесёшь");
            else if (свободно <= 0)
                почему.Add($"{т.Текст()} полон{(т == Тара.СУМКА ? "а" : "")}");
        }
        if (всего > 0)
            Кнопка($"оставить {где}   [Esc]", Оставить);

        _почему.Text = string.Join("; ", почему.Distinct());
        Visible = true;
        Лечь();
    }

    public void Убрать()
    {
        Visible = false;
        _взять = null;
        _оставить = null;
    }

    private void Выбрать(Тара? тара)
    {
        var что = _взять;
        Убрать();
        что?.Invoke(тара);
    }

    private void Оставить()
    {
        var что = _оставить;
        Убрать();
        что?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (Visible)
            Лечь();
    }

    /// <summary>Посередине экрана: размер по содержимому.</summary>
    private void Лечь()
    {
        var экран = GetParentControl()?.Size ?? GetViewportRect().Size;
        var размер = GetCombinedMinimumSize();
        Size = размер;
        Position = (экран - размер) / 2;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible)
            return;
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Оставить();
            AcceptEvent();
        }
    }

    private Button Кнопка(string текст, System.Action что)
    {
        var к = new Button
        {
            Text = текст,
            Alignment = HorizontalAlignment.Left,
            FocusMode = FocusModeEnum.None,
        };
        к.Pressed += что;
        _кнопки.AddChild(к);
        return к;
    }
}
