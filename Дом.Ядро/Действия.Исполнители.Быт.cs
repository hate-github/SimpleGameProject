// Перенос house/actions.py, строки 1916–2330: исполнители быта и стройки.
//
// Исполнитель возвращает текст главной строки хода — или `НЕ_СОСТОЯЛОСЬ`,
// когда пока он шёл, надобность отпала, или `СДЕЛАНО_МОЛЧА`, когда строку
// и шум он дал сам. Дальше общий эпилог.
//
// Регистрируются они не атрибутом, а вызовом в статическом конструкторе:
// в Python это декоратор `@исполняет`, здесь — та же запись в общий
// справочник, только явная.

namespace Дом.Ядро;

public static partial class Действия
{
    internal static void ЗарегистрироватьБыт()
    {
        исполняет("поесть", (h, npc, target, spent) =>
        {
            var b = h.B;
            double need = npc.eaters();
            double used = Ресурсы.Spend(h, npc, "еда", need);
            double доля = used / need;
            // мать ест после него: ребёнку идёт полная порция, пока она есть,
            // и потому при том же запасе она сама голоднее (GDD 12.6)
            foreach (var р in npc.дети)
                р.сытость = Util.Clamp(р.сытость + b["ребёнок_еда_за_порцию"]
                                       * Math.Min(1.0, доля * 1.4));
            npc.satiety = Util.Clamp(npc.satiety + порция(h, npc, b) * доля
                                     * (1.0 - b["ребёнок_отдаёт"] * npc.дети.Count));
            h.stats["съедено"] = h.stats.Взять("съедено", 0.0) + used;
            // горячая еда пахнет сильнее — и выдаёт хозяина всему подъезду
            Социальное.smell(h, npc, hot: npc.burning
                || (h.powered(npc) && npc.shelter.Взять("обогреватель", 0.0) != 0.0));
            return $"{npc.@short} {Util.Vb(npc.sex, "поел")}"
                + (npc.dependents > 0
                   ? $" и {Util.Vb(npc.sex, "покормил")} "
                     + (npc.dependent_acc.Length > 0 ? npc.dependent_acc : npc.dependent_name)
                   : "");
        });

        исполняет("поесть_мясо", (h, npc, target, spent) =>
        {
            var b = h.B;
            double need = npc.eaters();
            double used = Ресурсы.Spend(h, npc, "мясо", need);
            npc.satiety = Util.Clamp(npc.satiety + порция(h, npc, b) * (used / need));
            npc.mood = Util.Clamp(npc.mood - b["людоедство_настроение_за_раз"]);
            Социальное.smell(h, npc, hot: true);
            return null;
        });

        исполняет("попить", (h, npc, target, spent) =>
        {
            var b = h.B;
            if (h.water_on)
            {
                npc.hydration = Util.Clamp(npc.hydration + b["вода_за_порцию"]);
                // из-под крана вода в запас не идёт, но и из мира не уходит
                return $"{npc.@short} {Util.Vb(npc.sex, "набрал")} воды из-под крана";
            }
            Ресурсы.Spend(h, npc, "вода", npc.eaters());
            npc.hydration = Util.Clamp(npc.hydration + b["вода_за_порцию"]);
            return $"{npc.@short} {Util.Vb(npc.sex, "достал")} воду из запаса";
        });

        исполняет("топить_снег", (h, npc, target, spent) =>
        {
            var b = h.B;
            // на электроплитке, пока есть свет, вода достаётся даром
            var очаг = h.хозяин_жилья(npc);
            bool on_power = npc.shelter.Взять("буржуйка", 0.0) == 0.0 && h.powered(npc)
                            && npc.shelter.Взять("обогреватель", 0.0) != 0.0;
            double cost;
            string how;
            if (on_power)
            {
                cost = 0.0;
                how = "на плитке";
            }
            else
            {
                // печка уже топится — доплачиваем немного; холодная — платим
                // как за топку, но тогда и квартира прогревается, топливо
                // не выброшено
                bool already = очаг.burning;
                cost = b["снег_топливо"] * (already ? b["снег_на_горящей_печке"] : 1.0);
                очаг.burning = true;
                how = already ? "на горячей печке" : "затопив печку";
            }
            if (очаг.stock.Взять("топливо", 0.0) < cost
                && npc.stock.Взять("материалы", 0.0) >= b["мебель_за_топку"])
            {
                Ресурсы.Spend(h, npc, "материалы", b["мебель_за_топку"]);   // в ход пошла мебель
                cost = 0.0;
                how = "на мебели";
            }
            Ресурсы.Spend(h, очаг, "топливо", cost);
            npc.stock["вода"] = npc.stock.Взять("вода", 0.0) + b["снег_вода"];
            h.stats["натоплено_вода"] = h.stats.Взять("натоплено_вода", 0.0) + b["снег_вода"];
            return $"{npc.@short} {Util.Vb(npc.sex, "натопил")} снега {how}"
                   + (cost != 0.0 ? $" (-{Текст.G(cost)} топлива)" : "");
        });

        исполняет("топить", (h, npc, target, spent) =>
        {
            var b = h.B;
            string said;
            // в буран тепло вылетает в щели, и та же печка съедает больше
            double расход = h.режим == Режим.БУРАН ? b["буран_топливо"] : 1.0;
            if (npc.stock.Взять("топливо", 0.0) >= 1)
            {
                Ресурсы.Spend(h, npc, "топливо", расход);
                said = $"{npc.@short} {Util.Vb(npc.sex, "затопил")} буржуйку";
            }
            else
            {
                Ресурсы.Spend(h, npc, "материалы", b["мебель_за_топку"]);
                said = $"{npc.@short} {Util.Vb(npc.sex, "разломал")} мебель и "
                       + $"{Util.Vb(npc.sex, "затопил")} ею";
            }
            npc.burning = true;
            return said;
        });

        исполняет("банкомат", (h, npc, target, spent) =>
        {
            var b = h.B;
            string said;
            double касса = h.банкомат;
            double снял = Math.Truncate(Math.Min(Math.Min(npc.счёт, b["банкомат_лимит"]), касса));
            if (снял <= 0)
                said = $"{npc.@short} {Util.Vb(npc.sex, "сходил")} к банкомату — пусто";
            else
            {
                npc.счёт -= снял;
                npc.stock["деньги"] = npc.stock.Взять("деньги", 0.0) + снял;
                h.банкомат = касса - снял;
                h.stats["принесено_деньги"] = h.stats.Взять("принесено_деньги", 0.0) + снял;
                h.stats["снято_наличных"] = h.stats.Взять("снято_наличных", 0.0) + снял;
                said = $"{npc.@short} {Util.Vb(npc.sex, "отстоял")} очередь к банкомату: "
                       + $"{Util.Vb(npc.sex, "снял")} {Текст.G(снял)}";
            }
            npc.warmth = Util.Clamp(npc.warmth - b["банкомат_холод"]);
            return said;
        });

        исполняет("одежда", (h, npc, target, spent) =>
        {
            var b = h.B;
            Ресурсы.Spend(h, npc, "материалы", b["одежда_материалы"]);
            npc.одежда += 1;
            double новый = npc.мороз_предел(b);
            return $"{npc.@short} {Util.Vb(npc.sex, "подбил")} куртку изнутри "
                   + $"(теперь держит до {Текст.Ф(новый, 0)}°)";
        });

        исполняет("вытяжка", (h, npc, target, spent) =>
        {
            var b = h.B;
            var flat = h.where(npc);
            double было = flat.вентиляция;
            flat.вентиляция = 1.0;
            h.bump("вытяжек_прочищено");
            return было < b["вентиляция_признак"]
                ? $"{npc.@short} {Util.Vb(npc.sex, "полез")} на кухне в вентиляцию: "
                  + "решётка изнутри в ледяной шубе, оттуда не тянет вовсе"
                : $"{npc.@short} {Util.Vb(npc.sex, "прочистил")} вытяжку — "
                  + "тяга была никакая";
        });

        исполняет("костёр", (h, npc, target, spent) =>
        {
            var b = h.B;
            string? said = null;
            var flat = h.where(npc);
            string чем;
            if (npc.stock.Взять("топливо", 0.0) >= 1)
            {
                Ресурсы.Spend(h, npc, "топливо", 1);
                чем = "на дровах";
            }
            else
            {
                Ресурсы.Spend(h, npc, "материалы", b["костёр_материалы"]);
                чем = "на мебели";
            }
            flat.костёр = h.day;
            npc.warmth = Util.Clamp(npc.warmth + 6);
            if (h.rng.Chance(b["костёр_пожар"]))
            {
                // то, ради чего это и опасно
                npc.injuries.Add("ожог руки");
                npc.health = Util.Clamp(npc.health - h.rng.Uni(8, 18));
                if (flat.shelter.Взять("утепление", 0.0) > 0)
                    flat.shelter["утепление"] = flat.shelter.Взять("утепление", 0.0) - 1;
                flat.вложено = Math.Max(0.0, flat.вложено - 2.0);
                npc.panic = Util.Clamp(npc.panic + 20);
                h.journal.line($"В кв.{flat.apt} занялось. {npc.@short} "
                    + $"{Util.Vb(npc.sex, "сбил")} огонь, но {Util.Vb(npc.sex, "обжёг")} "
                    + "руки, и окна выгорели.", 2);
                Социальное.house_shock(h, panic: 10, mood: -5);
                Социальное.emit(h, npc, 4, "ссора", night: false);
                h.bump("пожаров");
                h.событие(ВидСобытия.ПОЖАР, кто: npc.id, где: flat.apt);
            }
            else
                said = $"{npc.@short} {Util.Vb(npc.sex, "развёл")} костёр посреди "
                       + $"комнаты {чем}";
            return said;
        });

        исполняет("отдых", (h, npc, target, spent) =>
        {
            npc.mood = Util.Clamp(npc.mood + 5);
            npc.rest = Util.Clamp(npc.rest + 6);
            npc.panic = Util.Clamp(npc.panic - 3);
            // отдых стоит два часа, и восемь раз подряд — это ровно сутки.
            // Поведение честное (сил нет, дел нет), а вот восемь одинаковых
            // строк в журнале превращают человека в сломанный автомат.
            // Пишется он поэтому не здесь, а один раз за день, в конце
            if (npc.отдых_день != h.day)
            {
                npc.отдых_день = h.day;
                npc.отдых_раз = 0;
            }
            npc.отдых_раз += 1;
            return null;
        });

        исполняет("быт", (h, npc, target, spent, детали) =>
        {
            var b = h.B;
            string? занятие = детали as string;
            npc.mood = Util.Clamp(npc.mood + 3.0 * npc.normalcy);
            npc.panic = Util.Clamp(npc.panic - 1.5);
            // быт — это всё-таки движение по квартире, и на градус-полтора
            // человек от него согревается. Но только если есть на что:
            // голодному, промёрзшему и не спавшему тот же час даёт передышку,
            // а не тепло
            bool если_есть_силы = Math.Min(npc.satiety, npc.rest) > b["быт_силы_порог"]
                                  && npc.health > 40;
            if (если_есть_силы)
                npc.warmth = Util.Clamp(npc.warmth + b["быт_согревает"]);
            else
                npc.rest = Util.Clamp(npc.rest + b["быт_передышка"]);
            var variants = Мир.подходящие_кому(h, npc, h.реплики_быт);
            // то, что он сегодня уже делал, второй раз не показываем: быт
            // можно брать до четырёх раз в день, и одна и та же строка подряд
            // читается как заевшая пластинка. Если все варианты кончились —
            // берём любой
            if (!h.сутки.быт_сказано.Есть(npc.id))
                h.сутки.быт_сказано[npc.id] = new HashSet<string>(StringComparer.Ordinal);
            var сказано = h.сутки.быт_сказано.Взять(npc.id, null!);
            var свежие = variants.Where(v => !сказано.Contains(v.текст)).ToList();
            if (свежие.Count == 0)
                свежие = variants;
            // привычка выбирает занятие, а не строку: у «книги» их может быть
            // три, и человек читает по-разному, но читает. Если сегодня
            // подходящей строки не нашлось — занимается обычным
            var по_привычке = свежие.Where(
                v => !string.IsNullOrEmpty(занятие)
                     && string.Equals(v.занятие, занятие, StringComparison.Ordinal)).ToList();
            string текст;
            string? делает;
            if (свежие.Count > 0)
            {
                var выбран = h.rng.Pick(по_привычке.Count > 0 ? по_привычке : свежие);
                текст = выбран.текст;
                делает = выбран.делает;
            }
            else
            {
                текст = "занимал{ся|ась} своими делами";
                делает = null;
            }
            сказано.Add(текст);
            if (string.Equals(делает, "уход_за_оружием", StringComparison.Ordinal)
                && npc.weapon != Оружие.НЕТ)
            {
                double было = npc.рука.Взять(npc.weapon,
                    Таблицы.СВОЙСКОЕ.TryGetValue(npc.weapon, out var своё) ? своё : 0.0);
                npc.рука[npc.weapon] =
                    Util.Clamp(было + b["рука_за_уход"], 0.0, 1.0);
                h.bump("ухода_за_оружием");
            }
            // имена в репликах склоняются по тем же формам, что и у взрослых:
            // «для Вани», а не «для Ваня»
            текст = текст
                .Replace("{ребёнок}", npc.dependent_name.Length > 0
                                      ? npc.dependent_name : "ребёнок", StringComparison.Ordinal)
                .Replace("{ребёнок_род}", npc.dependent_gen.Length > 0 ? npc.dependent_gen
                    : (npc.dependent_name.Length > 0 ? npc.dependent_name : "ребёнка"),
                    StringComparison.Ordinal)
                .Replace("{ребёнок_вин}", npc.dependent_acc.Length > 0 ? npc.dependent_acc
                    : (npc.dependent_name.Length > 0 ? npc.dependent_name : "ребёнка"),
                    StringComparison.Ordinal);
            h.hooks.Зов_реплика(h, текст);
            return $"{npc.@short} {Util.Gform(текст, npc.sex)}";
        }, детали: true);

        // --- стройка и услуги ---

        исполняет("заказать", (h, npc, target, spent, что) =>
        {
            var b = h.B;
            // что заказывать, решено в сборе и пришло сюда; нужда и страх
            // нужны услуге для цены и считаются здесь, как их считал сбор:
            // услуги про действия не знают. null от заказать — «пока он шёл,
            // надобность отпала»
            var said = Услуги.заказать(h, npc, target as NPC, холод_впереди(h, npc, b),
                                       тревога(h, npc), что as string);
            return said is null ? НЕ_СОСТОЯЛОСЬ : said;
        }, детали: true);

        исполняет("генератор", (h, npc, target, spent) =>
        {
            Ресурсы.Spend(h, h.хозяин_жилья(npc), "топливо", 2);
            npc.shelter["питание"] = h.day;     // свет в квартире на сутки (GDD 15)
            npc.mood = Util.Clamp(npc.mood + 10);
            npc.warmth = Util.Clamp(npc.warmth + 4);
            foreach (string g in npc.guests.OrderBy(x => x, StringComparer.Ordinal))
            {
                var o = h.get(g);
                if (o is not null && o.alive)
                    o.mood = Util.Clamp(o.mood + 6);
            }
            return $"{npc.@short} {Util.Vb(npc.sex, "запустил")} генератор — "
                   + "на весь подъезд гул и свет в окне";
        });

        // уровни 3 и 4 (GDD 15): один исполнитель на все ключи построек;
        // какой именно строят, приходит замыканием при регистрации
        foreach (string ключ in Каталог.ПОСТРОЙКИ.Ключи)
        {
            string key = ключ;
            исполняет(key, (h, npc, target, spent) => Постройка(h, npc, key));
        }

        исполняет("утепление", (h, npc, target, spent) =>
        {
            var b = h.B;
            string? said = null;
            Ресурсы.Spend(h, npc, "материалы", b["утепление_материалы"]);
            вложить(h, npc, b["утепление_материалы"]);
            var flat = h.where(npc);
            string? дыра = null;
            foreach (string в in new[] { "окно", "стена", "потолок", "пол" })
                if (flat.дыры.Взять(в, 0) > 0)
                {
                    дыра = в;
                    break;
                }
            if (дыра is not null)
            {
                // сначала заделывают то, через что вошли: плёнка на окне
                // не держит ничего, пока в стене пролом от чужого лома
                flat.дыры[дыра] = flat.дыры.Взять(дыра, 0) - 1;
                if (flat.дыры.Взять(дыра, 0) <= 0)
                    flat.дыры.Убрать(дыра);
                h.bump("дыр_заделано");
                said = $"{npc.@short} {Util.Vb(npc.sex, "заделал")} {ДЫРА_ВИН[дыра]}";
            }
            else if (npc.shelter.Взять("утепление", 0.0) < b["максимум_утепления"])
            {
                npc.shelter["утепление"] = npc.shelter.Взять("утепление", 0.0) + 1;
                said = $"{npc.@short} {Util.Vb(npc.sex, "утеплил")} окна "
                       + $"(уровень {Текст.G(npc.shelter.Взять("утепление", 0.0))})";
            }
            return said;
        });

        исполняет("дверь", (h, npc, target, spent) =>
        {
            var b = h.B;
            Ресурсы.Spend(h, npc, "материалы", b["дверь_материалы"]);
            вложить(h, npc, b["дверь_материалы"]);
            // новый засов — старые ключи больше не подходят
            foreach (var кто in h.people.Значения)
                кто.ключи.Remove(npc.apt);
            npc.shelter["дверь"] = npc.shelter.Взять("дверь", 0.0) + 1;
            return $"{npc.@short} {Util.Vb(npc.sex, "укрепил")} дверь "
                   + $"(уровень {Текст.G(npc.shelter.Взять("дверь", 0.0))})";
        });

        исполняет("буржуйка", (h, npc, target, spent) =>
        {
            var b = h.B;
            Ресурсы.Spend(h, npc, "материалы", b["буржуйка_материалы"]);
            вложить(h, npc, b["буржуйка_материалы"]);
            npc.shelter["буржуйка"] = 1;
            string said = $"{npc.@short} {Util.Vb(npc.sex, "собрал")} буржуйку";
            h.note(said);
            return said;
        });
    }

