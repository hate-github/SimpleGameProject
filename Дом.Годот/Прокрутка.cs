// Прокрутка: окно, которое не влезло в экран, листается, а не обрезается.
//
// Окна игры собраны одинаково — столбец посередине экрана
// (`CenterContainer`). Пока строк мало, так и надо; но полка с тридцатью
// вещами, четыре инструмента в руках, кладовая участка — и столбец выше
// экрана: середина уводит его верх и низ за края, и то, что внизу
// (что в руках, мысли полки, «закрыть»), пропадало — нажать было нечем
// (автор, 28.09.2026). Теперь столбец стоит в прокрутке на весь экран:
// влез — посередине, как было; не влез — листается колесом, сбоку полоса,
// и кнопка, до которой дошёл фокус, сама въезжает в экран.
//
// Ловушка Godot: `ScrollContainer` своей высоты по содержимому не имеет —
// ему нужен размер снаружи (здесь — весь экран окна), а ребёнку —
// растягиваться (`ExpandFill`): иначе короткое содержимое прилипает
// к верху, а не стоит посередине.

using Godot;

namespace Дом.Годот;

public static class Прокрутка
{
    /// <summary>Середина экрана с прокруткой — в неё кладут столбец окна.</summary>
    public static CenterContainer По_центру(Control окно)
    {
        var прокрутка = new ScrollContainer
        {
            Name = "прокрутка",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        прокрутка.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        окно.AddChild(прокрутка);
        var середина = new CenterContainer
        {
            Name = "середина",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        прокрутка.AddChild(середина);
        return середина;
    }
}
