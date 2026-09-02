# -*- coding: utf-8 -*-
"""Вывод: журнал дня, чат жильцов, панель скрытых шкал и финальный разбор.

Панель шкал — это тот самый отладочный экран, без которого нельзя понять,
почему сосед пришёл с дубиной. Здесь видно всё, что игрок видеть не должен.
"""
import sys

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
        self.w()
        self.w(f"══ ДЕНЬ {h.day} · метель · {weather}{tail} " + "═" * max(0, 40 - len(tail)))
        shown = [t for imp, t in self.buf if imp >= (2 if self.verbosity == 0 else (1 if self.verbosity == 1 else 0))]
        for t in shown:
            prefix = "  "
            self.w(prefix + t)
        self.buf.clear()

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
                self.w(f"  {p.short:<8} кв{p.apt:<3} — изгнан (день {p.died_day})")
                continue
            st = p.stock
            top_hate = max(((k, v) for k, v in p.hate.items() if v > 25), key=lambda kv: kv[1], default=None)
            top_aware = max(((k, v) for k, v in p.aware.items() if v > 45), key=lambda kv: kv[1], default=None)
            marks = []
            if top_hate:
                marks.append(f"злость→{h.people[top_hate[0]].short} {int(top_hate[1])}")
            if top_aware:
                marks.append(f"знает о {h.people[top_aware[0]].short} {int(top_aware[1])}")
            if p.allies:
                marks.append("союз: " + "+".join(h.people[a].short for a in sorted(p.allies)))
            if p.group:
                marks.append(p.group)
            if p.injuries:
                marks.append("/".join(p.injuries))
            if p.sick:
                marks.append(p.sick)
            self.w(
                f"  {p.short:<8} кв{p.apt:<3}"
                f" сыт{int(p.satiety):>3} вод{int(p.hydration):>3} тепл{int(p.warmth):>3} сон{int(p.rest):>3}"
                f" настр{int(p.mood):>3} здор{int(p.health):>3} паника{int(p.panic):>3}"
                f" │ еда {st.get('еда',0):>4.1f} топл {st.get('топливо',0):>4.1f}"
                f" мат {st.get('материалы',0):>3.0f} лек {st.get('лекарства',0):>2.0f}"
                + (" │ " + ", ".join(marks) if marks else "")
            )


def daily_chat(h, lines):
    """Общий чат жильцов (GDD 19). Пока есть связь — главный канал слухов."""
    if h.network <= 0:
        return
    rng = h.rng
    people = h.alive()
    if not people:
        return
    said = 0
    limit = 2 if h.network > 0.5 else 1
    talkers = sorted(people, key=lambda p: -p.trait("общительность"))
    for p in talkers:
        if said >= limit:
            break
        key = None
        if p.panic > 70:
            key = "паника"
        elif p.desperation() > 0.6:
            key = "просьба"
        elif h.incidents and rng.chance(0.5):
            key = "подозрение"
        elif p.mood < 35:
            key = "тоска"
        elif rng.chance(0.35):
            key = "быт"
        if not key:
            continue
        variants = lines.get("чат", {}).get(key, [])
        if not variants:
            continue
        text = rng.pick(variants)
        others = [o for o in people if o.id != p.id]
        who = rng.pick(others) if others else p
        text = text.replace("{кто}", who.short).replace("{кв}", str(who.apt))
        if rng.chance(0.55 + 0.04 * p.trait("общительность")):
            h.journal.chat(p.short, text)
            said += 1


# ---------------------------------------------------------------- финал

def final_report(h, days, seed, w=None):
    w = w or h.journal.w
    alive = [p for p in h.people.values() if p.alive and not p.exiled]
    dead = [p for p in h.people.values() if not p.alive or p.exiled]
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
    w("")
    w("Хроника дома:")
    for c in h.chronicle:
        w("  " + c)
    w("")
    s = h.stats
    w("Счётчики:")
    order = ["попыток_кражи", "краж", "краж_сорвано", "налётов", "убийств", "выстрелов",
             "изгнаний", "смертей", "союзов_заключено", "союзов_распалось", "обменов",
             "помощи", "отказов", "ложных_обвинений", "детей_брошено"]
    for k in order:
        if s.get(k):
            w(f"  {k.replace('_', ' '):<20} {s[k]}")
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
    w("Диагностика (для настройки, не для игрока):")
    w(f"  первое происшествие: {'день ' + str(h.first_incident_day) if h.first_incident_day else 'не было'}")
    w(f"  первый налёт:        {'день ' + str(s['первый_налёт_день']) if s.get('первый_налёт_день') else 'не было'}")
    w(f"  первая смерть:       {'день ' + str(s['первая_смерть_день']) if s.get('первая_смерть_день') else 'никто не умер'}")
    w(f"  первый союз:         {'день ' + str(s['первый_союз_день']) if s.get('первый_союз_день') else 'не сложился'}")
    w(f"  богатство района:    {h.scav_richness:.2f} (стартовало с 1.00)")
    w(f"  средняя паника:      {sum(p.panic for p in alive)/len(alive):.0f}" if alive else "  средняя паника: —")
