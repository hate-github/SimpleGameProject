// Записанные звуки: где лежат, по чему идёт человек, как звучит шаг
// и на какой шине громкости звучит что (`Шины`, канал настроек).
//
// Записи автора лежат в `звуки/` (что откуда — `звуки/ЧИТАЙ.md`, какие
// есть — закрытый список `Дом.Экран.Слух.ЗАПИСИ`). Они нарочно понижены
// до 8–16 кГц: ретро здесь не в одной картинке.
//
// Шаг — это **один** шаг: у каждой записи удар в середине, до него
// тишина. Поэтому ходьба не крутит запись по кругу, а бьёт по шагу
// на каждые полметра с лишним пути, с места удара и с чуть другой
// высотой — так два одинаковых шага подряд не звучат одинаково.
//
// По чему идёт человек, говорит тело под ногами: мир помечает его
// (`Поверхность.Пометить`), а без пометки поверхность читается по имени
// коробки сцены. Снег — двор и сугробы; бетон — подъезд, подвал, приямок,
// пол гаража; дерево — пол своей квартиры и настил в гараже.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public static class Звуки
{
    private static readonly Dictionary<string, AudioStream?> _кэш = new(StringComparer.Ordinal);

    /// <summary>Запись по имени файла без расширения. Нет файла — null и строка
    /// в лог: без звука игра идёт дальше.</summary>
    public static AudioStream? Взять(string имя, bool петля = false)
    {
        string путь = $"res://звуки/{имя}.mp3";
        if (!_кэш.TryGetValue(путь, out var поток))
        {
            поток = ResourceLoader.Exists(путь) ? GD.Load<AudioStream>(путь) : null;
            if (поток is null)
                GD.PrintErr($"нет звука {путь}");
            _кэш[путь] = поток;
        }
        // Петлёй записи ставятся здесь, а не в настройках импорта: файлы
        // импорта в репозиторий не входят. Одна и та же запись петлёй
        // и не петлёй не бывает — вьюга, лампа и кипение только петлёй.
        if (петля && поток is AudioStreamMP3 mp3)
            mp3.Loop = true;
        return поток;
    }

    /// <summary>
    /// Разовый звук без места — то, что делает сам герой (перезарядка,
    /// топор, жарка на его печке, нож по овощу): слышно ровно так же,
    /// где бы ни стоял слушающий. Шина — «шаги и вещи». Длинную запись
    /// (<paramref name="секунд"/> больше нуля) играет куском со случайного
    /// места и гасит к концу куска.
    /// </summary>
    public static AudioStreamPlayer? Сыграть(Node куда, string имя, float громкость = 0f, float секунд = 0f)
    {
        if (Взять(имя) is not { } поток)
            return null;
        var п = new AudioStreamPlayer { Stream = поток, VolumeDb = громкость, Bus = Шины.ШАГИ, Name = "звук_" + имя };
        куда.AddChild(п);
        Пустить(п, поток, громкость, секунд);
        return п;
    }

    /// <summary>Пустить звук: целиком — и убрать, когда кончится; куском —
    /// со случайного места, погасить за полсекунды до конца куска.</summary>
    public static void Пустить(Node п, AudioStream поток, float громкость, float секунд)
    {
        double длина = поток.GetLength();
        if (секунд <= 0f || длина <= секунд + 0.5)
        {
            п.Connect("finished", Callable.From(п.QueueFree));
            п.Call("play", 0.0f);
            return;
        }
        float откуда = (float)GD.RandRange(0.0, длина - секунд - 0.2);
        п.Call("play", откуда);
        var гаснет = п.CreateTween();
        гаснет.TweenInterval(System.Math.Max(0.1, секунд - 0.5));
        гаснет.TweenProperty(п, "volume_db", громкость - 40f, 0.5);
        гаснет.TweenCallback(Callable.From(п.QueueFree));
    }

    /// <summary>Свои отзвуки (`Дом.Экран.Слух`: съел, попил) — без места,
    /// каждый через свою задержку.</summary>
    public static void Своё(Node куда, IReadOnlyList<Отзвук> отзвуки)
    {
        foreach (var о in отзвуки)
        {
            var это = о;
            if (это.через <= 0)
                Сыграть(куда, это.запись, это.дб, это.секунд);
            else
                куда.GetTree().CreateTimer(это.через).Timeout += () =>
                {
                    if (GodotObject.IsInstanceValid(куда))
                        Сыграть(куда, это.запись, это.дб, это.секунд);
                };
        }
    }

    /// <summary>
    /// Источник звука в точке мира. Затухание — движка: это вещь рядом
    /// с героем, а не звук, громкость которого считает дом.
    ///
    /// Петля — это фон (лампа, кипение, огонь), разовый звук — шаги и вещи;
    /// так звук и попадает на шину своего канала громкости. Фон не замирает,
    /// пока открыты настройки: там его и крутят, и крутить надо на слух.
    /// </summary>
    public static AudioStreamPlayer3D Точка(Node куда, string имя, Vector3 где,
                                            float громкость = 0f, float размер = 2f,
                                            float дальше_не = 25f, bool петля = false)
    {
        var п = new AudioStreamPlayer3D
        {
            Stream = Взять(имя, петля),
            Position = где,
            VolumeDb = громкость,
            UnitSize = размер,
            MaxDistance = дальше_не,
            Name = "звук_" + имя,
            Bus = петля ? Шины.ФОН : Шины.ШАГИ,
        };
        if (петля)
            п.ProcessMode = Node.ProcessModeEnum.Always;
        куда.AddChild(п);
        return п;
    }
}

