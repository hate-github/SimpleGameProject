// Погреб в подвале: дощатая клетушка у стены, дверь на навесном замке,
// два ящика на полу и полка с банками.
//
// Погреб в доме был и раньше — кладовая «погреб6» в `npcs.json`, — но
// в мире от него стояла одна доска с номером на стене подвала: подошёл,
// нажал, и мысль «сходить в погреб» делалась сама, а банки оказывались
// дома раньше героя. Теперь погреб устроен как гараж (`Хранилище`):
// замок отпирают своим ключом или взламывают, ящики обыскивают,
// найденное несут домой.
//
// Клетушка — как в подвалах хрущёвок: перегородки из досок со щелями,
// не до потолка, дверь наружу, в проход, петля с навесным замком.
// Строится она здесь, из досок, по гнезду `погреб<i>` сцены: гнездо
// стоит на полу у стены, клетушка встаёт задом к стене (+Z гнезда)
// и дверью в проход (−Z). Гнездо двигают мышью — клетушка едет с ним.
//
// Доски одной вещи — один меш (`Доски`): щелей много, а рисовать каждую
// доску отдельно незачем. Форма у стенки сплошная: в щель не пролезть,
// и луч сквозь неё не смотрит.
//
// Своего света у погреба нет: лампы подвала светят без теней и достают
// и сквозь доски. Погреб — не улица: часы обыска погода не множит
// (`Обыск.часы`), метели здесь нет.

using System.Globalization;
using Godot;

namespace Дом.Годот;

public partial class Погреб : Хранилище
{
    // Размеры клетушки, в метрах. Начало — гнездо: пол у стены, посередине.
    private const float ШИРИНА = 2.1f;       // гнёзда стоят через 2.4 — между клетушками щель
    private const float ГЛУБИНА = 1.5f;
    private const float ВЫСОТА = 2.1f;       // не до потолка: подвал 2.8
    private const float ДОСКА = 0.035f;      // толщина доски стенки
    private const float ПРОЁМ = 0.9f;        // ширина двери: человек проходит с запасом в ладонь
    private const float ПРОЁМ_В = 1.9f;
    private const float ЗАД = 0.04f;         // задняя стенка — вплотную к стене подвала
    private const float ПЕРЁД = ЗАД - ГЛУБИНА;
    private const float ЗАМОК_В = 0.95f;     // на какой высоте петля с замком
    private const float РАСПАХ = 100f;       // дверь открывается наружу, в проход
    private const float СКОРОСТЬ = 2.2f;     // долей распаха в секунду

    private static readonly Color[] ДЕРЕВО =
    {
        new("#4f4130"), new("#5a4a36"), new("#463a2c"), new("#62513b"), new("#54452f"),
    };
    private static readonly Color[] КАРТОШКА =
    {
        new("#7a5c3a"), new("#6e5232"), new("#846444"), new("#5f4a30"),
    };
    private static readonly Color[] СОЛЕНЬЯ =
    {
        new("#4d6b3a"), new("#8a3a2a"), new("#6a2a3a"), new("#9a7a3a"), new("#5a6a2a"),
    };
    private static readonly Color ЖЕЛЕЗО = new("#3c3c3a");

    private Node3D _петля = null!;             // ось двери: дверь — её ребёнок
    private StaticBody3D _дверь_тело = null!;
    private Aabb _дверь_габарит;               // в осях петли
    private float _створка;                    // 0 — закрыта, 1 — открыта
    private float _цель;
    private bool _ждёт_твёрдости;              // остановилась, но в ней ещё стоит человек
    private Предмет? _замок_цель;
    private Node3D? _сорванный;                // сорванный замок лежит у порога
    private AudioStreamPlayer3D? _звук_двери;
    private StandardMaterial3D _дерево = null!, _стекло = null!, _железо = null!;
    private int _тел;                          // имена форм: у соседей по узлу они разные

    protected override string Чей() => кладовая is null ? "погреб" : $"погреб кв.{кладовая.apt}";

