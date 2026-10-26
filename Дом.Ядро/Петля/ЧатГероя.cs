// Герой пишет в общий чат сам (ГДД 14, 19).
//
// «Реплика в общем чате действует на всех, кто ещё в сети, а не только
// на собеседника: просьба поднимает осведомлённость о нужде говорящего, паника
// заражает, а тема „подозрения“ с названным по имени соседом работает как
// наговор — названный попадает в подозреваемые при следующей краже». У соседей
// это уже есть (`Чат.daily_chat`): тему там выбирают самочувствие и бросок.
// Здесь её выбирает игрок, а что реплика делает с прочитавшими — то же самое
// (`Чат.Последствия`): своего правила нет. Дом за героя больше не пишет
// (`NPC.пишет_сам`, ставит движок).
//
// - Темы — пять тем чата (`ТЕМЫ`); слова — те же строки «чат» из `lines.json`,
//   что у соседей, годные к сегодняшнему дню (`Мир.подходящие`).
// - Подозрение называет соседа, которого выбрал игрок: по имени, если герой
//   его знает (`Знакомство`), иначе по квартире — «из кв.14 всю ночь стучали».
// - Кто прочёл — бросок от зерна мира, жильца и дня, мимо `h.rng`
//   (`Округа.Бросок`): у каждого шанс — сеть дома, как у соседских реплик.
// - Раз в день (`Телефон.написал_день`), заряд — «телефон_написать_заряд»,
//   часы — «телефон_написать_часов» (`петля.json`). Сети нет или сел — нельзя.
//
// Только у игрока: без него `Чат.daily_chat` считает, как считал, и эталон цел.

using System.Text.Json;

namespace Дом.Ядро;

public static class ЧатГероя
{
    /// <summary>Темы, как их видит игрок: ключ строк «чат» и подпись кнопки.
    /// Порядок — порядок кнопок.</summary>
    public static readonly IReadOnlyList<(string тема, string подпись)> ТЕМЫ = new[]
    {
        ("просьба", "попросить"),
        ("паника", "страшно"),
        ("подозрение", "кто это был?"),
        ("тоска", "одиноко"),
        ("быт", "о быте"),
    };

    /// <summary>Строки «чат» из `lines.json` — те же, что пишут соседи.</summary>
    public static JsonElement Шаблоны(string каталог)
    {
        using var поток = File.OpenRead(Path.Combine(каталог, "lines.json"));
        using var док = JsonDocument.Parse(поток);
        return док.RootElement.GetProperty("чат").Clone();
    }

    /// <summary>Почему сейчас не написать — или null, если можно.</summary>
    public static string? Нельзя(House h, NPC я, Телефон т, РучкиПетли ручки)
    {
        if (!я.здесь())
            return "не до чата";
        if (т.сел)
            return "телефон сел — зарядить можно дома, пока есть свет";
        if (h.network <= 0.0)
            return "сети нет — чат молчит";
        if (т.заряд < ручки["телефон_написать_заряд"])
            return $"заряда {Текст.G(Math.Floor(т.заряд))}% — не хватит";
        if (т.написал_день == h.day)
            return "сегодня ты уже писал в чат";
        return null;
    }

    /// <summary>
    /// Что можно написать на тему сегодня. Подозрение — только с названным:
    /// по имени, если имя знаешь, иначе по квартире.
    /// </summary>
    public static IReadOnlyList<string> Слова(House h, JsonElement шаблоны, string тема, bool знает_имя)
    {
        if (шаблоны.ValueKind != JsonValueKind.Object || !шаблоны.TryGetProperty(тема, out var варианты)
            || варианты.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var годные = Мир.подходящие(h, варианты);
        if (!string.Equals(тема, "подозрение", StringComparison.Ordinal))
            return годные;
        bool Имя(string s) => s.Contains("{кто}", StringComparison.Ordinal);
        bool Кв(string s) => s.Contains("{кв}", StringComparison.Ordinal);
        return годные.Where(s => знает_имя ? Имя(s) || Кв(s) : Кв(s) && !Имя(s)).ToList();
    }

    /// <summary>Что вышло: написано ли, весть герою, сама реплика, часы.</summary>
    public sealed record Написано(bool да, string весть, string? текст, double часов);

    /// <summary>
    /// Написать в чат на <paramref name="тема"/>; подозрение — о <paramref name="кого"/>.
    /// Дом трогается: те, кто прочёл, — как от соседской реплики. Зовущий
    /// тратит часы (`Написано.часов`) и делает это, пока дом спит на вопросе.
    /// </summary>
    public static Написано Написать(House h, NPC я, Телефон т, РучкиПетли ручки,
                                    JsonElement шаблоны, string тема, NPC? кого, long зерно)
    {
        if (!ТЕМЫ.Any(т_ => string.Equals(т_.тема, тема, StringComparison.Ordinal)))
            throw new ArgumentException($"нет такой темы чата: {тема}", nameof(тема));
        if (Нельзя(h, я, т, ручки) is { } нельзя)
            return new Написано(false, нельзя, null, 0.0);
        bool подозрение = string.Equals(тема, "подозрение", StringComparison.Ordinal);
        if (подозрение && (кого is null || string.Equals(кого.id, я.id, StringComparison.Ordinal) || !кого.здесь()))
            return new Написано(false, "кого назвать?", null, 0.0);
        bool знает_имя = кого is not null && Знакомство.Знает(h, я, кого);
        var слова = Слова(h, шаблоны, тема, знает_имя);
        if (слова.Count == 0)
            return new Написано(false, "на это сказать нечего", null, 0.0);

        int i = Math.Min(слова.Count - 1,
                         (int)(Округа.Бросок(зерно, "чат:" + тема, я.id, h.day) * слова.Count));
        string текст = слова[i];
        if (кого is not null)
            текст = текст.Replace("{кто}", кого.@short, StringComparison.Ordinal)
                         .Replace("{кв}", кого.apt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                  StringComparison.Ordinal);
        h.hooks.Зов_реплика(h, текст);
        h.journal.chat(я.@short, текст);

        // кто прочёл: сеть дома, у каждого свой бросок — как у соседских реплик
        var слышат = h.alive()
            .Where(o => !string.Equals(o.id, я.id, StringComparison.Ordinal)
                        && Округа.Бросок(зерно, "чат:прочёл", o.id, h.day) < h.network)
            .ToList();
        Чат.Последствия(h, я, тема, кого ?? я, слышат);
        я.день_разговора = h.day;
        т.Написал(h.day, ручки["телефон_написать_заряд"]);

        string весть = h.network < 0.5 ? "написал в чат — сеть слабая, дойдёт не до всех" : "написал в чат";
        return new Написано(true, весть, текст, ручки["телефон_написать_часов"]);
    }
}
