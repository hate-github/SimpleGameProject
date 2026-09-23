// Долгое дело: взлом, обыск — то, что делается не мгновенно.
//
// Посреди экрана, сразу под прицелом, — полоса, которая заполняется,
// и в ней слово с бегущими точками («взлом.», «взлом..», «взлом...»);
// под полосой — как бросить. Пока дело идёт, герой скован: ни шагу,
// ни поворота головы (`Герой.Скован`), экраны не открываются, и мышь
// остаётся за делом, а не за камерой (задание автора, пп. 1 и 5: карман
// открывался посреди взлома и ложился поверх шкалы). Бросить — клавиша
// действия или Esc; ничего не случилось.
//
// Дело, которому нужен курсор (будущая мини-игра), говорит об этом само
// (`Долгое.мышь`): тогда корневой узел отпускает мышь на время дела,
// а камера по-прежнему стоит.
//
// Первая версия ставила слово и тонкую черту над подсказкой, но корневой
// узел раскладывал экран раньше, чем шкала появлялась, — и слово висело
// в левом верхнем углу, а черты не было видно вовсе (у неё не было
// размера). Теперь шкала раскладывается и сама, по размеру родителя.
//
// Итог дела меняет дом, а менять его можно, только пока он спит на вопросе.
// Поэтому, пока дело идёт, ответить на вопрос нельзя (`ДомУзел.Ответить`):
// дом не проснётся посреди взлома, и итог ляжет в спящий дом.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

/// <summary>Что делается долго: слово для шкалы, сколько секунд, что
/// сделать в конце, что — если прервали, что — пока идёт (прошло секунд),
/// и нужен ли делу курсор.</summary>
public sealed record Долгое(string слово, double секунд, System.Action готово,
                            System.Action? прервано = null,
                            System.Action<double>? идёт = null,
                            bool мышь = false);

public partial class ДелоUI : Control
{
    private Panel _рамка = null!;
    private ColorRect _шкала = null!;
    private Label _слово = null!;
    private Label _как_бросить = null!;
    private Долгое? _дело;
    private Предмет? _цель;
    private double _прошло;
    private double _без_взгляда;

    private const double ТОЧКИ = 0.3;       // как часто прибавляется точка
    private const double ТЕРПЕНИЕ = 0.25;   // сколько можно не смотреть на вещь

    private const float ШИРИНА = 320;
    private const float ВЫСОТА = 30;
    private const float ОТ_ПРИЦЕЛА = 24;    // полоса — сразу под точкой прицела

    public bool занят => _дело is not null;

    /// <summary>Нужен ли идущему делу курсор (мини-игра), а не захваченная мышь.</summary>
    public bool нужна_мышь => _дело?.мышь ?? false;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        _рамка = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _рамка.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.035f, 0.03f, 0.78f),
            BorderColor = new Color("#8a8474"),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
        });
        AddChild(_рамка);

        _шкала = new ColorRect
        {
            Color = new Color(0.85f, 0.78f, 0.6f, 0.85f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_шкала);

        // слово поверх заполнения: с обводкой, чтобы читалось и на светлой
        // части полосы, и на тёмной
        _слово = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _слово.AddThemeColorOverride("font_color", new Color("#f4efe2"));
        _слово.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _слово.AddThemeConstantOverride("outline_size", 5);
        _слово.AddThemeFontSizeOverride("font_size", 17);
        AddChild(_слово);

        _как_бросить = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _как_бросить.AddThemeColorOverride("font_color", new Color("#a8a294"));
        _как_бросить.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_как_бросить);

        Разложить(GetParentControl()?.Size ?? GetViewportRect().Size);
    }

    /// <summary>Разложить по окну: посередине, сразу под прицелом.</summary>
    public void Разложить(Vector2 окно)
    {
        Position = new Vector2(окно.X / 2 - ШИРИНА / 2, окно.Y / 2 + ОТ_ПРИЦЕЛА);
        Size = new Vector2(ШИРИНА, ВЫСОТА + 24);
        if (_рамка is null)
            return;                 // ещё не готов: разложится в _Ready
        _рамка.Position = Vector2.Zero;
        _рамка.Size = new Vector2(ШИРИНА, ВЫСОТА);
        _слово.Position = Vector2.Zero;
        _слово.Size = new Vector2(ШИРИНА, ВЫСОТА);
        _как_бросить.Position = new Vector2(0, ВЫСОТА + 3);
        _как_бросить.Size = new Vector2(ШИРИНА, 18);
        Показать();
    }

    public void Начать(Долгое дело, Предмет цель)
    {
        Прервать();
        _дело = дело;
        _цель = цель;
        _прошло = 0;
        _без_взгляда = 0;
        Visible = true;
        _как_бросить.Text = $"{Управление.В_скобках(Клавиши.ДЕЙСТВИЕ)} или [Esc] — бросить";
        Показать();
    }

    /// <summary>Бросить дело: ничего не случилось.</summary>
    public void Прервать()
    {
        if (_дело is null)
            return;
        var д = _дело;
        Закончить();
        д.прервано?.Invoke();
    }

    /// <summary>
    /// Кадр дела. <paramref name="можно"/> — ходить и смотреть можно,
    /// и дом спит на вопросе; иначе дело прерывается.
    /// </summary>
    public void Кадр(double delta, Предмет? под_прицелом, bool можно)
    {
        if (_дело is null)
            return;
        if (!можно)
        {
            Прервать();
            return;
        }
        if (ReferenceEquals(под_прицелом, _цель))
            _без_взгляда = 0;
        else if ((_без_взгляда += delta) > ТЕРПЕНИЕ)
        {
            Прервать();
            return;
        }

        _прошло += delta;
        _дело.идёт?.Invoke(_прошло);
        if (_дело is null)
            return;                 // пока шло, его могли прервать
        if (_прошло >= _дело.секунд)
        {
            var д = _дело;
            Закончить();
            д.готово();
            return;
        }
        Показать();
    }

    private void Закончить()
    {
        _дело = null;
        _цель = null;
        Visible = false;
    }

    private void Показать()
    {
        if (_дело is null || _шкала is null)
            return;
        int точек = 1 + (int)(_прошло / ТОЧКИ) % 3;
        _слово.Text = _дело.слово + new string('.', точек);
        float доля = (float)System.Math.Clamp(_прошло / _дело.секунд, 0, 1);
        // заполнение — внутри рамки, на пиксель от края
        _шкала.Position = new Vector2(1, 1);
        _шкала.Size = new Vector2((ШИРИНА - 2) * доля, ВЫСОТА - 2);
    }
}
