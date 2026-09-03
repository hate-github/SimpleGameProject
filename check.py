# -*- coding: utf-8 -*-
"""Самопроверка прототипа. Запускать после любой правки:

    python check.py                 — всё сразу (около минуты)
    python check.py --прогонов 40   — глубже
    python check.py --золотой       — перезаписать эталон осознанно

Проверяет пять вещей:
  1. данные          — balance.json, npcs.json, events.json не противоречат коду;
  2. инварианты      — то, что обязано быть верно в конце каждого дня;
  3. баланс ресурсов — приход минус расход сходится до последней банки;
  4. покрытие        — нет ли действий, которые ни разу не предлагаются;
  5. детерминизм     — одно зерно даёт один и тот же прогон, в том числе
                       в другом процессе с другим PYTHONHASHSEED;
  6. эталон          — поведение не изменилось незаметно (золотой прогон).

Возвращает код 1, если что-то не так, — чтобы это можно было повесить на хук.
"""
import argparse
import hashlib
import io
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
ЭТАЛОН = os.path.join(ROOT, "data", "эталон.json")
ЗЁРНА_ЭТАЛОНА = (3, 17, 42, 101)
# ниже этого числа прогонов «ни разу не предложено» ещё ничего не доказывает:
# людоедство и генератор случаются в трети жизней, и на десяти прогонах их
# отсутствие — статистика, а не мёртвый код. Самая редкая живая ветка —
# «съехать»: она случается три раза на сто жизней, потому что топить одну
# печку на двоих почти всегда выгоднее, чем разойтись. На 25 прогонах её
# не увидеть в половине случаев, и она честно объявлялась мёртвой
НАДЁЖНАЯ_ВЫБОРКА = 120


def digest(seed, days=30):
    """Отпечаток прогона: и текст журнала, и итоговое состояние."""
    from house.engine import Simulation
    buf = io.StringIO()
    h = Simulation(seed=seed, days=days, verbosity=2, secrets=True, stream=buf).run()
    state = repr(sorted(
        (p.short, p.alive, p.exiled, p.died_day, p.cause,
         round(p.satiety, 6), round(p.panic, 6), round(p.health, 6),
         sorted(p.allies), p.living_with, sorted(p.guests),
         sorted((k, round(v, 6)) for k, v in p.stock.items()))
        for p in h.people.values()))
    return hashlib.sha256((buf.getvalue() + state).encode("utf-8")).hexdigest()[:16]


# ---------------------------------------------------------------- проверки

def проверить_данные(w):
    from house.engine import load_json, validate_data
    try:
        validate_data(load_json("balance.json"), load_json("npcs.json"), load_json("events.json"))
    except (ValueError, KeyError) as e:
        w(str(e))
        return ["данные не прошли проверку"]
    return []


def проверить_ручки(w):
    """Ручки, которых нет в коде, и ключи, которых нет в файле."""
    from house.engine import load_json
    B = load_json("balance.json")
    src = ""
    for name in sorted(os.listdir(os.path.join(ROOT, "house"))):
        if name.endswith(".py"):
            src += open(os.path.join(ROOT, "house", name), encoding="utf-8").read()
    for name in ("run.py", "batch.py", "check.py", "ab.py"):
        path = os.path.join(ROOT, name)
        if os.path.exists(path):
            src += open(path, encoding="utf-8").read()

    заголовки = [k for k, v in B.items() if isinstance(v, str)]
    мёртвые = [k for k, v in B.items()
               if not isinstance(v, str) and ('"%s"' % k) not in src]
    # читаем только то, что действительно берётся из баланса: b[...], h.B[...], B[...]
    читаются = set(re.findall(r'(?:b|B|bal|b_n|h\.B|self\.balance)\s*(?:\.get)?\s*[\[(]\s*"([^"]+)"', src))
    нет_в_файле = sorted(k for k in читаются if k not in B)
    bad = []
    if мёртвые:
        w("  ручки, которых нет в коде: " + ", ".join(sorted(мёртвые)))
        bad.append(f"мёртвых ручек: {len(мёртвые)}")
    if нет_в_файле:
        w("  код читает то, чего нет в balance.json: " + ", ".join(нет_в_файле))
        bad.append(f"отсутствующих ключей: {len(нет_в_файле)}")
    if not bad:
        w(f"  все {len(B) - len(заголовки)} ручек живые, лишних чтений нет")
    return bad


