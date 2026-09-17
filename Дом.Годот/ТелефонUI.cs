// Телефон (ГДД 19). Три приложения: мысли, чат подъезда и заметки.
//
// Мысли — это вопрос дня: что приходит в голову сделать, по весу, как дом
// его и собрал (порядок не трогается, вес показан). Нажать мысль —
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
    private TabContainer _вкладки = null!;
    private int _показано;                  // сколько реплик чата уже на экране
    private int _заметок = -1;              // сколько заметок нарисовано

    /// <summary>Откуда брать чат. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    /// <summary>Откуда брать заметки: они переживают жизнь.</summary>
    public Наследие? Наследие { get; set; }

    /// <summary>Мысль выбрана: её номер в вопросе и строка «где». Корень
    /// закрепляет её на экране.</summary>
    public System.Action<int, string>? Выбрано { get; set; }

    public override void _Ready()
    {
        _вкладки = new TabContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        AddChild(_вкладки);

        Мысли_страница();
        _чат = Страница("чат");
        _заметки = Страница("заметки");
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
        if (в.вопрос != Вопрос.ЧТО_ДЕЛАТЬ)
        {
            _мысли_шапка.Text = "сейчас не до раздумий — ответь на то, что спрашивают";
            return;
        }
        _мысли_шапка.Text = в.заголовок.Split('\n')[0]
                            + " — нажми мысль, чтобы понять, где это делается";
        for (int i = 0; i < в.варианты.Count; i++)
        {
            int номер = i;                       // замыкание берёт копию
            var (что, вес) = Разобрать(в.варианты[i].строка);
            var кнопка = new Button
            {
                Text = вес.Length > 0 ? $"{что}   · {вес}" : что,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            кнопка.Pressed += () => Выбрать(номер);
            _мысли.AddChild(кнопка);
        }
    }

    /// <summary>Открыть телефон на мыслях.</summary>
    public void К_мыслям() => _вкладки.CurrentTab = 0;

    private void Выбрать(int i)
    {
        if (_вопрос is null || i >= _вопрос.варианты.Count)
            return;
        var (что, _) = Разобрать(_вопрос.варианты[i].строка);
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
