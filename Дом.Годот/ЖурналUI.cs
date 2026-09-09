// Лента дня: то, что человек читает.
//
// Первое, что вообще нужно движку в этой игре (навык godot-boundary):
// не пространство и не анимация, а строка «08:30 Оксана спустилась
// в свой погреб».
//
// Узел ничего не считает: он получает `СтрокаЛенты` и решает про неё
// ровно два вопроса — показывать ли (по заметности) и каким цветом.
// Что случилось и когда — уже решено в ядре.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class ЖурналUI : ScrollContainer
{
    private RichTextLabel _текст = null!;
    private int _день = -1;

    /// <summary>Что показывать: 0 — только крупное, 1 — обычно,
    /// 2 — каждое действие. Та же шкала, что у журнала.</summary>
    [Export] public int Подробность { get; set; } = 1;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        FollowFocus = true;
        _текст = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        AddChild(_текст);
    }

    /// <summary>Строка не от дома: заголовок партии, итог, беда.</summary>
    public void Шапка(string текст)
    {
        _текст.AppendText($"\n[b]{Экранировать(текст)}[/b]\n");
        Вниз();
    }

    /// <summary>Строка ленты. Заметность ниже порога не показывается —
    /// но остаётся в ленте, и стоит игроку прибавить подробность,
    /// как весь день можно перечитать заново, ничего не переигрывая.</summary>
    public void Строка(СтрокаЛенты с)
    {
        int порог = Подробность == 0 ? 2 : (Подробность == 1 ? 1 : 0);
        if (с.заметность < порог)
            return;

        if (с.день != _день)
        {
            _день = с.день;
            _текст.AppendText($"\n[b]── день {с.день} ──[/b]\n");
        }

        string цвет = с.скрытая ? "#8a7fb0"          // то, чего дом не знает
                    : с.заметность >= 2 ? "#e8e2d0"  // крупное
                    : "#a8a294";
        string час = с.час is not null ? $"[color=#6f6a5e]{с.час}[/color] " : "";
        _текст.AppendText($"{час}[color={цвет}]{Экранировать(с.текст)}[/color]\n");
        Вниз();
    }

    /// <summary>Лента идёт вниз, и смотреть на неё надо снизу.</summary>
    private void Вниз()
    {
        var полоса = GetVScrollBar();
        if (полоса is not null)
            ScrollVertical = (int)полоса.MaxValue;
    }

    /// <summary>Квадратная скобка в BBCode — начало тега. В журнале она
    /// встречается («[чат] Толик: …»), и без экранирования такая строка
    /// пропала бы целиком.</summary>
    private static string Экранировать(string s)
        => s.Replace("[", "[lb]", System.StringComparison.Ordinal);
}
