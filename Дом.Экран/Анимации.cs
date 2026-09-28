// Анимации (задание автора, п. 21): имена, предмет и что когда играть.
//
// Настоящих анимаций у проекта нет — их сделает автор. Здесь то, что
// им понадобится: закрытый список движений с понятными именами (как
// в задании — Idle, Walk, Run, Interact, Loot, Attack, BreakLock, UseTool,
// Equip, Unequip, Injured, Restrained), движение с предметом
// (`BreakLock(Crowbar)`) и порядок, в котором ищется клип: сперва
// `BreakLock_Crowbar`, потом просто `BreakLock`. Нет ни того, ни другого —
// движок играет заглушку (`Дом.Годот.Аниматор`). Заменить заглушку
// настоящей анимацией — положить клип с таким именем в модель; кода
// менять не надо.
//
// Имена — латиницей и именно такие, нарочно: это не слова игры, а имена
// клипов, которые автор будет набирать в своём редакторе анимаций.
//
// Что когда играть, решает этот слой, без движка, — и потому проверяется
// из консоли: держат — Restrained, ранен — Injured, идёт дело — его
// движение с его инструментом, идёт — Walk, стоит — Idle.
//
// **Клипы людей пришли из Mixamo** (28.09.2026, `Дом.Годот/модели/люди`):
// `ЛЮДИ` — какой файл каким клипом игры стал. Один файл бывает несколькими
// клипами: удар сверху — это и топор, и дубина, и удар по замку. Вверх
// по лестнице — свой шаг (`Walk_Stairs`): лестница здесь «предмет»
// в имени клипа, и нет клипа — идёт обычный шаг. Раздел консоли «люди»
// сверяет, что имя клипа — движение из списка (с предметом из данных
// или лестницей), что файл лежит и что всё, что делает сосед, играется
// клипом, а не заглушкой.

using Дом.Ядро;

namespace Дом.Экран;

/// <summary>Движения — имена клипов. Порядок и имена — из задания автора.</summary>
public enum Анимация
{
    Idle, Walk, Run, Interact, Loot, Attack, BreakLock, UseTool, Equip, Unequip, Injured, Restrained,
}

/// <summary>Движение с предметом: <c>BreakLock(Crowbar)</c>. Ассет — имя
/// предмета у моделей автора (`data/инструменты.json`), или null.</summary>
public sealed record Движение(Анимация что, string? ассет = null)
{
    public override string ToString() => ассет is null ? что.ToString() : $"{что}({ассет})";

    /// <summary>Разовое — сыграть раз и вернуться к тому, что было:
    /// достать, убрать, толкнуть дверь. Прочие идут, пока длится состояние.</summary>
    public bool разовое => что is Анимация.Interact or Анимация.Equip or Анимация.Unequip;
}

/// <summary>Клип человека: имя клипа игры, файл Mixamo без расширения
/// и шаг ли это — клип ходьбы, из которого вынимается ход вперёд
/// (фигуру ведёт навигация) и по которому подгоняется скорость ног.</summary>
public sealed record КлипЧеловека(string клип, string файл, bool шаг = false);

public static class Анимации
{
    /// <summary>Вверх по лестнице: «предмет» в имени клипа шага
    /// (`Walk_Stairs`, `Run_Stairs`).</summary>
    public const string ЛЕСТНИЦА = "Stairs";

    /// <summary>Клипы людей из Mixamo: имя клипа игры ← файл.</summary>
    public static readonly IReadOnlyList<КлипЧеловека> ЛЮДИ = new КлипЧеловека[]
    {
        new("Idle", "Idle"),
        new("Walk", "Walking", шаг: true),
        new("Run", "Running", шаг: true),
        new($"Walk_{ЛЕСТНИЦА}", "Ascending Stairs", шаг: true),
        new($"Run_{ЛЕСТНИЦА}", "Running Up Stairs", шаг: true),
        new("Injured", "Injured Walking", шаг: true),
        // руками — кулаком; стволом — с плеча; прочим — сверху вниз
        new("Attack", "Punching"),
        new("Attack_Rifle", "Firing Rifle"),
        new("Attack_Shotgun", "Firing Rifle"),
        new("Attack_Pistol", "Firing Rifle"),
        new("Attack_Axe", "Standing Melee Attack Downward"),
        new("Attack_Club", "Standing Melee Attack Downward"),
        new("Attack_Knife", "Standing Melee Attack Downward"),
        new("Attack_Crowbar", "Standing Melee Attack Downward"),
        new("Attack_Hammer", "Standing Melee Attack Downward"),
        // по замку и разбор квартиры — тем же ударом сверху
        new("BreakLock", "Standing Melee Attack Downward"),
        new("UseTool", "Standing Melee Attack Downward"),
        // полез внутрь: ящики, чужой шкаф, тело
        new("Loot", "Looking Through Files Low"),
    };

    /// <summary>Какие клипы искать для движения — по порядку: с предметом,
    /// потом без него.</summary>
    public static IReadOnlyList<string> Клипы(Движение д)
        => д.ассет is { Length: > 0 } а
           ? new[] { $"{д.что}_{а}", д.что.ToString() }
           : new[] { д.что.ToString() };

    /// <summary>
    /// Что делает герой. Положение важнее всего: держат — вырывается,
    /// без сознания и ранен — Injured. Потом дело (взлом, обыск — со своим
    /// движением и инструментом). Потом ноги.
    /// </summary>
    public static Движение Героя(Положение положение, Движение? дело, bool идёт)
        => положение switch
        {
            Положение.СКРУТИЛИ => new Движение(Анимация.Restrained),
            Положение.БЕЗ_СОЗНАНИЯ => new Движение(Анимация.Injured),
            _ when дело is not null => дело,
            Положение.РАНЕН => new Движение(Анимация.Injured),
            _ => new Движение(идёт ? Анимация.Walk : Анимация.Idle),
        };

    /// <summary>Движение долгого дела: взлом — BreakLock тем инструментом,
    /// обыск — Loot.</summary>
    public static Движение Ломать(Инструмент инструмент) => new(Анимация.BreakLock, инструмент.ассет);

    public static Движение Обыскивать() => new(Анимация.Loot);

    /// <summary>
    /// Что делает сосед. Идёт — Walk, спешит (на стук) — Run. Стоит —
    /// по делу (дневному: хроника пишет только их): отнять — Attack,
    /// ломает чужую дверь днём — BreakLock тем, что в руках, разбирает
    /// пустую квартиру — UseTool, возится с телом — Loot; иначе — Idle
    /// с тем, что в руках.
    /// </summary>
    public static Движение Соседа(string ключ, Руки руки, bool идёт, bool спешит = false)
    {
        if (идёт)
            return new Движение(спешит ? Анимация.Run : Анимация.Walk);
        return ключ switch
        {
            "отнять" => new Движение(Анимация.Attack, руки.ассет),
            "кража_днём" when !руки.пусты => new Движение(Анимация.BreakLock, руки.ассет),
            "разбор" when !руки.пусты => new Движение(Анимация.UseTool, руки.ассет),
            "тело" => new Движение(Анимация.Loot),
            _ => new Движение(Анимация.Idle, руки.ассет),
        };
    }
}
