# -*- coding: utf-8 -*-
"""Запуск одного прогона.

    python run.py                     — 30 дней, случайное зерно
    python run.py --зерно 7           — повторить тот же прогон
    python run.py --подробно          — печатать каждое действие каждого
    python run.py --секреты           — показывать то, чего дом не знает (кто украл)
    python run.py --дней 15 --тихо    — только крупные события
    python run.py --файл лог.txt      — записать в файл
"""
import argparse
import io
import random
import sys

from house.engine import Simulation
from house import report


def main():
    # без этого русский текст в консоли Windows превращается в кашу
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="Текстовая симуляция дома («Опять здесь»)")
    ap.add_argument("--дней", type=int, default=30)
    ap.add_argument("--зерно", type=int, default=None)
    ap.add_argument("--подробно", action="store_true", help="каждое действие каждого жильца")
    ap.add_argument("--тихо", action="store_true", help="только крупные события")
    ap.add_argument("--секреты", action="store_true", help="показывать скрытое от дома")
    ap.add_argument("--файл", type=str, default=None)
    args = ap.parse_args()

    seed = args.зерно if args.зерно is not None else random.randrange(1, 10 ** 6)
    verbosity = 2 if args.подробно else (0 if args.тихо else 1)

    buf = io.StringIO()
    sim = Simulation(seed=seed, days=args.дней, verbosity=verbosity,
                     secrets=args.секреты, stream=buf)
    h = sim.run()
    report.final_report(h, args.дней, seed)

    text = buf.getvalue()
    if args.файл:
        with open(args.файл, "w", encoding="utf-8") as f:
            f.write(text)
        print(f"Записано в {args.файл} ({len(text.splitlines())} строк). Зерно: {seed}")
    else:
        sys.stdout.write(text)


if __name__ == "__main__":
    main()
