// Карман вне времени (ГДД 9): открывается клавишей I, время стоит.
//
// «Время стоит» здесь не режим, который надо было заводить, а то, как оно
// и так устроено: карман открывается только пока дом спит на вопросе.
// В любой другой момент поток дома считает, и лезть в чужой шкаф из кадра
// было бы чтением из-под руки у считающего — то самое правило, которое
// держит три остальных экрана.
//
// Экран показывает две колонки: что в шкафу и что в кармане. Класть можно
// только из шкафа, доставать — только обратно; никакого «переложить между
// жизнями» нет, потому что содержимое умирает вместе с героем (ГДД 8).
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
    private Label _заголовок = null!;
    private Label _свидетели = null!;

    /// <summary>Откуда брать. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    /// <summary>Открыт ли сейчас.</summary>
    public bool открыт => Visible;

    public override void _Ready()
    {
        Visible = false;
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var фон = new StyleBoxFlat { BgColor = new Color("#12110f") };
        AddThemeStyleboxOverride("panel", фон);

        var середина = new CenterContainer();
        середина.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(середина);

        var столбец = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        середина.AddChild(столбец);

        _заголовок = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        столбец.AddChild(_заголовок);

        _свидетели = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _свидетели.AddThemeColorOverride("font_color", new Color("#c86a5a"));
        столбец.AddChild(_свидетели);

        var две = new HBoxContainer();
        столбец.AddChild(две);

        _шкаф = Колонка(две, "в шкафу — положить");
        _внутри = Колонка(две, "в кармане — достать");

        var закрыть = new Button { Text = "закрыть (I)" };
        закрыть.Pressed += Убрать;
        столбец.AddChild(закрыть);
    }

    private static VBoxContainer Колонка(HBoxContainer куда, string имя)
    {
        var столбец = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        куда.AddChild(столбец);
        var l = new Label { Text = имя };
        l.AddThemeColorOverride("font_color", new Color("#6f6a5e"));
        столбец.AddChild(l);
        return столбец;
    }

    public void Показать()
    {
        if (Сеанс?.я.карман is null)
            return;                     // кармана нет — нечего и открывать
        Visible = true;
        Обновить();
    }

    public void Убрать()
    {
        Visible = false;
        _свидетели.Text = "";
    }

    private void Обновить()
    {
        var я = Сеанс!.я;
        var карман = я.карман!;
        _заголовок.Text = $"карман вне времени — {Текст.Ф(карман.занято, 1)} "
                          + $"из {Текст.Ф(карман.предел, 1)}";

        foreach (var узел in _шкаф.GetChildren().Skip(1))
            узел.QueueFree();
        foreach (var узел in _внутри.GetChildren().Skip(1))
            узел.QueueFree();

        foreach (var р in я.stock.Ключи.OrderBy(x => x, StringComparer.Ordinal))
        {
            double сколько = я.stock.Взять(р, 0.0);
            if (сколько <= 0.0)
                continue;
            Кнопка(_шкаф, $"{р} {Текст.G(Текст.Округлить(сколько, 2))}  →",
                   () => Положить(р, System.Math.Min(1.0, сколько)));
        }

        foreach (var р in карман.ресурсы.OrderBy(x => x, StringComparer.Ordinal))
            Кнопка(_внутри, $"←  {р} {Текст.G(Текст.Округлить(карман.Сколько(р), 2))}",
                   () =>
                   {
                       карман.Достать(я, р, 1.0);
                       Обновить();
                   });
    }

    private static void Кнопка(VBoxContainer куда, string текст, System.Action что)
    {
        var к = new Button { Text = текст, Alignment = HorizontalAlignment.Left };
        к.Pressed += что;
        куда.AddChild(к);
    }

    private void Положить(string ресурс, double сколько)
    {
        var видели = Сеанс!.я.карман!.Положить(Сеанс.дом, Сеанс.я, ресурс, сколько);
        _свидетели.Text = видели.Count == 0
            ? ""
            : "Это видели: " + string.Join(", ", видели.Select(
                  в => Сеанс.дом.get(в.кто)?.@short ?? в.кто))
              + " — вещь исчезла у них на глазах.";
        Обновить();
    }

    /// <summary>I открывает и закрывает. Повтор клавиши отбрасывается —
    /// зажатая «I» иначе мигала бы экраном.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey к || !к.Pressed || к.Echo)
            return;
        if (к.Keycode == Key.I)
        {
            if (Visible)
                Убрать();
            else
                Показать();
            AcceptEvent();
        }
        else if (Visible && к.Keycode == Key.Escape)
        {
            Убрать();
            AcceptEvent();
        }
    }
}
