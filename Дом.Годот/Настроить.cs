// Настройки в игре: прочитать, записать, применить.
//
// Что настраивается и как лежит на диске — `Дом.Экран/Настройки.cs`;
// здесь — что это значит для движка: действия ввода (`Управление`),
// шины звука (`Шины`), окно.
//
// Файл — `user://настройки.json`, рядом с наследием, но отдельно:
// настройки не часть петли и не обнуляются вместе с ней. Сломанный файл
// игру не останавливает: что не разобралось, то по умолчанию, а в лог
// уходит, что именно.
//
// Окно, встроенное в редактор (вкладка «Игра» в Godot 4.4), режим
// и размер менять не даёт — ими распоряжается редактор. Там применяется
// всё, кроме окна, а экран настроек говорит об этом прямо.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public static class Настроить
{
    public static Настройки Прочитать()
    {
        var замечания = new List<string>();
        Настройки н;
        try
        {
            н = System.IO.File.Exists(Пути.Настройки)
                ? Настройки.ИзJson(System.IO.File.ReadAllText(Пути.Настройки), замечания)
                : new Настройки();
        }
        catch (System.Exception e) when (e is System.IO.IOException
                                             or System.UnauthorizedAccessException)
        {
            замечания.Add($"файл не открылся: {e.Message}");
            н = new Настройки();
        }
        // имена клавиш знает движок — вторая починка уже с ним
        замечания.AddRange(н.Починить(Управление.Известна));
        foreach (var з in замечания)
            GD.PushWarning($"[настройки] {з}");
        return н;
    }

    public static void Записать(Настройки н)
    {
        try
        {
            System.IO.File.WriteAllText(Пути.Настройки, н.ВJson(),
                                        new System.Text.UTF8Encoding(false));
        }
        catch (System.Exception e) when (e is System.IO.IOException
                                             or System.UnauthorizedAccessException)
        {
            GD.PushError($"[настройки] не записались: {e.Message}");
        }
    }

    /// <summary>Применить всё: клавиши, звук, окно.</summary>
    public static void Всё(Настройки н, Viewport корень)
    {
        Управление.Применить(н);
        Шины.Применить(н);
        Окно(н, корень);
    }

    /// <summary>Игра встроена во вкладку редактора — окном ей не распоряжаться.</summary>
    public static bool Встроено => Engine.IsEmbeddedInEditor();

    /// <summary>Экран, на котором стоит окно, целиком.</summary>
    public static Размер Экран()
    {
        var с = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
        return new Размер(с.X, с.Y);
    }

    /// <summary>Рабочий стол того же экрана — без панели задач.</summary>
    public static Размер Стол()
    {
        var с = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen()).Size;
        return new Размер(с.X, с.Y);
    }

    /// <summary>
    /// Режим, размер, картинка мира и синхронизация.
    ///
    /// На весь экран Godot видеорежим не меняет, поэтому «разрешение» там —
    /// доля экрана для трёхмерной картинки (`Scaling3DScale`): мир рисуется
    /// меньше и растягивается, надписи — в полный размер. В окне картинка
    /// всегда полная, а разрешение — это размер самого окна.
    /// </summary>
    public static void Окно(Настройки н, Viewport корень)
    {
        DisplayServer.WindowSetVsyncMode(н.синхронизация ? DisplayServer.VSyncMode.Enabled
                                                         : DisplayServer.VSyncMode.Disabled);
        bool встроено = Встроено;
        корень.Scaling3DScale = н.полный && !встроено ? (float)н.чёткость : 1f;
        if (встроено)
            return;

        var нужен = н.режим switch
        {
            Разрешения.ПОЛНЫЙ => DisplayServer.WindowMode.ExclusiveFullscreen,
            Разрешения.БЕЗ_РАМКИ => DisplayServer.WindowMode.Fullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        if (DisplayServer.WindowGetMode() != нужен)
            DisplayServer.WindowSetMode(нужен);
        if (нужен != DisplayServer.WindowMode.Windowed)
            return;

        // полный экран ставит окну «без рамки» и сам его не снимает
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
        var размеры = Разрешения.Для_окна(Стол());
        var р = размеры[Разрешения.Ближайший(размеры, н.окно)];
        var размер = new Vector2I(р.ширина, р.высота);

        // рамка — разница между окном с заголовком и без; меряется до смены
        // размера: заголовок от размера не зависит, а замер после смены
        // может ещё не дойти
        var рамка = DisplayServer.WindowGetSizeWithDecorations() - DisplayServer.WindowGetSize();
        var сдвиг = DisplayServer.WindowGetPosition() - DisplayServer.WindowGetPositionWithDecorations();
        DisplayServer.WindowSetSize(размер);

        var стол = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var угол = стол.Position + (стол.Size - (размер + рамка)) / 2;
        угол = new Vector2I(System.Math.Max(угол.X, стол.Position.X),
                            System.Math.Max(угол.Y, стол.Position.Y));
        DisplayServer.WindowSetPosition(угол + сдвиг);
    }
}