/// <summary>
/// Шины громкости — по одной на канал настроек (`Дом.Экран.Громкость`).
/// Источник выбирает шину по тому, что он такое, а игрок двигает шину
/// целиком: метель, шаги и вещи, фон. Что у каждого канала есть шина,
/// сверяет консоль (раздел «настройки») — по строкам выбора ниже.
/// </summary>
public static class Шины
{
    public const string ОБЩАЯ = "Master";
    // имя старое: шину завёл ещё ЗвукUI, и на ней стоят «стены» —
    // фильтр низких частот, которым дома глушится метель
    public const string МЕТЕЛЬ = "Ветер";
    public const string ШАГИ = "Шаги";
    public const string ФОН = "Фон";

    /// <summary>Шина канала из настроек.</summary>
    public static string Канала(string канал) => канал switch
    {
        Громкость.ОБЩАЯ => ОБЩАЯ,
        Громкость.МЕТЕЛЬ => МЕТЕЛЬ,
        Громкость.ШАГИ => ШАГИ,
        Громкость.ФОН => ФОН,
        _ => throw new ArgumentException($"у канала «{канал}» нет шины"),
    };

    /// <summary>Завести шины, которых ещё нет. Зовётся до первого звука:
    /// источник, чья шина не заведена, играет в общую — мимо своей громкости.</summary>
    public static void Завести()
    {
        foreach (var имя in new[] { МЕТЕЛЬ, ШАГИ, ФОН })
        {
            if (AudioServer.GetBusIndex(имя) >= 0)
                continue;
            AudioServer.AddBus();
            int шина = AudioServer.BusCount - 1;
            AudioServer.SetBusName(шина, имя);
            AudioServer.SetBusSend(шина, ОБЩАЯ);
            if (имя == МЕТЕЛЬ)
                AudioServer.AddBusEffect(шина, new AudioEffectLowPassFilter
                {
                    CutoffHz = ЗвукUI.ЧАСТОТА_ДОМА,
                });
        }
    }

    /// <summary>Стены: фильтр низких частот на шине метели.</summary>
    public static AudioEffectLowPassFilter Стены()
    {
        Завести();
        return (AudioEffectLowPassFilter)AudioServer.GetBusEffect(
            AudioServer.GetBusIndex(МЕТЕЛЬ), 0);
    }

    /// <summary>Громкость каналов из настроек. Ноль — не «очень тихо»,
    /// а выключенная шина.</summary>
    public static void Применить(Настройки н)
    {
        Завести();
        foreach (var к in Громкость.ВСЕ)
        {
            int шина = AudioServer.GetBusIndex(Канала(к.ключ));
            double доля = н.Уровень(к.ключ);
            AudioServer.SetBusVolumeDb(шина, (float)Громкость.Децибелы(доля));
            AudioServer.SetBusMute(шина, доля <= Громкость.ТИШЕ_НЕКУДА);
        }
    }
}

/// <summary>По чему идёт человек.</summary>
public static class Поверхность
{
    public const string СНЕГ = "снег";
    public const string БЕТОН = "бетон";
    public const string ДЕРЕВО = "дерево";

    // Ключ метаданных — латиницей: другие Godot не принимает
    // («Invalid metadata identifier»), и пометка молча не ставится.
    private const string КЛЮЧ = "surface";

    /// <summary>Поверхность коробки сцены — по началу её имени.</summary>
    public static string ПоИмени(string имя)
    {
        if (имя.StartsWith("снег", StringComparison.Ordinal)
            || имя.StartsWith("пол_крыльцо", StringComparison.Ordinal)   // утоптанный пятачок
            || имя.StartsWith("сугроб", StringComparison.Ordinal))
            return СНЕГ;
        if (имя.StartsWith("доска", StringComparison.Ordinal))
            return ДЕРЕВО;
        return БЕТОН;
    }

    public static void Пометить(Node тело, string что) => тело.SetMeta(КЛЮЧ, что);

    /// <summary>По чему стоит тот, кто стоит на этом теле.</summary>
    public static string Чья(GodotObject? тело)
    {
        if (тело is not Node узел)
            return БЕТОН;
        return узел.HasMeta(КЛЮЧ) ? узел.GetMeta(КЛЮЧ).AsString()
                                  : ПоИмени(узел.Name.ToString());
    }
}
