// Окна похода: ворота в город (куда пойти) и прилавок магазина (что купить).
//
// Решают не окна. Куда можно и сколько часов — `Округа.Нельзя`
// и `Округа.Часы_дороги`; цена, склад и кошелёк — `Магазин`. Окно
// показывает строки, которые ему дали, и зовёт то, что ему дали. Как
// верстак: закрывается клавишей действия или Esc.
//
// **Разговор** (просьба автора 01.10.2026: «при диалоге — окно, как на
// скриншоте, но в стиле игры»). Окно с собеседником — беседа у двери,
// рассказ знатока, прилавок с продавщицей — показывается не столбцом
// посреди затемнённого экрана, а полосой внизу: слева лицо в рамке
// (`ПортретUI`), справа кто, что сказал и ответы, сверху тонкая полоса
// цвета вести. Экран почти не темнеет — сосед в дверях остаётся виден.
// Надписи и строки те же самые: они переставляются из столбца в полосу
// и обратно. Лица нет — окно прежнее.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

/// <summary>Строка окна: что написано, можно ли нажать и что при нажатии.</summary>
public sealed record СтрокаОкна(string текст, bool можно, System.Action? сделать);

public partial class ПоходUI : PanelContainer
{
    private Label _заголовок = null!, _текст = null!, _весть = null!;
    private VBoxContainer _строки = null!;
    private Button _закрыть = null!;
    private System.Func<IReadOnlyList<СтрокаОкна>> _строки_дай = () => System.Array.Empty<СтрокаОкна>();
    private System.Func<string> _текст_дай = () => "";

    // посередине: прокрутка со столбцом
    private Control _середина = null!;
    private VBoxContainer _столбец = null!;
    // разговор: полоса внизу, слева лицо
    private Control _низ_слой = null!;
    private PanelContainer _низ = null!;
    private VBoxContainer _низ_столбец = null!;
    private ScrollContainer _низ_строки = null!;
    private ПортретUI _портрет = null!;
    private StyleBoxFlat _фон = null!;
    private bool _разговор;
    private readonly List<Button> _ответы = new();   // кнопки по номерам — для цифр

    private static readonly Color ТЕМНО = new(0.07f, 0.065f, 0.06f, 0.92f);
    private static readonly Color ПОЧТИ_ВИДНО = new(0.07f, 0.065f, 0.06f, 0.22f);

    public bool открыт => Visible;

    /// <summary>Закрыли — корневому узлу надо вернуть мышь и ходьбу.</summary>
    public System.Action Закрыт { get; set; } = () => { };

