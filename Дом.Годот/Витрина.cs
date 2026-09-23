// Витрина моделей — стенд, а не игра: проверить, что модель перенеслась
// в движок как надо, и что грани у неё не вывернуты.
//
// Автор попросил отдельную локацию: модели приходят из Blender, и дважды
// уже приходили не такими — в сотых долях метра и Z-вверх (деревья,
// лопата), с частью вывернутых граней и вывернутые целиком (гараж,
// `модели/ЧИТАЙ.md`). Глазами в игре это видно поздно и не всегда: там
// модель стоит в темноте, и половину её закрывают стены.
//
// Витрина сама находит всё, что лежит в `res://модели` (fbx, glb, gltf,
// obj, dae, blend — вложенные папки тоже), и ставит в ряд на светлом полу
// рядом с человеком ростом 1.75 м — как модель пришла из импорта, без
// поправок, которые делает игра. Над каждой — рамка в метрах, и если она
// лежит на боку (длина по Z больше высоты вдвое), так и написано.
//
// Вывернутость меряется двумя способами, и оба — числом:
//   · **против нормалей** — треугольник, у которого порядок вершин
//     говорит «лицо сюда», а нормали — «туда»: свет и отсечение считают
//     его разными сторонами;
//   · **наизнанку** — деталь, у которой объём, посчитанный по граням,
//     вышел со знаком минус: все грани смотрят внутрь. Знак меряется
//     относительно обычного куба Godot — так витрина не гадает, какой
//     порядок вершин в движке «лицевой».
// И глазом: B подсвечивает изнанку — всё, что камера видит сзади, красится
// малиновым. У целой закрытой модели снаружи малинового нет вовсе.
//
// Первыми в ряду стоят два эталона — обычный куб Godot и тот же куб
// наизнанку (`FlipFaces`). Их строки обязаны читаться «0, наизнанку 0»
// и «НАИЗНАНКУ»: если нет, врёт сама витрина, а не модели. Считается всё
// в мировых осях — корень импорта тоже бывает повёрнут и отражён
// (FBX с Z-вверх приходит с поворотом на корне).
//
// Управление: мышь с зажатой кнопкой — вращать вокруг модели, колесо —
// ближе и дальше, стрелки влево-вправо — следующая модель, B — изнанка,
// N — треугольники против нормалей (красным), F — показать модель целиком,
// Esc — выйти. Открыть: сцена `витрина.tscn`, в редакторе — F6.

using Godot;

namespace Дом.Годот;

public partial class Витрина : Node3D
{
    private const string ПАПКА = "res://модели";
    private static readonly string[] ВИДЫ = { ".fbx", ".glb", ".gltf", ".obj", ".dae", ".blend" };
    private const float ПРОМЕЖУТОК = 3.0f;

    private sealed class Образец
    {
        public string путь = "";
        public Node3D узел = null!;
        public Aabb рамка;
        public int деталей, треугольников, против, наизнанку, поверхностей, двусторонних;
        public readonly List<string> вывернутые = new();
        public readonly List<MeshInstance3D> меши = new();
        public readonly List<MeshInstance3D> метки = new();   // треугольники против нормалей
    }

    private readonly List<Образец> _образцы = new();
    private int _текущий;
    private Camera3D _камера = null!;
    private float _рыск = 0.6f, _тангаж = -0.35f, _даль = 4f;
    private Vector3 _центр;
    private bool _изнанка, _метки;
    private ShaderMaterial _изнанка_мат = null!;
    private Label _сводка = null!;
    private int _знак = 1;          // знак «наружу» в этом движке — по кубу

