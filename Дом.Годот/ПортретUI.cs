// Портрет собеседника в окне разговора (просьба автора 01.10.2026: «при
// диалоге — окно, как на скриншоте, но в стиле игры»): лицо из сетки автора
// в тёмной рамке с тонкой каймой — как карточка на пропуске, а не неоновый
// шестиугольник. Пиксели лица крупные и растягиваются без сглаживания;
// карточка выше, чем шире, — лишнее по бокам ячейки срезается.
//
// Кто на каком лице — `data/лица.json` (`Дом.Экран.Лица`); нет лица — нет
// и портрета, а окно остаётся прежним.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class ПортретUI : PanelContainer
{
    /// <summary>Размер лица в рамке, в точках экрана 1280 × 800.</summary>
    public static readonly Vector2 РАЗМЕР = new(164, 196);

    private TextureRect _лицо = null!;

    private static IReadOnlyDictionary<string, int>? _кто;
    private static readonly Dictionary<int, Texture2D?> _лица = new();

    /// <summary>Лицо человека по таблице лиц — или null.</summary>
    public static Texture2D? Лицо(string? id)
    {
        if (id is null)
            return null;
        _кто ??= Лица.Прочитать(Пути.Данные);
        if (!_кто.TryGetValue(id, out int номер))
            return null;
        if (!_лица.TryGetValue(номер, out var т))
        {
            string путь = $"res://лица/{Лица.Файл(номер)}";
            т = ResourceLoader.Exists(путь) ? GD.Load<Texture2D>(путь) : null;
            _лица[номер] = т;
        }
        return т;
    }

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("#0b0a09"),
            BorderColor = new Color("#8a8474"),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 5, ContentMarginBottom = 5,
        });
        _лицо = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = TextureFilterEnum.Nearest,
            CustomMinimumSize = РАЗМЕР,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_лицо);
    }

    /// <summary>Показать лицо человека; лица нет — портрет прячется. Есть ли он.</summary>
    public bool Показать(string? id)
    {
        var т = Лицо(id);
        _лицо.Texture = т;
        Visible = т is not null;
        return Visible;
    }
}