    public override void _Ready()
    {
        Visible = false;
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _фон = new StyleBoxFlat { BgColor = ТЕМНО };
        AddThemeStyleboxOverride("panel", _фон);
        // не влезло в экран — листается (`Прокрутка`)
        var центр = Прокрутка.По_центру(this);
        _середина = (Control)центр.GetParent();
        _столбец = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        _столбец.AddThemeConstantOverride("separation", 12);
        центр.AddChild(_столбец);

        _заголовок = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _заголовок.AddThemeFontSizeOverride("font_size", 26);
        _заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        _столбец.AddChild(_заголовок);

        _текст = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _текст.AddThemeColorOverride("font_color", new Color("#b8b0a0"));
        _столбец.AddChild(_текст);

        _строки = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _строки.AddThemeConstantOverride("separation", 4);
        _столбец.AddChild(_строки);

        _весть = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _весть.AddThemeColorOverride("font_color", new Color("#d8b070"));
        _столбец.AddChild(_весть);

        _закрыть = new Button { Text = "Отойти  [Esc]" };
        _закрыть.Pressed += Убрать;
        _столбец.AddChild(_закрыть);

        // ---- разговор ----
        _низ_слой = new Control { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_низ_слой);
        _низ = new PanelContainer();
        _низ.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.055f, 0.05f, 0.97f),
            BorderColor = new Color("#d8b070"),
            BorderWidthTop = 3,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 16,
            ContentMarginBottom = 14,
        });
        _низ_слой.AddChild(_низ);
        var ряд = new HBoxContainer();
        ряд.AddThemeConstantOverride("separation", 18);
        _низ.AddChild(ряд);
        _портрет = new ПортретUI();
        ряд.AddChild(_портрет);
        _низ_столбец = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _низ_столбец.AddThemeConstantOverride("separation", 8);
        ряд.AddChild(_низ_столбец);
        _низ_строки = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _низ_столбец.AddChild(_низ_строки);
    }

    /// <summary>Показать окно: заголовок, строки (их просят заново после
    /// каждого нажатия — склад и кошелёк меняются) и текст над ними.
    /// <paramref name="лицо"/> — с кем разговор (id жильца или «продавщица»):
    /// есть у него лицо — окно встаёт разговором внизу.</summary>
    public void Показать(string заголовок, System.Func<string> текст,
                         System.Func<IReadOnlyList<СтрокаОкна>> строки, string? лицо = null)
    {
        _заголовок.Text = заголовок;
        _текст_дай = текст;
        _строки_дай = строки;
        Весть("");
        Разговором(_портрет.Показать(лицо));
        Visible = true;
        Разложить();
    }

    /// <summary>Сказать под строками, чем кончилось нажатие.</summary>
    public void Весть(string что)
    {
        _весть.Text = что;
        _весть.Visible = что.Length > 0;
    }

    /// <summary>Переставить надписи и строки: в полосу разговора или в столбец посередине.</summary>
    private void Разговором(bool да)
    {
        _разговор = да;
        if (да)
        {
            В(_заголовок, _низ_столбец, 0);
            В(_текст, _низ_столбец, 1);
            В(_строки, _низ_строки);
            В(_весть, _низ_столбец);
            В(_закрыть, _низ_столбец);
        }
        else
        {
            foreach (var у in new Control[] { _заголовок, _текст, _строки, _весть, _закрыть })
                В(у, _столбец);
            _текст.CustomMinimumSize = Vector2.Zero;
        }
        var выровнять = да ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _заголовок.HorizontalAlignment = выровнять;
        _текст.HorizontalAlignment = выровнять;
        _весть.HorizontalAlignment = выровнять;
        _заголовок.AddThemeFontSizeOverride("font_size", да ? 20 : 26);
        _текст.AddThemeColorOverride("font_color", new Color(да ? "#e8d8a8" : "#b8b0a0"));
        _закрыть.SizeFlagsHorizontal = да ? SizeFlags.ShrinkEnd : SizeFlags.Fill;
        _середина.Visible = !да;
        _низ_слой.Visible = да;
        _фон.BgColor = да ? ПОЧТИ_ВИДНО : ТЕМНО;
    }

    private static void В(Control что, Control куда, int место = -1)
    {
        if (что.GetParent() != куда)
        {
            что.GetParent()?.RemoveChild(что);
            куда.AddChild(что);
        }
        if (место >= 0)
            куда.MoveChild(что, место);
    }

    private void Разложить()
    {
        foreach (var узел in _строки.GetChildren())
        {
            _строки.RemoveChild(узел);
            узел.QueueFree();
        }
        _текст.Text = _текст_дай();
        _ответы.Clear();
        foreach (var с in _строки_дай())
        {
            if (с.сделать is null)
            {
                var надпись = new Label { Text = с.текст };
                надпись.AddThemeColorOverride("font_color", new Color("#8a8474"));
                _строки.AddChild(надпись);
                continue;
            }
            // в разговоре ответы с номерами от нуля и цифрой — как в окне вопроса
            int номер = _ответы.Count;
            var к = new Button
            {
                Text = _разговор && номер < 10 ? $"{номер}.  {с.текст}" : с.текст,
                Alignment = HorizontalAlignment.Left,
                Disabled = !с.можно,
            };
            _ответы.Add(к);
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

    /// <summary>Полоса разговора — внизу, шириной с окно вопроса-разговора;
    /// строк много — листаются, а полоса не выше половины экрана.</summary>
    public override void _Process(double delta)
    {
        if (!Visible || !_разговор)
            return;
        var экран = Size;
        float ширина = Mathf.Min(1000, экран.X * 0.8f);
        // ширина текста известна заранее: подпись с переносом при нулевой
        // ширине просит высоту в строку на слово (ловушка телефона)
        _текст.CustomMinimumSize = new Vector2(ширина - ПортретUI.РАЗМЕР.X - 90, 0);
        _низ_строки.CustomMinimumSize = new Vector2(0, Mathf.Min(экран.Y * 0.42f, _строки.GetCombinedMinimumSize().Y));
        float высота = Mathf.Min(экран.Y - 60, _низ.GetCombinedMinimumSize().Y);
        _низ.Size = new Vector2(ширина, высота);
        _низ.Position = new Vector2((экран.X - ширина) / 2, экран.Y - высота - 28);
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
            return;
        }
        // в разговоре — цифра: ответ с этим номером; повтор клавиши отбрасывается
        if (_разговор && e is InputEventKey { Pressed: true, Echo: false } к)
        {
            int номер = к.Keycode switch
            {
                >= Key.Key0 and <= Key.Key9 => (int)(к.Keycode - Key.Key0),
                >= Key.Kp0 and <= Key.Kp9 => (int)(к.Keycode - Key.Kp0),
                _ => -1,
            };
            if (номер >= 0 && номер < _ответы.Count && !_ответы[номер].Disabled)
            {
                _ответы[номер].EmitSignal(BaseButton.SignalName.Pressed);
                AcceptEvent();
            }
        }
    }
}
