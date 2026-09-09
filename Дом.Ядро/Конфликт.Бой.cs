// Перенос house/conflict.py, строки 603–780: бой, потасовка и засада.
//
// GDD 17: бой намеренно простой и смертельный; численное превосходство
// решает; угроза оружием часто ценнее выстрела.

namespace Дом.Ядро;

public static partial class Конфликт
{
    /// <summary>Чем кончился бой.</summary>
    public enum ИсходБоя { A, B, НИЧЬЯ }

    /// <summary>
    /// Короткий и смертельный бой (GDD 17).
    ///
    /// Численное превосходство решает объёмом ударов, а не поправкой
    /// к меткости: бьёт каждый, кто пришёл. Пока за раунд от стороны бил
    /// ровно один человек, трое голыми руками забивали одного в 0.4% боёв —
    /// при том что документ обещает «два-три попадания убивают любого».
    /// </summary>
    public static ИсходБоя fight(House h, IReadOnlyList<NPC> side_a, IReadOnlyList<NPC> side_b,
                                 string place = "", string reason = "")
    {
        var b = h.B;
        var ранены_сейчас = new HashSet<string>(StringComparer.Ordinal);
        int rounds = 0;
        while (rounds < 4)
        {
            rounds += 1;
            var a_alive = side_a.Where(p => p.alive && p.health > 0).ToList();
            var b_alive = side_b.Where(p => p.alive && p.health > 0).ToList();
            if (a_alive.Count == 0 || b_alive.Count == 0)
                break;
            double pa = Util.Sum(a_alive.Select(p => p.power()));
            double pb = Util.Sum(b_alive.Select(p => p.power()));
            // кто попадает в этом раунде: бросок у каждого бойца свой
            var удары = new List<(NPC боец, List<NPC> по)>();
            foreach (var (att, dfn, pw_att, pw_dfn) in new[]
                     { (a_alive, b_alive, pa, pb), (b_alive, a_alive, pb, pa) })
            {
                if (att.Count == 0 || dfn.Count == 0)
                    continue;
                double hit_p = Util.Clamp(
                    0.35 + 0.4 * (pw_att / Math.Max(0.3, pw_att + pw_dfn)), 0.15, 0.9);
                foreach (var боец in att)
                    if (h.rng.Chance(hit_p))
                        удары.Add((боец, dfn));
            }
            foreach (var (shooter, поПоле) in удары)
            {
                var dfn = поПоле.Where(x => x.alive && x.health > 0).ToList();
                if (dfn.Count == 0)
                    break;
                var victim = h.rng.Pick(dfn)!;
                bool gun = Таблицы.ОГНЕСТРЕЛ.Contains(shooter.weapon)
                           && shooter.stock.Взять("патроны", 0.0) > 0;
                if (gun)
                {
                    shooter.stock["патроны"] = shooter.stock.Взять("патроны", 0.0) - 1;
                    h.stats["израсходовано_патроны"] =
                        h.stats.Взять("израсходовано_патроны", 0.0) + 1;
                    Социальное.emit(h, shooter, 5, "выстрел", night: true);
                    Социальное.house_shock(h, panic: b["паника_от_выстрела"], mood: -6);
                    h.bump("выстрелов");
                }
                // чем убили — тем дом и будет это помнить: выстрел пугает
                // иначе, чем кулаки. Ружьё без патронов в драке не оружие,
                // а палка
                var чем = (gun || !Таблицы.ОГНЕСТРЕЛ.Contains(shooter.weapon))
                          ? shooter.weapon : Оружие.НЕТ;
                double dmg = h.rng.Uni(b["бой_урон_мин"], b["бой_урон_макс"])
                             * (gun ? 1.6 : 1.0);
                victim.health = Util.Clamp(victim.health - dmg);
                if (victim.health <= 0)
                {
                    shooter.bump("убийств");
                    h.bump("убийств");
                    h.note($"{shooter.@short} {Util.Vb(shooter.sex, "убил")} "
                           + $"{victim.form("acc")}");
                    умер(h, victim,
                         reason.Length > 0
                            ? $"{Util.Vb(victim.sex, "убит")} в драке ({reason})"
                            : Util.Vb(victim.sex, "убит") + " в драке",
                         killer: shooter, оружие: чем, свидетели: весь_дом(h),
                         строка: $"{shooter.@short} {Util.Vb(shooter.sex, "убил")} "
                                 + $"{victim.form("acc")}. {place}");
                }
                else
                {
                    string injury = gun ? "огнестрел"
                        : h.rng.Pick(new[] { "ушиб руки", "порез руки", "перелом ноги" })!;
                    victim.injuries.Add(injury);
                    ранены_сейчас.Add(victim.id);
                    h.journal.line($"{victim.@short} {Util.Vb(victim.sex, "получил")} "
                                   + $"{injury} ({shooter.@short}). {place}", 1);
                    double добьёт = gun ? b["бой_шанс_смерти_огнестрел"]
                                        : b["бой_шанс_смерти_холодное"];
                    // третье попадание почти всегда последнее (GDD 17)
                    добьёт *= 1.0 + 0.6 * Math.Max(0, victim.injuries.Count - 2);
                    if (h.rng.Chance(добьёт))
                        умер(h, victim,
                             Util.Vb(victim.sex, "убит") + (gun ? " выстрелом" : " в драке"),
                             killer: shooter, оружие: чем, свидетели: весь_дом(h),
                             строка: $"{victim.@short} не {Util.Vb(victim.sex, "дожил")} "
                                     + "до утра.");
                }
            }
            // мораль: получил — чаще всего выходит из драки (GDD 17: numbers
            // decide, но никто не бьётся до последнего за банку тушёнки)
            a_alive = side_a.Where(p => p.alive && p.health > 0).ToList();
            b_alive = side_b.Where(p => p.alive && p.health > 0).ToList();
            if (a_alive.Count == 0)
                return ИсходБоя.B;
            if (b_alive.Count == 0)
                return ИсходБоя.A;
            foreach (var (side, other, tag) in new[]
                     { (b_alive, a_alive, ИсходБоя.A), (a_alive, b_alive, ИсходБоя.B) })
            {
                // раненым считается тот, кому досталось СЕЙЧАС, а не тот,
                // у кого ушиб с прошлой недели: старые травмы уже в power()
                var hurt = side.Where(p => ранены_сейчас.Contains(p.id)).ToList();
                if (hurt.Count == 0)
                    continue;
                double nerve = Util.Sum(side.Select(p => p.t01("храбрость"))) / side.Count;
                nerve += 0.2 * (side.Count - other.Count) - 0.25 * hurt.Count / side.Count;
                if (h.rng.Chance(Util.Clamp(b["бой_порог_морали"] - nerve * 0.6, 0.1, 0.9)))
                    return tag;
            }
        }
        return ИсходБоя.НИЧЬЯ;
    }