    public override void _Ready()
    {
        Мир_вокруг();
        _знак = Знак_куба();
        _изнанка_мат = new ShaderMaterial { Shader = new Shader { Code = ИЗНАНКА } };

        float x = 0;
        var узлы = new List<(string путь, Node3D? узел)>
        {
            ("эталон: куб", new MeshInstance3D { Mesh = new BoxMesh() }),
            ("эталон: куб наизнанку", new MeshInstance3D { Mesh = new BoxMesh { FlipFaces = true } }),
        };
        foreach (string путь in Найти(ПАПКА).OrderBy(п => п, System.StringComparer.Ordinal))
            узлы.Add((путь, Загрузить(путь)));
        foreach (var (путь, узел) in узлы)
        {
            if (узел is null)
                continue;
            var о = Поставить(путь, узел);
            // по оси X — в ряд: левый край рамки к правому краю прошлой.
            // Сдвигается только место: поворот и масштаб — как пришли
            float ширина = Mathf.Max(о.рамка.Size.X, 0.5f);
            var сдвиг = new Vector3(x - о.рамка.Position.X, -о.рамка.Position.Y,
                                    -о.рамка.GetCenter().Z);
            о.узел.Position += сдвиг;
            о.рамка = new Aabb(о.рамка.Position + сдвиг, о.рамка.Size);
            Человек(new Vector3(x - 0.6f, 0, 0.8f));
            Подпись(о, new Vector3(x + ширина / 2, о.рамка.Size.Y + 0.4f, 0));
            x += ширина + ПРОМЕЖУТОК;
            _образцы.Add(о);
        }

        Экран();
        Выбрать(0);
        foreach (var о in _образцы)
            GD.Print($"[витрина] {Строка(о)}");
        if (_образцы.Count == 0)
            GD.Print($"[витрина] в {ПАПКА} нет моделей");
    }

    // ------------------------------------------------------------ мир

