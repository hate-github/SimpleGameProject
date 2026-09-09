// Телефон (ГДД 19). Первое приложение — мессенджер.
//
// Общий чат жильцов уже есть в симуляции и уже что-то делает с домом:
// реплика в нём — это не украшение, а способ узнать, у кого что есть
// и кто кого боится. Поэтому телефон здесь и заводится первым, раньше
// всякой карты и всякой анимации: он показывает то, что уже работает.
//
// Экран ничего не решает: он берёт строки вида `ЧАТ` из ленты и кладёт
// их в столбик. Что сказано и кем — решено в `house/chat.py` и перенесено
// в `Дом.Ядро/Чат.cs`.
//
// Чего здесь нет и не будет: возможности написать самому. Игрок в чат
// не пишет — пока в симуляции нет действия «написать в чат», приделать
// поле ввода значило бы приделать кнопку, которая ничего не делает.
// Появится действие — появится и поле.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class ТелефонUI : PanelContainer
{
    private RichTextLabel _текст = null!;
    private int _показано;

    /// <summary>Откуда брать. Ставится корневым узлом.</summary>
    public Сеанс? Сеанс { get; set; }

    public override void _Ready()
    {
        var столбец = new VBoxContainer();
        AddChild(столбец);

        var шапка = new Label { Text = "чат подъезда" };
        шапка.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        столбец.AddChild(шапка);

        var прокрутка = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        столбец.AddChild(прокрутка);

        _текст = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        прокрутка.AddChild(_текст);
    }

    /// <summary>Дописать то, что пришло с прошлого раза. Именно дописать:
    /// перерисовывать весь чат каждый кадр — значит терять место прокрутки
    /// под рукой у читающего.</summary>
    public void Обновить()
    {
        if (Сеанс is null)
            return;
        var чат = Сеанс.Вида(ВидСтроки.ЧАТ);
        for (; _показано < чат.Count; _показано++)
        {
            var с = чат[_показано];
            // «  [чат] Толик: замёрз» → «Толик: замёрз»: скобки нужны
            // консоли, чтобы строка не терялась среди прочих, а здесь
            // весь экран и так про чат
            string текст = с.текст.Replace("  [чат] ", "", System.StringComparison.Ordinal);
            _текст.AppendText($"[color=#6f6a5e]день {с.день}[/color]  "
                              + $"{текст.Replace("[", "[lb]", System.StringComparison.Ordinal)}\n");
        }
    }
}
