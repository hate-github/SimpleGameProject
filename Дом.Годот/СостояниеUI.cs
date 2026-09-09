// Своё состояние: шесть шкал, шкаф и что я знаю о соседях.
//
// Шкалы приходят числами, а не строками, и потому рисуются полосками —
// ровно затем слой экрана и заведён. Консоль из тех же чисел печатает
// «сыт 85», и оба вида не могут разойтись: считает их одно место.
//
// Знание о соседях — через `Сеанс.Знание()`, то есть через `believed`
// и `Сведения.aware`, а не через чужие поля. Это не вежливость к архитектуре:
// показать игроку настоящий чужой шкаф значит отдать ему то, чего нет
// ни у одного NPC, — и вся игра про догадки перестанет быть игрой.

using Godot;
using Дом.Экран;
using Дом.Ядро;

namespace Дом.Годот;

public partial class СостояниеUI : ScrollContainer
{
    private VBoxContainer _столбец = null!;

    /// <summary>Откуда брать. Ставится корневым узлом до первого
    /// <see cref="Обновить"/>.</summary>
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

        var с = Сеанс.Состояние();
        Заголовок($"{с.имя}, кв.{с.квартира} — {с.роль}");

        foreach (var (имя, значение) in с.шкалы)
            Шкала(имя, значение);

        if (с.беды.Count > 0)
            Строка(string.Join("; ", с.беды), new Color("#c86a5a"));

        Заголовок("в шкафу");
        if (с.шкаф.Count == 0)
            Строка("пусто", new Color("#6f6a5e"));
        foreach (var з in с.шкаф)
            Строка($"{з.ресурс} {Текст.G(з.сколько)}");

        Строка($"часов до ночи {Текст.Ф(с.часов_до_ночи, 1)}", new Color("#6f6a5e"));

        Заголовок("что я знаю о соседях");
        foreach (var сосед in Сеанс.Знание())
            Строка($"{сосед.имя}: еда {Текст.Ф(сосед.еда, 1)}, "
                   + $"дрова {Текст.Ф(сосед.дрова, 1)}, "
                   + $"доверие {Текст.Ф(сосед.доверие, 1)}",
                   // чем меньше знаю о человеке, тем бледнее строка:
                   // оценка при `aware` под двадцать — почти выдумка
                   new Color("#a8a294") with { A = (float)Mathf.Clamp(
                       0.35 + сосед.знаю_о_нём / 140.0, 0.35, 1.0) });
    }

    private void Заголовок(string текст)
    {
        var l = new Label { Text = текст };
        l.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        _столбец.AddChild(l);
    }

    private void Строка(string текст, Color? цвет = null)
    {
        var l = new Label
        {
            Text = текст,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeColorOverride("font_color", цвет ?? new Color("#a8a294"));
        _столбец.AddChild(l);
    }

    /// <summary>Полоска на шкалу. Ноль слева, сто справа — у всех шести
    /// одинаково, иначе их не сравнить глазом.</summary>
    private void Шкала(string имя, double значение)
    {
        var строка = new HBoxContainer();
        var подпись = new Label { Text = имя, CustomMinimumSize = new Vector2(80, 0) };
        подпись.AddThemeColorOverride("font_color", new Color("#a8a294"));
        строка.AddChild(подпись);

        var полоса = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = Mathf.Clamp(значение, 0, 100),
            ShowPercentage = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 14),
        };
        строка.AddChild(полоса);

        var число = new Label
        {
            Text = Текст.Ф(значение, 0),
            CustomMinimumSize = new Vector2(36, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        число.AddThemeColorOverride("font_color", new Color("#a8a294"));
        строка.AddChild(число);

        _столбец.AddChild(строка);
    }
}
