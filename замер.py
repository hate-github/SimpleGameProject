# -*- coding: utf-8 -*-
"""Замер того, чем меряются этапы ПЛАН.md. Не проверка, а линейка.

    python замер.py                  — 40 прогонов
    python замер.py --прогонов 60    — как в разборе
    python замер.py --дней 30

`batch.py` показывает, что дом делает; `check.py` — что он делает без ошибок.
Здесь — ровно те числа, которыми в плане назначены цели: доля ранних краж,
запас у вора, настроение и нормальность по дням, разброс между людьми,
день первого крайнего поступка у каждого.

Ничего не патчится насовсем: наблюдатель ставится на время прогонов тем же
приёмом, что `checks.Coverage`, и снимается после.
"""
import argparse
import io
import statistics as st
import sys

from house import actions, conflict
from house.engine import Simulation

# поступки, до которых в обычной жизни не доходят (по actions.НОРМА >= 1.0)
КРАЙНИЕ_ДНЁМ = ("разбор", "дверь", "отнять", "кража_днём", "тело")
# дни, на которые смотрит план
ДНИ_СНИМКА = (1, 5, 7, 10, 14, 15, 20, 25, 30)


class Наблюдатель:
    """Собирает то, чего нет в h.stats: кто, что и когда именно сделал."""

    def __init__(self):
        self.кражи = []        # по одной записи на ночную кражу
        self.крайние = {}      # (зерно, id) -> первый день крайнего поступка
        self.вылазки = set()   # (зерно, день) — в этот день кто-то выходил
        self.дни = {}          # (зерно, день) -> снимок дома
        self.зерно = None

    # --- установка ---
    def __enter__(self):
        self._execute = actions.execute
        self._steal = conflict.steal
        self._kill = conflict.убить_соседа
        self._siege = conflict.run_siege
        self._day = Simulation.one_day

        def execute(h, npc, key, target):
            if key in КРАЙНИЕ_ДНЁМ:
                self._крайний(h, npc)
            if key == "вылазка":
                self.вылазки.add((self.зерно, h.day))
            return self._execute(h, npc, key, target)

        def steal(h, thief, target):
            self.кражи.append({
                "зерно": self.зерно, "день": h.day, "кто": thief.id,
                "еда_дней": thief.days_of("еда"),
                "топливо_дней": thief.days_of("топливо"),
                "ненависть": thief.hate.get(target.id, 0.0),
                "отчаяние": thief.desperation(),
                "нормальность": thief.normalcy,
            })
            self._крайний(h, thief)
            return self._steal(h, thief, target)

        def убить_соседа(h, killer, victim):
            self._крайний(h, killer)
            return self._kill(h, killer, victim)

        def run_siege(h, leader, target):
            self._крайний(h, leader)
            return self._siege(h, leader, target)

        def one_day(sim):
            r = self._day(sim)
            self._снимок(sim.h)
            return r

        actions.execute = execute
        conflict.steal = steal
        conflict.убить_соседа = убить_соседа
        conflict.run_siege = run_siege
        Simulation.one_day = one_day
        return self

    def __exit__(self, *exc):
        actions.execute = self._execute
        conflict.steal = self._steal
        conflict.убить_соседа = self._kill
        conflict.run_siege = self._siege
        Simulation.one_day = self._day
        return False

    # --- сбор ---
    def _крайний(self, h, npc):
        ключ = (self.зерно, npc.id)
        if ключ not in self.крайние:
            self.крайние[ключ] = h.day

    def _снимок(self, h):
        живые = h.alive()
        доверие = [a.trust.get(c.id, 3.0) for a in живые for c in живые if a.id != c.id]
        self.дни[(self.зерно, h.day)] = {
            "настроение": [p.mood for p in живые],
            "нормальность": [p.normalcy for p in живые],
            "паника": [p.panic for p in живые],
            "доверие_макс": max(доверие) if доверие else 0.0,
            "живых": len(живые),
        }


# ---------------------------------------------------------------- счёт

def медиана(v, default=None):
    v = [x for x in v if x is not None]
    return st.median(v) if v else default


def сред(v):
    v = list(v)
    if not v:
        return 0.0, 0.0
    m = st.mean(v)
    if len(v) < 2:
        return m, 0.0
    import math
    return m, 1.96 * st.stdev(v) / math.sqrt(len(v))


def по_дню(н, день, поле):
    """Все значения поля за этот день по всем прогонам, в один список."""
    out = []
    for (_з, д), с in н.дни.items():
        if д == день:
            out.extend(с[поле]) if isinstance(с[поле], list) else out.append(с[поле])
    return out


