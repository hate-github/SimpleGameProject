// Иконки ячеек инвентаря — снимки моделей автора (docs/ЕДА_ПЛАН.md,
// этап 2). Рисовать иконки отдельно не нужно: у каждого варианта есть
// модель, и иконка снимается с неё — плесень на батоне и вскрытая банка
// видны в ячейке сразу.
//
// Снимок — маленькое окно своего мира (`SubViewport`, свой World3D,
// прозрачный фон), которое рисуется один раз и держит картинку. Окно
// на вариант, а не на вещь: вариантов у еды десятки, вещей — сотни.
// Окна живут, пока жив экран: текстура окна — это и есть картинка.

using Godot;

namespace Дом.Годот;

public partial class ИконкиЕды : Node
{
    private const int РАЗМЕР = 96;
    private readonly Dictionary<string, SubViewport> _снимки = new(StringComparer.Ordinal);

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    /// <summary>Иконка варианта — или null, если модели нет: тогда ячейка
    /// обходится словом.</summary>
    public Texture2D? Дай(string модель, IReadOnlyList<string> меши)
    {
        if (МоделиАвтора.Первый(модель, меши) is not { } н)
            return null;
        string ключ = модель + "|" + н.имя;
        if (!_снимки.TryGetValue(ключ, out var окно))
        {
            окно = Снять(н);
            _снимки[ключ] = окно;
        }
        return окно.GetTexture();
    }

    private SubViewport Снять(МоделиАвтора.Найдено н)
    {
        var окно = new SubViewport
        {
            Size = new Vector2I(РАЗМЕР, РАЗМЕР),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        Сцена(окно, н, рыск: 35f, тангаж: 22f, даль: 2.4f, фон: null);
        AddChild(окно);
        return окно;
    }

    /// <summary>
    /// Поставить модель в окно: ось с моделью, середина которой в нуле,
    /// свет и глаз. Общая для иконки и карточки — чтобы вещь в ячейке
    /// и в карточке выглядела одинаково. Возвращает ось: её и крутят.
    /// </summary>
    public static Node3D Сцена(SubViewport окно, МоделиАвтора.Найдено н, float рыск, float тангаж,
                               float даль, Color? фон)
    {
        var ось = new Node3D { RotationDegrees = new Vector3(тангаж, рыск, 0) };
        окно.AddChild(ось);
        var модель = МоделиАвтора.Узел(н, 1.0f);
        // Узел ставит низ на ноль — поднять середину в центр оси
        var р = н.рамка;
        float больше = Math.Max(р.Size.X, Math.Max(р.Size.Y, р.Size.Z));
        float высота = больше > 1e-6f ? р.Size.Z / больше : 0f;
        модель.Position = new Vector3(0, -высота / 2, 0);
        ось.AddChild(модель);

        var среда = new Godot.Environment
        {
            BackgroundMode = фон is null ? Godot.Environment.BGMode.ClearColor : Godot.Environment.BGMode.Color,
            BackgroundColor = фон ?? new Color(0, 0, 0, 0),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#9a948a"),
            AmbientLightEnergy = 0.9f,
        };
        окно.AddChild(new Camera3D
        {
            Position = new Vector3(0, 0, даль),
            Fov = 35,
            Near = 0.01f,
            Environment = среда,
            Current = true,
        });
        окно.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-40, 30, 0),
            LightEnergy = 1.3f,
            LightColor = new Color("#f2e6d0"),
        });
        окно.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-10, -150, 0),
            LightEnergy = 0.35f,
            LightColor = new Color("#b8c8e0"),
        });
        return ось;
    }
}