def проверить_прогоны(w, прогонов, дней):
    from house import checks
    from house.runner import many
    with checks.Coverage() as cov:
        runs = many(range(1, прогонов + 1), days=дней, jobs=1)
    bad = []
    нарушений = [v for r in runs for v in r["нарушения"]]
    if нарушений:
        for v in нарушений[:12]:
            w("  " + v)
        if len(нарушений) > 12:
            w(f"  ... и ещё {len(нарушений) - 12}")
        bad.append(f"нарушений инвариантов и баланса: {len(нарушений)}")
    else:
        w(f"  {прогонов} прогонов: инварианты и баланс ресурсов сходятся")

    мёртвые = cov.dead_branches()
    if мёртвые:
        w("  ни разу не предложены: " + ", ".join(мёртвые))
        if прогонов >= НАДЁЖНАЯ_ВЫБОРКА:
            bad.append(f"мёртвых веток: {len(мёртвые)}")
        else:
            w(f"    (на {прогонов} прогонах это может быть просто редкость — "
              f"чтобы судить, нужно хотя бы {НАДЁЖНАЯ_ВЫБОРКА}: python check.py --прогонов {НАДЁЖНАЯ_ВЫБОРКА})")
    невыбранные = sorted(k for k in cov.offered if not cov.done.get(k))
    if невыбранные:
        w("  предлагаются, но никогда не выбираются: " + ", ".join(невыбранные))
    осады = cov.siege.get("осад", 0)
    исходы = {k[7:] for k in cov.siege if k.startswith("исход: ")}
    ожидаем = {"откупился", "отбился", "ограблен", "изгнан", "убит", "сбежал"}
    if осады:
        w(f"  осад {осады}, средний состав "
          f"{cov.siege.get('состав всего', 0) / осады:.2f} чел.; исходы: "
          + ", ".join(f"{k[7:]} {cov.siege[k]}" for k in sorted(cov.siege) if k.startswith("исход: ")))
        нет = sorted(ожидаем - исходы)
        if нет:
            w("  исходы GDD 16, которые не случились ни разу: " + ", ".join(нет))
    return bad, cov, runs


def проверить_детерминизм(w):
    """Одно зерно = один прогон, в том числе в другом процессе."""
    bad = []
    свой = {s: digest(s) for s in ЗЁРНА_ЭТАЛОНА}
    if {s: digest(s) for s in ЗЁРНА_ЭТАЛОНА} != свой:
        bad.append("прогон не повторяется даже в одном процессе")
        w("  повтор в том же процессе дал другой результат")
        return bad, свой
    код = ("import sys, json; sys.path.insert(0, %r); "
           "import check; print(json.dumps({s: check.digest(s) for s in check.ЗЁРНА_ЭТАЛОНА}))" % ROOT)
    for hashseed in ("0", "1", "31337"):
        env = dict(os.environ, PYTHONHASHSEED=hashseed)
        p = subprocess.run([sys.executable, "-c", код], capture_output=True, env=env, cwd=ROOT)
        if p.returncode != 0:
            w("  не удалось запустить подпроцесс: " + p.stderr.decode("utf-8", "replace")[-300:])
            bad.append("проверка детерминизма не отработала")
            return bad, свой
        чужой = json.loads(p.stdout.decode("utf-8"))
        расход = [s for s in свой if свой[s] != чужой[str(s)]]
        if расход:
            w(f"  PYTHONHASHSEED={hashseed}: разошлись зёрна {расход}")
            bad.append("прогон зависит от PYTHONHASHSEED")
            return bad, свой
    w(f"  {len(ЗЁРНА_ЭТАЛОНА)} зёрен × 3 значения PYTHONHASHSEED — совпадение полное")
    return bad, свой


def проверить_эталон(w, свой, переписать):
    if переписать:
        with open(ЭТАЛОН, "w", encoding="utf-8") as f:
            json.dump({str(k): v for k, v in свой.items()}, f, ensure_ascii=False, indent=2)
        w("  эталон перезаписан: " + ", ".join(f"{k}={v}" for k, v in свой.items()))
        return []
    if not os.path.exists(ЭТАЛОН):
        w("  эталона ещё нет — создайте его: python check.py --золотой")
        return []
    было = json.load(open(ЭТАЛОН, encoding="utf-8"))
    расход = [s for s in свой if было.get(str(s)) != свой[s]]
    if расход:
        w(f"  поведение изменилось на зёрнах {расход}")
        w("  если это осознанная правка — обновите эталон: python check.py --золотой")
        return ["прогон разошёлся с эталоном"]
    w("  поведение совпадает с эталоном")
    return []


# ---------------------------------------------------------------- запуск

def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="Самопроверка прототипа «Опять здесь»")
    ap.add_argument("--прогонов", type=int, default=25)
    ap.add_argument("--дней", type=int, default=30)
    ap.add_argument("--золотой", action="store_true", help="перезаписать эталон")
    ap.add_argument("--подробно", action="store_true", help="полный отчёт о покрытии")
    args = ap.parse_args()

    беды = []
    w = print

    print("1. ДАННЫЕ")
    беды += проверить_данные(w)
    print()
    print("2. РУЧКИ")
    беды += проверить_ручки(w)
    print()
    print(f"3. ИНВАРИАНТЫ, БАЛАНС И ПОКРЫТИЕ ({args.прогонов} прогонов)")
    b, cov, runs = проверить_прогоны(w, args.прогонов, args.дней)
    беды += b
    if args.подробно:
        print()
        cov.report()
    print()
    print("4. ДЕТЕРМИНИЗМ")
    b, свой = проверить_детерминизм(w)
    беды += b
    print()
    print("5. ЭТАЛОН")
    беды += проверить_эталон(w, свой, args.золотой)

    print()
    print("─" * 60)
    if беды:
        print("НЕ В ПОРЯДКЕ:")
        for x in беды:
            print("  ·", x)
        return 1
    print("Всё в порядке.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
