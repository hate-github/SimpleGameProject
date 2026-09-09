// Перенос house/actions.py, строки 2488–3080: лечение, жильё, улица,
// кладовые и интрига.
//
// Здесь кончается быт и начинается всё, ради чего дом и написан: кто кого
// пустил, кто чью квартиру занял, кто кого ждал на площадке и кто первым
// пошёл за телом.

namespace Дом.Ядро;

public static partial class Действия
{
    internal static void ЗарегистрироватьУлицу()
    {
        исполняет("лечить", (h, npc, target, spent) =>
        {
            var b = h.B;
            var t = (NPC)target!;
            var (lvl, kind) = Каталог.COST["лечить"];
            string? said = null;
            // медик умеет то, чего не умеет сам себе перевязывающий (GDD 12.6)
            bool медик = npc.skills.Contains("медик", StringComparer.Ordinal);
            bool чужой = !string.Equals(t.id, npc.id, StringComparison.Ordinal);
            if (чужой)
            {
                // перевязывающий видит человека вплотную и целиком: это самый
                // точный взгляд в доме, и потому именно медик первым понимает,
                // что сосед не жилец
                Социальное.встретились(h, npc, t);
                // впустит ли: решение больного, а не медика. Раньше это стояло
                // воротами в оценке медика и читало доверие больного к ней —
                // чужие мысли. Правило то же: доверие не ниже порога,
                // включительно — отсюда крошечная прибавка к мерке
                double мерка = t.trust.Взять(npc.id, 3.0) - b["лечение_доверие"] + 1e-9;
                if (!Решение.по_правилу(h, t, Вопрос.ВПУСТИТЬ_МЕДИКА,
                                        "впустить", "не впустить", мерка))
                {
                    npc.ask_record(t.id).не_впустил = h.day;
                    h.bump("лечений_не_впустили");
                    h.journal.line($"{t.@short} не {Util.Vb(t.sex, "пустил")} "
                        + $"{npc.form("acc")} с аптечкой: не настолько "
                        + $"{Util.Vb(t.sex, "доверял")}.", 1);
                    return НЕ_СОСТОЯЛОСЬ;
                }
                // шёл по тому, что видел, а вблизи оказалось — лечить нечего:
                // рана зажила, кашель прошёл. Аптечка остаётся в сумке,
                // а взгляд теперь точный: он его только что осмотрел
                if (!нужен_врач(t))
                {
                    var v = npc.видит(t.id);
                    if (v is not null)
                        v.хворь = false;
                    h.bump("лечений_впустую");
                    h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "пришёл")} "
                        + $"к {t.form("dat")} с аптечкой — а лечить оказалось "
                        + "нечего.", 1);
                    return НЕ_СОСТОЯЛОСЬ;
                }
                h.bump("лечений_соседа");
                h.событие(ВидСобытия.ЛЕЧЕНИЕ, кто: npc.id, кому: t.id, где: t.apt);
            }
            Ресурсы.Spend(h, npc, "лекарства", 1);
            var ребёнок = t.слабейший_ребёнок();
            // ребёнку — первому: и мать так решает, и медик так решает
            if (ребёнок is not null && (ребёнок.болен is not null || ребёнок.здоровье < 70))
            {
                ребёнок.здоровье = Util.Clamp(ребёнок.здоровье
                    + b["ребёнок_лечение"] * (медик ? 1.4 : 1.0));
                if (ребёнок.болен is not null
                    && h.rng.Chance(медик ? b["лечение_медиком_болезнь"]
                                          : b["самолечение_болезнь"]))
                    ребёнок.болен = null;
                if (чужой)
                {
                    Социальное.adjust(t, npc.id, trust: b["доверие_за_лечение"], hate: -14);
                    Социальное.сблизились(h, npc, t, b["близость_за_лечение"]);
                    Социальное.загладил(h, npc, t, b["обида_за_лечение"]);
                    Социальное.проверить_наговор(h, npc, t);
                    t.mood = Util.Clamp(t.mood + 12);
                    npc.mood = Util.Clamp(npc.mood + b["настроение_от_помощи"]);
                    t.favors[npc.id] = t.favors.Взять(npc.id, 0) + 1;
                    said = $"{npc.@short} {Util.Vb(npc.sex, "осмотрел")} {ребёнок.вин} "
                           + $"у {t.form("gen")}";
                }
                else
                    said = $"{npc.@short} {Util.Vb(npc.sex, "выхаживал")} {ребёнок.вин}";
                if (!string.IsNullOrEmpty(said))
                    h.journal.line(said!, Каталог.NOTABLE.Взять("лечить", 0));
                if (lvl != 0 && !string.IsNullOrEmpty(kind))
                    Социальное.emit(h, npc, lvl, kind!, night: false);
                return СДЕЛАНО_МОЛЧА;
            }
            if (медик)
            {
                // перевязать — не значит вылечить всё. Медсестра с аптечкой
                // закрывает одну рану за раз и сбивает жар; перелом от этого
                // не срастается. Раньше здесь стояло «очистить все травмы»:
                // один час работы полностью обнулял человека
                for (int i = 0; i < (int)b["лечение_медиком_ран"]; i++)
                    if (t.injuries.Count > 0)
                        t.injuries.RemoveAt(t.injuries.Count - 1);
                if (!string.IsNullOrEmpty(t.sick) && h.rng.Chance(b["лечение_медиком_болезнь"]))
                    t.sick = null;
                t.health = Util.Clamp(t.health + b["лечение_медиком"]);
            }
            else
            {
                if (t.injuries.Count > 0)
                    t.injuries.RemoveAt(t.injuries.Count - 1);
                if (!string.IsNullOrEmpty(t.sick) && h.rng.Chance(b["самолечение_болезнь"]))
                    t.sick = null;
                t.health = Util.Clamp(t.health + b["лечение_самому"]);
            }
            if (чужой)
            {
                сказать_сразу(h, "лечить",
                    $"{npc.@short} {Util.Vb(npc.sex, "перевязал")} {t.form("acc")}");
                var плата = медик ? Услуги.плата_за_лечение(h, npc, t, b) : null;
                Социальное.adjust(t, npc.id, trust: b["доверие_за_лечение"], hate: -12);
                Социальное.сблизились(h, npc, t, b["близость_за_лечение"]);
                Социальное.загладил(h, npc, t, b["обида_за_лечение"]);
                Социальное.проверить_наговор(h, npc, t);
                t.mood = Util.Clamp(t.mood + 10);
                npc.mood = Util.Clamp(npc.mood + b["настроение_от_помощи"]);
                t.favors[npc.id] = t.favors.Взять(npc.id, 0) + 1;
                if (плата is not null && плата.Count > 0)
                    h.journal.line($"   {t.@short} {Util.Vb(t.sex, "расплатился")} "
                        + $"с {npc.form("ins")}: "
                        // цена услуги дробная, и в журнале выходило
                        // «еда 3.02698»: в подъезде так не считают
                        + string.Join(", ", плата.Ключи.Select(
                            k => $"{k} {Текст.G(Текст.Округлить(плата.Взять(k, 0.0), 1))}"))
                        + ".", 1);
                else if (плата is not null)
                {
                    // взять было нечего, и обе стороны это запомнили
                    Социальное.adjust(npc, t.id, trust: -0.5);
                    h.journal.line($"   {t.form("dat")} платить нечем. "
                                   + $"{npc.@short} {Util.Vb(npc.sex, "ушёл")} молча.", 1);
                }
            }
            else
                said = $"{npc.@short} {Util.Vb(npc.sex, "обработал")} раны";
            return said;
        });

        исполняет("переехать", (h, npc, target, spent) =>
        {
            Сожительство.переехать(h, npc, (NPC)target!);
            return null;
        });

        исполняет("съехать", (h, npc, target, spent) =>
        {
            Сожительство.съехать(h, npc, (NPC)target!);
            return null;
        });

        исполняет("выгнать", (h, npc, target, spent) =>
        {
            Сожительство.выгнать(h, npc, (NPC)target!);
            return null;
        });

        исполняет("занять", (h, npc, target, spent) =>
        {
            var b = h.B;
            var flat = (Flat)target!;
            var старая = h.flats[npc.apt];
            var прежний = h.чей(flat);
            if (прежний is not null && string.IsNullOrEmpty(прежний.living_with))
                return НЕ_СОСТОЯЛОСЬ;      // пока он собирался, туда уже въехали
            Социальное.вошёл_в_квартиру(h, npc, flat);
            npc.apt = flat.apt;            // и гости переезжают с ним
            npc.floor = flat.floor;
            flat.открыта = true;           // он как-то туда вошёл
            if (прежний is not null)
            {
                // у него был этот угол, но сам он жил у соседа. Меняются
                // местами: человек не остаётся без адреса, он получает
                // брошенную дыру
                прежний.apt = старая.apt;
                прежний.floor = старая.floor;
            }
            Сожительство.occupy_flat(h, npc);           // забрать то, что лежало
            if (flat.тулуп && npc.одежда < b["одежда_максимум"])
            {
                flat.тулуп = false;
                npc.одежда = (int)b["одежда_максимум"];
                h.bump("тулупов_снято_с_мёртвых");
                h.journal.line($"   В прихожей висел тулуп. {npc.@short} "
                               + $"{Util.Vb(npc.sex, "забрал")} его себе.", 1);
            }
            h.bump("занято_квартир");
            npc.bump("занял_квартиру");
            var чем_лучше = new List<string>();
            if (flat.shelter.Взять("буржуйка", 0.0) != 0.0
                && старая.shelter.Взять("буржуйка", 0.0) == 0.0)
                чем_лучше.Add("там буржуйка");
            if (flat.shelter.Взять("утепление", 0.0) > старая.shelter.Взять("утепление", 0.0))
                чем_лучше.Add("окна заклеены");
            if (flat.shelter.Взять("дверь", 0.0) > старая.shelter.Взять("дверь", 0.0))
                чем_лучше.Add("дверь целее");
            string хвост = чем_лучше.Count > 0 ? " — " + string.Join(", ", чем_лучше) : "";
            h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "перебрался")} "
                + $"в кв.{flat.apt}{хвост}. Свою {Util.Vb(npc.sex, "бросил")}.", 2);
            h.note($"{npc.@short} {Util.Vb(npc.sex, "занял")} кв.{flat.apt}"
                   + (прежний is not null ? $" (была {прежний.form("gen")})" : ""));
            if (flat.body is not null && flat.body.порций > 0)
                npc.mood = Util.Clamp(npc.mood - b["занять_тело_штраф"]);
            foreach (var w in h.others(npc))
                Социальное.adjust(w, npc.id, aware: 12);
            if (прежний is not null && прежний.alive)
            {
                прежний.mood = Util.Clamp(прежний.mood - 15);
                прежний.panic = Util.Clamp(прежний.panic + 12);
                Социальное.adjust(прежний, npc.id, trust: -3.0,
                                  hate: b["ненависть_за_захват"]);
                h.journal.line($"{прежний.@short} {Util.Vb(прежний.sex, "остался")} "
                               + "без своего угла.", 2);
                Социальное.register_incident(h, "захват", null);
                Социальное.judge(h, npc, "воровство", hate: 10.0, trust: -1.0);
            }
            return null;
        });

        исполняет("позвать", (h, npc, target, spent) =>
        {
            var b = h.B;
            // час на сговор уже потрачен, и потрачен он в любом случае:
            // и когда пошли, и когда отказали
            var t = target as NPC;
            string имя = npc.зов_куда;
            var м = Улица.МЕСТА.FirstOrDefault(
                x => string.Equals(x.имя, имя, StringComparison.Ordinal));
            if (м is null || !Улица.место_доступно(h, npc, м, часов: npc.time_left))
                м = Улица.выбрать_место(h, npc, часов: npc.time_left, без_магазина: true);
            if (м is null || t is null || !t.здесь())
            {
                // пока сговаривались, идти стало некуда
            }
            else if (Решение.по_правилу(h, t, Вопрос.ПОЙТИ_ВДВОЁМ, "пойти", "отказать",
                                        пойдёт_вдвоём(h, npc, t, м, b)))
            {
                double dur = Math.Min(Math.Min(
                    h.rng.Uni(b["вылазка_часы_мин"], b["вылазка_часы_макс"])
                    * м.часы * Улица.погода_часы(h), npc.time_left), t.time_left);
                npc.time_left -= dur;
                t.time_left -= dur;
                // у второго это тоже вылазка: и по дневному пределу, и по тому,
                // что дом видит его вернувшимся с пакетами
                mark(h, t, "вылазка", null);
                npc.часы_работы += dur;
                t.часы_работы += dur;
                Улица._outing(h, npc, dur, м, спутник: t);
            }
            else
            {
                // отказ — такой же настоящий исход, как согласие. Второй
                // ничего не теряет, а звавший запоминает, что с ним не пошли
                Социальное.отдалились(npc, t.id, b["близость_за_отказ"]);
                npc.ask_record(t.id).отказ_зов = h.day;
                npc.mood = Util.Clamp(npc.mood - 4);
                h.bump("отказов_идти");
                h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "звал")} {t.form("acc")} "
                    + $"{м.куда}. {t.@short} не {Util.Vb(t.sex, "пошёл")}.", 1);
            }
            return null;
        });

        исполняет("отдать_на_ночь", (h, npc, target, spent) =>
        {
            var b = h.B;
            var t = target as NPC;
            var р = npc.слабейший_ребёнок();
            if (р is null || !string.IsNullOrEmpty(р.у) || t is null || !t.здесь())
            {
                // некому и некого
            }
            else if (Решение.по_правилу(h, t, Вопрос.ВЗЯТЬ_РЕБЁНКА_НА_НОЧЬ,
                         "взять", "отказать", возьмёт_ребёнка(h, npc, t, р, b)))
            {
                р.у = t.id;
                // и кормит его тот, к кому отнесли: те же 0.6 рта,
                // что он ест дома
                double need = b["ребёнок_рот"];
                double used = Ресурсы.Spend(h, t, "еда", need);
                р.сытость = Util.Clamp(р.сытость + b["ребёнок_еда_за_порцию"]
                                       * Math.Min(1.0, used / need));
                // для матери это тяжелее любой просьбы: она отдаёт то, ради
                // чего живёт. Нормальность падает не потому, что поступок
                // плохой, а потому, что до такого дошло
                Социальное.переступил(h, npc, "отдать_на_ночь");
                Социальное.сблизились(h, npc, t, b["близость_за_ночёвку"]);
                npc.mood = Util.Clamp(npc.mood - 6);
                t.mood = Util.Clamp(t.mood + 4);
                h.bump("ночей_у_чужих");
                h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "отнёс")} {р.вин} "
                               + $"к {t.form("dat")} на ночь — там топят.", 2);
                h.note($"{р.имя} ночует у {t.form("gen")}");
            }
            else
            {
                Социальное.отдалились(npc, t.id, b["близость_за_отказ"]);
                npc.ask_record(t.id).отказ_ночёвка = h.day;
                npc.mood = Util.Clamp(npc.mood - 8);
                h.bump("отказов_взять_ребёнка");
                h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "просил")} {t.form("acc")} "
                    + $"взять {р.вин} на ночь. {t.@short} не "
                    + $"{Util.Vb(t.sex, "взял")}.", 2);
            }
            return null;
        });

        // --- улица и кладовые ---

        исполняет("вылазка", (h, npc, target, spent) =>
        {
            Улица._outing(h, npc, spent, target as Место);
            return null;  // текст пишет сам _outing
        });

        исполняет("уйти", (h, npc, target, spent) =>
        {
            var b = h.B;
            // один бросок по четырём вещам, и все четыре уже есть в модели.
            // Исходов два: повернул с полпути или ушёл. Третьего нет —
            // «дошёл» и «замёрз по дороге» это не два исхода для дома, а один
            double дойдёт = Util.Clamp(b["пункт_база"]
                - h.снег * b["пункт_за_снег"]
                - Math.Max(0.0, -h.outside - 15) * b["пункт_за_мороз"]
                + npc.одежда * b["пункт_за_одежду"]
                - (1.0 - npc.health / 100.0) * b["пункт_за_здоровье"]
                - npc.dependents * b["пункт_за_ребёнка"], 0.0, 1.0);
            if (h.rng.Chance(дойдёт))
            {
                Конфликт.уйти_из_дома(h, npc);
                return СДЕЛАНО_МОЛЧА;
            }
            // вернулся с полпути — самый частый исход и самый нужный: потерян
            // день, но человек жив и теперь ЗНАЕТ, что дороги нет. Это и есть
            // цена надежды, и это то, что делает вторую попытку решением
            npc.warmth = Util.Clamp(npc.warmth - b["уйти_возврат_тепло"]);
            npc.rest = Util.Clamp(npc.rest - b["уйти_возврат_сон"]);
            npc.дорога_закрыта = h.day;
            npc.возвращался += 1;
            h.bump("возвратов_с_полпути");
            bool обморозил = false;
            if (!npc.hurt("обморож") && h.rng.Chance(b["уйти_возврат_обморожение"]))
            {
                npc.injuries.Add(h.rng.Pick(new[] { "обморожение рук", "обморожение ног" })!);
                npc.health = Util.Clamp(npc.health - h.rng.Uni(6, 14));
                обморозил = true;
            }
            return $"{npc.@short} {Util.Vb(npc.sex, "вышел")} к школе №7 и "
                   + $"{Util.Vb(npc.sex, "вернулся")} затемно: до перекрёстка "
                   + "дорога есть, дальше стена по грудь"
                   + (обморозил ? "; отморозил пальцы" : "");
        });

        исполняет("проведать", (h, npc, target, spent) =>
        {
            var b = h.B;
            var t = (NPC)target!;
            // он вскрывает дверь и находит то, что находит. Дом узнаёт
            // о смерти задним числом и от него одного — а значит, он же
            // и первый, кого спросят, откуда он знает. Забота оплачивается
            // подозрением, и это ровно та цена, которая тут и бывает
            int сколько = h.day - (t.died_day ?? h.day);
            string дней = (сколько % 10 == 1 && сколько % 100 != 11) ? "день"
                : ((сколько % 10 >= 2 && сколько % 10 <= 4
                    && !(сколько % 100 >= 12 && сколько % 100 <= 14)) ? "дня" : "дней");
            // строка идёт ДО того, как он выйдет на площадку и скажет: иначе
            // в журнале сначала не верят вести, а потом находят тело
            h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "постучал")} в кв.{t.apt}, "
                + $"не {Util.Vb(npc.sex, "дождался")} и {Util.Vb(npc.sex, "открыл")} "
                + $"дверь {Util.Vb(npc.sex, "сам")}. "
                + $"{t.@short} {Util.Vb(t.sex, "мёртв")} уже {сколько} {дней}.", 2);
            Социальное.нашёл_тело(h, npc, t);
            npc.panic = Util.Clamp(npc.panic + b["проведать_паника"]);
            npc.mood = Util.Clamp(npc.mood - b["проведать_настроение"]);
            npc.bump("нашёл_тело");
            h.bump("тел_найдено");
            return null;
        });

        исполняет("кладовая", (h, npc, target, spent) =>
        {
            Улица._из_кладовой(h, npc, (Кладовая)target!, spent);
            return null;
        });

        исполняет("вскрыть_кладовую", (h, npc, target, spent) =>
        {
            var к = (Кладовая)target!;
            var got = Улица.вскрыть_кладовую(h, npc, к);
            string взято = got.Count > 0
                ? string.Join(", ", got.Ключи.Select(r => $"{r} {Текст.G(got.Взять(r, 0.0))}"))
                : "ничего";
            h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "сорвал")} замок с "
                + $"{(к.вид == ВидКладовой.ПОГРЕБ ? "погреба" : "гаража")} кв.{к.apt}: "
                + $"{взято}.", 2);
            h.note($"{npc.@short} вскрыл {к.имя}");
            return null;
        });

        исполняет("тело", (h, npc, target, spent) =>
        {
            var b = h.B;
            var подходят = h.пустые_для(npc)
                .Where(f => f.body is not null && f.body.порций > 0).ToList();
            if (подходят.Count == 0)
                return НЕ_СОСТОЯЛОСЬ;
            var flat = Ближе(подходят, npc);
            Социальное.вошёл_в_квартиру(h, npc, flat);
            double take = Math.Min(flat.body!.порций, 4.0);
            flat.body.порций -= take;
            flat.body.тронуто = true;
            npc.stock["мясо"] = npc.stock.Взять("мясо", 0.0) + take;
            h.stats["принесено_мясо"] = h.stats.Взять("принесено_мясо", 0.0) + take;
            npc.mood = Util.Clamp(npc.mood - b["людоедство_настроение"]);
            npc.panic = Util.Clamp(npc.panic + 12);
            npc.людоед = true;
            h.bump("людоедство");
            if (npc.раскрыт)
                // прятаться больше не от кого
                h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "ходил")} в кв.{flat.apt}. "
                               + "Уже не таясь.", 1);
            else
                h.journal.line(h.rng.Pick(new[]
                {
                    $"{npc.@short} {Util.Vb(npc.sex, "ходил")} в кв.{flat.apt} и "
                        + $"{Util.Vb(npc.sex, "вернулся")} с чем-то тяжёлым, "
                        + "завёрнутым в простыню.",
                    $"Ночью на лестнице долго возились. Утром дверь кв.{flat.apt} "
                        + "была приоткрыта.",
                    $"{npc.@short} {Util.Vb(npc.sex, "провёл")} полдня в кв.{flat.apt} "
                        + $"и не {Util.Vb(npc.sex, "сказал")}, зачем.",
                })!, 1);
            h.journal.secret($"{npc.@short} взял тело {flat.body.падеж} — "
                             + $"{Текст.G(take)} порц.");
            // мог кто-то увидеть на лестнице
            foreach (var other in h.others(npc))
            {
                if (other.away || string.Equals(other.id, npc.id, StringComparison.Ordinal))
                    continue;
                double seen = 0.14 + (Math.Abs(other.floor - flat.floor) <= 1 ? 0.12 : 0.0);
                if (h.rng.Chance(seen))
                {
                    Конфликт.reveal_taboo(h, npc, witness: other);
                    break;
                }
            }
            return null;
        });

        исполняет("вынести", (h, npc, target, spent) =>
        {
            var подходят = h.пустые_для(npc)
                .Where(f => f.body is not null && f.body.порций > 0).ToList();
            if (подходят.Count == 0)
                return НЕ_СОСТОЯЛОСЬ;
            var flat = Ближе(подходят, npc);
            Социальное.вошёл_в_квартиру(h, npc, flat);
            string name = flat.body!.вин.Length > 0 ? flat.body.вин : flat.body.кто;
            flat.body.порций = 0.0;
            npc.warmth = Util.Clamp(npc.warmth - 10);
            npc.mood = Util.Clamp(npc.mood - 6);
            foreach (var p in h.alive())
            {
                Социальное.adjust(p, npc.id, trust: 1.0);
                p.mood = Util.Clamp(p.mood + 3);
            }
            h.bump("тел_вынесено");
            h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "вынес")} {name} во двор и "
                + $"{Util.Vb(npc.sex, "завалил")} снегом. Больше в той квартире "
                + "брать нечего.", 2);
            return null;
        });

        // --- интрига и прочее ---

        исполняет("наблюдение", (h, npc, target, spent) =>
        {
            var t = (NPC)target!;
            Социальное.observe(h, npc, t);
            return $"{npc.@short} {Util.Vb(npc.sex, "присматривался")} "
                   + $"к кв.{h.хозяин_жилья(t).apt}";
        });

        исполняет("подкараулить", (h, npc, target, spent) =>
        {
            var t = (NPC)target!;
            h.сутки.караулят[t.id] = npc.id;
            var пара = string.CompareOrdinal(npc.id, t.id) <= 0
                       ? (npc.id, t.id) : (t.id, npc.id);
            h.засады[пара] = h.day;
            npc.караулил = h.day;
            h.bump("засад_поставлено");
            // дом этого не видит: человек сидит на своей же площадке, у него
            // есть на это полное право. Видит только тот, кто пойдёт и посмотрит
            h.journal.secret($"{npc.@short} {Util.Vb(npc.sex, "сел")} на площадке и "
                             + $"{Util.Vb(npc.sex, "ждал")} {t.form("acc")}.");
            return null;
        });

        исполняет("лестница", (h, npc, target, spent) =>
        {
            var b = h.B;
            h.сутки.смотрел_лестницу[npc.id] = h.day;
            NPC? караулит = null;
            foreach (string жертва_id in h.сутки.караулят.Ключи)
                if (string.Equals(жертва_id, npc.id, StringComparison.Ordinal))
                {
                    караулит = h.get(h.сутки.караулят.Взять(жертва_id, null!));
                    break;
                }
            if (караулит is not null && караулит.здесь())
            {
                // увидел. Сегодня он никуда не пойдёт — и это уже победа того,
                // кто сидит: чтобы отнять у человека день, необязательно его бить
                h.сутки.не_выходить[npc.id] = h.day;
                Социальное.испугался(h, npc, караулит, b["страх_за_насилие"]);
                Социальное.adjust(npc, караулит.id,
                                  hate: b["ненависть_за_налёт"] * 0.5, aware: 20);
                h.bump("засад_замечено");
                h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "приоткрыл")} дверь и "
                    + $"{Util.Vb(npc.sex, "постоял")}, слушая. На площадке кто-то был. "
                    + $"{Util.Vb(npc.sex, "Закрыл")} обратно.", 2);
                return null;
            }
            return $"{npc.@short} {Util.Vb(npc.sex, "постоял")} у двери, слушая "
                   + "площадку, прежде чем выйти";
        });

        исполняет("шепнуть", (h, npc, target, spent) =>
        {
            var b = h.B;
            var t = (NPC)target!;
            // то же вранье о человеке, что и в разговоре, но не от злости,
            // а по замыслу: и мишень, и слушатель выбраны заранее
            var з = npc.замысел;
            var враг = з is not null ? h.живой(з.враг) : null;
            if (враг is null || з is null)
                return НЕ_СОСТОЯЛОСЬ;
            Социальное.встретились(h, npc, t);
            double вес = Социальное.вес_слов(t, npc);
            string said;
            if (t.trust.Взять(враг.id, 3.0) >= b["наговор_защита_доверием"])
            {
                // слушатель этому человеку верит — и теперь косо смотрит
                // на шепчущего
                Социальное.adjust(t, npc.id, trust: -0.8, hate: 5);
                Социальное.отдалились(t, npc.id, b["близость_за_отказ"]);
                said = $"{npc.@short} {Util.Vb(npc.sex, "сказал")} {t.form("dat")} "
                       + $"кое-что про {враг.form("acc")}. {t.@short} не "
                       + $"{Util.Vb(t.sex, "поверил")}";
            }
            else
            {
                Социальное.adjust(t, враг.id, hate: b["наговор_злость"] * вес,
                                  trust: -0.5 * вес, aware: 4);
                Социальное.отдалились(t, враг.id, b["близость_за_отказ"] * вес);
                h.bump("наговоров");
                said = $"{npc.@short} {Util.Vb(npc.sex, "сказал")} {t.form("dat")} "
                       + $"кое-что про {враг.form("acc")} — вполголоса, на площадке";
            }
            Социальное.соврал(h, npc, t, "человек", враг.id);
            з.напор += b["напор_за_шёпот"];
            з.ходов += 1;
            h.bump("шёпотов");
            return said;
        });

        исполняет("подбросить", (h, npc, target, spent) =>
        {
            var b = h.B;
            var з = npc.замысел;
            var враг = target as NPC;
            string? чем = что_подбросить(h, npc);
            if (враг is null || чем is null || з is null)
                return НЕ_СОСТОЯЛОСЬ;
            Ресурсы.Spend(h, npc, чем, 1);
            h.ожидает.подброшено[враг.apt] = Ожидает.подброс(npc.id, чем, h.day);
            з.напор += b["напор_за_подброс"];
            з.ходов += 1;
            h.bump("подбросов");
            // журнал пишет только то, что видно дому; сам поступок — секрет
            h.journal.secret($"{npc.@short} {Util.Vb(npc.sex, "положил")} "
                + $"{(RES_ВИН.TryGetValue(чем, out var в) ? в : чем)} "
                + $"под дверь кв.{враг.apt}.");
            return null;
        });

        исполняет("отнять", (h, npc, target, spent) =>
        {
            var b = h.B;
            var victim = (NPC)target!;
            npc.bump("отъёмов");
            h.bump("отъёмов");
            h.событие(ВидСобытия.ОТЪЁМ, кто: npc.id, кому: victim.id, где: h.where(npc).apt);
            Социальное.нарушил(h, npc, victim, "не_делать", victim.id);
            // смысл разбоя в том, что слабый не сопротивляется. Решает жертва:
            // слабый отдаёт по правилу, остальные — монетой, как и было
            bool scared = victim.t01("храбрость") * 3.0 + victim.power() * 1.2
                          < npc.power() * 2.6;
            bool отдал = scared
                ? Решение.по_правилу(h, victim, Вопрос.ОТДАТЬ_НА_ЛЕСТНИЦЕ,
                                     "отдать", "упереться", 1.0)
                : Решение.монета(h, victim, Вопрос.ОТДАТЬ_НА_ЛЕСТНИЦЕ,
                                 "отдать", "упереться", 0.75);
            if (отдал)
            {
                var moved = Конфликт.take_carried(h, victim, npc, limit: b["отъём_максимум"]);
                h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "зажал")} "
                    + $"{victim.form("acc")} на лестнице и {Util.Vb(npc.sex, "забрал")} "
                    + $"{Конфликт._fmt(moved)}.", 2);
                victim.mood = Util.Clamp(victim.mood - 14);
                victim.panic = Util.Clamp(victim.panic + 16);
            }
            else
            {
                h.journal.line($"{npc.@short} {Util.Vb(npc.sex, "полез")} к "
                    + $"{victim.form("dat")} на лестнице — {victim.@short} не "
                    + $"{Util.Vb(victim.sex, "отдал")}.", 2);
                bool won = Конфликт.scuffle(h, npc, victim, place: "на лестнице");
                if (won)
                    Конфликт.take_carried(h, victim, npc, limit: b["отъём_максимум"] * 0.5);
            }
            // заодно вытряхивают карманы: ключи стоят дороже банки тушёнки
            if (victim.guests.Count == 0 && string.IsNullOrEmpty(victim.living_with)
                && Конфликт.хочу_его_квартиру(h, npc, victim) > 0
                && h.rng.Chance(b["ключи_шанс"]))
            {
                npc.ключи.Add(victim.apt);
                h.journal.line($"   Ключи от кв.{victim.apt} "
                               + $"{Util.Vb(npc.sex, "забрал")} тоже.", 2);
                h.bump("ключей_отнято");
                // на той же связке висит и ключ от погреба. Это самое дорогое,
                // что можно вытряхнуть из чужого кармана: не банка, а доступ
                foreach (string kid in victim.ключи_кладовых
                             .OrderBy(x => x, StringComparer.Ordinal).ToList())
                {
                    var к = h.кладовые.Взять(kid, null);
                    victim.ключи_кладовых.Remove(kid);
                    npc.ключи_кладовых.Add(kid);
                    h.bump("ключей_от_кладовых_отнято");
                    if (к is not null)
                        h.journal.line($"   И ключ от {к.имя_род} — с той же связки.", 2);
                }
            }
            // и нож из кармана. За оружием на лестницу и ходят: у того, кто
            // зажал соседа, оно обычно уже есть, а у того, кто пришёл за ним, — нет
            var отнял = Конфликт.отнять_оружие(h, victim, npc, b["оружие_при_отъёме"]);
            if (отнял is not null)
                h.journal.line($"   И "
                    + $"{(Таблицы.ОРУЖИЕ_ВИН.TryGetValue(отнял.Value, out var о) ? о : отнял.Value.Текст())} "
                    + $"— {Util.Vb(npc.sex, "забрал")} тоже.", 2);
            Социальное.встретились(h, npc, victim);
            Социальное.обидели(h, victim, npc, b["обида_за_отъём"]);
            Социальное.adjust(victim, npc.id, trust: -5.0,
                              hate: b["ненависть_за_налёт"] * 0.8, aware: 15);
            Социальное.отдалились(victim, npc.id, b["близость_за_обиду"]);
            Социальное.испугался(h, victim, npc, b["страх_за_насилие"]);
            Социальное.register_incident(h, "отъём", null);
            // это видят и слышат: разбой в подъезде не спрячешь
            var видели = h.others(npc)
                .Where(w => !string.Equals(w.id, victim.id, StringComparison.Ordinal)
                            && h.rng.Chance(0.8)).ToList();
            // и видят, с чем он к человеку полез
            Социальное.увидел_оружие(h, victim, npc, свидетели: видели);
            foreach (var w in видели)
            {
                Социальное.adjust(w, npc.id, aware: 8);
                w.panic = Util.Clamp(w.panic + 7);
            }
            // разбой каждый мерит своей меркой (GDD 12.1, «Ценности»)
            Социальное.judge(h, npc, Каталог.ТЕГИ.Взять("отнять", Array.Empty<string>()),
                             hate: 20 + 12 * 0.5, trust: -2.5, witnesses: видели);
            return null;
        });

        исполняет("кража_днём", (h, npc, target, spent) =>
        {
            var t = (NPC)target!;
            Социальное.вошёл_в_квартиру(h, npc, h.flats[t.apt]);
            Конфликт.steal(h, npc, t);
            return null;
        });

        исполняет("собрание", (h, npc, target, spent) =>
        {
            Площадка.провести(h, npc);
            return null;
        });
    }

    /// <summary>Ближайшая по этажу из подходящих. `min` в Python берёт первую
    /// из равных — здесь то же самое.</summary>
    private static Flat Ближе(IReadOnlyList<Flat> где, NPC npc)
    {
        var лучший = где[0];
        foreach (var f in где)
            if (Math.Abs(f.floor - npc.floor) < Math.Abs(лучший.floor - npc.floor))
                лучший = f;
        return лучший;
    }
}
