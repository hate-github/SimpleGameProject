// Сосед в мире (ГДД 23; задание автора, пп. 23, 26): пальто, голова, имя,
// рука с тем, что в ней, — и ноги.
//
// Моделей людей у проекта нет, и рисовать их нечем. Но пустая площадка
// врёт сильнее грубой фигуры: она говорит «дома никого», когда дом
// посчитал обратное. Цвет пальто — от имени: соседи различаются
// с первого взгляда и не меняются между ходами.
//
// Фигура больше не появляется из воздуха. Хроника говорит, где человек
// в этот час; если это другое место, он туда **идёт** по навигационной
// сетке (`Навигация.Путь`) — от своей двери, от входа с улицы, от чужой
// двери к следующей. Уходит так же: к своей двери или к выходу,
// и только там пропадает. Пока идёт — сквозь него проходят (он на пути
// у всех, а площадка — два на три метра); встал — твёрдый, если героя
// на этом месте нет.
//
// Руки и движения — те же узлы, что у героя (`Руки3D`, `Аниматор`):
// идёт — Walk, спешит на стук — Run, стоит — по делу.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class Сосед3D : StaticBody3D
{
    public string id { get; private set; } = "";
    public Руки3D руки { get; private set; } = null!;
    public Аниматор аниматор { get; private set; } = null!;

    private CollisionShape3D _форма = null!;
    private Vector3[] _путь = System.Array.Empty<Vector3>();
    private int _точка;
    private bool _бегом;
    private System.Action? _дошёл;
    private Движение _на_месте = new(Анимация.Idle);

    /// <summary>Куда идёт или где стоит.</summary>
    public Vector3 цель { get; private set; }

    /// <summary>Идёт ли сейчас.</summary>
    public bool идёт => _точка < _путь.Length;

    private const float ШАГ = 1.25f;       // м/с — по лестнице хрущёвки не разбежишься
    private const float БЕГОМ = 2.6f;

    public static Сосед3D Собрать(string id, string имя, Color пальто, Color лицо)
    {
        var с = new Сосед3D { id = id, Name = $"сосед_{id}", CollisionLayer = 0, CollisionMask = 0 };
        // корпус — то, что качается на ходу и клонится от ран
        var корпус = new Node3D { Name = "корпус" };
        с.AddChild(корпус);
        корпус.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Height = 1.55f, Radius = 0.26f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = пальто, Roughness = 0.95f },
            Position = new Vector3(0, 0.78f, 0),
        });
        корпус.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Height = 0.24f, Radius = 0.12f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = лицо, Roughness = 0.9f },
            Position = new Vector3(0, 1.66f, 0),
        });
        // правая рука: тот же узел, что у героя у камеры
        с.руки = new Руки3D
        {
            Name = "руки",
            Position = new Vector3(0.32f, 0.92f, -0.12f),
            RotationDegrees = new Vector3(-130, 0, 0),
        };
        корпус.AddChild(с.руки);
        с.аниматор = new Аниматор { Name = "аниматор", Рука = с.руки, Тело = корпус };
        с.AddChild(с.аниматор);
        с._форма = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Height = 1.75f, Radius = 0.28f },
            Position = new Vector3(0, 0.88f, 0),
        };
        с.AddChild(с._форма);
        с.AddChild(new Label3D
        {
            Text = имя,
            FontSize = 96,
            // подпись ростом с ладонь: площадка тесная, и с полуметра
            // имя в четверть метра закрывало собой человека
            PixelSize = 0.0013f,
            Modulate = new Color("#e6dfcd"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, 1.90f, 0),
            DoubleSided = true,
            Shaded = false,
        });
        return с;
    }

    /// <summary>Что в руках и что делает, когда стоит на месте.</summary>
    public void Держать(Руки руки, Движение на_месте)
    {
        this.руки.Держать(руки);
        _на_месте = на_месте;
        if (!идёт)
            аниматор.Играть(на_месте);
    }

    /// <summary>
    /// Встать на месте: лицом туда-то, с именем двери (смотреть на человека
    /// у двери — то же, что смотреть на дверь), твёрдым — если герой
    /// не стоит здесь же.
    /// </summary>
    public void Встать(Vector3 где, float лицом, string зовут, bool твёрдый)
    {
        _путь = System.Array.Empty<Vector3>();
        _точка = 0;
        цель = где;
        Position = где;
        RotationDegrees = new Vector3(0, лицом, 0);
        Name = зовут;
        CollisionLayer = твёрдый ? Мир.СЛОЙ_ЛЮДЕЙ : 0;
        аниматор.Играть(_на_месте);
    }

    /// <summary>
    /// Пойти по пути; дошёл — <paramref name="дошёл"/>. Пока идёт, сквозь
    /// него проходят, и луч прицела его не видит: остановиться, чтобы
    /// поговорить, — это прийти туда, где он встанет.
    /// </summary>
    public void Идти(Vector3[] путь, bool бегом, System.Action? дошёл)
    {
        _путь = путь;
        _точка = 1;
        _бегом = бегом;
        _дошёл = дошёл;
        цель = путь[^1];
        CollisionLayer = 0;
        Name = $"сосед_{id}_идёт";
        аниматор.Играть(new Движение(бегом ? Анимация.Run : Анимация.Walk));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!идёт)
            return;
        float шаг = (_бегом ? БЕГОМ : ШАГ) * (float)delta;
        while (шаг > 0 && _точка < _путь.Length)
        {
            var к = _путь[_точка] - Position;
            float до = к.Length();
            if (до <= шаг)
            {
                Position = _путь[_точка];
                шаг -= до;
                _точка++;
                continue;
            }
            Position += к / до * шаг;
            // лицом по ходу — только по горизонтали
            var курс = new Vector3(к.X, 0, к.Z);
            if (курс.LengthSquared() > 1e-4f)
                Rotation = new Vector3(0, Mathf.Atan2(-курс.X, -курс.Z), 0);
            шаг = 0;
        }
        if (!идёт)
        {
            var дело = _дошёл;
            _дошёл = null;
            аниматор.Играть(_на_месте);
            дело?.Invoke();
        }
    }
}
