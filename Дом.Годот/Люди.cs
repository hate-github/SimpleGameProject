// Люди — тело автора со скелетом Mixamo (`модели/люди`), вещи на нём и клипы,
// переложенные в имена игры (`Дом.Экран.Анимации.ЛЮДИ`).
//
// Тело одно на всех: сетка тела, вещи и скелет — из файла тела
// (`Дом.Экран.Тела`), клипы — из всех файлов клипов в одну библиотеку,
// общую для всех соседей. При загрузке:
//   · клипы сняты со старого скелета (модель ростом 11 м, до правки плеч):
//     повороты костей в них ложатся на любое тело Mixamo, а ход бёдер
//     переводится в рост тела — от покоя бёдер клипа к покою бёдер тела;
//     прочие сдвиги и масштабы костей из клипов вынуты, иначе клип тянул бы
//     кости тела к длинам старого скелета;
//   · из шага (ходьба, бег, лестница) вынимается ход вперёд — средняя
//     скорость бёдер за круг: фигуру ведёт навигация, а клип, уходящий
//     вперёд, отдёргивал бы её назад на каждом круге; качание и шаг
//     вбок остаются, на лестнице уходит и подъём;
//   · у шага запоминается, сколько метров в секунду он проходит сам —
//     аниматор подгоняет под это скорость ног (`Аниматор.Скорость`);
//   · все клипы — петлёй: состояние длится, пока длится дело;
//   · рост — 1.75 м по скелету в покое;
//   · лицом в −Z, как ходит фигура соседа; Mixamo ставит лицом в +Z;
//   · покой скелета бывает развёрнут: в файле с курткой (30.09) он стоял лицом
//     в +Z, в файле с одеждой (02.10, сохранён из Blender) — в +X, как привязка.
//     Клипы задают повороты костей целиком, и на ходу тело смотрит одинаково,
//     а покой — нет; где покой нужен (предмет в кисти), он сперва
//     поворачивается вокруг вертикали лицом в +Z (`Лицом_вперёд`);
//   · краска — по зонам и материалам (`Дом.Экран.Тела`): зона тела — цветом
//     соседа для неё, серое у вещи — цветом соседа для этой вещи (пальто,
//     брюки, обувь, шапка, перчатки), цветное — как у автора. Цвета соседа —
//     `instance uniform`, сетки и материалы — одни на всех; шейдер собирается
//     из списка красок, и новая краска — строка в `Тела.Униформа`.
//     Зоны тела под одеждой (`ВещьТела.прячет`) вынуты из сетки тела.
// Нет файлов — null, и сосед остаётся капсулой: без модели игра идёт.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public static class Люди
{
    private const string ПАПКА = "res://модели/люди";
    private const string БЁДРА = "mixamorig_Hips";
    public const string КИСТЬ = "mixamorig_RightHand";
    public const float РОСТ = 1.75f;

    /// <summary>Тело, которое носят соседи. Пока одно — мужское, с курткой.</summary>
    public static readonly Тело ТЕЛО = Тела.МУЖЧИНА;

    /// <summary>Модель соседа: корень (его вешают в фигуру), клипы, скелет
    /// и гнездо в правой кисти — туда ложится то, что в руках.</summary>
    public sealed record Модель(Node3D корень, AnimationPlayer проигрыватель, Skeleton3D скелет,
                                BoneAttachment3D кисть);

    /// <summary>Цвета соседа по краске: верхняя вещь, кожа, кофта, брюки, обувь, шапка, перчатки.</summary>
    public sealed record Цвета(IReadOnlyDictionary<Краска, Color> краски);

    private static bool _пробовали;
    private static PackedScene? _основа;
    private static AnimationLibrary? _клипы;
    private static ArrayMesh? _тело;
    private static readonly Dictionary<string, Mesh> _вещи = new(System.StringComparer.Ordinal);
    private static float _масштаб = 1f;
    private static Vector3 _бёдра;
    private static float _рост_скелета = 1f;
    private static readonly Dictionary<string, float> _шаг = new(System.StringComparer.Ordinal);

    /// <summary>Сколько метров в секунду проходят ноги в клипе шага.</summary>
    public static IReadOnlyDictionary<string, float> Шаг => _шаг;

    /// <summary>Во сколько раз модель изменена до роста в 1.75 м.</summary>
    public static float Масштаб => _масштаб;

    /// <summary>
    /// Шейдер людей: role — номер `Дом.Экран.Краска` (0 — свой цвет из файла),
    /// на каждую краску с цветом соседа — свой `instance uniform` из `Тела.Униформа`.
    /// </summary>
    private static string Шейдер()
    {
        var краски = System.Enum.GetValues<Краска>().Where(к => Тела.Униформа(к) is not null).ToList();
        var код = new System.Text.StringBuilder();
        код.Append("shader_type spatial;\nrender_mode diffuse_lambert, specular_disabled;\n");
        foreach (var к in краски)
            код.Append($"instance uniform vec4 {Тела.Униформа(к)} : source_color = vec4(0.5, 0.5, 0.5, 1.0);\n");
        код.Append("uniform int role = 0;\n");
        код.Append("uniform vec4 own : source_color = vec4(0.8, 0.8, 0.8, 1.0);\n");
        код.Append("uniform float shade = 1.0;\n");
        код.Append("void fragment() {\n    vec3 c = own.rgb;\n");
        foreach (var к in краски)
            код.Append($"    if (role == {(int)к}) c = {Тела.Униформа(к)}.rgb;\n");
        код.Append("    ALBEDO = min(c * shade, vec3(1.0));\n    ROUGHNESS = 0.95;\n}\n");
        return код.ToString();
    }

    private static Shader? _шейдер;
    private static readonly Dictionary<string, ShaderMaterial> _материалы = new(System.StringComparer.Ordinal);

    // соседи различаются не только пальто: кофта, брюки, обувь и кожа — свои,
    // от имени, как пальто (`Мир.Пальто`), — тот же человек в каждой жизни тот же
    private static readonly Color[] КОФТЫ =
    {
        new("#5b5f66"), new("#6b5a4c"), new("#4f5b4f"), new("#6a4f55"), new("#585858"), new("#4c5566"),
    };
    private static readonly Color[] БРЮКИ =
    {
        new("#2c2f36"), new("#3a3a3a"), new("#2f3a33"), new("#3b342e"), new("#22262e"),
    };
    private static readonly Color[] ОБУВЬ = { new("#1c1917"), new("#2a2320"), new("#1a1c20") };
    private static readonly Color[] КОЖА = { new("#8a6f56"), new("#93775d"), new("#7f654f"), new("#9a806a") };
    // шапка у автора — тёмно-серая (0.32 при эталоне 0.8), и цвет соседа темнеет
    // до 0.4: поэтому шапки ярче прочего — вязаные, в цвет, а не в тон пальто
    private static readonly Color[] ШАПКИ =
    {
        new("#8c3b3b"), new("#3d5a8c"), new("#4f7a4f"), new("#8c7a52"), new("#9a9a9a"), new("#6b4f8c"), new("#3a3a3a"),
    };
    private static readonly Color[] ПЕРЧАТКИ = { new("#2b2b2b"), new("#3d3027"), new("#45474f"), new("#5c4033"), new("#2f3640") };

    /// <summary>Цвета соседа по имени: пальто и лицо даёт мир, прочее — выбор от имени.</summary>
    public static Цвета Цвета_соседа(string id, Color пальто, Color лицо)
    {
        static int Выбор(string id, string соль, int n)
        {
            int h = 0;
            foreach (char c in соль + id)
                h = unchecked(h * 31 + c);
            return (h & 0x7fffffff) % n;
        }
        var кожа = КОЖА[Выбор(id, "кожа", КОЖА.Length)];
        return new Цвета(new Dictionary<Краска, Color>
        {
            [Краска.ВЕРХ] = пальто,
            [Краска.КОЖА] = лицо.Lerp(кожа, 0.5f),        // лицо от мира — основа, оттенок кожи — от имени
            [Краска.КОФТА] = КОФТЫ[Выбор(id, "кофта", КОФТЫ.Length)],
            [Краска.БРЮКИ] = БРЮКИ[Выбор(id, "брюки", БРЮКИ.Length)],
            [Краска.ОБУВЬ] = ОБУВЬ[Выбор(id, "обувь", ОБУВЬ.Length)],
            [Краска.ШАПКА] = ШАПКИ[Выбор(id, "шапка", ШАПКИ.Length)],
            [Краска.ПЕРЧАТКИ] = ПЕРЧАТКИ[Выбор(id, "перчатки", ПЕРЧАТКИ.Length)],
        });
    }

    /// <summary>Собрать модель человека в цветах соседа. Нет файлов — null.</summary>
    public static Модель? Собрать(Цвета цвета)
    {
        if (!Загрузить())
            return null;
        var корень = _основа!.Instantiate<Node3D>();
        var скелет = корень.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().First();
        var игрок = корень.FindChildren("*", "AnimationPlayer", true, false).Cast<AnimationPlayer>().FirstOrDefault();
        if (игрок is null)
        {
            // тело без своей анимации (сохранено из Blender, 02.10) импорт оставляет
            // без проигрывателя — заводим его у корня, где его кладёт импорт Mixamo
            игрок = new AnimationPlayer { Name = "AnimationPlayer" };
            корень.AddChild(игрок);
        }
        foreach (var имя in игрок.GetAnimationLibraryList())
            игрок.RemoveAnimationLibrary(имя);
        игрок.AddAnimationLibrary("", _клипы);
        // физических костей у соседа нет — узел импорта не нужен
        foreach (var лишнее in скелет.FindChildren("*", "PhysicalBoneSimulator3D", true, false))
            лишнее.Free();
        foreach (var меш in скелет.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            string имя = меш.Name;
            if (имя == ТЕЛО.сетка)
                меш.Mesh = _тело;
            else if (_вещи.TryGetValue(имя, out var вещь))
                меш.Mesh = вещь;
            else
            {
                меш.Visible = false;        // сетки нет в таблице — не надевать (консоль «люди» об этом скажет)
                continue;
            }
            foreach (var (краска, цвет) in цвета.краски)
                if (Тела.Униформа(краска) is { } униформа)
                    меш.SetInstanceShaderParameter(униформа, цвет);
        }
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
    /// Покой — лицом в +Z, как у Mixamo, каким бы его ни сохранил файл.
    /// </summary>
    public static Transform3D Кисть_в_покое(Модель м)
    {
        int кость = м.скелет.FindBone(КИСТЬ);
        return м.корень.Transform * м.скелет.Transform * Лицом_вперёд(м.скелет) * м.скелет.GetBoneGlobalRest(кость);
    }

    /// <summary>
    /// Поворот покоя скелета вокруг вертикали так, чтобы левая нога была
    /// в +X — лицом в +Z, как у Mixamo. У файла с курткой (30.09) — без
    /// поворота, у файла из Blender (02.10) — на −90°.
    /// </summary>
    public static Transform3D Лицом_вперёд(Skeleton3D скелет)
    {
        int л = скелет.FindBone("mixamorig_LeftUpLeg"), п = скелет.FindBone("mixamorig_RightUpLeg");
        if (л < 0 || п < 0)
            return Transform3D.Identity;
        var влево = скелет.GetBoneGlobalRest(л).Origin - скелет.GetBoneGlobalRest(п).Origin;
        return new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(влево.Z, влево.X)), Vector3.Zero);
    }

    private static bool Загрузить()
    {
        if (_пробовали)
            return _основа is not null;
        _пробовали = true;
        string основа = $"{ПАПКА}/{ТЕЛО.файл}.fbx";
        if (!ResourceLoader.Exists(основа))
        {
            GD.PrintErr($"нет модели человека {основа} — соседи капсулами");
            return false;
        }
        _основа = GD.Load<PackedScene>(основа);

        var образец = _основа.Instantiate<Node3D>();
        var скелет = образец.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().First();
        (_бёдра, _рост_скелета) = Покой(скелет);
        _масштаб = РОСТ / _рост_скелета;
        _шейдер = new Shader { Code = Шейдер() };
        var прятать = ТЕЛО.под_вещами.ToHashSet(System.StringComparer.Ordinal);
        foreach (var меш in скелет.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            string имя = меш.Name;
            if (имя == ТЕЛО.сетка)
                _тело = Сетка_тела((ArrayMesh)меш.Mesh, прятать);
            else if (ТЕЛО.вещи.FirstOrDefault(в => в.сетка == имя) is { } вещь)
                _вещи[имя] = Сетка_вещи((ArrayMesh)меш.Mesh, вещь.краска);
            else
                GD.PrintErr($"в {основа} сетка «{имя}» не из таблицы тела — не надевается");
        }
        образец.Free();
        if (_тело is null)
        {
            GD.PrintErr($"в {основа} нет сетки тела «{ТЕЛО.сетка}» — соседи капсулами");
            _основа = null;
            return false;
        }

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
                var свой = узел.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().First();
                var (бёдра, рост) = Покой(свой);
                var клип = (Animation)игрок.GetAnimation(игрок.GetAnimationList()[0]).Duplicate(true);
                узел.Free();
                клип.LoopMode = Animation.LoopModeEnum.Linear;
                На_тело(клип, бёдра, _рост_скелета / Mathf.Max(0.01f, рост));
                взят = (клип, к.шаг ? Без_хода(клип) * _масштаб : 0f);
                взятые[к.файл] = взят;
            }
            if (к.шаг)
                _шаг[к.клип] = взят.шаг;
            _клипы.AddAnimation(к.клип, взят.клип);
        }
        return true;
    }

    /// <summary>Покой скелета: где бёдра и сколько от носков до макушки.</summary>
    private static (Vector3 бёдра, float рост) Покой(Skeleton3D скелет)
    {
        float низ = float.MaxValue, верх = float.MinValue;
        for (int b = 0; b < скелет.GetBoneCount(); b++)
        {
            float y = скелет.GetBoneGlobalRest(b).Origin.Y;
            низ = Mathf.Min(низ, y);
            верх = Mathf.Max(верх, y);
        }
        int бёдра = скелет.FindBone(БЁДРА);
        return (бёдра >= 0 ? скелет.GetBoneRest(бёдра).Origin : Vector3.Zero, Mathf.Max(0.01f, верх - низ));
    }

    /// <summary>
    /// Клип чужого скелета — на тело: повороты остаются (кости Mixamo
    /// в покое все без поворота, и поворот из клипа кладёт кость так же),
    /// ход бёдер — от их покоя в клипе, в масштабе тела, к покою бёдер тела;
    /// прочие сдвиги и масштабы костей вынуты.
    /// </summary>
    private static void На_тело(Animation клип, Vector3 бёдра_клипа, float к)
    {
        for (int т = клип.GetTrackCount() - 1; т >= 0; т--)
        {
            var вид = клип.TrackGetType(т);
            bool бёдра = клип.TrackGetPath(т).ToString().EndsWith(":" + БЁДРА, System.StringComparison.Ordinal);
            if (вид == Animation.TrackType.Scale3D || вид == Animation.TrackType.Position3D && !бёдра)
            {
                клип.RemoveTrack(т);
                continue;
            }
            if (вид != Animation.TrackType.Position3D)
                continue;
            for (int k = 0; k < клип.TrackGetKeyCount(т); k++)
            {
                var v = (Vector3)клип.TrackGetKeyValue(т, k);
                клип.TrackSetKeyValue(т, k, _бёдра + (v - бёдра_клипа) * к);
            }
        }
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

    /// <summary>Имя материала поверхности — как у автора в Blender.</summary>
    private static string Материал(ArrayMesh меш, int s) =>
        меш.SurfaceGetMaterial(s)?.ResourceName is { Length: > 0 } имя ? имя : меш.SurfaceGetName(s);

    /// <summary>Цвет поверхности из файла (sRGB, как отдаёт движок); материала
    /// нет — эталонный серый, то есть ровно цвет соседа.</summary>
    private static Color Свой(ArrayMesh меш, int s) =>
        (меш.SurfaceGetMaterial(s) as BaseMaterial3D)?.AlbedoColor
        ?? new Color((float)Тела.ЭТАЛОН, (float)Тела.ЭТАЛОН, (float)Тела.ЭТАЛОН).LinearToSrgb();

    /// <summary>
    /// Сетка тела для соседей: без зон, что прячутся под одеждой, и каждая
    /// зона — краской из таблицы. Зона не из таблицы — своим цветом (консоль
    /// «люди» об этом скажет).
    /// </summary>
    private static ArrayMesh Сетка_тела(ArrayMesh исходная, IReadOnlySet<string> прятать)
    {
        var сетка = new ArrayMesh();
        for (int s = 0; s < исходная.GetSurfaceCount(); s++)
        {
            string зона = Материал(исходная, s);
            if (прятать.Contains(зона))
                continue;
            var формат = исходная.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights;
            сетка.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, исходная.SurfaceGetArrays(s), flags: формат);
            int новая = сетка.GetSurfaceCount() - 1;
            сетка.SurfaceSetName(новая, зона);
            var свой = Свой(исходная, s);
            сетка.SurfaceSetMaterial(новая, ТЕЛО.зоны.TryGetValue(зона, out var краска)
                ? Краской(краска, свой, 1f)
                : Краской(Краска.СВОЯ, свой, 1f));
        }
        return сетка;
    }

    /// <summary>Вещь: серые материалы — цветом соседа для вещи, цветные — свои
    /// (`Тела.Покраска`; альбедо в Godot — sRGB, правило — в линейных).</summary>
    private static ArrayMesh Сетка_вещи(ArrayMesh исходная, Краска вещи)
    {
        var сетка = (ArrayMesh)исходная.Duplicate();
        for (int s = 0; s < сетка.GetSurfaceCount(); s++)
        {
            var свой = Свой(сетка, s);
            var л = свой.SrgbToLinear();
            var (краска, доля) = Тела.Покраска(вещи, л.R, л.G, л.B);
            сетка.SurfaceSetMaterial(s, Краской(краска, свой, (float)доля));
        }
        return сетка;
    }

    /// <summary>Материал краски: один на все одинаковые — цвета соседа идут через instance uniform.</summary>
    private static ShaderMaterial Краской(Краска краска, Color свой, float доля)
    {
        string ключ = $"{(int)краска}|{свой.ToHtml()}|{доля:0.000}";
        if (_материалы.TryGetValue(ключ, out var м))
            return м;
        м = new ShaderMaterial { Shader = _шейдер };
        м.SetShaderParameter("role", (int)краска);
        м.SetShaderParameter("own", свой);
        м.SetShaderParameter("shade", доля);
        _материалы[ключ] = м;
        return м;
    }
}
