# -*- coding: utf-8 -*-
"""Сравнить две настройки одной ручки честно.

    python ab.py агрессивность_дома 1.5 2.0
    python ab.py бой_шанс_смерти_холодное 0.08 0.13 --прогонов 80
    python ab.py --список                      — какие ручки вообще есть

Зачем это, если есть batch.py: батч печатает средние, а средние врут. При шести
жильцах две честные выборки по 50 прогонов расходятся на треть человека, поэтому
разница «5.3 против 5.7» не значит ничего. Здесь два приёма против этого:

  · ПАРНЫЕ ЗЁРНА — зерно 7 при 1.5 сравнивается с зерном 7 при 2.0. Один и тот же
    мир, отличается только ручка; половина шума уходит, и хватает вдвое меньше
    прогонов;
  · ИНТЕРВАЛ — рядом с каждой разницей стоит её погрешность и прямо сказано,
    отличается она от шума или нет.

Ручка меняется на лету, файл balance.json править не нужно.
"""
import argparse
import sys

from house.engine import load_json
from house.runner import many, парное, парное_поле, причины

МЕТРИКИ = [
    ("выжило", "выжило из шести", None),
    ("налётов", "осад за прогон", "налётов"),
    ("первый_налёт_день", "день первой осады", "первый_налёт_день"),
    ("краж", "краж", "краж"),
    ("первая_кража_день", "день первой кражи", "первая_кража_день"),
    ("отъёмов", "отъёмов на лестнице", "отъёмов"),
    ("убийств", "убийств", "убийств"),
    ("изгнаний", "изгнаний", "изгнаний"),
    ("смертей", "смертей", "смертей"),
    ("союзов_заключено", "союзов", "союзов_заключено"),
    ("переездов", "переездов", "переездов"),
]


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="Парное A/B по одной ручке баланса")
    ap.add_argument("ручка", nargs="?")
    ap.add_argument("а", nargs="?", type=float)
    ap.add_argument("б", nargs="?", type=float)
    ap.add_argument("--прогонов", type=int, default=60)
    ap.add_argument("--дней", type=int, default=30)
    ap.add_argument("--список", action="store_true")
    args = ap.parse_args()

    B = load_json("balance.json")
    if args.список or not args.ручка:
        числа = [(k, v) for k, v in B.items() if isinstance(v, (int, float))]
        print(f"Ручек с числом: {len(числа)}\n")
        for k, v in числа:
            print(f"  {k:<34} {v}")
        return 0
    if args.ручка not in B:
        print(f"Нет такой ручки: {args.ручка}. Список: python ab.py --список")
        return 1
    if args.а is None or args.б is None:
        print(f"Сейчас {args.ручка} = {B[args.ручка]}. Нужны два значения для сравнения.")
        return 1

    seeds = range(1, args.прогонов + 1)
    A = many(seeds, days=args.дней, overrides={args.ручка: args.а})
    Б = many(seeds, days=args.дней, overrides={args.ручка: args.б})

    print(f"«{args.ручка}»: {args.а} против {args.б}")
    print(f"{args.прогонов} парных зёрен по {args.дней} дней "
          f"(в файле сейчас {B[args.ручка]})")
    print()
    print(f"{'метрика':<22}{'при ' + str(args.а):>10}{'при ' + str(args.б):>10}"
          f"{'разница':>11}{'95%':>9}   вывод")
    print("─" * 78)
    for key, label, stat in МЕТРИКИ:
        if stat is None:
            ma = sum(r["выжило"] for r in A) / len(A)
            mb = sum(r["выжило"] for r in Б) / len(Б)
            d, ci, sig = парное_поле(A, Б, "выжило")
        else:
            есть = [r for r in A if stat in r["stats"]] and [r for r in Б if stat in r["stats"]]
            if not есть:
                continue
            ma = sum(r["stats"].get(stat, 0) for r in A) / len(A)
            mb = sum(r["stats"].get(stat, 0) for r in Б) / len(Б)
            d, ci, sig = парное(A, Б, stat)
        вывод = "различие есть" if sig else "неотличимо от шума"
        print(f"{label:<22}{ma:>10.2f}{mb:>10.2f}{d:>+11.2f}{ci:>9.2f}   {вывод}")

    print()
    for имя, runs in ((str(args.а), A), (str(args.б), Б)):
        c = причины(runs)
        всего = max(1, sum(c.values()))
        print(f"причины смерти при {имя}: "
              + ", ".join(f"{k} {100 * v / всего:.0f}%" for k, v in c.most_common(5)))
    print()
    нарушений = sum(len(r["нарушения"]) for r in A) + sum(len(r["нарушения"]) for r in Б)
    print(f"нарушений инвариантов за оба прогона: {нарушений}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
