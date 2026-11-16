// Записка героя на своей двери (импровизация 17.11.2026 — новая механика,
// автор: «чтобы игра ощущалась живее, добавились новые механики»).
//
// Соседи пишут на дверях давно (`Дом.Экран.Беседы.Записки`): «болеем»,
// «мы у Вали», крест мелом. Теперь может и герой — одно из четырёх, — и это
// не украшение: соседи, прочитав, решают иначе. Поправка ложится на оценку
// дела соседа, у которого цель — тот, у кого записка (`Действия.gather`):
//
//   · «не стучать» — реже идут поговорить, просить и меняться;
//   · «болею» — то же, вдвое сильнее: заразы боятся больше вежливости;
//   · «меняю» — чаще приходят меняться;
//   · «помогу» — чаще приходят просить, и разговор теплее.
//
// Записку можно оставить и на чужой двери (`СОСЕДУ`): позвать хозяина
// поговорить, меняться или предложить помощь. Её читает он один, и только
// к герою он идёт охотнее — тем же путём, поправкой к оценке дела
// (`Поправка_соседу`); висит записка день, когда написана, и следующий.
//
// Отнять, подкараулить, обворовать записка не мешает: кто идёт за чужим,
// табличек не читает. Записка есть только у игрока (`NPC.записка` мимо снимка
// и сейва дома, в сохранении игры — «игрок»): у соседей её нет, и прогон без
// игрока считается бит в бит как раньше. Числа первые попавшиеся — подбирать
// игрой.

namespace Дом.Ядро;

/// <summary>Записка героя на чужой двери: какая (`ЗапискаГероя.СОСЕДУ`)
/// и до какого дня висит.</summary>
public sealed record ЗапискаСоседу(string вид, int до_дня);

public static class ЗапискаГероя
{
    /// <summary>Что можно написать на своей двери.</summary>
    public static readonly IReadOnlyList<string> ВИДЫ = new[] { "не_стучать", "болею", "меняю", "помогу" };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ПОПРАВКИ =
        new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal)
        {
            ["не_стучать"] = new Dictionary<string, double>(StringComparer.Ordinal)
                { ["разговор"] = -1.5, ["попросить"] = -1.5, ["обмен"] = -1.0 },
            ["болею"] = new Dictionary<string, double>(StringComparer.Ordinal)
                { ["разговор"] = -2.5, ["попросить"] = -2.5, ["обмен"] = -2.0 },
            ["меняю"] = new Dictionary<string, double>(StringComparer.Ordinal)
                { ["обмен"] = 1.5 },
            ["помогу"] = new Dictionary<string, double>(StringComparer.Ordinal)
                { ["попросить"] = 1.5, ["разговор"] = 0.5 },
        };

    /// <summary>Что можно написать на чужой двери: позвать хозяина к себе.</summary>
    public static readonly IReadOnlyList<string> СОСЕДУ = new[] { "поговорить", "обмен", "помогу" };

    /// <summary>Сколько дней после того, как написана, висит записка на чужой двери.</summary>
    public const int СОСЕДУ_ДНЕЙ = 1;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> СОСЕДУ_ПОПРАВКИ =
        new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal)
        {
            ["поговорить"] = new Dictionary<string, double>(StringComparer.Ordinal) { ["разговор"] = 2.0 },
            ["обмен"] = new Dictionary<string, double>(StringComparer.Ordinal) { ["обмен"] = 2.0 },
            ["помогу"] = new Dictionary<string, double>(StringComparer.Ordinal) { ["попросить"] = 2.0 },
        };

    /// <summary>На сколько сдвигается оценка дела <paramref name="ключ"/> соседа
    /// <paramref name="кто"/>, идущего к герою <paramref name="герой"/>: записка героя
    /// на его собственной двери, если она ещё висит в день <paramref name="день"/>.
    /// Нет записки — ноль.</summary>
    public static double Поправка_соседу(NPC герой, string кто, int день, string ключ)
        => герой.записки_соседям is { } все && все.TryGetValue(кто, out var з) && день <= з.до_дня
           && СОСЕДУ_ПОПРАВКИ.TryGetValue(з.вид, out var т) && т.TryGetValue(ключ, out var п) ? п : 0.0;

    /// <summary>На сколько сдвигается оценка дела <paramref name="ключ"/> соседа,
    /// идущего к двери с запиской <paramref name="записка"/>; без записки — ноль.</summary>
    public static double Поправка(string? записка, string ключ)
        => записка is not null && ПОПРАВКИ.TryGetValue(записка, out var т) && т.TryGetValue(ключ, out var п) ? п : 0.0;
}
