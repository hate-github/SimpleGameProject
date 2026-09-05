# -*- coding: utf-8 -*-
"""Вывод: журнал дня, чат жильцов, панель скрытых шкал и финальный разбор.

Панель шкал — это тот самый отладочный экран, без которого нельзя понять,
почему сосед пришёл с дубиной. Здесь видно всё, что игрок видеть не должен.
"""
import sys

from .util import clamp, vb

BAR = "─" * 78


class Journal:
    def __init__(self, verbosity=1, secrets=False, stream=None):
        self.verbosity = verbosity      # 0 — только крупное, 1 — обычно, 2 — каждое действие
        self.secrets = secrets
        self.stream = stream or sys.stdout
        self.buf = []

    # --- запись ---
    def line(self, text, importance=1, hidden=False):
        if hidden and not self.secrets:
            return
        self.buf.append((importance, text))

    def event(self, text, scripted=False):
        mark = "◆" if scripted else "◇"
        self.buf.append((2, f"{mark} {text}"))

    def secret(self, text):
        if self.secrets:
            self.buf.append((1, f"    ⌁ {text}"))

    def chat(self, who, text):
        self.buf.append((1, f"  [чат] {who}: {text}"))

    # --- вывод ---
    def w(self, s=""):
        self.stream.write(s + "\n")

    def flush_day(self, h):
        weather = f"{h.outside:+.0f}°C"
        infra = []
        if not h.heating:
            infra.append("без отопления")
        if not h.water_on:
            infra.append("без воды")
        if not h.power_on:
            infra.append("без света")
        if h.network <= 0:
            infra.append("без связи")
        tail = (" · " + ", ".join(infra)) if infra else ""
        режим = h.mods.get("режим", "метель")
        self.w()
        self.w(f"══ ДЕНЬ {h.day} · {режим} · {weather}{tail} "
               + "═" * max(0, 40 - len(tail) - (len(режим) - 6)))
        shown = [t for imp, t in self.buf if imp >= (2 if self.verbosity == 0 else (1 if self.verbosity == 1 else 0))]
        for t in shown:
            prefix = "  "
            self.w(prefix + t)
        self.buf.clear()

    def сводка_дня(self, h):
        """Что заметили соседи (GDD 4.4).

        Главный инструмент обучения по документу: игрок должен понимать,
        чем живёт дом, до того как это его убьёт. Формулировки написаны
        в NOISE_MEANING и до сих пор никуда не выводились.
        """
        if self.verbosity < 1:
            return
        сводка = h.mods.pop("сводка", {})
        for pid in sorted(сводка):
            p = h.people.get(pid)
            if not p or not p.alive or p.exiled:
                continue
            строки = сводка[pid][:3]
            if строки:
                self.line(f"{p.short} за день заметил{'а' if p.sex == 'ж' else ''}: "
                          + "; ".join(строки), 1)

    def panel(self, h):
        """Скрытые шкалы — для дизайнера, не для игрока."""
        if self.verbosity < 1:
            return
        self.w("  " + "┄" * 74)
        for p in sorted(h.people.values(), key=lambda x: x.apt):
            if not p.alive:
                self.w(f"  {p.short:<8} кв{p.apt:<3} † {p.cause} (день {p.died_day})")
                continue
            if p.exiled:
                как = "ушёл к пункту обогрева" if p.ушёл else "изгнан"
                self.w(f"  {p.short:<8} кв{p.apt:<3} — {как} (день {p.died_day})")
                continue
            st = p.stock
            top_hate = max(((k, v) for k, v in p.hate.items() if v > 25), key=lambda kv: kv[1], default=None)
            top_aware = max(((k, v) for k, v in p.aware.items() if v > 45), key=lambda kv: kv[1], default=None)
            marks = []
            if top_hate:
                marks.append(f"злость→{h.people[top_hate[0]].short} {int(top_hate[1])}")
            if top_aware:
                marks.append(f"знает о {h.people[top_aware[0]].short} {int(top_aware[1])}")
            страшный = p.самый_страшный()
            if страшный and страшный[1] > 20:
                marks.append(f"боится {h.people[страшный[0]].short} {int(страшный[1])}")
            дыры = h.where(p).дыры
            if дыры:
                marks.append("дыры: " + "+".join(sorted(дыры)))
            # шахта обмёрзла: то, чего человек про свою квартиру не знает,
            # а дизайнеру видеть надо — иначе угар выглядит как случайность
            вент = h.where(p).вентиляция
            if вент < 0.5:
                marks.append(f"вытяжка {вент:.2f}")
            if p.allies:
                marks.append("союз: " + "+".join(h.people[a].short for a in sorted(p.allies)))
            if p.group:
                marks.append(p.group)
            if p.injuries:
                marks.append("/".join(p.injuries))
            if p.sick:
                marks.append(p.sick)
            for р in p.дети:
                marks.append(f"{р['имя']}: сыт{int(р['сытость'])} тепл{int(р['тепло'])}"
                             f" здор{int(р['здоровье'])}"
                             + (f" ({р['болен']})" if р["болен"] else ""))
            self.w(
                f"  {p.short:<8} кв{p.apt:<3}"
                f" сыт{int(p.satiety):>3} вод{int(p.hydration):>3} тепл{int(p.warmth):>3} сон{int(p.rest):>3}"
                f" настр{int(p.mood):>3} здор{int(p.health):>3} паника{int(p.panic):>3}"
                f" │ еда {st.get('еда',0):>4.1f} топл {st.get('топливо',0):>4.1f}"
                f" мат {st.get('материалы',0):>3.0f} лек {st.get('лекарства',0):>2.0f}"
                f" нал {st.get('деньги',0):>3.0f}" + (f"+{p.счёт:g}" if h.банки and p.счёт else "")
                + (" │ " + ", ".join(marks) if marks else "")
            )


