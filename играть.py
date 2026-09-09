# -*- coding: utf-8 -*-
"""Прожить метель за одного из жильцов — руками, в консоли.

    python играть.py                      — сыграть за Оксану, случайное зерно
    python играть.py --кто аркадий        — за деда из первой квартиры
    python играть.py --зерно 42 --дней 30 — повторить тот же мир
    python играть.py --кто список         — кто вообще живёт в доме

Зачем это, если есть run.py: `run.py` показывает, что дом делает сам.
Здесь за одного из пятнадцати решает человек — тем же швом, что и NPC
(`decision.Решающий`), по тем же правилам и с теми же оценками. Это и есть
проверка, ради которой шов делался: пройти тридцать дней и не встретить
места, где игра решает за тебя.

Отличий от прогона два, и оба про то, чтобы можно было играть:
  · журнал печатается сразу, а не в конце дня — иначе отвечать пришлось бы
    вслепую, не зная, что случилось за стеной;
  · перед вопросом печатается своё состояние: сытость, вода, тепло, сон,
    здоровье и что в шкафу.

Ответ — номер варианта. Ещё понимает: «?» — состояние дома, «я» — свои
запасы, «т» — что известно о соседях, «в» — выход.
"""
import argparse
import io
import random
import sys

from house import report
from house.decision import Человек
from house.engine import Simulation


# сколько вариантов показывать сразу: у жильца в доме на пятнадцать их бывает
# и тридцать, и читать их все каждый ход невозможно. Остальные — по «всё»
КОРОТКИЙ_СПИСОК = 12


class ЖивойЖурнал(report.Journal):
    """Журнал, который печатает строку сразу.

    Обычный копит день и выкладывает его в конце — для чтения потом это
    правильно, для игры нет: вопрос «что делать» приходит посреди дня,
    и до ответа игрок должен знать, что уже случилось. Заголовок дня
    печатается перед первой строкой этого дня.
    """

    def __init__(self, *a, **kw):
        super().__init__(*a, **kw)
        self.дом = None
        self._день = None

    def _порог(self):
        return 2 if self.verbosity == 0 else (1 if self.verbosity == 1 else 0)

    def _шапка(self):
        if self.дом is None or self._день == self.дом.day:
            return
        self._день = self.дом.day
        self.w()
        self.w(self.шапка_дня(self.дом))

    def line(self, text, importance=1, hidden=False):
        if hidden and not self.secrets:
            return
        if importance < self._порог():
            return
        self._шапка()
        self.w("  " + (f"{self.час} {text}" if self.час else text))

    def flush_day(self, h):
        self.дом = h
        self._шапка()
        self.buf.clear()


def строка_состояния(npc):
    """Как он себя чувствует и что у него есть — одной строкой перед вопросом."""
    шкаф = ", ".join(f"{r} {v:g}" for r, v in sorted(npc.stock.items()) if v)
    беды = []
    if npc.injuries:
        беды.append(", ".join(npc.injuries))
    if npc.sick:
        беды.append("болен" if npc.sex != "ж" else "больна")
    хвост = f" · {'; '.join(беды)}" if беды else ""
    return (f"  сыт {npc.satiety:.0f} · вода {npc.hydration:.0f} · тепло {npc.warmth:.0f} · "
            f"сон {npc.rest:.0f} · здоровье {npc.health:.0f} · паника {npc.panic:.0f}{хвост}\n"
            f"  в шкафу: {шкаф or 'пусто'} · часов до ночи {npc.time_left:.1f}")


def дом_коротко(h):
    """Кто ещё жив, где живёт и каким кажется — то, что игрок видит и так."""
    строки = []
    for p in sorted(h.people.values(), key=lambda x: x.apt):
        if not p.здесь():
            когда = f"† день {p.died_day}" if p.died_day else "†"
            строки.append(f"  кв{p.apt:<3} {p.short:<9} {когда} {p.cause or ''}")
        else:
            гость = f", у {h.get(p.living_with).short}" if p.living_with else ""
            строки.append(f"  кв{p.apt:<3} {p.short:<9} {p.role}{гость}")
    return "\n".join(строки)