    /// <summary>Уровни 3 и 4 (GDD 15): арматура, стальные листы,
    /// звукоизоляция и собранный генератор.</summary>
    private static string Постройка(House h, NPC npc, string key)
    {
        var b = h.B;
        var п = Каталог.ПОСТРОЙКИ.Взять(key, null!);
        Ресурсы.Spend(h, npc, "материалы", b[п.материалы]);
        вложить(h, npc, b[п.материалы]);
        string поле = string.Equals(key, "генератор_собрать", StringComparison.Ordinal)
                      ? "генератор" : key;
        string said;
        if (string.Equals(поле, "генератор", StringComparison.Ordinal))
        {
            Ресурсы.Spend(h, npc, "движок", 1.0);
            npc.shelter["генератор"] = 1;
            said = $"{npc.@short} {Util.Vb(npc.sex, "собрал")} генератор — "
                   + "движок из погреба наконец завёлся";
        }
        else
        {
            npc.shelter[поле] = npc.shelter.Взять(поле, 0.0) + 1;
            said = поле switch
            {
                "листы" => $"{npc.@short} {Util.Vb(npc.sex, "прибил")} стальные листы "
                           + "на дверь",
                "звукоизоляция" => $"{npc.@short} {Util.Vb(npc.sex, "заглушил")} "
                                   + "генератор — гула больше не слышно",
                _ => $"{npc.@short} {Util.Vb(npc.sex, "протянул")} арматуру по стене "
                     + "и потолку",
            };
        }
        h.note(said);
        return said;
    }
}