    /// <summary>
    /// Потасовка из-за пакета: не бой из GDD 17, а короткая свалка.
    /// Кто-то получает по рёбрам, кто-то отпускает сумку. Насмерть — только
    /// если в ход пошло оружие, и то редко.
    /// </summary>
    public static bool scuffle(House h, NPC a, NPC b_npc, string place = "")
    {
        var (strong, weak) = a.power() >= b_npc.power() ? (a, b_npc) : (b_npc, a);
        weak.injuries.Add(h.rng.Pick(new[] { "ушиб", "порез" })!);
        weak.health = Util.Clamp(weak.health - h.rng.Uni(8, 18));
        if (h.rng.Chance(0.35))
        {
            strong.injuries.Add("ушиб");
            strong.health = Util.Clamp(strong.health - h.rng.Uni(4, 10));
        }
        Социальное.emit(h, a, 4, "ссора", night: false);
        // `place` до сих пор принимался и не использовался: возились всегда
        // «на площадке», даже когда свалка шла в комнате, где один из двоих спал
        string где = place.Length > 0 ? $"в {place}" : "на площадке";
        h.journal.line($"Возились {где}. {weak.@short} {Util.Vb(weak.sex, "ушёл")} "
                       + "с разбитым лицом.", 1);
        // проигравший теперь знает, чем это кончается, и с кем
        Социальное.испугался(h, weak, strong, h.B["страх_за_насилие"]);
        Социальное.увидел_оружие(h, weak, strong);
        // и остаётся без того, чем мог бы ответить в следующий раз. Это
        // единственное место, где насилие меняет расклад сил надолго,
        // а не на один вечер
        var отнял = отнять_оружие(h, weak, strong, h.B["оружие_после_драки"]);
        if (отнял is not null)
            h.journal.line($"   {strong.@short} {Util.Vb(strong.sex, "забрал")} "
                + $"{(Таблицы.ОРУЖИЕ_ВИН.TryGetValue(отнял.Value, out var в) ? в : отнял.Value.Текст())} "
                + $"{weak.form("gen")}.", 2);
        return ReferenceEquals(strong, a);
    }

