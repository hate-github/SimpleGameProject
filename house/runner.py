# -*- coding: utf-8 -*-
"""Как гонять много жизней: параллельно, с подменой ручек, со сводкой и разбросом.

Одна история ничего не доказывает. Но и сто историй ничего не доказывают, если
смотреть на голое среднее: при шести жильцах разброс выживаемости таков, что
две честные выборки по 50 прогонов расходятся на треть человека. Поэтому здесь
средние всегда идут с интервалом, а сравнение двух настроек — на парных зёрнах.
"""
import io
import math
import os
import statistics as st
from collections import Counter

# multiprocessing нужен процессам-воркерам как модуль верхнего уровня,
# поэтому функция прогона лежит здесь, а не внутри скриптов.


def run_one(arg):
    """(зерно, дней, {ручка: значение}) -> сводка одной жизни."""
    seed, days, overrides = arg
    from .engine import Simulation
    from . import checks
    sim = Simulation(seed=seed, days=days, verbosity=0, secrets=False,
                     stream=io.StringIO(), overrides=overrides)
    start = checks.snapshot(sim.h)
    h = sim.run()
    alive = [p for p in h.people.values() if p.alive and not p.exiled]
    return {
        "зерно": seed,
        "выжило": len(alive),
        "паника": (sum(p.panic for p in alive) / len(alive)) if alive else 0.0,
        "имена": sorted(p.short for p in alive),
        "причины": [без_рода((p.cause or "?").split(" (")[0])
                    for p in h.people.values() if not p.alive or p.exiled],
        "судьбы": {p.short: (p.died_day, без_рода((p.cause or "").split(" (")[0]))
                   for p in h.people.values()},
        "stats": {k: v for k, v in h.stats.items() if isinstance(v, (int, float))},
        "богатство": h.scav_richness,
        "нарушения": checks.invariants(h) + checks.ledger(h, start),
    }


def без_рода(cause):
    """«убита выстрелом» и «убит выстрелом» — одна и та же причина смерти."""
    for f, m in (("убита", "убит"), ("умерла", "умер"), ("изгнана", "изгнан")):
        if cause.startswith(f):
            return m + cause[len(f):]
    return cause


def many(seeds, days=30, overrides=None, jobs=None):
    """Прогнать список зёрен. По умолчанию — на всех ядрах минус два."""
    args = [(s, days, overrides) for s in seeds]
    jobs = jobs if jobs is not None else max(1, min(8, (os.cpu_count() or 2) - 2))
    # На Windows дочерний процесс заново импортирует __main__ по файлу. Если запуск
    # идёт из `python -c` или из stdin, файла нет и spawn падает — тогда считаем
    # в один поток, молча и правильно, вместо стены трейсбеков
    import sys
    главный = getattr(sys.modules.get("__main__"), "__file__", None)
    можно = главный and os.path.exists(главный)
    if jobs <= 1 or len(args) < 4 or not можно:
        return [run_one(a) for a in args]
    import multiprocessing as mp
    with mp.Pool(jobs) as pool:
        return pool.map(run_one, args)


# ---------------------------------------------------------------- статистика

def сводка(values):
    """Среднее и половина 95%-интервала. Без интервала среднее — просто число."""
    v = list(values)
    if not v:
        return 0.0, 0.0
    m = st.mean(v)
    if len(v) < 2:
        return m, 0.0
    return m, 1.96 * st.stdev(v) / math.sqrt(len(v))


def метрика(runs, key, default=0):
    return [r["stats"].get(key, default) for r in runs]


def доля(runs, key):
    """В какой доле прогонов это случилось хоть раз, в процентах."""
    return 100.0 * sum(1 for r in runs if r["stats"].get(key, 0)) / max(1, len(runs))


def впервые(runs, key):
    """Средний день первого события — только по прогонам, где оно было."""
    v = [r["stats"][key] for r in runs if key in r["stats"]]
    return (сводка(v) if v else (None, None)), len(v)


def парное(a, b, key):
    """Сравнить две настройки на одних и тех же зёрнах.

    Возвращает (разница, полуинтервал, достоверно ли). Парность убирает
    половину шума: один и тот же мир, отличается только ручка.
    """
    d = [x["stats"].get(key, 0) - y["stats"].get(key, 0) for x, y in zip(b, a)]
    if len(d) < 2:
        return 0.0, 0.0, False
    m = st.mean(d)
    s = st.stdev(d)
    if s == 0:
        return m, 0.0, m != 0
    se = s / math.sqrt(len(d))
    return m, 1.96 * se, abs(m / se) >= 1.96


def парное_поле(a, b, поле):
    d = [x[поле] - y[поле] for x, y in zip(b, a)]
    if len(d) < 2:
        return 0.0, 0.0, False
    m, s = st.mean(d), st.stdev(d)
    if s == 0:
        return m, 0.0, m != 0
    se = s / math.sqrt(len(d))
    return m, 1.96 * se, abs(m / se) >= 1.96


def причины(runs):
    c = Counter()
    for r in runs:
        for x in r["причины"]:
            c[x] += 1
    return c
