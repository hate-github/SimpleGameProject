// Карточка вещи в инвентаре (docs/ЕДА_ПЛАН.md, этап 2): нажал на ячейку —
// модель крупно, её крутят мышью (зажать и вести), колесо приближает;
// рядом — что это, сколько насыщает, свежесть и срок, и дела: съесть,
// в карман, на полку. Пока карточку не трогают, модель сама медленно
// поворачивается — чтобы было видно, что её можно крутить.
//
// Слова карточки считает `Дом.Экран.Инвентарь` — здесь только рисуется.
// Живёт внутри экрана кармана и на его паузе: время стоит, пока смотришь.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class КарточкаЕды : PanelContainer
{
    private SubViewport _окно = null!;
    private SubViewportContainer _рамка = null!;
    private Node3D? _ось;
    private Camera3D? _глаз;
    private Label _имя = null!;
    private Label _текст = null!;
    private Label _весть = null!;
    private HBoxContainer _дела = null!;
    // модель в оси — большая сторона в единицу; с трёх единиц при угле 35°
    // она целиком в кадре и повёрнутая (с 1.8 банка вылезала за края)
    private const float ДАЛЬ = 3.0f;
    private float _рыск = 35f, _тангаж = 22f, _даль = ДАЛЬ;
    private bool _тащат;
    private double _покой = 10;

    /// <summary>Какую стопку показывает — по номеру первой вещи.</summary>
    public int? номер { get; private set; }

    /// <summary>Закрыли карточку.</summary>
    public System.Action Закрыта { get; set; } = () => { };

    public override void _Ready()
    {
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("#1b1916"),
            BorderColor = new Color("#4a4438"),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
        });

        var ряд = new HBoxContainer();
        ряд.AddThemeConstantOverride("separation", 18);
        AddChild(ряд);

        _рамка = new SubViewportContainer
        {
            Stretch = true,
            CustomMinimumSize = new Vector2(340, 340),
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "зажать и вести — повернуть, колесо — ближе и дальше",
        };
        _рамка.GuiInput += Мышь;
        ряд.AddChild(_рамка);
        _окно = new SubViewport
        {
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            HandleInputLocally = false,
        };
        _рамка.AddChild(_окно);

        var столбец = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        столбец.AddThemeConstantOverride("separation", 8);
        ряд.AddChild(столбец);
        _имя = new Label();
        _имя.AddThemeFontSizeOverride("font_size", 20);
        _имя.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        столбец.AddChild(_имя);
        _текст = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(330, 0),
        };
        _текст.AddThemeColorOverride("font_color", new Color("#c8c2b0"));
        столбец.AddChild(_текст);
        _весть = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _весть.AddThemeColorOverride("font_color", new Color("#d8b070"));
        столбец.AddChild(_весть);
        _дела = new HBoxContainer();
        _дела.AddThemeConstantOverride("separation", 6);
        столбец.AddChild(_дела);
    }

    /// <summary>Показать стопку: модель, слова и дела. <paramref name="весть"/> —
    /// что вышло из прошлого дела (съел, отравился, переложил).</summary>
    public void Показать(ЯчейкаЕды я, IReadOnlyList<(string что, System.Action сделать)> дела, string весть = "")
    {
        bool та_же = номер == я.номера[0] && Visible;
        номер = я.номера[0];
        _имя.Text = я.штук > 1 ? $"{я.имя} ×{я.штук}" : я.имя;
        _текст.Text = я.описание;
        _весть.Text = весть;
        foreach (var узел in _дела.GetChildren())
        {
            _дела.RemoveChild(узел);
            узел.QueueFree();
        }
        foreach (var (что, сделать) in дела)
        {
            var к = new Button { Text = что };
            var дело = сделать;
            к.Pressed += () => дело();
            _дела.AddChild(к);
        }
        var закрыть = new Button { Text = "назад" };
        закрыть.Pressed += Убрать;
        _дела.AddChild(закрыть);

        // модель — заново, только если сменился вариант (съел половину —
        // банка стала початой, и видно это должно быть сразу)
        foreach (var узел in _окно.GetChildren())
        {
            _окно.RemoveChild(узел);
            узел.QueueFree();
        }
        _ось = null;
        _глаз = null;
        if (МоделиАвтора.Первый(я.модель, я.меши) is { } н)
        {
            if (!та_же)
            {
                _рыск = 35f;
                _тангаж = 22f;
                _даль = ДАЛЬ;
                _покой = 10;
            }
            _ось = ИконкиЕды.Сцена(_окно, н, _рыск, _тангаж, _даль, new Color("#1b1916"));
            _глаз = _окно.GetChildren().OfType<Camera3D>().FirstOrDefault();
        }
        Visible = true;
    }

    public void Убрать()
    {
        if (!Visible)
            return;
        Visible = false;
        номер = null;
        Закрыта();
    }

    private void Мышь(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } кн:
                _тащат = кн.Pressed;
                _покой = 0;
                _рамка.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _даль = Math.Clamp(_даль - 0.15f, 1.4f, 5f);
                _покой = 0;
                _рамка.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _даль = Math.Clamp(_даль + 0.15f, 1.4f, 5f);
                _покой = 0;
                _рамка.AcceptEvent();
                break;
            case InputEventMouseMotion д when _тащат:
                _рыск += д.Relative.X * 0.5f;
                _тангаж = Math.Clamp(_тангаж + д.Relative.Y * 0.5f, -80f, 80f);
                _покой = 0;
                _рамка.AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible || _ось is null)
            return;
        _покой += delta;
        if (!_тащат && _покой > 1.5)
            _рыск += (float)delta * 20f;       // сама — медленно, пока не трогают
        _ось.RotationDegrees = new Vector3(_тангаж, _рыск, 0);
        if (_глаз is not null)
            _глаз.Position = new Vector3(0, 0, _даль);
    }
}