    /// <summary>Чем кончилась засада.</summary>
    public enum ИсходЗасады { УБИТ, ОТБИЛСЯ, ОБОБРАН }

    /// <summary>
    /// Он дождался её на площадке (GDD 16, 17).
    ///
    /// Не осада и не случайная встреча: человек сидел здесь три часа именно
    /// ради этого. Отсюда и разница с обычным отъёмом — врасплох, и потому
    /// чаще получается; и не только за пакет: за ключи от квартиры, а если
    /// счёты достаточно тяжелы и он сильнее — то и насмерть.
    /// </summary>
    public static ИсходЗасады засада(House h, NPC кто, NPC жертва)
    {
        var b = h.B;
        h.bump("засад_сработало");
        Социальное.обидели(h, жертва, кто, b["обида_за_засаду"]);
        Социальное.встретились(h, кто, жертва);
        // врасплох: жертва не успевает ни развернуться, ни позвать
        double сила = кто.power() * b["засада_врасплох"];
        double ненависть = кто.hate.Взять(жертва.id, 0.0);
        bool насмерть = ненависть >= b["засада_насмерть_злость"]
                        && сила > жертва.power() * b["засада_насмерть_превосходство"]
                        && кто.weapon != Оружие.НЕТ
                        && h.rng.Chance(b["засада_насмерть_шанс"]);
        if (насмерть)
        {
            // не «убил», а «бросился»: дальше решает бой, и на площадке он
            // может кончиться в любую сторону. Врасплох — преимущество,
            // а не приговор
            h.journal.line($"{кто.@short} {Util.Vb(кто.sex, "ждал")} {жертва.form("acc")} "
                           + $"на площадке и не {Util.Vb(кто.sex, "дал")} даже "
                           + "развернуться.", 2);
            Социальное.переступил(h, кто, "убить_соседа");
            h.bump("покушений_на_соседа");
            fight(h, new[] { кто }, new[] { жертва }, place: "на площадке",
                  reason: "ждал у двери");
            Социальное.judge(h, кто, "насилие", hate: 25.0, trust: -4.0,
                             witnesses: new[] { жертва }, участники: new[] { жертва });
            return жертва.alive ? ИсходЗасады.ОТБИЛСЯ : ИсходЗасады.УБИТ;
        }
        ИсходЗасады исход;
        if (сила > жертва.power() * b["засада_превосходство"] || h.rng.Chance(0.7))
        {
            var moved = take_carried(h, жертва, кто, limit: b["отъём_максимум"]);
            h.journal.line($"{кто.@short} {Util.Vb(кто.sex, "вышел")} из-за угла навстречу "
                           + $"{жертва.form("dat")}: {_fmt(moved)}.", 2);
            жертва.mood = Util.Clamp(жертва.mood - 16);
            жертва.panic = Util.Clamp(жертва.panic + 20);
            исход = ИсходЗасады.ОБОБРАН;
        }
        else
        {
            h.journal.line($"{кто.@short} {Util.Vb(кто.sex, "ждал")} на площадке, но "
                           + $"{жертва.@short} не {Util.Vb(жертва.sex, "отдал")} ничего.", 2);
            scuffle(h, кто, жертва, place: "на площадке");
            исход = ИсходЗасады.ОТБИЛСЯ;
        }
        // ключи — то, ради чего на площадке и ждут: они дороже банки
        if (жертва.guests.Count == 0 && string.IsNullOrEmpty(жертва.living_with)
            && h.rng.Chance(b["засада_ключи_шанс"]))
        {
            кто.ключи.Add(жертва.apt);
            h.journal.line($"   Ключи от кв.{жертва.apt} {Util.Vb(кто.sex, "забрал")} "
                           + "тоже.", 2);
            h.bump("ключей_отнято");
            foreach (string kid in жертва.ключи_кладовых.OrderBy(x => x, StringComparer.Ordinal)
                                                        .ToList())
            {
                жертва.ключи_кладовых.Remove(kid);
                кто.ключи_кладовых.Add(kid);
                h.bump("ключей_от_кладовых_отнято");
            }
        }
        Социальное.испугался(h, жертва, кто, b["страх_за_насилие"] * 1.3);
        Социальное.adjust(жертва, кто.id, hate: b["ненависть_за_налёт"], trust: -5.0,
                          aware: 20);
        Социальное.отдалились(жертва, кто.id, b["близость_за_обиду"]);
        Социальное.register_incident(h, "засада", null);
        Социальное.emit(h, кто, 3, "ссора", night: false);
        return исход;
    }
}