def daily_chat(h, lines):
    """Общий чат жильцов (GDD 19: «основной канал общения и слухов»).

    Чат не декорация: каждая реплика что-то делает с домом, иначе обрыв связи
    на десятый день ничего не меняет, а GDD 12.5 обещает, что об этом «игрок
    узнаёт из чата». Темы соответствуют разд. 14: припасы, безопасность,
    подозрения, личное.
    """
    from . import social, world
    if h.network <= 0:
        return
    b = h.B
    rng = h.rng
    people = h.alive()
    if not people:
        return
    said = 0
    сказанное = set()          # одну и ту же фразу за день дважды не пишут
    limit = 2 if h.network > 0.5 else 1
    talkers = sorted(people, key=lambda p: -p.trait("общительность"))
    for p in talkers:
        if said >= limit:
            break
        key = None
        # пороги подобраны под то, что чат живёт лишь до десятого дня (GDD 19):
        # с прежними «паника > 70» и «настроение < 35» обе темы не звучали
        # ни разу за сорок прогонов — до таких значений дом доходит уже
        # без связи, и девять реплик из двадцати шести были мёртвым текстом
        if p.panic > 45:
            key = "паника"
        elif p.desperation() > 0.6:
            key = "просьба"
        elif h.day - p.stats.get("день_беседы", -99) >= b["чат_одиночество_дней"]:
            # тоска в чате — про одиночество, а не про шкалу настроения:
            # к тому дню, когда настроение падает, связи уже нет. «Напишите
            # хоть кто-нибудь» пишет тот, с кем второй день никто не заговорил.
            #
            # И стоит она выше подозрения нарочно. Пока она была последней
            # в цепочке, до неё доходили дважды за шестьдесят жизней: самые
            # общительные, а говорят в чате именно они, разговаривают каждый
            # день, и условие «сегодня со мной никто не заговорил» у них
            # не выполнялось почти никогда
            key = "тоска"
        elif h.incidents and rng.chance(0.5):
            key = "подозрение"
        elif p.mood < b["чат_тоска_настроение"]:
            key = "тоска"
        elif rng.chance(0.35):
            key = "быт"
        if not key:
            continue
        variants = [в for в in world.подходящие(h, lines.get("чат", {}).get(key, []))
                    if в not in сказанное]
        if not variants:
            continue
        шаблон = rng.pick(variants)
        text = шаблон
        others = [o for o in people if o.id != p.id]
        who = rng.pick(others) if others else p
        if key == "подозрение" and others:
            # вслух называют не случайного соседа, а того, на кого сам думаешь:
            # по злости, по недоверию и по тому, что успел о нём узнать.
            # Раньше здесь стоял ровный жребий, и Лида с лояльностью 9 могла
            # при всём доме назвать вором человека, о котором ничего не знает,
            # — а дом это запоминал и потом на него же и думал (conflict.suspect)
            who = rng.weighted([
                (o, max(0.05, 1.0 + p.hate.get(o.id, 0.0) / 15.0
                        + (5.0 - p.trust.get(o.id, 3.0)) * 0.5
                        + p.confidence(o.id) * 1.5
                        + o.stats.get("поймали", 0) * 2.0))
                for o in others])
        text = text.replace("{кто}", who.short).replace("{кв}", str(who.apt))
        if not rng.chance(0.55 + 0.04 * p.trait("общительность")):
            continue
        h.journal.chat(p.short, text)
        сказанное.add(шаблон)
        said += 1

        # --- а вот теперь то, чего у чата не было: последствия ---
        слышат = [o for o in others if rng.chance(h.network)]
        if key == "просьба":
            # «у меня кончается» — это заявление на весь подъезд
            for o in слышат:
                social.adjust(o, p.id, aware=b["чат_осведомлённость"])
                social.note_signal(o, p.id, "еда", 0.5, 0.3)
                social.add_panic(o, b["чат_паника_от_просьбы"])
        elif key == "паника":
            for o in слышат:
                social.add_panic(o, b["чат_паника"] * (0.7 + 0.6 * o.t01("вспыльчивость")))
        elif key == "подозрение":
            # реплика называет соседа по имени — и дом это запоминает
            if who.id != p.id:
                social.judge(h, who, "воровство",
                             hate=b["чат_подозрение_ненависть"], trust=-0.4,
                             witnesses=слышат)
                social.adjust(p, who.id, hate=b["чат_подозрение_ненависть"], trust=-0.5)
                h.mods.setdefault("названы_в_чате", {})[who.id] = h.day
                h.bump("обвинений_в_чате")
        elif key == "тоска":
            for o in слышат:
                o.mood = clamp(o.mood + b["чат_настроение"])
                social.adjust(o, p.id, trust=b["чат_доверие"])
        elif key == "быт":
            for o in слышат:
                o.mood = clamp(o.mood + b["чат_настроение"] * 0.5)
                social.adjust(o, p.id, trust=b["чат_доверие"] * 0.5)
        p.stats["день_разговора"] = h.day