def знание(h, я):
    """Что я знаю о соседях: оценка шкафа и насколько я вообще в курсе."""
    строки = []
    for o in sorted(h.others(я), key=lambda x: x.apt):
        if not o.здесь():
            continue
        строки.append(f"  {o.short:<9} еда {я.believed(o.id, 'еда'):.1f} · "
                      f"дрова {я.believed(o.id, 'топливо'):.1f} · "
                      f"знаю о нём {я.сведения_о(o.id).aware:.0f} · "
                      f"доверие {я.trust.get(o.id, 3.0):.1f}")
    return "\n".join(строки)


def сделать_спросить(h, кто):
    """Тот самый `спросить(вопрос, строки) -> номер`, который ждёт `Человек`."""
    def спросить(вопрос, строки):
        я = h.people[кто]
        # заголовок дня — до вопроса: игрок должен знать, какое сегодня число
        # и сколько на улице, прежде чем решать
        h.journal._шапка()
        print()
        print("┈" * 74)
        print(вопрос)
        print(строка_состояния(я))

        def показать(сколько):
            for i, s in enumerate(строки[:сколько]):
                print(f"  {i}. {s}")
            if сколько < len(строки):
                print(f"  … и ещё {len(строки) - сколько} — «всё»")

        показать(КОРОТКИЙ_СПИСОК)
        while True:
            try:
                ответ = input("> ").strip().lower()
            except EOFError:
                return 0
            if ответ in ("в", "выход", "q"):
                print("Вышел из игры.")
                sys.exit(0)
            if ответ in ("?", "дом"):
                print(дом_коротко(h))
                continue
            if ответ in ("я", "я?"):
                print(строка_состояния(я))
                continue
            if ответ in ("т", "соседи"):
                print(знание(h, я))
                continue
            if ответ in ("всё", "все", "*"):
                показать(len(строки))
                continue
            if ответ.isdigit() and int(ответ) < len(строки):
                return int(ответ)
            print("  номер варианта, или: всё — весь список, ? — дом, "
                  "я — состояние, т — что знаю о соседях, в — выход")
    return спросить


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="Прожить метель за одного из жильцов")
    ap.add_argument("--кто", default="оксана", help="id жильца (или «список»)")
    ap.add_argument("--зерно", type=int, default=None)
    ap.add_argument("--дней", type=int, default=30)
    ap.add_argument("--подробно", action="store_true", help="печатать и мелочи быта")
    args = ap.parse_args()

    seed = args.зерно if args.зерно is not None else random.randrange(1, 10 ** 6)
    sim = Simulation(seed=seed, days=args.дней,
                     verbosity=2 if args.подробно else 1, stream=io.StringIO())
    h = sim.h

    if args.кто == "список" or args.кто not in h.people:
        if args.кто != "список":
            print(f"Нет такого жильца: {args.кто}")
        print("В доме живут:")
        for p in sorted(h.people.values(), key=lambda x: x.apt):
            print(f"  {p.id:<9} кв{p.apt:<3} {p.name}, {p.age} — {p.role}")
        return 1

    # живой журнал вместо копящего — и в него же пишет всё остальное
    живой = ЖивойЖурнал(verbosity=sim.h.journal.verbosity,
                        secrets=False, stream=sys.stdout)
    живой.дом = h
    h.journal = живой

    # и один жилец из пятнадцати решает не мягким выбором, а вопросом человеку.
    # Правила при этом те же: шов ровно в том месте, где NPC зовёт `Решающего`
    я = h.people[args.кто]
    я.решающий = Человек(спросить=сделать_спросить(h, args.кто))

    print(f"Зерно {seed}. Вы — {я.name}, кв.{я.apt}: {я.role}.")
    print("Ответ — номер варианта. «всё» — весь список, «?» — дом, "
          "«я» — состояние, «т» — что знаю о соседях, «в» — выход.")
    sim.run()
    print()
    report.final_report(h, args.дней, seed)
    if я.здесь():
        print(f"\n{я.short} дожил{'а' if я.sex == 'ж' else ''} до конца метели.")
    else:
        print(f"\n{я.short}: {я.cause} (день {я.died_day}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
