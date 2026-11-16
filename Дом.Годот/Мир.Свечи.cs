// Свечи у дверей, за которыми недавно умерли (импровизация 17.11.2026): у кого
// — считает `Дом.Экран.Беседы.Свечи` по плану подъезда; мир только ставит на пол
// сбоку от двери огарок с язычком и тёплым, чуть дрожащим светом. На доске
// у входа в те же дни — «ПОМЯНЕМ», на двери — крест мелом, если тело внутри.

using Godot;

namespace Дом.Годот;

public partial class Мир
{
    private readonly Dictionary<int, Node3D> _свечи = new();

    /// <summary>Поставить свечи к этим дверям и убрать от прочих.</summary>
    public void Свечи(IReadOnlyCollection<int> квартиры)
    {
        foreach (int кв in _свечи.Keys.Where(к => !квартиры.Contains(к)).ToList())
        {
            if (GodotObject.IsInstanceValid(_свечи[кв]))
                _свечи[кв].QueueFree();
            _свечи.Remove(кв);
        }
        foreach (int кв in квартиры)
        {
            if (_свечи.TryGetValue(кв, out var есть) && GodotObject.IsInstanceValid(есть))
                continue;
            if (Дверь_где(кв) is not var (перед, дверь))
                continue;
            var к = дверь - перед;
            к.Y = 0;
            var вбок = new Vector3(-к.Z, 0, к.X).Normalized() * 0.42f;
            var свеча = new Node3D { Name = $"свеча{кв}" };
            свеча.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.032f, Height = 0.12f },
                Position = new Vector3(0, 0.06f, 0),
                MaterialOverride = Материал(new Color("#e8e0c8"), 0.6f),
            });
            свеча.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.015f, Height = 0.04f },
                Position = new Vector3(0, 0.14f, 0),
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color("#ffd27a"),
                },
            });
            var огонь = new OmniLight3D
            {
                Position = new Vector3(0, 0.18f, 0),
                LightColor = new Color("#ffb060"),
                LightEnergy = 0.6f,
                OmniRange = 1.4f,
                ShadowEnabled = false,
            };
            свеча.AddChild(огонь);
            AddChild(свеча);
            свеча.GlobalPosition = перед + к * 0.75f + вбок;
            // огонёк дрожит: свет то чуть ярче, то тусклее
            var дрожь = огонь.CreateTween().SetLoops();
            дрожь.TweenProperty(огонь, "light_energy", 0.42f, 0.17);
            дрожь.TweenProperty(огонь, "light_energy", 0.7f, 0.23);
            _свечи[кв] = свеча;
        }
    }
}
