// Люди — модель автора со скелетом Mixamo (`модели/люди/`) и её клипы,
// переложенные в имена игры (`Дом.Экран.Анимации.ЛЮДИ`).
//
// Модель одна на всех: сетка и скелет — из `Idle.fbx` (в каждом файле
// Mixamo они свои, одинаковые), клипы — из всех файлов в одну библиотеку,
// общую для всех соседей. При загрузке:
//   · из шага (ходьба, бег, лестница) вынимается ход вперёд — средняя
//     скорость бёдер за круг: фигуру ведёт навигация, а клип, уходящий
//     вперёд, отдёргивал бы её назад на каждом круге; качание и шаг
//     вбок остаются, на лестнице уходит и подъём;
//   · у шага запоминается, сколько метров в секунду он проходит сам —
//     аниматор подгоняет под это скорость ног (`Аниматор.Скорость`);
//   · все клипы — петлёй: состояние длится, пока длится дело;
//   · рост — 1.75 м по скелету в покое (рамка сетки у скина ничего не
//     говорит: вершины лежат в своих единицах, модель на сто раз меньше);
//   · лицом в −Z, как ходит фигура соседа; Mixamo ставит лицом в +Z;
//   · цвет — одежда и кожа по маске в цвете вершин: голова и кисти —
//     кожа, ниже щиколотки — ботинки. Цвета у каждого соседа свои
//     (`instance uniform`), сетка и материал — одни на всех.
// Нет файлов — null, и сосед остаётся капсулой: без модели игра идёт.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public static class Люди
{
    private const string ПАПКА = "res://модели/люди";
    private const string ОСНОВА = "Idle";
    private const string БЁДРА = "mixamorig_Hips";
    public const string КИСТЬ = "mixamorig_RightHand";
    public const float РОСТ = 1.75f;

    /// <summary>Модель соседа: корень (его вешают в фигуру), клипы, скелет
    /// и гнездо в правой кисти — туда ложится то, что в руках.</summary>
    public sealed record Модель(Node3D корень, AnimationPlayer проигрыватель, Skeleton3D скелет,
                                BoneAttachment3D кисть);

    private static bool _пробовали;
    private static PackedScene? _основа;
    private static AnimationLibrary? _клипы;
    private static ArrayMesh? _сетка;
    private static ShaderMaterial? _материал;
    private static float _масштаб = 1f;
    private static readonly Dictionary<string, float> _шаг = new(System.StringComparer.Ordinal);

    /// <summary>Сколько метров в секунду проходят ноги в клипе шага.</summary>
    public static IReadOnlyDictionary<string, float> Шаг => _шаг;

    /// <summary>Во сколько раз модель уменьшена до роста в 1.75 м.</summary>
    public static float Масштаб => _масштаб;

    private const string ШЕЙДЕР = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
instance uniform vec4 coat : source_color = vec4(0.35, 0.30, 0.25, 1.0);
instance uniform vec4 skin : source_color = vec4(0.80, 0.65, 0.55, 1.0);
uniform vec4 boots : source_color = vec4(0.12, 0.10, 0.09, 1.0);
void fragment() {
    vec3 c = mix(coat.rgb, skin.rgb, COLOR.r);
    ALBEDO = mix(c, boots.rgb, COLOR.g);
    ROUGHNESS = 0.95;
}";

    /// <summary>Собрать модель человека: одежда и кожа — свои. Нет файлов — null.</summary>
    public static Модель? Собрать(Color одежда, Color кожа)
    {
        if (!Загрузить())
            return null;
        var корень = _основа!.Instantiate<Node3D>();
        var скелет = корень.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().First();
        var меш = скелет.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().First();
        var игрок = корень.FindChildren("*", "AnimationPlayer", true, false).Cast<AnimationPlayer>().First();
        foreach (var имя in игрок.GetAnimationLibraryList())
            игрок.RemoveAnimationLibrary(имя);
        игрок.AddAnimationLibrary("", _клипы);
        // физических костей у соседа нет — узел импорта не нужен
        foreach (var лишнее in скелет.FindChildren("*", "PhysicalBoneSimulator3D", true, false))
            лишнее.Free();
        меш.Mesh = _сетка;
        меш.MaterialOverride = _материал;
        меш.SetInstanceShaderParameter("coat", одежда);
        меш.SetInstanceShaderParameter("skin", кожа);
        корень.Scale = Vector3.One * _масштаб;
        корень.RotationDegrees = new Vector3(0, 180, 0);      // Mixamo — лицом в +Z, сосед ходит в −Z
        var кисть = new BoneAttachment3D { Name = "кисть", BoneName = КИСТЬ };
        скелет.AddChild(кисть);
        return new Модель(корень, игрок, скелет, кисть);
    }

    /// <summary>
    /// Где гнездо кисти в покое — в осях того, кто держит модель (корень
    /// модели стоит в нём с поворотом и масштабом <see cref="Собрать"/>).
    /// По нему руки кладутся в ладонь: их место в кисти считается один раз.
    /// </summary>
    public static Transform3D Кисть_в_покое(Модель м)
    {
        int кость = м.скелет.FindBone(КИСТЬ);
        return м.корень.Transform * м.скелет.Transform * м.скелет.GetBoneGlobalRest(кость);
    }

    private static bool Загрузить()
    {
        if (_пробовали)
            return _основа is not null;
        _пробовали = true;
        string основа = $"{ПАПКА}/{ОСНОВА}.fbx";
        if (!ResourceLoader.Exists(основа))
        {
            GD.PrintErr($"нет модели человека {основа} — соседи капсулами");
            return false;
        }
        _основа = GD.Load<PackedScene>(основа);

        // рост — по скелету в покое: от носков до макушки
        var образец = _основа.Instantiate<Node3D>();
        var скелет = образец.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().First();
        float низ = float.MaxValue, верх = float.MinValue;
        for (int b = 0; b < скелет.GetBoneCount(); b++)
        {
            float y = скелет.GetBoneGlobalRest(b).Origin.Y;
            низ = Mathf.Min(низ, y);
            верх = Mathf.Max(верх, y);
        }
        _масштаб = РОСТ / Mathf.Max(0.01f, верх - низ);
        var меш = скелет.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().First();
        _сетка = С_маской((ArrayMesh)меш.Mesh);
        образец.Free();
        _материал = new ShaderMaterial { Shader = new Shader { Code = ШЕЙДЕР } };

        _клипы = new AnimationLibrary();
        // один файл — один клип, сколько бы имён игры он ни получил
        var взятые = new Dictionary<string, (Animation клип, float шаг)>(System.StringComparer.Ordinal);
        foreach (var к in Анимации.ЛЮДИ)
        {
            if (!взятые.TryGetValue(к.файл, out var взят))
            {
                string путь = $"{ПАПКА}/{к.файл}.fbx";
                if (!ResourceLoader.Exists(путь))
                {
                    GD.PrintErr($"нет клипа {путь} — «{к.клип}» будет заглушкой");
                    continue;
                }
                var узел = GD.Load<PackedScene>(путь).Instantiate<Node3D>();
                var игрок = узел.FindChildren("*", "AnimationPlayer", true, false).Cast<AnimationPlayer>().First();
                var клип = (Animation)игрок.GetAnimation(игрок.GetAnimationList()[0]).Duplicate(true);
                узел.Free();
                клип.LoopMode = Animation.LoopModeEnum.Linear;
                взят = (клип, к.шаг ? Без_хода(клип) * _масштаб : 0f);
                взятые[к.файл] = взят;
            }
            if (к.шаг)
                _шаг[к.клип] = взят.шаг;
            _клипы.AddAnimation(к.клип, взят.клип);
        }
        return true;
    }

    /// <summary>
    /// Вынуть из шага ход вперёд: бёдра к концу круга там же, где в начале, —
    /// из каждого ключа вычитается средняя скорость за круг. Возвращает,
    /// сколько единиц модели в секунду клип проходил (по пути, с подъёмом).
    /// </summary>
    private static float Без_хода(Animation клип)
    {
        for (int т = 0; т < клип.GetTrackCount(); т++)
        {
            if (клип.TrackGetType(т) != Animation.TrackType.Position3D
                || !клип.TrackGetPath(т).ToString().EndsWith(":" + БЁДРА, System.StringComparison.Ordinal))
                continue;
            int n = клип.TrackGetKeyCount(т);
            if (n < 2)
                return 0f;
            var начало = (Vector3)клип.TrackGetKeyValue(т, 0);
            var конец = (Vector3)клип.TrackGetKeyValue(т, n - 1);
            double t0 = клип.TrackGetKeyTime(т, 0), t1 = клип.TrackGetKeyTime(т, n - 1);
            if (t1 - t0 < 1e-6)
                return 0f;
            var скорость = (конец - начало) / (float)(t1 - t0);
            for (int k = 0; k < n; k++)
            {
                double t = клип.TrackGetKeyTime(т, k);
                var v = (Vector3)клип.TrackGetKeyValue(т, k);
                клип.TrackSetKeyValue(т, k, v - скорость * (float)(t - t0));
            }
            return скорость.Length();
        }
        return 0f;
    }

    /// <summary>
    /// Та же сетка с маской в цвете вершин: красный — кожа (голова с шеей
    /// и кисти), зелёный — ботинки. Вершины скина лежат в своих осях, поэтому
    /// верх — самая длинная ось рамки, бок — вторая.
    /// </summary>
    private static ArrayMesh С_маской(ArrayMesh исходная)
    {
        var сетка = new ArrayMesh();
        for (int s = 0; s < исходная.GetSurfaceCount(); s++)
        {
            var массивы = исходная.SurfaceGetArrays(s);
            var точки = массивы[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var рамка = исходная.GetAabb();
            int верх = (int)рамка.Size.MaxAxisIndex();
            int бок = Enumerable.Range(0, 3).Where(i => i != верх).OrderByDescending(i => рамка.Size[i]).First();
            float низ = рамка.Position[верх], рост = Mathf.Max(1e-6f, рамка.Size[верх]);
            float середина = рамка.Position[бок] + рамка.Size[бок] / 2;
            var цвета = new Color[точки.Length];
            for (int i = 0; i < точки.Length; i++)
            {
                float h = (точки[i][верх] - низ) / рост;
                float в_бок = Mathf.Abs(точки[i][бок] - середина) / рост;
                bool кожа = h > 0.862f || h < 0.47f && в_бок > 0.115f;
                bool ботинки = h < 0.065f;
                цвета[i] = new Color(кожа ? 1 : 0, ботинки ? 1 : 0, 0, 1);
            }
            массивы[(int)Mesh.ArrayType.Color] = цвета;
            var формат = исходная.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights;
            сетка.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, массивы, flags: формат);
        }
        return сетка;
    }
}
