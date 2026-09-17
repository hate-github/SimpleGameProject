// Окно вопроса: то, что дом спрашивает здесь и сейчас.
//
// Правило одно для всех (ГДД 13): игроку показываются те же оценённые
// варианты, что видит NPC, и в том же порядке. Поэтому здесь нельзя
// три вещи, и они названы в навыке прямо: не решать за игрока,
// не фильтровать варианты, не сортировать их иначе.
//
// Порядок особенно: он приходит из ядра по убыванию веса, и номер варианта
// значит то же самое, что в консоли. Пересортировать список «поудобнее» —
// значит поменять игру, не заметив этого. Вес показывается: объяснение
// решения — часть игры, а не отладка.
//
// ----------------------------------------------------------------------
//
// **Вопроса дня здесь нет.** Это мысли героя о том, что делать, и лежат
// они в телефоне (`ТелефонUI`, вкладка «мысли»): нажать мысль — узнать,
// где она делается, а сделать — подойти туда и нажать клавишу действия
// (`Дом.Экран.Места`). Первые версии держали вопрос дня листом в углу —
// десять строк поверх подъезда, с цифрами, — и автор попросил убрать его
// с экрана: лист закрывал полподъезда и читался приказом, а это догадки.
//
// Здесь остаются вопросы-ситуации: стучат в дверь, зовут на вылазку,
// просят поделиться, спрашивает собрание, пришла ночь. На них отвечают
// сейчас, и приходят они окном посередине: мышь свободна, герой стоит.
// Номер — цифрой или щелчком.
//
// Повтор клавиши (<c>Echo</c>) отбрасывается, и это не мелочь: зажатый
// на полсекунды «0» однажды ответил за игрока на дюжину вопросов подряд.
// Один вопрос — одно нажатие.

using Godot;
using Дом.Ядро;

namespace Дом.Годот;

public partial class ВопросUI : PanelContainer
{
    private Label _заголовок = null!;
    private ScrollContainer _окно = null!;
    private VBoxContainer _кнопки = null!;
    private System.Action<int>? _ответить;
    private ВопросИгроку? _текущий;

    private const int ВЫСОТА_КНОПКИ = 34;

    /// <summary>Открыто ли окно: пока открыто, герой стоит и мышь свободна.</summary>
    public bool Развёрнут => Visible;

    public ВопросИгроку? текущий => _текущий;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.065f, 0.06f, 0.94f),
            BorderColor = new Color("#8a8474"),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        });

        var столбец = new VBoxContainer();
        столбец.AddThemeConstantOverride("separation", 8);
        AddChild(столбец);

        _заголовок = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _заголовок.AddThemeFontSizeOverride("font_size", 17);
        _заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        столбец.AddChild(_заголовок);

        // Список прокручивается, а не растёт без предела: ночью вариантов
        // бывает по два на соседа
        _окно = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        столбец.AddChild(_окно);

        _кнопки = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _кнопки.AddThemeConstantOverride("separation", 2);
        _окно.AddChild(_кнопки);
    }

    /// <summary>Показать вопрос. Вопрос дня не показывается: он в телефоне.</summary>
    public void Показать(ВопросИгроку в, System.Action<int> ответить)
    {
        _текущий = в;
        _ответить = ответить;
        Visible = в.вопрос != Вопрос.ЧТО_ДЕЛАТЬ;
        Разложить();
    }

    public void Убрать()
    {
        Visible = false;
        _текущий = null;
        _ответить = null;
        Очистить();
    }

    /// <summary>Разложить заново — после настроек.</summary>
    public void Переписать()
    {
        if (Visible)
            Разложить();
    }

    private void Разложить()
    {
        Очистить();
        if (_текущий is null || !Visible)
            return;
        _заголовок.Text = _текущий.заголовок;
        for (int i = 0; i < _текущий.варианты.Count; i++)
        {
            int номер = i;                         // замыкание берёт копию
            var кнопка = new Button
            {
                Text = i < 10 ? $"{i}.  {_текущий.варианты[i].строка}"
                              : $"     {_текущий.варианты[i].строка}",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, ВЫСОТА_КНОПКИ - 2),
            };
            кнопка.Pressed += () => _ответить?.Invoke(номер);
            _кнопки.AddChild(кнопка);
        }
        Лечь();
    }

    /// <summary>Посередине экрана; высота — по числу вариантов, но не выше
    /// экрана.</summary>
    private void Лечь()
    {
        var экран = GetParentControl()?.Size ?? GetViewportRect().Size;
        int строк = _кнопки.GetChildCount();
        float ширина = Mathf.Min(760, экран.X * 0.62f);
        float список = Mathf.Min(экран.Y * 0.6f, строк * ВЫСОТА_КНОПКИ);
        _окно.CustomMinimumSize = new Vector2(0, список);
        float высота = Mathf.Min(экран.Y - 80, GetCombinedMinimumSize().Y);
        Size = new Vector2(ширина, высота);
        Position = new Vector2((экран.X - ширина) / 2, (экран.Y - высота) / 2);
    }

    public override void _Process(double delta)
    {
        if (Visible)
            Лечь();
    }

    /// <summary>Убрать кнопки. `QueueFree` снимает узел не сразу, поэтому
    /// его сначала отцепляют: иначе раскладка успевает посчитать высоту
    /// вместе с теми кнопками, которых уже нет.</summary>
    private void Очистить()
    {
        foreach (var узел in _кнопки.GetChildren())
        {
            _кнопки.RemoveChild(узел);
            узел.QueueFree();
        }
    }

    /// <summary>Цифра — вариант. Повтор клавиши отбрасывается.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _текущий is null || e is not InputEventKey к
            || !к.Pressed || к.Echo)
            return;
        int номер = к.Keycode switch
        {
            >= Key.Key0 and <= Key.Key9 => (int)(к.Keycode - Key.Key0),
            >= Key.Kp0 and <= Key.Kp9 => (int)(к.Keycode - Key.Kp0),
            _ => -1,
        };
        if (номер >= 0 && номер < _текущий.варианты.Count)
        {
            _ответить?.Invoke(номер);
            AcceptEvent();
        }
    }
}
