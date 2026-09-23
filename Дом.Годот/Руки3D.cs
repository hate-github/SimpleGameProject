// Что в руках — видно (задание автора, пп. 22–23). Один узел на героя
// и на соседа: у героя он висит у камеры справа внизу, у фигуры на лестнице —
// у правой руки. Что держать, говорит запись `Дом.Экран.Руки`; кто её
// посчитал — герой сам или хроника соседа, — узлу всё равно.
//
// Предмет — модель автора, если она есть: `модели/в_руках/<ассет>.tscn`
// (или .glb, .gltf, .fbx), где ассет — имя из `data/инструменты.json`
// (Hammer, Crowbar, Axe…). Нет модели — заглушка из коробок и цилиндров:
// ручка и голова молотка, лезвие топора, ствол винтовки. Уговор для модели
// тот же, что у заглушки: хват — в начале координат, предмет растёт вверх
// по +Y (ствол — вперёд по −Z). Положил модель — заглушка ушла сама,
// кода менять не надо.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class Руки3D : Node3D
{
    public const string ПАПКА = "res://модели/в_руках/";

    private Руки _руки = Руки.ПУСТЫ;
    private Node3D? _вещь;

    /// <summary>Что сейчас в руках.</summary>
    public Руки руки => _руки;

    /// <summary>Модель предмета — если автор её положил, иначе null.
    /// Нужна аниматору: в модели могут быть свои клипы.</summary>
    public Node3D? вещь => _вещь;

    /// <summary>Взять в руки (или опустеть). То же самое — ничего не делает.</summary>
    public void Держать(Руки р)
    {
        if (р == _руки)
            return;
        _руки = р;
        _вещь?.QueueFree();
        _вещь = null;
        if (р.пусты || р.ассет is not string ассет)
            return;
        _вещь = Модель(ассет) ?? Заглушка(ассет);
        _вещь.Name = ассет;
        AddChild(_вещь);
    }

    /// <summary>Модель автора по имени ассета — первая из найденных.</summary>
    public static Node3D? Модель(string ассет)
    {
        foreach (string как in new[] { ".tscn", ".glb", ".gltf", ".fbx" })
        {
            string путь = ПАПКА + ассет + как;
            if (!ResourceLoader.Exists(путь))
                continue;
            if (GD.Load(путь) is PackedScene сцена && сцена.Instantiate() is Node3D узел)
                return узел;
        }
        return null;
    }

    // ------------------------------------------------------------ заглушки

    private static readonly Color ДЕРЕВО = new("#5a4128");
    private static readonly Color МЕТАЛЛ = new("#72777b");
    private static readonly Color ТЁМНОЕ = new("#2a2a2a");
    private static readonly Color КРАСНОЕ = new("#7a2a22");

    /// <summary>Заглушка предмета: грубо, но узнаваемо — по силуэту видно,
    /// молоток это или монтировка.</summary>
    public static Node3D Заглушка(string ассет)
    {
        var у = new Node3D();
        switch (ассет)
        {
            case "Hammer":
                Цилиндр(у, ДЕРЕВО, 0.016f, 0.32f, new Vector3(0, 0.16f, 0));
                Коробка(у, МЕТАЛЛ, new Vector3(0.12f, 0.045f, 0.045f), new Vector3(0, 0.32f, 0));
                break;
            case "BoltCutter":
                Цилиндр(у, КРАСНОЕ, 0.012f, 0.42f, new Vector3(-0.02f, 0.21f, 0));
                Цилиндр(у, КРАСНОЕ, 0.012f, 0.42f, new Vector3(0.02f, 0.21f, 0));
                Коробка(у, МЕТАЛЛ, new Vector3(0.05f, 0.11f, 0.03f), new Vector3(0, 0.47f, 0));
                break;
            case "Crowbar":
                Цилиндр(у, ТЁМНОЕ, 0.012f, 0.72f, new Vector3(0, 0.36f, 0));
                Коробка(у, ТЁМНОЕ, new Vector3(0.07f, 0.02f, 0.02f), new Vector3(0.03f, 0.71f, 0));
                break;
            case "Axe":
                Цилиндр(у, ДЕРЕВО, 0.018f, 0.6f, new Vector3(0, 0.3f, 0));
                Коробка(у, МЕТАЛЛ, new Vector3(0.16f, 0.1f, 0.02f), new Vector3(0.06f, 0.55f, 0));
                break;
            case "Lockpick":
                Коробка(у, МЕТАЛЛ, new Vector3(0.006f, 0.1f, 0.003f), new Vector3(0, 0.05f, 0));
                break;
            case "Knife":
                Цилиндр(у, ТЁМНОЕ, 0.012f, 0.1f, new Vector3(0, 0.05f, 0));
                Коробка(у, МЕТАЛЛ, new Vector3(0.022f, 0.14f, 0.003f), new Vector3(0, 0.17f, 0));
                break;
            case "Club":
                у.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.018f, Height = 0.55f },
                    MaterialOverride = Краска(ДЕРЕВО),
                    Position = new Vector3(0, 0.275f, 0),
                });
                break;
            case "Pistol":
                Коробка(у, ТЁМНОЕ, new Vector3(0.03f, 0.1f, 0.05f), new Vector3(0, 0.05f, 0));
                Коробка(у, ТЁМНОЕ, new Vector3(0.03f, 0.04f, 0.17f), new Vector3(0, 0.11f, -0.05f));
                break;
            case "Shotgun":
            case "Rifle":
                float длина = ассет == "Rifle" ? 0.95f : 0.75f;
                Коробка(у, ТЁМНОЕ, new Vector3(0.04f, 0.06f, длина), new Vector3(0, 0.06f, -длина / 2 + 0.1f));
                Коробка(у, ДЕРЕВО, new Vector3(0.04f, 0.1f, 0.28f), new Vector3(0, 0.02f, 0.22f));
                break;
            default:
                Коробка(у, МЕТАЛЛ, new Vector3(0.05f, 0.3f, 0.05f), new Vector3(0, 0.15f, 0));
                break;
        }
        return у;
    }

    private static StandardMaterial3D Краска(Color c) => new() { AlbedoColor = c, Roughness = 0.8f };

    private static void Цилиндр(Node3D куда, Color c, float r, float h, Vector3 где)
        => куда.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h },
            MaterialOverride = Краска(c),
            Position = где,
        });

    private static void Коробка(Node3D куда, Color c, Vector3 размер, Vector3 где)
        => куда.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = размер },
            MaterialOverride = Краска(c),
            Position = где,
        });
}
