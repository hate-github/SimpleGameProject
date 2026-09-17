// Записанные звуки: где лежат, по чему идёт человек и как звучит шаг.
//
// Записи автора лежат в `звуки/` (что откуда — `звуки/ЧИТАЙ.md`). Они
// нарочно понижены до 11–16 кГц: ретро здесь не в одной картинке.
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

    /// <summary>Источник звука в точке мира. Затухание — движка: это вещь
    /// рядом с героем, а не звук, громкость которого считает дом.</summary>
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
        };
        куда.AddChild(п);
        return п;
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
