// Перенос house/chat.py: общий чат жильцов (GDD 19).
//
// Лежит отдельно от вывода, потому что это не вывод: каждая реплика двигает
// панику, доверие и подозрения. Иначе обрыв связи на десятый день ничего
// не менял бы, а GDD 12.5 обещает, что об этом «игрок узнаёт из чата».

using System.Text.Json;

namespace Дом.Ядро;

public static class Чат
{
    /// <summary>
    /// Общий чат жильцов (GDD 19: «основной канал общения и слухов»).
    /// Темы соответствуют разд. 14: припасы, безопасность, подозрения, личное.
    /// </summary>
    public static void daily_chat(House h, JsonElement lines)
    {
        if (h.network <= 0)
            return;
        var b = h.B;
        var rng = h.rng;
        var people = h.alive();
        if (people.Count == 0)
            return;
        int said = 0;
        // одну и ту же фразу за день дважды не пишут
        var сказанное = new HashSet<string>(StringComparer.Ordinal);
        int limit = h.network > 0.5 ? 2 : 1;
        var talkers = people.OrderByDescending(p => p.trait("общительность")).ToList();
        var чат = lines.Есть("чат") ? lines.GetProperty("чат") : default;
        foreach (var p in talkers)
        {
            if (said >= limit)
                break;
            string? key = null;
            // пороги подобраны под то, что чат живёт лишь до десятого дня
            // (GDD 19): с прежними «паника > 70» и «настроение < 35» обе темы
            // не звучали ни разу за сорок прогонов.
            //
            // Просьба стоит выше паники нарочно, и по той же причине, по какой
            // выше подозрения стоит тоска. Пока паника шла первой, «нужна еда»
            // не помещалось в чат вовсе: к тому дню, когда у человека пустой
            // шкаф, ему уже страшно. Человек с пустым шкафом пишет
            // не «мне страшно», а «дайте хоть что-нибудь»
            if (p.desperation() > 0.6)
                key = "просьба";
            else if (p.panic > 45)
                key = "паника";
            else if (h.day - p.день_беседы >= b["чат_одиночество_дней"])
                // тоска в чате — про одиночество, а не про шкалу настроения:
                // к тому дню, когда настроение падает, связи уже нет.
                // «Напишите хоть кто-нибудь» пишет тот, с кем второй день
                // никто не заговорил. И стоит она выше подозрения нарочно:
                // пока она была последней, до неё доходили дважды
                // за шестьдесят жизней
                key = "тоска";
            else if (h.incidents > 0 && rng.Chance(0.5))
                key = "подозрение";
            else if (p.mood < b["чат_тоска_настроение"])
                key = "тоска";
            else if (rng.Chance(0.35))
                key = "быт";
            if (key is null)
                continue;
            var варианты = чат.ValueKind == JsonValueKind.Object && чат.Есть(key)
                           ? чат.GetProperty(key) : default;
            var годные = (варианты.ValueKind == JsonValueKind.Array
                          ? Мир.подходящие(h, варианты)
                          : new List<string>())
                .Where(в => !сказанное.Contains(в)).ToList();
            if (годные.Count == 0)
                continue;
            string шаблон = rng.Pick(годные)!;
            string text = шаблон;
            var others = people
                .Where(o => !string.Equals(o.id, p.id, StringComparison.Ordinal)).ToList();
            var who = others.Count > 0 ? rng.Pick(others)! : p;
            if (string.Equals(key, "подозрение", StringComparison.Ordinal)
                && others.Count > 0)
                // вслух называют не случайного соседа, а того, на кого сам
                // думаешь: по злости, по недоверию и по тому, что успел о нём
                // узнать. Раньше здесь стоял ровный жребий, и Лида
                // с лояльностью 9 могла при всём доме назвать вором человека,
                // о котором ничего не знает, — а дом это запоминал и потом
                // на него же и думал
                who = rng.Weighted(others.Select(o => (o, Math.Max(0.05,
                        1.0 + p.hate.Взять(o.id, 0.0) / 15.0
                        + (5.0 - p.trust.Взять(o.id, 3.0)) * 0.5
                        + p.confidence(o.id) * 1.5
                        + o.поймали * 2.0))).ToList())!;
            text = text.Replace("{кто}", who.@short, StringComparison.Ordinal)
                       .Replace("{кв}", who.apt.ToString(
                           System.Globalization.CultureInfo.InvariantCulture),
                           StringComparison.Ordinal);
            if (!rng.Chance(0.55 + 0.04 * p.trait("общительность")))
                continue;
            h.hooks.Зов_реплика(h, text);
            h.journal.chat(p.@short, text);
            сказанное.Add(шаблон);
            said += 1;

            // --- а вот теперь то, чего у чата не было: последствия ---
            var слышат = others.Where(_ => rng.Chance(h.network)).ToList();
            if (string.Equals(key, "просьба", StringComparison.Ordinal))
                // «у меня кончается» — это заявление на весь подъезд
                foreach (var o in слышат)
                {
                    Социальное.adjust(o, p.id, aware: b["чат_осведомлённость"]);
                    Социальное.note_signal(o, p.id, "еда", 0.5, 0.3);
                    Социальное.add_panic(o, b["чат_паника_от_просьбы"]);
                }
            else if (string.Equals(key, "паника", StringComparison.Ordinal))
                foreach (var o in слышат)
                    Социальное.add_panic(o,
                        b["чат_паника"] * (0.7 + 0.6 * o.t01("вспыльчивость")));
            else if (string.Equals(key, "подозрение", StringComparison.Ordinal))
            {
                // реплика называет соседа по имени — и дом это запоминает
                if (!string.Equals(who.id, p.id, StringComparison.Ordinal))
                {
                    Социальное.judge(h, who, "воровство",
                        hate: b["чат_подозрение_ненависть"], trust: -0.4,
                        witnesses: слышат);
                    Социальное.adjust(p, who.id, hate: b["чат_подозрение_ненависть"],
                                      trust: -0.5);
                    // и те, кто это прочёл, запоминают, на кого думать. Знают
                    // об этом только они: разговор, которого сосед не слышал,
                    // на его подозрения не влияет
                    bool знает = p.знает_о_кражах();
                    foreach (var o in слышат)
                    {
                        o.memory.Add(new Память
                        {
                            день = h.day, вид = Вид.НАЗВАЛИ_ВОРОМ, кто = who.id,
                        });
                        // а «в доме крадут» они выносят отсюда, только если
                        // говорящий знает это сам, а не тычет пальцем после
                        // драки: эта реплика идёт после любого происшествия,
                        // не только кражи
                        if (знает)
                            o.memory.Add(new Память
                            {
                                день = h.day, вид = Вид.СЛЫШАЛ_О_ВОРЕ, кто = who.id,
                            });
                    }
                    h.bump("обвинений_в_чате");
                }
            }
            else if (string.Equals(key, "тоска", StringComparison.Ordinal))
                foreach (var o in слышат)
                {
                    o.mood = Util.Clamp(o.mood + b["чат_настроение"]);
                    Социальное.adjust(o, p.id, trust: b["чат_доверие"]);
                }
            else if (string.Equals(key, "быт", StringComparison.Ordinal))
                foreach (var o in слышат)
                {
                    o.mood = Util.Clamp(o.mood + b["чат_настроение"] * 0.5);
                    Социальное.adjust(o, p.id, trust: b["чат_доверие"] * 0.5);
                }
            p.день_разговора = h.day;
        }
    }
}
