// Перенос house/actions.py: обмен (GDD 18).
//
// Главное здесь: инициатор не знает правды о чужом складе. И что у соседа
// есть, и насколько соседу это нужно, он прикидывает по своей оценке — той
// самой, ради которой построены шум, запах и слухи.

namespace Дом.Ядро;

public static partial class Действия
{
    // деньги здесь наравне с банками тушёнки — в этом весь смысл: пока их
    // берут, у соседа можно купить, а не выпросить (GDD 18). Патроны в этом
    // списке не мелочь: они лежат в вылазке, их выносят при краже, но выменять
    // их было нельзя, и за 40 жизней 114 штук осело у людей, которым нечем
    // стрелять
    public static readonly IReadOnlyList<string> TRADABLE = new[]
        { "еда", "топливо", "лекарства", "материалы", "вода", "деньги", "патроны" };

    /// <summary>
    /// Ключ памяти о неудачной мене: «он не взял у меня это за то». Пара,
    /// а не один ресурс: «не отдал еду за воду» и «не отдал еду за дрова» —
    /// разные ответы, и запоминать их надо порознь.
    /// </summary>
    public static string ключ_отказа(string give, string get_)
        => $"не_взял_{give}_за_{get_}";

    /// <summary>Обмен по GDD 18: сделка идёт, если обе стороны считают
    /// её выгодной.</summary>
    public static double _trade_score(House h, NPC a, NPC b_npc)
    {
        var deal = find_deal(h, a, b_npc);
        if (deal is null)
            return 0.0;
        var (give, get_) = deal.Value;
        double gain = Социальное.value_of(a, get_.res, get_.n)
                      - Социальное.value_of(a, give.res, give.n);
        return Util.Clamp(gain * 1.2 + a.trust.Взять(b_npc.id, 3.0) * 0.2
                          - a.боится(b_npc.id) * h.B["страх_вес_контакта"], 0.0, 9.0);
    }

    /// <summary>
    /// Найти сделку, которую ОБЕ стороны сочтут выгодной (GDD 18).
    ///
    /// Стоимость единицы считается по одному разу на человека: цена линейна
    /// по количеству, а количество в сделке всегда единица. Это самое горячее
    /// место симуляции — через него шло 60 % всего времени.
    /// </summary>
    public static ((string res, double n) give, (string res, double n) get_)?
        find_deal(House h, NPC a, NPC b_npc)
    {
        var b = h.B;
        // к тому, кто отказал трижды подряд, какое-то время не ходят ни с чем:
        // человек перебирал пары ресурсов у одной и той же двери и получал
        // «нет» по восемьдесят раз за жизнь
        if (h.day - a.ask_record(b_npc.id).мена_закрыт < b["мена_обида_дней"])
            return null;
        var va = new Dictionary<string, double>(StringComparer.Ordinal);
        var vb = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var r in TRADABLE)
        {
            va[r] = Социальное.value_of(a, r, 1.0);
            // как, по мнению a, живёт b_npc — а не как он живёт на самом деле
            vb[r] = Социальное.value_of(b_npc, r, 1.0,
                days: Социальное.believed_days(a, b_npc, r), глазами: a);
        }
        (double score, (string, double) give, (string, double) get_)? best = null;
        foreach (var give in TRADABLE)
        {
            if (a.stock.Взять(give, 0.0) < 2)
                continue;
            foreach (var get_ in TRADABLE)
            {
                if (string.Equals(get_, give, StringComparison.Ordinal))
                    continue;
                // предлагают за то, что у соседа, по-твоему, есть
                if (a.believed(b_npc.id, get_) < b["обмен_порог_веры"])
                    continue;
                // и по знанию, а не по догадке: основанием считается след
                // в памяти — видел с мешками, учуял, подсмотрел шкаф,
                // догадался или ему сказали. Деньги — исключение: наличные
                // не пахнут, не гремят и не видны с площадки, зато про них
                // и не надо узнавать
                if (!string.Equals(get_, "деньги", StringComparison.Ordinal)
                    && !a.знаю_про(b_npc.id, get_, h.day, b["мена_знание_дней"]))
                    continue;
                // и не за тем, за чем на днях уже ходил зря
                if (h.day - a.ask_record(b_npc.id).нет.Взять(get_, -99.0)
                    < b["мена_память_дней"])
                    continue;
                // и не с тем, от чего он на днях уже отказался. 83 % всех
                // отказов в мене были повтором того же самого
                if (h.day - a.ask_record(b_npc.id).мена.Взять(ключ_отказа(give, get_), -99.0)
                    < b["мена_память_дней"])
                    continue;
                double n = 1.0;
                double mine_gain = va[get_] - va[give];
                double their_gain = vb[give] - vb[get_];
                if (mine_gain > 0.2 && their_gain > 0.2)
                {
                    double score = mine_gain + their_gain;
                    if (best is null || score > best.Value.score)
                        best = (score, (give, n), (get_, n));
                }
            }
        }
        return best is null ? null : (best.Value.give, best.Value.get_);
    }
}