    /// <summary>
    /// Собрать клетушку. <paramref name="номер"/> — квартира хозяина: он
    /// написан на двери, и от него же ложатся доски — клетушки не выходят
    /// одна в одну.
    /// </summary>
    public void Собрать(int номер)
    {
        _кость.Randomize();
        _дерево = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.92f,
        };
        _стекло = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.3f,
            Metallic = 0.1f,
        };
        _железо = new StandardMaterial3D
        {
            AlbedoColor = new Color("#4a4a46"),
            Metallic = 0.6f,
            Roughness = 0.5f,
        };
        Стены(номер);
        Дверь(номер);
        Замки();
        Ящики(номер);
        Показать();
    }

    // ------------------------------------------------------------ стены

    private void Стены(int номер)
    {
        var д = new Доски(номер * 31 + 1, ДЕРЕВО);
        float x = ШИРИНА / 2;
        // зад и бока — во всю высоту
        var зад = (new Vector3(-x, 0, ЗАД - ДОСКА), new Vector3(x, ВЫСОТА, ЗАД));
        var левый = (new Vector3(-x, 0, ПЕРЁД), new Vector3(-x + ДОСКА, ВЫСОТА, ЗАД - ДОСКА));
        var правый = (new Vector3(x - ДОСКА, 0, ПЕРЁД), new Vector3(x, ВЫСОТА, ЗАД - ДОСКА));
        // перед — две стенки по сторонам двери и доски над ней
        var перед_л = (new Vector3(-x + ДОСКА, 0, ПЕРЁД), new Vector3(-ПРОЁМ / 2, ВЫСОТА, ПЕРЁД + ДОСКА));
        var перед_п = (new Vector3(ПРОЁМ / 2, 0, ПЕРЁД), new Vector3(x - ДОСКА, ВЫСОТА, ПЕРЁД + ДОСКА));
        var над = (new Vector3(-ПРОЁМ / 2, ПРОЁМ_В, ПЕРЁД), new Vector3(ПРОЁМ / 2, ВЫСОТА, ПЕРЁД + ДОСКА));

        Стенка(д, зад.Item1, зад.Item2, 0);
        Стенка(д, левый.Item1, левый.Item2, 2);
        Стенка(д, правый.Item1, правый.Item2, 2);
        Стенка(д, перед_л.Item1, перед_л.Item2, 0);
        Стенка(д, перед_п.Item1, перед_п.Item2, 0);
        Стенка(д, над.Item1, над.Item2, 1, доска: 0.1f);

        // стойки по углам и у двери — изнутри, за досками
        const float С = 0.05f;
        foreach (float сx in new[] { -x + ДОСКА, x - ДОСКА - С, -ПРОЁМ / 2 - С, ПРОЁМ / 2 })
            Брус(д, new Vector3(сx, 0, ПЕРЁД + ДОСКА), new Vector3(сx + С, ВЫСОТА, ПЕРЁД + ДОСКА + С));
        foreach (float сx in new[] { -x + ДОСКА, x - ДОСКА - С })
            Брус(д, new Vector3(сx, 0, ЗАД - ДОСКА - С), new Vector3(сx + С, ВЫСОТА, ЗАД - ДОСКА));
        // ригели снаружи поперёк передних досок — на них доски и прибиты
        foreach (float y in new[] { 0.25f, 1.75f })
        {
            Брус(д, new Vector3(-x, y, ПЕРЁД - 0.025f), new Vector3(-ПРОЁМ / 2 - 0.02f, y + 0.08f, ПЕРЁД));
            Брус(д, new Vector3(ПРОЁМ / 2 + 0.09f, y, ПЕРЁД - 0.025f), new Vector3(x, y + 0.08f, ПЕРЁД));
        }
        AddChild(д.Меш("стены_погреба", _дерево));

        // формы — сплошные коробки: щели только для глаза
        foreach (var (а, б) in new[] { зад, левый, правый, перед_л, перед_п, над })
            Тело(а, б, 1);
    }

    // ------------------------------------------------------------ дверь

    private void Дверь(int номер)
    {
        // Петля — у левого края проёма, если смотреть из клетушки;
        // дверь в её осях лежит от нуля вдоль +X, изнутри — в сторону +Z.
        _петля = new Node3D { Name = "петля_погреба", Position = new Vector3(-ПРОЁМ / 2, 0, ПЕРЁД) };
        AddChild(_петля);
        const float ЗАЗОР = 0.012f;
        var д = new Доски(номер * 57 + 3, ДЕРЕВО);
        Стенка(д, new Vector3(ЗАЗОР, 0.03f, 0), new Vector3(ПРОЁМ - ЗАЗОР, ПРОЁМ_В - 0.015f, ДОСКА), 0);
        // планки изнутри, поперёк досок
        foreach (float y in new[] { 0.3f, ПРОЁМ_В - 0.35f })
            д.Брус(new Vector3(ПРОЁМ / 2, y, ДОСКА + 0.012f), new Vector3(ПРОЁМ - 0.1f, 0.09f, 0.024f));
        // ручка и петля под замок — снаружи
        д.Брус(new Vector3(ПРОЁМ - 0.16f, ЗАМОК_В + 0.24f, -0.02f), new Vector3(0.03f, 0.14f, 0.03f), ЖЕЛЕЗО);
        д.Брус(new Vector3(ПРОЁМ - 0.06f, ЗАМОК_В, -0.004f), new Vector3(0.1f, 0.05f, 0.008f), ЖЕЛЕЗО);
        _петля.AddChild(д.Меш("дверь_погреба", _дерево));

        _дверь_габарит = new Aabb(new Vector3(ЗАЗОР, 0.03f, -0.035f),
                                  new Vector3(ПРОЁМ - 2 * ЗАЗОР, ПРОЁМ_В - 0.045f, ДОСКА + 0.06f));
        _дверь_тело = new StaticBody3D { Name = "дверь_погреба_форма", CollisionLayer = 1, CollisionMask = 0 };
        _дверь_тело.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = _дверь_габарит.Size },
            Position = _дверь_габарит.GetCenter(),
        });
        _петля.AddChild(_дверь_тело);

        // прицел — на всю дверь и поворачивается вместе с ней
        var цель = Предмет.Вокруг(_петля, _дверь_габарит, 0.04f, "створка_погреба");
        цель.Подсказка = ДверьПодсказка;
        цель.Действие = ДверьДействие;

        // номер квартиры — краской на двери, лицом в проход
        if (кладовая is not null)
            _петля.AddChild(new Label3D
            {
                Name = "номер_погреба",
                Text = номер.ToString(CultureInfo.InvariantCulture),
                FontSize = 64,
                PixelSize = 0.0025f,
                Modulate = new Color("#cfc6b0"),
                Position = new Vector3(ПРОЁМ / 2, 1.5f, -0.006f),
                RotationDegrees = new Vector3(0, 180, 0),
                DoubleSided = false,
                Shaded = false,
            });
        _звук_двери = Звук("шаги_скрип", new Vector3(0, 1.0f, ПЕРЁД), -4f, 2f);
    }

    private string ДверьПодсказка()
    {
        if (_замок_висит)
            return Шапка(" — дверь на замке");
        return _цель > 0 ? $"{Шапка()}\n{Жми}  прикрыть дверь"
                         : $"{Шапка()}\n{Жми}  открыть дверь";
    }

    private void ДверьДействие()
    {
        if (_замок_висит)
        {
            Сказать("сначала замок");
            return;
        }
        _цель = _цель > 0 ? 0 : 1;
        // скрип досок, пониже — так скрипит дверь, а не половица
        Играть(_звук_двери, "шаги_скрип", откуда: 0.05f, высота: 0.7f);
    }

    public override void _Process(double delta)
    {
        if (_петля is null)
            return;                         // ещё не собран
        if (!Mathf.IsEqualApprox(_створка, _цель))
        {
            // пока дверь движется, формы у неё нет: иначе она сметает
            // стоящего рядом человека
            _дверь_тело.CollisionLayer = 0;
            _створка = Mathf.MoveToward(_створка, _цель, (float)delta * СКОРОСТЬ);
            // наружу, в проход (−Z), дверь уводит положительный угол
            float угол = Mathf.DegToRad(РАСПАХ) * Mathf.SmoothStep(0f, 1f, _створка);
            _петля.Rotation = new Vector3(0, угол, 0);
            if (Mathf.IsEqualApprox(_створка, _цель))
            {
                _ждёт_твёрдости = true;
                Показать();
            }
        }
        if (_ждёт_твёрдости && !ГеройВДвери())
        {
            _дверь_тело.CollisionLayer = 1;
            _ждёт_твёрдости = false;
        }
        // замок висит на закрытой двери; открыта — прицел на нём не держится
        if (_замок_цель is not null)
            _замок_цель.Активен = _цель == 0 && _створка == 0;
    }

    /// <summary>Стоит ли человек там, где дверь: по её рамке в осях
    /// клетушки, с запасом на толщину человека.</summary>
    private bool ГеройВДвери()
    {
        var г = _петля.Transform * _дверь_габарит;
        var где = ToLocal(ГдеГерой());
        const float ЗАПАС = 0.35f;
        return где.X > г.Position.X - ЗАПАС && где.X < г.End.X + ЗАПАС
               && где.Z > г.Position.Z - ЗАПАС && где.Z < г.End.Z + ЗАПАС
               && где.Y < г.End.Y && где.Y + 1.75f > г.Position.Y;
    }

    // ------------------------------------------------------------ замок

    private void Замки()
    {
        var где = new Vector3(ПРОЁМ / 2, ЗАМОК_В, ПЕРЁД);
        // петля на косяке: в неё и в петлю двери продет замок
        var скоба = new Доски(1, ДЕРЕВО);
        скоба.Брус(где + new Vector3(0.045f, 0, -0.004f), new Vector3(0.07f, 0.05f, 0.008f), ЖЕЛЕЗО);
        AddChild(скоба.Меш("скоба_погреба", _дерево));

        _замок = Замок("замок_погреба_вид", где + new Vector3(0, -0.05f, -0.03f), лежит: false);
        _сорванный = Замок("замок_погреба_сорван", new Vector3(ПРОЁМ / 2 + 0.2f, 0.012f, ПЕРЁД - 0.3f),
                           лежит: true);

        // Замок мелкий, поэтому прицел ему раздут — и по толщине тоже,
        // в обе стороны от двери: иначе изнутри луч упирается в дверь
        // раньше, чем в него, и из запертой клетушки не выйти
        var мишень = new Aabb(где + new Vector3(-0.13f, -0.16f, -0.3f), new Vector3(0.26f, 0.28f, 0.6f));
        _замок_цель = Предмет.Вокруг(this, мишень, 0, "замок_погреба");
        _замок_цель.Подсказка = ЗамокПодсказка;
        var замок_цель = _замок_цель;
        _замок_цель.Действие = () => ЗамокДействие(замок_цель);
        _звук_замка = Звук("удар_по_металлу", где, -6f, 2f);
    }

    /// <summary>Навесной замок: корпус и дужка. Сорванный лежит на полу
    /// на боку, дужкой вбок.</summary>
    private Node3D Замок(string имя, Vector3 где, bool лежит)
    {
        var узел = new Node3D { Name = имя, Position = где };
        узел.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.06f, 0.022f) },
            MaterialOverride = _железо,
        });
        узел.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.011f, OuterRadius = 0.017f, Rings = 12, RingSegments = 6 },
            MaterialOverride = _железо,
            Position = new Vector3(лежит ? 0.012f : 0, 0.035f, 0),
            RotationDegrees = new Vector3(90, 0, лежит ? 35 : 0),
        });
        if (лежит)
            узел.RotationDegrees = new Vector3(-90, 25, 0);
        AddChild(узел);
        return узел;
    }

    /// <summary>Сорванный замок лежит у порога, пока его не повесили
    /// обратно для вида: взлом виден глазом, не только подсказкой.</summary>
    protected override void Показать()
    {
        base.Показать();
        if (_сорванный is not null)
            _сорванный.Visible = _вскрыта && !_замок_висит;
    }

    // ------------------------------------------------------------ ящики

    private void Ящики(int номер)
    {
        float у_стены = ЗАД - ДОСКА - 0.02f;
        var размер = new Vector3(0.6f, 0.32f, 0.42f);
        var мишени = new List<Aabb>();
        for (int i = 0; i < 2; i++)
        {
            var низ = new Vector3(i == 0 ? -0.5f : 0.5f, 0, у_стены - размер.Z / 2);
            var д = new Доски(номер * 13 + i, ДЕРЕВО);
            Ящик(д, низ, размер);
            AddChild(д.Меш($"ящик_погреба{i}", _дерево));
            // в левом картошка, в правом банки — по три в ряд, в два ряда
            Node3D полный = i == 0
                ? Картошка(низ, размер, номер * 5 + i)
                : Банки($"банки_в_ящике{i}", номер * 5 + i,
                        from a in new[] { -0.18f, 0f, 0.18f }
                        from b in new[] { -0.08f, 0.08f }
                        select низ + new Vector3(a, 0.018f, b));
            var габарит = new Aabb(низ - new Vector3(размер.X / 2, 0, размер.Z / 2), размер);
            // ящик мешает ходить, но не прицелу — как мебель гаража
            Тело(габарит.Position, габарит.End, СЛОЙ_МЕБЕЛИ);
            _коробки.Add(new Коробка
            {
                полная = полный, где = габарит.GetCenter(), имя = $"ящик{i}",
                название = "ящик", внутри = "в ящике",
            });
            мишени.Add(габарит);
        }

        // полка на задней стенке, над ящиками; на ней банки
        float z = ЗАД - ДОСКА - 0.14f;
        var полка = new Доски(номер * 7 + 5, ДЕРЕВО);
        полка.Брус(new Vector3(0, 1.245f, z), new Vector3(ШИРИНА - 2 * ДОСКА - 0.02f, 0.03f, 0.26f));
        foreach (float x in new[] { -0.75f, 0.75f })
            полка.Брус(new Vector3(x, 1.16f, z), new Vector3(0.03f, 0.14f, 0.22f));
        AddChild(полка.Меш("полка_погреба", _дерево));
        var на_полке = Банки("банки_на_полке", номер * 3 + 11,
                             Enumerable.Range(0, 7)
                                 .Select(k => new Vector3(-0.72f + k * 0.24f, 1.26f,
                                                          z + (k % 2 == 0 ? -0.03f : 0.03f))));
        var полка_г = new Aabb(new Vector3(-0.82f, 1.26f, z - 0.1f), new Vector3(1.64f, 0.2f, 0.2f));
        _коробки.Add(new Коробка
        {
            полная = на_полке, где = полка_г.GetCenter(), имя = "полка",
            название = "полка с банками", внутри = "на полке",
        });
        мишени.Add(полка_г);

        for (int i = 0; i < _коробки.Count; i++)
        {
            int номер_ = i;
            var цель = Предмет.Вокруг(this, мишени[i], 0.08f, $"ящик_погреба_цель{i}");
            цель.Подсказка = () => КоробкаПодсказка(номер_);
            цель.Действие = () => КоробкаДействие(номер_, цель);
        }
        _звук_коробки = Звук("коробка", _коробки[0].где, -4f, 2f);
    }

    /// <summary>Ящик: дно и четыре борта, в каждом по две доски в высоту.
    /// <paramref name="низ"/> — середина дна.</summary>
    private static void Ящик(Доски д, Vector3 низ, Vector3 р)
    {
        const float Т = 0.018f;
        float x = р.X / 2, z = р.Z / 2;
        Стенка(д, низ + new Vector3(-x, 0, -z), низ + new Vector3(x, Т, z), 0, доска: 0.14f, щель: 0.006f);
        Стенка(д, низ + new Vector3(-x, Т, -z), низ + new Vector3(x, р.Y, -z + Т), 1, доска: 0.13f, щель: 0.03f);
        Стенка(д, низ + new Vector3(-x, Т, z - Т), низ + new Vector3(x, р.Y, z), 1, доска: 0.13f, щель: 0.03f);
        Стенка(д, низ + new Vector3(-x, Т, -z + Т), низ + new Vector3(-x + Т, р.Y, z - Т), 1, доска: 0.13f, щель: 0.03f);
        Стенка(д, низ + new Vector3(x - Т, Т, -z + Т), низ + new Vector3(x, р.Y, z - Т), 1, доска: 0.13f, щель: 0.03f);
    }

    /// <summary>Картошка: насыпь ниже края и клубни поверх.</summary>
    private Node3D Картошка(Vector3 низ, Vector3 р, int зерно)
    {
        var д = new Доски(зерно, КАРТОШКА);
        д.Брус(низ + new Vector3(0, 0.12f, 0), new Vector3(р.X - 0.05f, 0.2f, р.Z - 0.05f),
               new Color("#4a3a26"));
        var клубень = new SphereMesh { Radius = 0.045f, Height = 0.075f, RadialSegments = 8, Rings = 4 };
        var кость = new RandomNumberGenerator { Seed = (ulong)(uint)зерно };
        for (int a = 0; a < 6; a++)
            for (int b = 0; b < 4; b++)
                д.Форма(клубень, низ + new Vector3(
                    -р.X / 2 + 0.07f + a * (р.X - 0.14f) / 5 + кость.RandfRange(-0.015f, 0.015f),
                    0.23f + кость.RandfRange(-0.02f, 0.02f),
                    -р.Z / 2 + 0.07f + b * (р.Z - 0.14f) / 3 + кость.RandfRange(-0.015f, 0.015f)));
        var узел = д.Меш("картошка", _дерево);
        AddChild(узел);
        return узел;
    }

    /// <summary>Банки с соленьями: у каждой своя закрутка и крышка.
    /// Места — где стоит дно банки.</summary>
    private Node3D Банки(string имя, int зерно, IEnumerable<Vector3> места)
    {
        var д = new Доски(зерно, СОЛЕНЬЯ);
        var банка = new CylinderMesh
        {
            TopRadius = 0.04f, BottomRadius = 0.042f, Height = 0.15f, RadialSegments = 10, Rings = 1,
        };
        var крышка = new CylinderMesh
        {
            TopRadius = 0.043f, BottomRadius = 0.043f, Height = 0.018f, RadialSegments = 10, Rings = 1,
        };
        foreach (var дно in места)
        {
            д.Форма(банка, дно + new Vector3(0, 0.075f, 0));
            д.Форма(крышка, дно + new Vector3(0, 0.159f, 0), new Color("#8c8a80"));
        }
        var узел = д.Меш(имя, _стекло);
        AddChild(узел);
        return узел;
    }

    // ------------------------------------------------------------ доски

    /// <summary>Стенка из досок со щелями: коробка [а, б], доски идут
    /// одна за другой вдоль оси <paramref name="ось"/> (0 — X, 1 — Y, 2 — Z).</summary>
    private static void Стенка(Доски д, Vector3 а, Vector3 б, int ось,
                               float доска = 0.11f, float щель = 0.012f)
    {
        float длина = б[ось] - а[ось];
        int n = Math.Max(1, (int)MathF.Round((длина + щель) / (доска + щель)));
        float w = (длина - (n - 1) * щель) / n;
        for (int i = 0; i < n; i++)
        {
            var от = а;
            var до = б;
            от[ось] = а[ось] + i * (w + щель);
            до[ось] = от[ось] + w;
            д.Брус((от + до) / 2, до - от);
        }
    }

    private static void Брус(Доски д, Vector3 а, Vector3 б) => д.Брус((а + б) / 2, б - а);

    /// <summary>Сплошная форма по коробке [а, б] на этом слое.</summary>
    private void Тело(Vector3 а, Vector3 б, uint слой)
    {
        var тело = new StaticBody3D
        {
            Name = $"форма_погреба{_тел++}",
            CollisionLayer = слой,
            CollisionMask = 0,
        };
        тело.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = б - а },
            Position = (а + б) / 2,
        });
        AddChild(тело);
    }

    /// <summary>
    /// Доски одной вещи — один меш: брусья и простые формы, у каждой свой
    /// цвет (цвет в вершинах, материал один). Доски в стенке не одинаковые,
    /// и оттого клетушка читается деревом, а не серой коробкой. Вершины
    /// и обход граней берутся у готовых форм Godot: нормали у них верные,
    /// и угадывать, с какой стороны у грани лицо, не надо.
    /// </summary>
    private sealed class Доски
    {
        private readonly SurfaceTool _ст = new();
        private readonly RandomNumberGenerator _кость = new();
        private readonly Color[] _тона;
        private int _вершин;

        public Доски(int зерно, Color[] тона)
        {
            _кость.Seed = (ulong)(uint)зерно;
            _тона = тона;
            _ст.Begin(Mesh.PrimitiveType.Triangles);
        }

        public void Брус(Vector3 центр, Vector3 размер, Color? цвет = null)
            => Форма(new BoxMesh { Size = размер }, центр, цвет);

        public void Форма(PrimitiveMesh форма, Vector3 где, Color? цвет = null)
        {
            var м = форма.GetMeshArrays();
            var точки = м[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var нормали = м[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var номера = м[(int)Mesh.ArrayType.Index].AsInt32Array();
            var тон = цвет ?? _тона[_кость.RandiRange(0, _тона.Length - 1)];
            // и в одном тоне доска доске не ровня: чуть светлее, чуть темнее
            if (цвет is null)
                тон = тон.Lightened(_кость.RandfRange(0f, 0.08f)).Darkened(_кость.RandfRange(0f, 0.08f));
            _ст.SetColor(тон);
            for (int i = 0; i < точки.Length; i++)
            {
                _ст.SetNormal(нормали[i]);
                _ст.AddVertex(точки[i] + где);
            }
            foreach (int н in номера)
                _ст.AddIndex(_вершин + н);
            _вершин += точки.Length;
        }

        public MeshInstance3D Меш(string имя, Material материал)
        {
            var меш = _ст.Commit();
            if (меш.GetSurfaceCount() > 0)
                меш.SurfaceSetMaterial(0, материал);
            return new MeshInstance3D { Name = имя, Mesh = меш };
        }
    }
}
