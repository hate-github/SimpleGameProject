// Экран вопроса: тот самый шов, ради которого всё это делалось.
//
// Правило одно для всех (ГДД 13): игроку показываются те же оценённые
// варианты, что видит NPC, и в том же порядке. Поэтому здесь нельзя
// три вещи, и они названы в навыке прямо: не решать за игрока,
// не фильтровать варианты, не сортировать их иначе.
//
// Порядок особенно: он приходит из ядра по убыванию веса, и номер варианта
// значит то же самое, что в консоли. Пересортировать список «поудобнее» —
// значит поменять игру, не заметив этого.
//
// Вес показывается. Это не отладка: в ГДД 13 объяснение решения — часть
// игры, а не служебная информация. Интерфейс, который показывает игроку
// меньше, чем знает NPC, — уже другая игра.

using Godot;
using Дом.Ядро;

namespace Дом.Годот;

public partial class ВопросUI : PanelContainer
{
    private VBoxContainer _столбец = null!;
    private Label _заголовок = null!;
    private VBoxContainer _варианты = null!;
    private System.Action<int>? _ответить;

    /// <summary>Сколько вариантов показывать сразу. У жильца в доме
    /// на пятнадцать их бывает и тридцать; остальные — по кнопке.</summary>
    [Export] public int Коротко { get; set; } = 12;

    private bool _всё;
    private ВопросИгроку? _текущий;

    public override void _Ready()
    {
        Visible = false;
        _столбец = new VBoxContainer();
        AddChild(_столбец);

        _заголовок = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _столбец.AddChild(_заголовок);

        _варианты = new VBoxContainer();
        _столбец.AddChild(_варианты);
    }

    public void Показать(ВопросИгроку в, System.Action<int> ответить)
    {
        _текущий = в;
        _ответить = ответить;
        _всё = false;
        Visible = true;
        _заголовок.Text = в.заголовок;
        Разложить();
    }

    public void Убрать()
    {
        Visible = false;
        _текущий = null;
        _ответить = null;
        Очистить();
    }

    private void Разложить()
    {
        Очистить();
        if (_текущий is null)
            return;

        int сколько = _всё ? _текущий.варианты.Count
                           : System.Math.Min(Коротко, _текущий.варианты.Count);
        for (int i = 0; i < сколько; i++)
        {
            int номер = i;                         // замыкание берёт копию
            var в = _текущий.варианты[i];
            var кнопка = new Button
            {
                Text = $"{i}.  {в.строка}",
                Alignment = HorizontalAlignment.Left,
            };
            кнопка.Pressed += () => _ответить?.Invoke(номер);
            _варианты.AddChild(кнопка);
        }

        if (сколько < _текущий.варианты.Count)
        {
            var ещё = new Button
            {
                Text = $"… и ещё {_текущий.варианты.Count - сколько}",
                Alignment = HorizontalAlignment.Left,
            };
            ещё.Pressed += () => { _всё = true; Разложить(); };
            _варианты.AddChild(ещё);
        }
    }

    private void Очистить()
    {
        foreach (var узел in _варианты.GetChildren())
            узел.QueueFree();
    }

    /// <summary>Клавишами тоже: цифра — вариант, «всё» на пробел.
    /// Мышь для тридцати вариантов в день — это тридцать движений
    /// туда и обратно.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _текущий is null || e is not InputEventKey к || !к.Pressed)
            return;
        if (к.Keycode == Key.Space)
        {
            _всё = true;
            Разложить();
            AcceptEvent();
            return;
        }
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