    private void Мир_вокруг()
    {
        var окружение = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#3a3d42"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#c8ccd4"),
            AmbientLightEnergy = 0.55f,
        };
        AddChild(new WorldEnvironment { Environment = окружение });
        // свет с двух сторон — чтобы тыл модели не был чёрным и не прятал изнанку
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, 35, 0), LightEnergy = 1.1f, ShadowEnabled = true,
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30, -145, 0), LightEnergy = 0.5f,
        });
        // пол в клетку по метру
        var пол = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(400, 400) },
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = КЛЕТКА } },
            Position = new Vector3(0, -0.001f, 0),
        };
        AddChild(пол);
        _камера = new Camera3D { Fov = 60, Far = 500 };
        AddChild(_камера);
    }

    /// <summary>Человек ростом 1.75 м — мерка рядом с каждой моделью.</summary>
    private void Человек(Vector3 где)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Height = 1.75f, Radius = 0.25f },
            Position = где + new Vector3(0, 0.875f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.9f, 0.8f, 0.3f, 0.5f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        });
    }

    private void Подпись(Образец о, Vector3 где)
    {
        var надпись = new Label3D
        {
            Text = $"{System.IO.Path.GetFileName(о.путь)}\n"
                   + $"{Метры(о.рамка.Size.X)} × {Метры(о.рамка.Size.Y)} × {Метры(о.рамка.Size.Z)} м",
            Position = где,
            FontSize = 48,
            // подпись растёт с моделью: над гаражом в одиннадцать метров
            // надпись в полметра не прочесть
            PixelSize = Mathf.Max(0.004f, о.рамка.Size.Length() / 1600f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = о.против > 0 || о.наизнанку > 0 ? new Color("#ff8a8a") : new Color("#e8e2d0"),
        };
        AddChild(надпись);
    }

    // ------------------------------------------------------------ модели

    private static IEnumerable<string> Найти(string папка)
    {
        using var дир = DirAccess.Open(папка);
        if (дир is null)
            yield break;
        // в собранной игре от модели остаётся только «.import»: имя — до него
        var файлы = new SortedSet<string>(System.StringComparer.Ordinal);
        foreach (string имя in дир.GetFiles())
        {
            string файл = имя.EndsWith(".import", System.StringComparison.Ordinal)
                          ? имя[..^".import".Length] : имя;
            if (ВИДЫ.Any(в => файл.EndsWith(в, System.StringComparison.OrdinalIgnoreCase)))
                файлы.Add(файл);
        }
        foreach (string файл in файлы)
            yield return $"{папка}/{файл}";
        foreach (string под in дир.GetDirectories())
            foreach (string путь in Найти($"{папка}/{под}"))
                yield return путь;
    }

    private static Node3D? Загрузить(string путь)
    {
        var ресурс = ResourceLoader.Exists(путь) ? GD.Load(путь) : null;
        Node3D? узел = ресурс switch
        {
            PackedScene сцена => сцена.Instantiate() as Node3D,
            Mesh меш => new MeshInstance3D { Mesh = меш },
            _ => null,
        };
        if (узел is null)
            GD.PrintErr($"[витрина] {путь}: не модель");
        return узел;
    }

    /// <summary>Поставить модель в мир (в начале координат, как пришла)
    /// и разобрать каждую её деталь — в мировых осях.</summary>
    private Образец Поставить(string путь, Node3D узел)
    {
        AddChild(узел);
        var о = new Образец { путь = путь, узел = узел };
        Aabb? рамка = null;
        foreach (var м in узел.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
                              .Concat(узел is MeshInstance3D сам ? new[] { сам } : System.Array.Empty<MeshInstance3D>()))
        {
            if (м.Mesh is null)
                continue;
            о.меши.Add(м);
            о.деталей++;
            var в_мире = м.GlobalTransform;
            var р_м = в_мире * м.GetAabb();
            рамка = рамка is Aabb р ? р.Merge(р_м) : р_м;
            // зеркальный масштаб (на детали или на корне) переворачивает
            // лицо — движок это учитывает, и счёт должен тоже
            Разобрать(о, м, в_мире.Basis.Determinant() < 0 ? -1 : 1);
        }
        о.рамка = рамка ?? new Aabb(Vector3.Zero, Vector3.One);
        return о;
    }

    /// <summary>Треугольники против нормалей и знак объёма детали.</summary>
    private void Разобрать(Образец о, MeshInstance3D м, int зеркало)
    {
        int знак = _знак * зеркало;
        var плохие = new List<Vector3>();
        // двусторонний материал прячет вывернутость от глаза: грань видна
        // с обеих сторон, и выдаёт её только свет (нормали внутрь — темно)
        for (int s = 0; s < м.Mesh.GetSurfaceCount(); s++)
        {
            о.поверхностей++;
            if (м.GetActiveMaterial(s) is BaseMaterial3D { CullMode: BaseMaterial3D.CullModeEnum.Disabled })
                о.двусторонних++;
        }
        var тр = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        for (int s = 0; s < м.Mesh.GetSurfaceCount(); s++)
        {
            var массивы = м.Mesh.SurfaceGetArrays(s);
            var точки = массивы[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var нормали = массивы[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var номера = массивы[(int)Mesh.ArrayType.Index].AsInt32Array();
            int n = номера.Length > 0 ? номера.Length : точки.Length;
            for (int i = 0; i + 2 < n; i += 3)
            {
                int а = номера.Length > 0 ? номера[i] : i;
                int б = номера.Length > 0 ? номера[i + 1] : i + 1;
                int в = номера.Length > 0 ? номера[i + 2] : i + 2;
                var (A, B, C) = (точки[а], точки[б], точки[в]);
                о.треугольников++;
                тр.Add((A, B, C));
                if (нормали.Length != точки.Length)
                    continue;
                var лицо = (B - A).Cross(C - A) * знак;
                if (лицо.LengthSquared() < 1e-12f)
                    continue;
                if (лицо.Dot(нормали[а] + нормали[б] + нормали[в]) < 0)
                {
                    о.против++;
                    плохие.Add(A);
                    плохие.Add(B);
                    плохие.Add(C);
                }
            }
        }
        // Наизнанку. У замкнутой детали (каждое ребро — ровно у двух граней)
        // объём по граням от начала координат не зависит, и его знак — ответ.
        // У открытой (листва, плоскость, труба без донышка) объём ничего
        // не значит; там считается, какая доля граней смотрит к середине
        // детали, — у целой её меньше половины, у вывернутой больше.
        var середина = м.GetAabb().GetCenter();
        double объём = 0;
        int к_центру = 0, считано = 0;
        foreach (var (A, B, C) in тр)
        {
            var (a, b, c) = (A - середина, B - середина, C - середина);
            объём += a.Dot(b.Cross(c)) / 6.0;
            var лицо = (B - A).Cross(C - A) * знак;
            if (лицо.LengthSquared() < 1e-12f)
                continue;
            считано++;
            if (лицо.Dot((A + B + C) / 3f - середина) < 0)
                к_центру++;
        }
        bool замкнута = Замкнута(тр);
        double доля = считано == 0 ? 0 : к_центру / (double)считано;
        bool вывернута = замкнута ? объём * знак < 0 : доля > 0.6;
        if (вывернута)
        {
            о.наизнанку++;
            о.вывернутые.Add($"{м.Name} ({(замкнута ? "замкнутая, объём внутрь" : $"открытая, к центру {доля:P0}")})");
        }
        if (плохие.Count > 0)
        {
            var ст = new SurfaceTool();
            ст.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var т in плохие)
                ст.AddVertex(т);
            var метка = new MeshInstance3D
            {
                Mesh = ст.Commit(),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1, 0.1f, 0.1f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                Visible = false,
            };
            м.AddChild(метка);
            о.метки.Add(метка);
        }
    }

    /// <summary>Замкнута ли деталь: каждое ребро — ровно у двух граней.
    /// Вершины сваривают по месту: импорт режет их по швам текстуры.</summary>
    private static bool Замкнута(List<(Vector3 A, Vector3 B, Vector3 C)> тр)
    {
        if (тр.Count == 0)
            return false;
        (long, long, long) Ключ(Vector3 v) => ((long)System.Math.Round(v.X * 1e4),
                                               (long)System.Math.Round(v.Y * 1e4),
                                               (long)System.Math.Round(v.Z * 1e4));
        var рёбра = new Dictionary<((long, long, long), (long, long, long)), int>();
        void Ребро(Vector3 p, Vector3 q)
        {
            var (кп, кq) = (Ключ(p), Ключ(q));
            var к = кп.CompareTo(кq) <= 0 ? (кп, кq) : (кq, кп);
            рёбра[к] = рёбра.GetValueOrDefault(к) + 1;
        }
        foreach (var (A, B, C) in тр)
        {
            Ребро(A, B);
            Ребро(B, C);
            Ребро(C, A);
        }
        return рёбра.Values.All(n => n == 2);
    }

    /// <summary>
    /// Какой знак у «наружу» в этом движке: берётся обычный куб Godot,
    /// у которого грани заведомо смотрят наружу, и считается, согласен ли
    /// с его нормалями порядок вершин при плюсе. Так витрина не зависит
    /// от того, по часовой или против лицо в этой версии движка.
    /// </summary>
    private static int Знак_куба()
    {
        var м = new BoxMesh().GetMeshArrays();
        var точки = м[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var нормали = м[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var номера = м[(int)Mesh.ArrayType.Index].AsInt32Array();
        int за = 0, против = 0;
        for (int i = 0; i + 2 < номера.Length; i += 3)
        {
            var (A, B, C) = (точки[номера[i]], точки[номера[i + 1]], точки[номера[i + 2]]);
            float d = (B - A).Cross(C - A).Dot(нормали[номера[i]]);
            if (d > 0) за++;
            else if (d < 0) против++;
        }
        return за >= против ? 1 : -1;
    }

    // ------------------------------------------------------------ экран

    private void Экран()
    {
        var слой = new CanvasLayer();
        AddChild(слой);
        _сводка = new Label
        {
            Position = new Vector2(16, 12),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Size = new Vector2(GetViewport().GetVisibleRect().Size.X - 32, 0),
        };
        _сводка.AddThemeColorOverride("font_color", new Color("#e8e2d0"));
        _сводка.AddThemeColorOverride("font_outline_color", Colors.Black);
        _сводка.AddThemeConstantOverride("outline_size", 4);
        слой.AddChild(_сводка);
        Сводка();
    }

    private void Сводка()
    {
        var строки = new List<string> { "ВИТРИНА МОДЕЛЕЙ — всё из res://модели, как пришло из импорта", "" };
        for (int i = 0; i < _образцы.Count; i++)
            строки.Add((i == _текущий ? "▶ " : "   ") + Строка(_образцы[i]));
        строки.Add("");
        строки.Add("мышь с кнопкой — вращать · колесо — ближе/дальше · ← → — модель · "
                   + "F — вся модель · B — изнанка малиновым · N — против нормалей красным · Esc — выйти");
        строки.Add($"изнанка: {(_изнанка ? "видна" : "скрыта")} · против нормалей: "
                   + (_метки ? "видны" : "скрыты"));
        _сводка.Text = string.Join("\n", строки);
    }

    private static string Строка(Образец о)
    {
        var р = о.рамка.Size;
        string бок = р.Z > р.Y * 2 && р.Z > р.X ? " · ЛЕЖИТ НА БОКУ (Z-вверх?)" : "";
        string масштаб = р.Y < 0.2f && р.X < 0.2f && р.Z < 0.2f ? " · КРОШЕЧНАЯ (сотые доли метра?)"
                       : р.Y > 50 || р.X > 50 ? " · ОГРОМНАЯ (сантиметры?)" : "";
        return $"{о.путь.Replace(ПАПКА + "/", "")}: {о.деталей} дет., {о.треугольников} тр., "
               + $"{Метры(р.X)}×{Метры(р.Y)}×{Метры(р.Z)} м{бок}{масштаб} · против нормалей {о.против}"
               + (о.наизнанку > 0 ? $" · НАИЗНАНКУ: {string.Join(", ", о.вывернутые)}" : " · наизнанку 0")
               + $" · двусторонних поверхностей {о.двусторонних} из {о.поверхностей}";
    }

    private static string Метры(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private void Выбрать(int i)
    {
        if (_образцы.Count == 0)
            return;
        _текущий = (i % _образцы.Count + _образцы.Count) % _образцы.Count;
        var о = _образцы[_текущий];
        _центр = о.рамка.GetCenter();          // рамка уже в мировых осях
        _даль = Mathf.Max(1.5f, о.рамка.Size.Length() * 1.2f);
        Сводка();
        Камера();
    }

    private void Камера()
    {
        var смещение = new Vector3(
            Mathf.Cos(_тангаж) * Mathf.Sin(_рыск),
            -Mathf.Sin(_тангаж),
            Mathf.Cos(_тангаж) * Mathf.Cos(_рыск)) * _даль;
        _камера.Position = _центр + смещение;
        _камера.LookAt(_центр, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion д when д.ButtonMask != 0:
                _рыск -= д.Relative.X * 0.008f;
                _тангаж = Mathf.Clamp(_тангаж - д.Relative.Y * 0.008f, -1.45f, 1.45f);
                Камера();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _даль = Mathf.Max(0.3f, _даль * 0.9f);
                Камера();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _даль *= 1.1f;
                Камера();
                break;
            case InputEventKey { Pressed: true, Echo: false } к:
                switch (к.Keycode)
                {
                    case Key.Right:
                        Выбрать(_текущий + 1);
                        break;
                    case Key.Left:
                        Выбрать(_текущий - 1);
                        break;
                    case Key.F:     // клавиша: витрина — стенд разработчика, не игра
                        Выбрать(_текущий);
                        break;
                    case Key.B:     // клавиша: витрина — стенд разработчика, не игра
                        _изнанка = !_изнанка;
                        foreach (var о in _образцы)
                            foreach (var м in о.меши)
                                м.MaterialOverlay = _изнанка ? _изнанка_мат : null;
                        Сводка();
                        break;
                    case Key.N:     // клавиша: витрина — стенд разработчика, не игра
                        _метки = !_метки;
                        foreach (var о in _образцы)
                            foreach (var м in о.метки)
                                м.Visible = _метки;
                        Сводка();
                        break;
                    case Key.Escape:
                        GetTree().Quit();
                        break;
                }
                break;
        }
    }

    // Изнанка: второй проход рисует только задние грани (отсекаются
    // передние), малиновым. То, что закрыто лицевыми гранями, глубина
    // не пропускает — снаружи у целой модели малинового не видно.
    private const string ИЗНАНКА = """
        shader_type spatial;
        render_mode unshaded, cull_front;
        void fragment() {
            ALBEDO = vec3(1.0, 0.0, 0.75);
            ALPHA = 0.85;
        }
        """;

    private const string КЛЕТКА = """
        shader_type spatial;
        void fragment() {
            vec3 w = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
            vec2 k = floor(w.xz);
            float c = mod(k.x + k.y, 2.0);
            ALBEDO = mix(vec3(0.42), vec3(0.5), c);
            ROUGHNESS = 0.9;
        }
        """;
}