def разброс_по_дню(н, день, поле):
    """Размах между людьми внутри дома — по одному числу на прогон."""
    out = []
    for (_з, д), с in н.дни.items():
        if д == день and len(с[поле]) > 1:
            out.append(max(с[поле]) - min(с[поле]))
    return out


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="Линейка к ПЛАН.md")
    ap.add_argument("--прогонов", type=int, default=40)
    ap.add_argument("--дней", type=int, default=30)
    ap.add_argument("--от", type=int, default=1)
    args = ap.parse_args()

    зёрна = range(args.от, args.от + args.прогонов)
    итоги = []
    with Наблюдатель() as н:
        for s in зёрна:
            н.зерно = s
            sim = Simulation(seed=s, days=args.дней, verbosity=0, stream=io.StringIO())
            h = sim.run()
            живые = [p for p in h.people.values() if p.alive and not p.exiled]
            итоги.append({
                "зерно": s, "выжило": len(живые), "stats": dict(h.stats),
                "судьбы": {p.id: (p.died_day, p.cause) for p in h.people.values()},
                "дней": h.day,
            })

    n = len(итоги)
    print(f"ЗАМЕР: {n} прогонов по {args.дней} дней")
    print()

    m, ci = сред(r["выжило"] for r in итоги)
    print(f"Выживаемость: {m:.2f} ± {ci:.2f} из 6")
    print()

    # --- 1.1 и 1.3: кражи ---
    кражи = н.кражи
    ранние = [k for k in кражи if k["день"] < 7]
    первые_дни = [k for k in кражи if k["день"] <= 2]
    print(f"НОЧНЫЕ КРАЖИ (попытки): {len(кражи)} на {n} прогонов")
    if кражи:
        print(f"  до дня 7:            {len(ранние)} ({100.0*len(ранние)/len(кражи):.0f}%)   цель ≤5%")
        print(f"  в дни 1-2:           {len(первые_дни)}   цель 0")
        print(f"  запас вора, еда:     {медиана(k['еда_дней'] for k in кражи):.1f} дн   цель <3")
        print(f"  запас вора, топливо: {медиана(k['топливо_дней'] for k in кражи):.1f} дн")
        print(f"  ненависть к жертве:  {медиана(k['ненависть'] for k in кражи):.0f} из 100")
        print(f"  отчаяние вора:       {медиана(k['отчаяние'] for k in кражи):.2f}")
        print(f"  нормальность вора:   {медиана(k['нормальность'] for k in кражи):.2f}")
    дни_первой = [r["stats"]["первая_кража_день"] for r in итоги
                  if r["stats"].get("первая_кража_день")]
    if дни_первой:
        m, ci = сред(дни_первой)
        print(f"  день первой кражи:   {m:.1f} ± {ci:.1f}   цель 8-10 "
              f"(в {100.0*len(дни_первой)/n:.0f}% прогонов)")
    print(f"  краж всего за прогон: {сред(r['stats'].get('краж', 0) for r in итоги)[0]:.1f}")
    дни_налёта = [r["stats"]["первый_налёт_день"] for r in итоги
                  if r["stats"].get("первый_налёт_день")]
    if дни_налёта:
        m, ci = сред(дни_налёта)
        print(f"  день первой осады:   {m:.1f} ± {ci:.1f}   не раньше 7-го "
              f"(в {100.0*len(дни_налёта)/n:.0f}% прогонов); раньше 7-го: "
              f"{sum(1 for d in дни_налёта if d < 7)}")
    print("  нормальность и заражение: "
          + ", ".join(f"{k} {сред(r['stats'].get(k, 0) for r in итоги)[0]:.1f}"
                      for k in ("переступили", "чужой_пример_заразил",
                                "чужой_пример_испугал", "дом_держали")))
    print()

    # --- 1.2 и 2: настроение и нормальность по дням ---
    print("ПО ДНЯМ (медиана по живым, все прогоны):")
    print("  день  настроение  нормальность  паника  размах нормальности  живых")
    for д in ДНИ_СНИМКА:
        if д > args.дней:
            continue
        наст = медиана(по_дню(н, д, "настроение"))
        норм = медиана(по_дню(н, д, "нормальность"))
        пан = медиана(по_дню(н, д, "паника"))
        разб = медиана(разброс_по_дню(н, д, "нормальность"), 0.0)
        жив = медиана([с["живых"] for (_з, dd), с in н.дни.items() if dd == д], 0)
        if наст is None:
            continue
        print(f"  {д:>4}  {наст:>10.0f}  {норм:>12.2f}  {пан:>6.0f}  {разб:>19.2f}  {жив:>5.1f}")
    print("  (цели: настроение д1 >85, д10 55-70; размах нормальности д15 и д25 ≥0.30)")
    print()

    # --- 2.2: кто когда переступает ---
    print("ПЕРВЫЙ КРАЙНИЙ ПОСТУПОК (медиана дня; разбор, дверь, отъём, кража, тело, налёт):")
    все_id = sorted({i for (_з, i) in н.крайние})
    порядок = {}
    for pid in все_id:
        дни = [d for (з, i), d in н.крайние.items() if i == pid]
        доля = 100.0 * len(дни) / n
        порядок[pid] = медиана(дни)
        print(f"  {pid:<8} день {медиана(дни):>4.1f}   (в {доля:>3.0f}% прогонов)")
    if "лида" in порядок and "игорь" in порядок:
        print(f"  разрыв Лида − Игорь: {порядок['лида'] - порядок['игорь']:+.1f} дн   цель ≥5")
    print()

    # --- 5: доверию нечем расти ---
    д14 = [с["доверие_макс"] for (_з, d), с in н.дни.items() if d == 14]
    if д14:
        print(f"МАКСИМАЛЬНОЕ ДОВЕРИЕ В ДОМЕ к д14: {медиана(д14):.1f}   цель 6.5-8")
        print()

    # --- 4.1: дни, когда никто не вышел ---
    без_вылазок = []
    for r in итоги:
        дней = r["дней"]
        было = sum(1 for д in range(1, дней + 1) if (r["зерно"], д) in н.вылазки)
        без_вылазок.append(дней - было)
    m, ci = сред(без_вылазок)
    print(f"ДНЕЙ БЕЗ ВЫЛАЗОК (никто не вышел): {m:.1f} ± {ci:.1f} из {args.дней}")
    print()

    # --- 8: насилие против холода ---
    from house.runner import без_рода
    from collections import Counter
    c = Counter()
    for r in итоги:
        for _pid, (день, причина) in r["судьбы"].items():
            if день:
                c[без_рода((причина or "?").split(" (")[0])] += 1
    всего = max(1, sum(c.values()))
    насилие = sum(v for k, v in c.items() if k.startswith("убит") or "изгнан" in k)
    холод = sum(v for k, v in c.items() if k in ("холод", "голод", "обезвоживание", "истощение")
                or "болезн" in k or "ран" in k)
    print("ПРИЧИНЫ СМЕРТИ:")
    for k, v in c.most_common():
        print(f"  {k:<32} {v:>4}  ({100.0*v/всего:.0f}%)")
    print(f"  насилие {100.0*насилие/всего:.0f}% против нужды {100.0*холод/всего:.0f}%")
    print()

    # --- 6: Оксана и Ваня ---
    дожили = {}
    for pid in итоги[0]["судьбы"]:
        дожили[pid] = 100.0 * sum(1 for r in итоги if not r["судьбы"][pid][0]) / n
    print("ДОЖИЛИ (доля прогонов):")
    for pid, v in sorted(дожили.items(), key=lambda kv: -kv[1]):
        print(f"  {pid:<8} {v:>5.0f}%")
    смертей_детей = сред(r["stats"].get("смертей_детей", 0) for r in итоги)[0]
    print(f"  смертей детей за прогон: {смертей_детей:.2f}   цель 0.15-0.30")
    print()

    # --- 7 и 4.2: то, чего ещё нет, но будет чем мерить ---
    print("СОБЫТИЯ И СОБРАНИЯ:")
    for key, label, цель in (("собраний", "собраний за прогон", "≥1 в 60-80%"),
                             ("собраний_сорвалось", "из них сорвалось", "30%"),
                             ("обещаний", "обещаний за прогон", "8-20"),
                             ("обещаний_нарушено", "нарушено", "20-40%"),
                             ("вранья", "вранья за прогон", "5-15"),
                             ("вранья_раскрыто", "разоблачено", "30-50%")):
        m, _ = сред(r["stats"].get(key, 0) for r in итоги)
        доля_р = 100.0 * sum(1 for r in итоги if r["stats"].get(key, 0)) / n
        print(f"  {label:<22} {m:>5.2f}  (в {доля_р:>3.0f}% прогонов)   цель {цель}")


if __name__ == "__main__":
    main()
