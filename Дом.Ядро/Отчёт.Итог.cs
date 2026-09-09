// Перенос house/report.py, строки 164–339: итог прожитой жизни.
//
// Не игроку: это то, чем меряют настройку. Кто выжил, кто ушёл, кого
// не простили, что осталось в стенах и сколько дому стоила метель.
// Сверяется знак в знак с прототипом — вся арифметика вывода через `Текст`.

namespace Дом.Ядро;

public static partial class Отчёт
{
    /// <summary>Итог прожитой жизни: выжившие, погибшие, ушедшие, хроника,
    /// счётчики, деньги, отношения, страх, проломы, кладовые и диагностика.</summary>
    public static void final_report(House h, int days, long seed, Action<string>? w = null)
    {
        var журнал = h.journal as Журнал;
        w ??= журнал is not null ? журнал.w : Console.WriteLine;
        var alive = h.people.Значения.Where(p => p.здесь()).ToList();
        var ушли = h.people.Значения.Where(p => p.ушёл).ToList();
        var dead = h.people.Значения.Where(p => !p.здесь() && !p.ушёл).ToList();
        w("");
        w(new string('═', 78));
        w($"ИТОГ. {days} дней, зерно {seed}");
        w(new string('═', 78));
        w("");
        w($"Выжили ({alive.Count}):");
        foreach (var p in alive.OrderBy(x => x.apt))
        {
            var state = new List<string>();
            if (p.injuries.Count > 0)
                state.Add(string.Join(", ", p.injuries));
            if (!string.IsNullOrEmpty(p.sick))
                state.Add(p.sick!);
            if (p.dependents > 0)
                state.Add($"с ребёнком ({p.dependent_name})");
            w($"  {Текст.Слева(p.@short, 8)} кв{Текст.Слева(Журнал.Ц(p.apt), 3)} "
              + $"здоровье {Журнал.Ц((int)p.health, 3)}, "
              + $"настроение {Журнал.Ц((int)p.mood, 3)}, "
              + $"еды на {Текст.Ф(p.days_of("еда"), 1)} дн"
              + (state.Count > 0 ? " · " + string.Join("; ", state) : ""));
        }
        if (dead.Count > 0)
        {
            w("");
            w($"Погибли ({dead.Count}):");
            foreach (var p in dead.OrderBy(x => x.died_day ?? 0))
                w($"  день {Журнал.Ц(p.died_day ?? 0, 2)} — "
                  + $"{Текст.Слева(p.@short, 8)} {p.cause}");
        }
        if (ушли.Count > 0)
        {
            // третья строка итога, и она нарочно не сливается ни с одной
            // из двух. Дошёл человек или замёрз на объездной — изнутри
            // подъезда это выглядит одинаково, и правда видна только
            // с секретами
            w("");
            w($"Ушли ({ушли.Count}):");
            foreach (var p in ушли.OrderBy(x => x.died_day ?? 0))
            {
                string хвост = "";
                if (журнал is not null && журнал.secrets)
                    хвост = p.дошёл
                        ? "   ⌁ " + Util.Vb(p.sex, "дошёл")
                        : "   ⌁ " + Util.Vb(p.sex, "замёрз") + " на объездной";
                w($"  день {Журнал.Ц(p.died_day ?? 0, 2)} — "
                  + $"{Текст.Слева(p.@short, 8)} {p.cause}{хвост}");
            }
        }
        w("");
        w("Хроника дома:");
        foreach (string c in h.chronicle)
            w("  " + c);
        w("");
        var s = h.stats;
        w("Счётчики:");
        string[] order =
        {
            "попыток_кражи", "краж", "краж_сорвано", "налётов", "проломов",
            "вскрытых_квартир", "убийств", "выстрелов",
            "изгнаний", "смертей", "переездов", "союзов_заключено",
            "союзов_распалось", "обменов",
            "помощи", "отказов", "ложных_обвинений", "детей_брошено",
        };
        foreach (string k in order)
            if (s.Взять(k, 0.0) != 0)
                w($"  {Текст.Слева(k.Replace('_', ' '), 20)} {Текст.G(s.Взять(k, 0.0))}");
        w("");
        w("Деньги (GDD 18, в тысячах):");
        double налом = Util.Sum(h.people.Значения.Select(p => p.stock.Взять("деньги", 0.0)))
                       + Util.Sum(h.flats.Значения.Select(f => f.stock.Взять("деньги", 0.0)));
        double сгорело = Util.Sum(h.people.Значения.Select(p => p.счёт));
        w($"  курс на конец:       {Текст.Ф(h.курс(), 2)} (1.00 — как до метели)");
        w($"  потрачено в магазине: картой {Текст.Ф(s.Взять("потрачено_картой", 0.0), 1)}, "
          + $"налом {Текст.Ф(s.Взять("потрачено_налом", 0.0), 1)}");
        w($"  снято в банкоматах:  {Текст.Ф(s.Взять("снято_наличных", 0.0), 0)}");
        w($"  сгорело на картах:   {Текст.Ф(сгорело, 0)}");
        w($"  наличных в доме:     {Текст.Ф(налом, 0)}"
          + (h.курс() <= 0.0 ? "  — и на них уже ничего не купить" : ""));
        if (s.Взять("тулупов_куплено", 0.0) != 0)
            w($"  тулупов куплено:     {Текст.G(s.Взять("тулупов_куплено", 0.0))}");
        w("");
        w("Группы на конец:");
        foreach (var p in alive.OrderBy(x => x.apt))
            w($"  {Текст.Слева(p.@short, 8)} "
              + (string.IsNullOrEmpty(p.group) ? "—" : p.group)
              + (p.allies.Count > 0
                 ? "  союз: " + string.Join("+", p.allies
                     .OrderBy(x => x, StringComparer.Ordinal).Select(a => h.people[a].@short))
                 : ""));
        w("");
        w("Репутация щедрости (как её видит дом, 0 — «не даёт никогда», "
          + "1 — «даёт всегда»):");
        foreach (var p in h.people.Значения.OrderBy(x => x.apt))
        {
            var opinions = new List<double>();
            foreach (var o in h.people.Значения)
            {
                if (string.Equals(o.id, p.id, StringComparison.Ordinal) || !o.asking.Есть(p.id))
                    continue;
                var зап = o.asking.Взять(p.id, null!);
                if (зап.дали + зап.отказали > 0)
                    opinions.Add(o.generosity(p.id));
            }
            if (opinions.Count > 0)
                w($"  {Текст.Слева(p.@short, 8)} "
                  + $"{Текст.Ф(Util.Sum(opinions) / opinions.Count, 2)}"
                  + $"   (о {(string.Equals(p.sex, "ж", StringComparison.Ordinal) ? "ней" : "нём")} "
                  + $"судят {opinions.Count} чел.)");
        }
        w("");
        w("Матрица отношений (доверие 0-10 / ненависть 0-100 / "
          + "осведомлённость 0-100):");
        var ids = h.people.Значения.OrderBy(x => x.apt).Select(p => p.id).ToList();
        string head = new string(' ', 10)
            + string.Concat(ids.Select(i => Обрезать(h.people[i].@short, 6).PadLeft(16)));
        w("  " + head);
        foreach (string a_id in ids)
        {
            var a = h.people[a_id];
            string row = $"  {Текст.Слева(Обрезать(a.@short, 8), 8)}  ";
            foreach (string b_id in ids)
                row += string.Equals(a_id, b_id, StringComparison.Ordinal)
                    ? "·".PadLeft(16)
                    : $"{Текст.Справа(a.trust.Взять(b_id, 3), 5, 1)}/"
                      + $"{Журнал.Ц((int)a.hate.Взять(b_id, 0.0), 3)}/"
                      + $"{Журнал.Ц((int)a.сведения_о(b_id).aware, 3)}";
            w(row);
        }
        w("");
        w("Кого в доме боятся (страх 0-100, четвёртая шкала GDD 12.3):");
        bool боятся = false;
        foreach (var a in h.people.Значения.OrderBy(x => x.apt))
        {
            var пары = a.страх.Ключи
                .Where(k => a.страх.Взять(k, 0.0) >= 20)
                .Select(k => (h.people[k].@short, a.страх.Взять(k, 0.0)))
                .OrderByDescending(x => x.Item2).ToList();
            if (пары.Count > 0)
            {
                боятся = true;
                w($"  {Текст.Слева(a.@short, 8)} "
                  + string.Join(", ", пары.Select(п => $"{п.Item1} {(int)п.Item2}")));
            }
        }
        if (!боятся)
            w("  никто никого всерьёз не боится");
        var сдырами = h.flats.Значения.OrderBy(x => x.apt)
            .Where(f => f.дыры.Count > 0).ToList();
        if (сдырами.Count > 0)
        {
            w("");
            w("Проломы (GDD 16: чем вошли — то и осталось в стенах):");
            foreach (var f in сдырами)
                w($"  кв.{Текст.Слева(Журнал.Ц(f.apt), 3)} "
                  + string.Join(", ", f.дыры.Ключи.OrderBy(x => x, StringComparer.Ordinal)
                      .Select(k => $"{k} ×{f.дыры.Взять(k, 0)}"))
                  + $"   (−{Текст.Ф(f.потери_тепла(h.B), 1)}°)");
        }
        if (h.кладовые.Count > 0)
        {
            w("");
            w("Кладовые (погреба и гаражи — имущество за порогом квартиры):");
            foreach (var к in h.кладовые.Значения.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                var хозяин = h.хозяин_кладовой(к);
                // падежи: «ключ у Петра», «числится за Петром» — формы лежат
                // в данных персонажей, и отчёт обязан ими пользоваться так же,
                // как журнал
                var ключи = h.people.Значения.Where(p => p.ключи_кладовых.Contains(к.id))
                    .Select(p => p.form("gen"))
                    .OrderBy(x => x, StringComparer.Ordinal).ToList();
                string осталось = string.Join(", ", к.stock.Ключи
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Where(r => к.stock.Взять(r, 0.0) != 0)
                    .Select(r => $"{r} {Текст.G(к.stock.Взять(r, 0.0))}"));
                // погреб и гараж оба мужского рода: «ВСКРЫТА» читалось
                // как опечатка
                w($"  {Текст.Слева(к.имя, 12)} "
                  + $"{Текст.Слева(к.вскрыта ? "ВСКРЫТ" : "заперт", 8)} "
                  + $"ходок {Текст.Слева(Журнал.Ц(к.ходок), 3)} "
                  + $"осталось: {(осталось.Length > 0 ? осталось : "пусто")}"
                  + (ключи.Count > 0 ? $"; ключ у {string.Join(", ", ключи)}"
                                     : "; ключ потерян")
                  + (хозяин is not null ? $"; числится за {хозяин.form("ins")}" : ""));
            }
        }
        // то, что дом унёс с собой: кому и чего не простили. Это
        // не диагностика, это итог наравне с тем, кто выжил
        var счета = new List<string>();
        foreach (var p_ in h.people.Значения.OrderBy(x => x.apt))
            foreach (string кому in p_.не_прощу.OrderBy(x => x, StringComparer.Ordinal))
            {
                var другой = h.get(кому);
                if (другой is null)
                    continue;
                счета.Add($"  {p_.@short} → {другой.@short}"
                          + (p_.здесь() ? "" : " (не дожил)"));
            }
        if (счета.Count > 0)
        {
            w("");
            w("Не простили (это не проходит и не заглаживается):");
            foreach (string строка in счета)
                w(строка);
        }
        w("");
        w("Диагностика (для настройки, не для игрока):");
        w($"  первое происшествие: "
          + (h.first_incident_day is int д ? "день " + Журнал.Ц(д) : "не было"));
        w($"  первый налёт:        {День(s, "первый_налёт_день", "не было")}");
        w($"  первая смерть:       {День(s, "первая_смерть_день", "никто не умер")}");
        w($"  первый союз:         {День(s, "первый_союз_день", "не сложился")}");
        w($"  первое вскрытие:     {День(s, "первое_вскрытие", "не было")}"
          + $"   (умыслом {Текст.G(s.Взять("вскрыто_умыслом", 0.0))}, "
          + $"попутно {Текст.G(s.Взять("вскрыто_попутно", 0.0))})");
        w($"  снег во дворе:       {Текст.Ф(h.снег, 2)} м");
        w($"  первая попытка уйти: {День(s, "первая_попытка_уйти", "не было")}"
          + $"   (ушли {Текст.G(s.Взять("ушедших", 0.0))}, "
          + $"вернулись с полпути {Текст.G(s.Взять("возвратов_с_полпути", 0.0))})");
        double узнали = s.Взять("смертей_дом_узнал", 0.0);
        double задержка = s.Взять("задержка_известия", 0.0);
        w($"  дом узнал о смертях: {Текст.G(узнали)} из {Текст.G(s.Взять("смертей", 0.0))}"
          + (узнали != 0 ? $", в среднем через {Текст.Ф(задержка / узнали, 1)} дня" : ""));
        // только там, где кто-то живёт: в брошенной квартире шахта мёрзнет
        // сама по себе и никого этим не касается
        var жилые = new HashSet<int>(alive.Select(p => h.where(p).apt));
        var вент = h.flats.Значения
            .Where(f => жилые.Contains(f.apt) && f.вентиляция < 0.5)
            .Select(f => f.вентиляция).ToList();
        if (вент.Count > 0)
            w($"  вытяжка ниже половины: в {вент.Count} квартирах "
              + $"(худшая {Текст.Ф(вент.Min(), 2)}); "
              + $"угорело {Текст.G(s.Взять("смертей_от_угара", 0.0))}");
        w($"  богатство района:    {Текст.Ф(h.scav_richness, 2)} (стартовало с 1.00)");
        w("  что осталось где:    " + string.Join(", ",
            Улица.МЕСТА.Select(м => $"{м.имя} {Текст.Ф(h.богатство_места(м.имя), 2)}")));
        w(alive.Count > 0
          ? $"  средняя паника:      {Текст.Ф(Util.Sum(alive.Select(p => p.panic)) / alive.Count, 0)}"
          : "  средняя паника: —");
    }

    private static string День(Словарь<string, double> s, string ключ, string если_нет)
        => s.Есть(ключ) ? "день " + Текст.G(s.Взять(ключ, 0.0)) : если_нет;

    private static string Обрезать(string s, int сколько)
        => s.Length <= сколько ? s : s[..сколько];
}