# ---------------------------------------------------------------- финал

def final_report(h, days, seed, w=None):
    w = w or h.journal.w
    alive = [p for p in h.people.values() if p.alive and not p.exiled]
    ушли = [p for p in h.people.values() if p.ушёл]
    dead = [p for p in h.people.values()
            if (not p.alive or p.exiled) and not p.ушёл]
    w("")
    w("═" * 78)
    w(f"ИТОГ. {days} дней, зерно {seed}")
    w("═" * 78)
    w("")
    w(f"Выжили ({len(alive)}):")
    for p in sorted(alive, key=lambda x: x.apt):
        state = []
        if p.injuries:
            state.append(", ".join(p.injuries))
        if p.sick:
            state.append(p.sick)
        if p.dependents:
            state.append(f"с ребёнком ({p.dependent_name})")
        w(f"  {p.short:<8} кв{p.apt:<3} здоровье {int(p.health):>3}, настроение {int(p.mood):>3}"
          f", еды на {p.days_of('еда'):.1f} дн" + (" · " + "; ".join(state) if state else ""))
    if dead:
        w("")
        w(f"Погибли ({len(dead)}):")
        for p in sorted(dead, key=lambda x: x.died_day or 0):
            w(f"  день {p.died_day:>2} — {p.short:<8} {p.cause}")
    if ушли:
        # третья строка итога, и она нарочно не сливается ни с одной из двух.
        # Дошёл человек или замёрз на объездной — изнутри подъезда это
        # выглядит одинаково, и правда видна только с `--секреты`
        w("")
        w(f"Ушли ({len(ушли)}):")
        for p in sorted(ушли, key=lambda x: x.died_day or 0):
            хвост = ""
            if h.journal.secrets:
                хвост = ("   ⌁ " + vb(p.sex, "дошёл") if p.stats.get("дошёл")
                         else "   ⌁ " + vb(p.sex, "замёрз") + " на объездной")
            w(f"  день {p.died_day:>2} — {p.short:<8} {p.cause}{хвост}")
    w("")
    w("Хроника дома:")
    for c in h.chronicle:
        w("  " + c)
    w("")
    s = h.stats
    w("Счётчики:")
    order = ["попыток_кражи", "краж", "краж_сорвано", "налётов", "проломов",
             "вскрытых_квартир", "убийств", "выстрелов",
             "изгнаний", "смертей", "переездов", "союзов_заключено",
             "союзов_распалось", "обменов",
             "помощи", "отказов", "ложных_обвинений", "детей_брошено"]
    for k in order:
        if s.get(k):
            w(f"  {k.replace('_', ' '):<20} {s[k]}")
    w("")
    w("Деньги (GDD 18, в тысячах):")
    налом = sum(p.stock.get("деньги", 0.0) for p in h.people.values())
    налом += sum(f.stock.get("деньги", 0.0) for f in h.flats.values())
    сгорело = sum(p.счёт for p in h.people.values())
    w(f"  курс на конец:       {h.курс():.2f} (1.00 — как до метели)")
    w(f"  потрачено в магазине: картой {s.get('потрачено_картой', 0.0):.1f}, "
      f"налом {s.get('потрачено_налом', 0.0):.1f}")
    w(f"  снято в банкоматах:  {s.get('снято_наличных', 0.0):.0f}")
    w(f"  сгорело на картах:   {сгорело:.0f}")
    w(f"  наличных в доме:     {налом:.0f}"
      + ("  — и на них уже ничего не купить" if h.курс() <= 0.0 else ""))
    if s.get("тулупов_куплено"):
        w(f"  тулупов куплено:     {s['тулупов_куплено']}")
    w("")
    w("Группы на конец:")
    for p in sorted(alive, key=lambda x: x.apt):
        w(f"  {p.short:<8} {p.group or '—'}"
          + (f"  союз: {'+'.join(h.people[a].short for a in sorted(p.allies))}" if p.allies else ""))
    w("")
    w("Репутация щедрости (как её видит дом, 0 — «не даёт никогда», 1 — «даёт всегда»):")
    for p in sorted(h.people.values(), key=lambda x: x.apt):
        opinions = [o.generosity(p.id) for o in h.people.values()
                    if o.id != p.id and o.asking.get(p.id, {}).get("дали", 0)
                    + o.asking.get(p.id, {}).get("отказали", 0) > 0]
        if opinions:
            w(f"  {p.short:<8} {sum(opinions)/len(opinions):.2f}"
              f"   (о нём судят {len(opinions)} чел.)")
    w("")
    w("Матрица отношений (доверие 0-10 / ненависть 0-100 / осведомлённость 0-100):")
    ids = [p.id for p in sorted(h.people.values(), key=lambda x: x.apt)]
    head = " " * 10 + "".join(f"{h.people[i].short[:6]:>16}" for i in ids)
    w("  " + head)
    for a_id in ids:
        a = h.people[a_id]
        row = f"  {a.short[:8]:<8}  "
        for b_id in ids:
            if a_id == b_id:
                row += f"{'·':>16}"
            else:
                row += f"{a.trust.get(b_id,3):>5.1f}/{int(a.hate.get(b_id,0)):>3}/{int(a.aware.get(b_id,0)):>3}"
        w(row)
    w("")
    w("Кого в доме боятся (страх 0-100, четвёртая шкала GDD 12.3):")
    боятся = False
    for a in sorted(h.people.values(), key=lambda x: x.apt):
        пары = sorted(((h.people[k].short, v) for k, v in a.страх.items() if v >= 20),
                      key=lambda x: -x[1])
        if пары:
            боятся = True
            w(f"  {a.short:<8} " + ", ".join(f"{n} {int(v)}" for n, v in пары))
    if not боятся:
        w("  никто никого всерьёз не боится")
    дыры = [(f.apt, f.дыры) for f in sorted(h.flats.values(), key=lambda x: x.apt) if f.дыры]
    if дыры:
        w("")
        w("Проломы (GDD 16: чем вошли — то и осталось в стенах):")
        for apt, d in дыры:
            w(f"  кв.{apt:<3} " + ", ".join(f"{k} ×{v}" for k, v in sorted(d.items()))
              + f"   (−{h.flats[apt].потери_тепла(h.B):.1f}°)")
    if h.кладовые:
        w("")
        w("Кладовые (погреба и гаражи — имущество за порогом квартиры):")
        for к in sorted(h.кладовые.values(), key=lambda x: x.id):
            хозяин = h.хозяин_кладовой(к)
            ключи = sorted(p.short for p in h.people.values()
                           if к.id in p.ключи_кладовых)
            осталось = ", ".join(f"{r} {v:g}" for r, v in sorted(к.stock.items()) if v)
            w(f"  {к.имя:<12} {'ВСКРЫТА' if к.вскрыта else 'заперта':<8} "
              f"ходок {к.ходок:<3} осталось: {осталось or 'пусто'}"
              + (f"; ключ у {', '.join(ключи)}" if ключи else "; ключ потерян")
              + (f"; числится за {хозяин.short}" if хозяин else ""))
    w("")
    w("Диагностика (для настройки, не для игрока):")
    w(f"  первое происшествие: {'день ' + str(h.first_incident_day) if h.first_incident_day else 'не было'}")
    w(f"  первый налёт:        {'день ' + str(s['первый_налёт_день']) if s.get('первый_налёт_день') else 'не было'}")
    w(f"  первая смерть:       {'день ' + str(s['первая_смерть_день']) if s.get('первая_смерть_день') else 'никто не умер'}")
    w(f"  первый союз:         {'день ' + str(s['первый_союз_день']) if s.get('первый_союз_день') else 'не сложился'}")
    w(f"  первое вскрытие:     {'день ' + str(s['первое_вскрытие']) if s.get('первое_вскрытие') else 'не было'}"
      f"   (умыслом {s.get('вскрыто_умыслом', 0)}, попутно {s.get('вскрыто_попутно', 0)})")
    w(f"  снег во дворе:       {h.снег:.2f} м")
    w(f"  первая попытка уйти: {'день ' + str(s['первая_попытка_уйти']) if s.get('первая_попытка_уйти') else 'не было'}"
      f"   (ушли {s.get('ушедших', 0)}, вернулись с полпути {s.get('возвратов_с_полпути', 0)})")
    узнали = s.get("смертей_дом_узнал", 0)
    задержка = s.get("задержка_известия", 0)
    w(f"  дом узнал о смертях: {узнали} из {s.get('смертей', 0)}"
      + (f", в среднем через {задержка / узнали:.1f} дня" if узнали else ""))
    # только там, где кто-то живёт: в брошенной квартире шахта мёрзнет
    # сама по себе и никого этим не касается
    жилые = {h.where(p).apt for p in alive}
    вент = [f.вентиляция for f in h.flats.values()
            if f.apt in жилые and f.вентиляция < 0.5]
    if вент:
        w(f"  вытяжка ниже половины: в {len(вент)} квартирах "
          f"(худшая {min(вент):.2f}); угорело {s.get('смертей_от_угара', 0)}")
    w(f"  богатство района:    {h.scav_richness:.2f} (стартовало с 1.00)")
    from .actions import МЕСТА
    w("  что осталось где:    " + ", ".join(
        f"{м.имя} {h.богатство_места(м.имя):.2f}" for м in МЕСТА))
    w(f"  средняя паника:      {sum(p.panic for p in alive)/len(alive):.0f}" if alive else "  средняя паника: —")
