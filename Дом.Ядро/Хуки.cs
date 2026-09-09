// Перенос house/hooks.py: наблюдатели.
//
// Договор наблюдателя тот же, что в прототипе: он смотрит и записывает,
// но ничего в доме не меняет и не трогает `h.rng`. Вызов хука — не вызов
// случайности: прогон с пустыми списками и с полными один и тот же
// до последнего слова, и эталон это проверяет. Он же защищает движок
// от случайного влияния на симуляцию (навык godot-boundary).
//
// В Python точки заведены динамически (`зов("on_имя", …)`), здесь — полями
// с типами, и имена точек оставлены прежними. Точки добавляются вместе
// со своими модулями: пока перенесена модель, живут те, у которых уже
// есть зовущий.

namespace Дом.Ядро;

/// <summary>
/// Точки, в которых дом зовёт наблюдателей, и кто в них подписан.
/// <c>on_…</c> зовётся перед функцией домена с её аргументами,
/// <c>after_…</c> — после, с теми же аргументами и итогом.
/// </summary>
public sealed class Хуки
{
    /// <summary>(h) — день кончился, всё записано.</summary>
    public List<Action<House>> on_day { get; } = new();

    /// <summary>(h, Событие) — крупное событие дома (ВидСобытия).</summary>
    public List<Action<House, Событие>> on_событие { get; } = new();

    /// <summary>(h, текст) — реплика быта или чата, до подстановки рода.</summary>
    public List<Action<House, string>> on_реплика { get; } = new();

    /// <summary>(a, b_id, trust, hate, aware, страх) — до затухания:
    /// наблюдатель мерит намерение, а не остаток.</summary>
    public List<Action<NPC, string, double, double, double, double>> on_adjust { get; } = new();

    /// <summary>(h, npc) — сбор вариантов начался.</summary>
    public List<Action<House, NPC>> on_gather { get; } = new();

    /// <summary>(h, npc, корзина) — сбор кончился, до порога и выбора.</summary>
    public List<Action<House, NPC, Корзина>> after_gather { get; } = new();

    /// <summary>(h, npc, dur, м, спутник) — вылазка (street._outing).</summary>
    public List<Action<House, NPC, double, Место, NPC?>> on_outing { get; } = new();

    /// <summary>(…, итог) — вылазка кончилась.</summary>
    public List<Action<House, NPC, double, Место, NPC?>> after_outing { get; } = new();

    /// <summary>(h, кто, умерший).</summary>
    public List<Action<House, NPC, NPC>> on_узнал_о_смерти { get; } = new();

    /// <summary>(h, кто, умерший, итог) — итог: было ли это новостью.</summary>
    public List<Action<House, NPC, NPC, bool>> after_узнал_о_смерти { get; } = new();

    internal void Зов_day(House h)
    {
        foreach (var f in on_day)
            f(h);
    }

    internal void Зов_событие(House h, Событие e)
    {
        foreach (var f in on_событие)
            f(h, e);
    }

    internal void Зов_реплика(House h, string текст)
    {
        foreach (var f in on_реплика)
            f(h, текст);
    }

    internal void Зов_adjust(NPC a, string b_id, double trust, double hate,
                             double aware, double страх)
    {
        foreach (var f in on_adjust)
            f(a, b_id, trust, hate, aware, страх);
    }

    internal void Зов_gather(House h, NPC npc)
    {
        foreach (var f in on_gather)
            f(h, npc);
    }

    internal void Зов_после_gather(House h, NPC npc, Корзина корзина)
    {
        foreach (var f in after_gather)
            f(h, npc, корзина);
    }

    internal void Зов_outing(House h, NPC npc, double dur, Место м, NPC? спутник)
    {
        foreach (var f in on_outing)
            f(h, npc, dur, м, спутник);
    }

    internal void Зов_после_outing(House h, NPC npc, double dur, Место м, NPC? спутник)
    {
        foreach (var f in after_outing)
            f(h, npc, dur, м, спутник);
    }

    internal void Зов_узнал_о_смерти(House h, NPC кто, NPC умерший)
    {
        foreach (var f in on_узнал_о_смерти)
            f(h, кто, умерший);
    }

    internal void Зов_после_узнал_о_смерти(House h, NPC кто, NPC умерший, bool итог)
    {
        foreach (var f in after_узнал_о_смерти)
            f(h, кто, умерший, итог);
    }
}

/// <summary>
/// Реплика обычной жизни (lines.json, «быт»): текст с родовыми формами,
/// условие показа и занятие, которым это становится в ленте дня.
/// </summary>
