// Клавиши в игре — действия движка, а не буквы (`Дом.Экран.Клавиши`).
//
// До настроек буквы были зашиты в шести узлах, и каждый ловил свою.
// Теперь узел спрашивает «нажато ли действие», а какая клавиша за ним
// стоит, решает игрок (Esc → управление). Имя действия в движке — тот же
// ключ, что в файле настроек: разойтись им негде. Что ни одна буква
// не осталась зашитой, сверяет консоль (раздел «настройки»): строка
// с `Key.` и буквой проходит только с пометкой «клавиша: причина».
//
// Клавиша ловится по месту на клавиатуре (`PhysicalKeycode`): на русской
// раскладке W — это Ц, и ходьба от раскладки зависеть не должна.
//
// Кнопка мыши за действием срабатывает, только пока мышь захвачена:
// с отпущенной мышью щелчок — это щелчок по экрану (список, телефон),
// а не «постучать в дверь под прицелом».

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public static class Управление
{
    private static Настройки? _настройки;

    // в том же порядке, что `Клавиши.МЫШЬ`
    private static readonly MouseButton[] КНОПКИ =
    {
        MouseButton.Left, MouseButton.Right, MouseButton.Middle,
        MouseButton.Xbutton1, MouseButton.Xbutton2,
    };

    /// <summary>Разложить клавиши из настроек по действиям движка.
    /// Зовётся до того, как кто-нибудь спросит действие: спросить
    /// несуществующее действие — ошибка движка.</summary>
    public static void Применить(Настройки н)
    {
        _настройки = н;
        foreach (var п in Клавиши.ВСЕ)
        {
            if (!InputMap.HasAction(п.ключ))
                InputMap.AddAction(п.ключ);
            InputMap.ActionEraseEvents(п.ключ);
            if (Событие(н.Клавиша(п.ключ)) is InputEvent событие)
                InputMap.ActionAddEvent(п.ключ, событие);
        }
    }

    /// <summary>Событие движка под слово из настроек; незнакомое — ничего.</summary>
    public static InputEvent? Событие(string клавиша)
    {
        if (клавиша.Length == 0)
            return null;
        int i = Индекс_мыши(клавиша);
        if (i >= 0)
            return new InputEventMouseButton { ButtonIndex = КНОПКИ[i] };
        var код = OS.FindKeycodeFromString(клавиша);
        return код == Key.None ? null : new InputEventKey { PhysicalKeycode = код };
    }

    /// <summary>Знает ли движок такую клавишу — для починки файла.</summary>
    public static bool Известна(string клавиша)
        => Индекс_мыши(клавиша) >= 0 || OS.FindKeycodeFromString(клавиша) != Key.None;

    /// <summary>
    /// Слово для настроек из нажатия — или ничего, если нажали не клавишу
    /// и не кнопку мыши (колесо, отпускание, повтор зажатой клавиши).
    /// </summary>
    public static string? Клавиша(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey к when к.Pressed && !к.Echo:
                var код = к.PhysicalKeycode != Key.None ? к.PhysicalKeycode : к.Keycode;
                if (код == Key.None)
                    return null;
                string имя = OS.GetKeycodeString(код);
                return имя.Length == 0 ? null : имя;
            case InputEventMouseButton м when м.Pressed:
                int i = System.Array.IndexOf(КНОПКИ, м.ButtonIndex);
                return i >= 0 ? Клавиши.МЫШЬ[i] : null;
            default:
                return null;
        }
    }

    /// <summary>Нажато ли действие этим событием. Повтор зажатой клавиши
    /// нажатием не считается: один вопрос — одно нажатие (этап 3).</summary>
    public static bool Нажато(InputEvent e, string действие)
    {
        if (e is InputEventMouseButton && Input.MouseMode != Input.MouseModeEnum.Captured)
            return false;
        return e.IsActionPressed(действие, allowEcho: false);
    }

    /// <summary>Держат ли действие сейчас — для ходьбы.</summary>
    public static bool Зажато(string действие) => Input.IsActionPressed(действие);

    /// <summary>Как сейчас зовётся клавиша действия — для подсказок.</summary>
    public static string Имя(string действие)
        => Клавиши.Подпись(_настройки?.Клавиша(действие)
                           ?? Клавиши.Какая(действие)?.клавиша ?? "");

    /// <summary>«[E]» — клавиша действия в скобках, как её пишут подсказки.</summary>
    public static string В_скобках(string действие) => $"[{Имя(действие)}]";

    private static int Индекс_мыши(string клавиша)
    {
        for (int i = 0; i < Клавиши.МЫШЬ.Count; i++)
            if (Клавиши.МЫШЬ[i] == клавиша)
                return i;
        return -1;
    }
}
