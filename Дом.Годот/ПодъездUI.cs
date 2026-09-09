// План подъезда: кто где живёт и что с ним.
//
// Здесь показывается ровно то, что и так видно соседу: номер квартиры, имя,
// занятие, к кому переехал, и — если умер — когда и отчего. Шкаф, тело
// и чувства сюда не попадают: за них отвечает экран знания, где всё идёт
// через `believed` и осведомлённость.
//
// Квартиры по этажам, потому что подъезд — это дом: сверху вниз, как
// в жизни. Соседняя дверь на площадке и квартира сверху — это ещё
// и механика (откуда можно ломать стену), так что расположение здесь
// не украшение.

using Godot;
using Дом.Экран;
using Дом.Ядро;

namespace Дом.Годот;

public partial class ПодъездUI : ScrollContainer
{
    private VBoxContainer _столбец = null!;

    /// <summary>Откуда брать. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        _столбец = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        AddChild(_столбец);
    }

    public void Обновить()
    {
        if (Сеанс is null)
            return;
        foreach (var узел in _столбец.GetChildren())
            узел.QueueFree();

        var l = new Label { Text = "подъезд" };
        l.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        _столбец.AddChild(l);

        var мёртвые = Сеанс.План().Where(ж => !ж.здесь).ToList();

        int этаж = -1;
        foreach (var кв in Сеанс.Квартиры())
        {
            if (кв.этаж != этаж)
            {
                этаж = кв.этаж;
                var р = new Label { Text = $"— этаж {этаж} —" };
                р.AddThemeColorOverride("font_color", new Color("#4f4b42"));
                _столбец.AddChild(р);
            }
            _столбец.AddChild(Строка(кв));

            // что случилось в этой квартире на днях — строкой значков.
            // Это и есть «что светится, откуда дым»: рисуется из потока
            // событий, а не из полей дома
            var свежее = Сеанс.ВКвартире(кв.номер);
            if (свежее.Count > 0)
            {
                var з = new Label
                {
                    Text = "    " + string.Join(" ", свежее.Select(Значок)),
                };
                з.AddThemeColorOverride("font_color", new Color("#c8945a"));
                _столбец.AddChild(з);
            }
        }

        // выбывшие — отдельно внизу: их квартира на плане уже не их,
        // а помнить о них надо
        if (мёртвые.Count > 0)
        {
            var р = new Label { Text = "— не здесь —" };
            р.AddThemeColorOverride("font_color", new Color("#4f4b42"));
            _столбец.AddChild(р);
            foreach (var ж in мёртвые)
            {
                var м = new Label
                {
                    Text = $"кв{ж.квартира,-3} {ж.имя} — †"
                           + (ж.умер_днём is int д ? $" день {д}" : "")
                           + (ж.причина is not null ? $", {ж.причина}" : ""),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                м.AddThemeColorOverride("font_color", new Color("#5a5148"));
                _столбец.AddChild(м);
            }
        }
    }

    /// <summary>
    /// Значок события. Не «иконка»: пока это знак в строке, и заменить его
    /// картинкой можно будет, не трогая ничего, кроме этой таблицы, —
    /// потому что решение «что случилось» принято в ядре, а здесь только
    /// «чем это нарисовать».
    /// </summary>
    private static string Значок(Событие с) => с.вид switch
    {
        ВидСобытия.СМЕРТЬ => "†",
        ВидСобытия.УБИЙСТВО => "🕱",
        ВидСобытия.ИЗГНАНИЕ => "⇥",
        ВидСобытия.НАЛЁТ => "⚔",
        ВидСобытия.ИСХОД => "⚑",
        ВидСобытия.КРАЖА => "◔",
        ВидСобытия.ОТЪЁМ => "✊",
        ВидСобытия.ПОМОЩЬ => "✚",
        ВидСобытия.ОБМЕН => "⇄",
        ВидСобытия.ЛЕЧЕНИЕ => "❦",
        ВидСобытия.ПЕРЕЕЗД => "⌂",
        ВидСобытия.СОБРАНИЕ => "☰",
        ВидСобытия.ВСКРЫТИЕ => "⚿",
        ВидСобытия.ПОЖАР => "🔥",
        _ => "·",
    };

    private static Label Строка(КвартираНаПлане кв)
    {
        string кто = кв.кто is null ? "пусто" : $"{кв.кто.имя} — {кв.кто.роль}";
        if (кв.гости.Count > 0)
            кто += ", у него " + string.Join(", ", кв.гости.Select(г => г.имя));

        var метки = new List<string>();
        if (кв.разобрана)
            метки.Add("разобрана");
        if (кв.с_телом)
            метки.Add("тело");
        if (кв.горел)
            метки.Add("костёр");
        if (кв.открыта)
            метки.Add("открыта");
        string хвост = метки.Count > 0 ? "  · " + string.Join(", ", метки) : "";

        var l = new Label
        {
            Text = $"кв{кв.номер,-3} {кто}{хвост}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeColorOverride("font_color",
            кв.кто is null ? new Color("#5a5148")            // пустая
            : кв.гости.Count > 0 ? new Color("#7f9a8a")      // живут вдвоём
            : new Color("#a8a294"));
        return l;
    }
}
