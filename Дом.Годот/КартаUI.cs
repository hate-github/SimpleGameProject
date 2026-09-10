// Карта мест (ГДД 20): открывается клавишей M.
//
// Четыре места — и это не список равноценных кнопок, а очередь, которую
// разбирают сверху. Порядок задаёт не удобство, а то, насколько дико туда
// идти, пока жизнь ещё похожа на прежнюю: в первый день метели человек
// идёт в магазин на Заречной, а не роется в мусорках у своего подъезда.
// Рыться начинают, когда магазин вынесли.
//
// Экран показывает то, что герой **думает**, а не то, что есть: «по-моему
// там 100%» — это `знаю_место`, личное знание, которое бывает и неверным.
// Правды мира (`h.богатство_места`) здесь нет и быть не должно: с ней
// «где ещё что-то есть» перестаёт быть предметом обмена, услуги и вранья,
// а соврать о пустом магазине, чтобы сходить туда одному, — законный ход.
//
// И кнопок «пойти» здесь нет. Куда идти, решает дом, спрашивая через тот же
// шов, что и всё остальное; карта — это то, с чем игрок подходит к вопросу,
// а не второй способ отвечать на него.

using Godot;
using Дом.Экран;
using Дом.Ядро;

namespace Дом.Годот;

public partial class КартаUI : PanelContainer
{
    private VBoxContainer _столбец = null!;
    private Label _заголовок = null!;

    /// <summary>Откуда брать. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    public bool открыта => Visible;

    public override void _Ready()
    {
        Visible = false;
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var фон = new StyleBoxFlat { BgColor = new Color("#12110f") };
        AddThemeStyleboxOverride("panel", фон);

        var середина = new CenterContainer();
        середина.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(середина);

        var всё = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        середина.AddChild(всё);

        _заголовок = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _заголовок.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        всё.AddChild(_заголовок);

        _столбец = new VBoxContainer();
        всё.AddChild(_столбец);

        var закрыть = new Button { Text = "закрыть (M)" };
        закрыть.Pressed += Убрать;
        всё.AddChild(закрыть);
    }

    public void Показать()
    {
        if (Сеанс is null)
            return;
        Visible = true;
        Обновить();
    }

    public void Убрать() => Visible = false;

    private void Обновить()
    {
        var карта = Сеанс!.Карта();
        _заголовок.Text = карта.Count > 0 && карта[0].закрыто
            ? "на улицу не выйти: мороз ниже предела одежды на "
              + $"{Текст.Ф(-карта[0].запас_мороза, 0)}°"
            : "куда можно сходить";

        foreach (var узел in _столбец.GetChildren())
            узел.QueueFree();

        foreach (var м in карта)
        {
            var строка = new HBoxContainer();
            _столбец.AddChild(строка);

            Подпись(строка, м.имя, 220, м.пусто ? "#5a5148" : "#e8e2d0");
            Подпись(строка, $"{Текст.Ф(м.часы, 1)} ч", 60, "#a8a294");

            // сколько там, по его мнению, осталось — полоской, потому что
            // это оценка, а не число из ведомости
            var сколько = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                Value = Mathf.Clamp(м.знаю, 0.0, 1.0),
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(180, 14),
            };
            строка.AddChild(сколько);

            Подпись(строка, м.пусто ? "по-моему пусто"
                                    : $"по-моему там {Текст.Ф(м.знаю * 100, 0)}%",
                    170, м.пусто ? "#c86a5a" : "#a8a294");
            Подпись(строка,
                    $"дорога {Текст.Ф(м.дорога, 1)} · люди {Текст.Ф(м.угроза, 1)}",
                    200, "#6f6a5e");
        }

        var примечание = new Label
        {
            Text = "Это то, что я думаю. Сходит кто-нибудь — узнаю точнее.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        примечание.AddThemeColorOverride("font_color", new Color("#4f4b42"));
        _столбец.AddChild(примечание);
    }

    private static void Подпись(HBoxContainer куда, string текст, int ширина,
                                string цвет)
    {
        var l = new Label { Text = текст, CustomMinimumSize = new Vector2(ширина, 0) };
        l.AddThemeColorOverride("font_color", new Color(цвет));
        куда.AddChild(l);
    }

    /// <summary>M открывает и закрывает; повтор клавиши отбрасывается.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey к || !к.Pressed || к.Echo)
            return;
        if (к.Keycode == Key.M)
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
