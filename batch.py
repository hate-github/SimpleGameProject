# -*- coding: utf-8 -*-
"""Прогон многих жизней подряд — чтобы видеть не историю, а закономерность.

    python batch.py                 — 40 прогонов по 30 дней
    python batch.py --прогонов 200 --дней 30

Это главный инструмент настройки: одна история ничего не доказывает,
а сто историй показывают, что система делает обычно.
"""
import argparse
import io
import sys
from collections import Counter

from house.engine import Simulation


def без_рода(cause):
    """«убита выстрелом» и «убит выстрелом» — одна и та же причина смерти."""
    for f, m in (("убита", "убит"), ("умерла", "умер"), ("изгнана", "изгнан")):
        if cause.startswith(f):
            return m + cause[len(f):]
    return cause


def one(seed, days):
    sim = Simulation(seed=seed, days=days, verbosity=0, secrets=False, stream=io.StringIO())
    h = sim.run()
    alive = [p for p in h.people.values() if p.alive and not p.exiled]
    return {
        "seed": seed,
        "выжило": len(alive),
        "причины": [p.cause for p in h.people.values() if not p.alive or p.exiled],
        "stats": dict(h.stats),
        "богатство": h.scav_richness,
        "паника": (sum(p.panic for p in alive) / len(alive)) if alive else 0.0,
        "имена": sorted(p.short for p in alive),
        "судьбы": {p.short: (p.died_day, (p.cause or "").split(" (")[0]) for p in h.people.values()},
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--прогонов", type=int, default=40)
    ap.add_argument("--дней", type=int, default=30)
    ap.add_argument("--от", type=int, default=1)
    ap.add_argument("--подробно", action="store_true")
    args = ap.parse_args()

    out = sys.stdout
    try:
        out.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    runs = [one(s, args.дней) for s in range(args.от, args.от + args.прогонов)]
    n = len(runs)

    surv = Counter(r["выжило"] for r in runs)
    causes = Counter()
    for r in runs:
        for c in r["причины"]:
            c = без_рода((c or "?").split(" (")[0])
            causes[c] += 1
    who = Counter()
    for r in runs:
        for name in r["имена"]:
            who[name] += 1

    def avg(key):
        vals = [r["stats"].get(key, 0) for r in runs]
        return sum(vals) / n

    def share(key):
        return 100.0 * sum(1 for r in runs if r["stats"].get(key, 0)) / n

    def first(key):
        vals = [r["stats"][key] for r in runs if key in r["stats"]]
        return sum(vals) / len(vals) if vals else None

    print(f"ПРОГОНОВ: {n} по {args.дней} дней")
    print()
    print("Выжило человек из 6:")
    for k in range(7):
        bar = "█" * surv.get(k, 0)
        print(f"  {k}: {surv.get(k,0):>3}  {bar}")
    print(f"  в среднем {sum(r['выжило'] for r in runs)/n:.2f}")
    print()
    print("Кто доживает чаще (из 6 жильцов):")
    for name, c in who.most_common():
        print(f"  {name:<8} {100*c/n:>5.0f}%")
    print()
    print("Судьба каждого (доля прогонов, где дожил / средний день смерти):")
    names = list(runs[0]["судьбы"].keys())
    for name in names:
        days = [r["судьбы"][name][0] for r in runs if r["судьбы"][name][0]]
        alive_share = 100.0 * (n - len(days)) / n
        cause = Counter(без_рода(r["судьбы"][name][1]) for r in runs if r["судьбы"][name][0]).most_common(1)
        print(f"  {name:<8} дожил {alive_share:>3.0f}%"
              + (f", если нет — в среднем день {sum(days)/len(days):>4.1f}, чаще всего «{cause[0][0]}»" if days else ""))
    print()
    print("Причины смерти:")
    total_dead = sum(causes.values())
    for c, k in causes.most_common():
        print(f"  {c:<28} {k:>4}  ({100*k/max(1,total_dead):.0f}%)")
    print()
    print("Что рождает система (доля прогонов, где это случилось хоть раз):")
    for key, label in [("краж", "кража"), ("налётов", "налёт"), ("убийств", "убийство"),
                       ("союзов_заключено", "союз"), ("изгнаний", "изгнание"),
                       ("ложных_обвинений", "ложное обвинение"), ("детей_брошено", "ребёнок брошен"),
                       ("предательств", "предательство союзника"),
                       ("людоедство", "дошли до тела"), ("раскрытых_людоедов", "дом узнал"),
                       ("тел_вынесено", "тело вынесли во двор")]:
        print(f"  {label:<20} {share(key):>5.0f}%   в среднем за прогон {avg(key):.1f}")
    print()
    print("Когда это случается впервые (средний день):")
    for key, label in [("первая_кража_день", "первая кража"), ("первый_налёт_день", "первый налёт"),
                       ("первая_смерть_день", "первая смерть"), ("первый_союз_день", "первый союз")]:
        v = first(key)
        print(f"  {label:<16} {('день %.1f' % v) if v else 'не случается'}"
              f"   (в {share(key.replace('первая_','').replace('первый_','').replace('_день','') and key) if False else 100.0*sum(1 for r in runs if key in r['stats'])/n:.0f}% прогонов)")
    print()
    print("Экономика:")
    print(f"  съедено за прогон      {avg('съедено'):.0f} порций")
    print(f"  принесено с вылазок    еда {avg('принесено_еда'):.0f}, топливо {avg('принесено_топливо'):.0f}, "
          f"материалы {avg('принесено_материалы'):.0f}")
    print(f"  богатство района в конце {sum(r['богатство'] for r in runs)/n:.2f}")
    print(f"  средняя паника выживших  {sum(r['паника'] for r in runs)/n:.0f}")
    print()
    print("Чем кончаются осады:")
    for k in ("откупился", "переубедил", "отбился", "ограблен", "сбежал", "изгнан"):
        v = avg("исход_" + k)
        if v:
            print(f"  {k:<12} {v:.1f} за прогон")
    if args.подробно:
        print()
        for r in runs:
            print(f"  зерно {r['seed']:>4}: выжило {r['выжило']} ({', '.join(r['имена']) or '—'}), "
                  f"налётов {r['stats'].get('налётов',0)}, краж {r['stats'].get('краж',0)}")


if __name__ == "__main__":
    main()
