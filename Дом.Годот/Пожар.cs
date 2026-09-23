// Пожар над местом мира: зарево и дым (`Мир.Мир_на`).
//
// Моделей огня у проекта нет, и они не нужны, чтобы пожар читался:
// рыжий свет, который дрожит, и столб дыма над крышей видно издали
// и сквозь метель. Горит — сильно; сгорело — тонкая струйка, без света.
// Заменить на настоящий огонь — поменять этот узел, а не того, кто
// решает, что горит.

using Godot;

namespace Дом.Годот;

public partial class Пожар : Node3D
{
    public enum Сила { НЕТ, ГОРИТ, ТЛЕЕТ }

    private OmniLight3D _свет = null!;
    private GpuParticles3D _дым = null!;
    private Сила _сила = Сила.НЕТ;
    private double _часы;
    private float _энергия;

    /// <summary>Поставить над рамкой места: свет — внутри, у верха,
    /// дым — над крышей, шириной с половину здания.</summary>
    public void Поставить(Aabb рамка)
    {
        var верх = рамка.GetCenter() with { Y = рамка.End.Y };
        float ширина = Mathf.Max(2f, Mathf.Min(рамка.Size.X, рамка.Size.Z) * 0.5f);
        _свет = new OmniLight3D
        {
            LightColor = new Color("#ff8a3a"),
            OmniRange = Mathf.Max(12f, ширина * 3f),
            Position = рамка.GetCenter() with { Y = рамка.Position.Y + рамка.Size.Y * 0.6f },
            ShadowEnabled = false,
        };
        AddChild(_свет);

        var процесс = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 12f,
            InitialVelocityMin = 1.2f,
            InitialVelocityMax = 2.4f,
            Gravity = new Vector3(0.4f, 0.35f, 0),          // ветер сносит вбок
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(ширина / 2, 0.5f, ширина / 2),
            ScaleMin = 2.5f,
            ScaleMax = 5.0f,
            Color = new Color(0.12f, 0.11f, 0.10f, 0.55f),
            // клуб тает к концу жизни, а не пропадает разом
            ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient
                {
                    Offsets = new[] { 0f, 0.15f, 1f },
                    Colors = new[] { new Color(1, 1, 1, 0), Colors.White, new Color(1, 1, 1, 0) },
                },
            },
        };
        _дым = new GpuParticles3D
        {
            Amount = 48,
            Lifetime = 9.0,
            ProcessMaterial = процесс,
            DrawPass1 = new QuadMesh
            {
                Size = new Vector2(1.6f, 1.6f),
                Material = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                    VertexColorUseAsAlbedo = true,
                    AlbedoColor = Colors.White,
                    // клуб круглый и мягкий: без текстуры квад рисуется
                    // серым квадратом, и дым читается как рябь, а не дым
                    AlbedoTexture = new GradientTexture2D
                    {
                        Fill = GradientTexture2D.FillEnum.Radial,
                        FillFrom = new Vector2(0.5f, 0.5f),
                        FillTo = new Vector2(1.0f, 0.5f),
                        Width = 64,
                        Height = 64,
                        Gradient = new Gradient
                        {
                            Offsets = new[] { 0f, 1f },
                            Colors = new[] { Colors.White, new Color(1, 1, 1, 0) },
                        },
                    },
                },
            },
            Position = верх,
            VisibilityAabb = new Aabb(new Vector3(-30, -2, -30), new Vector3(60, 60, 60)),
        };
        AddChild(_дым);
        Как(_сила);
    }

    public void Как(Сила сила)
    {
        _сила = сила;
        if (_свет is null)
            return;
        _энергия = сила == Сила.ГОРИТ ? 4.5f : 0f;
        _свет.Visible = сила == Сила.ГОРИТ;
        _дым.Emitting = сила != Сила.НЕТ;
        _дым.Amount = сила == Сила.ГОРИТ ? 48 : 12;
    }

    public override void _Process(double delta)
    {
        if (_сила != Сила.ГОРИТ || _свет is null)
            return;
        _часы += delta;
        // огонь дрожит: две частоты, не в лад, — не мигалка
        float дрожь = 0.75f + 0.18f * Mathf.Sin((float)_часы * 7.3f)
                      + 0.07f * Mathf.Sin((float)_часы * 17.1f + 1.3f);
        _свет.LightEnergy = _энергия * дрожь;
    }
}
