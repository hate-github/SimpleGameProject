# -*- coding: utf-8 -*-
"""Самопроверка симуляции: инварианты, баланс ресурсов, покрытие веток.

В симуляции ошибка почти никогда не выглядит как исключение. Она выглядит как
ветка, которая молча не выполняется, или как еда, которая молча исчезла.
Поэтому здесь три инструмента:

  · инварианты      — то, что обязано быть верно в конце каждого дня;
  · баланс ресурсов — приход минус расход должен сходиться до последней банки;
  · покрытие        — сколько раз каждое действие предлагалось и выполнялось.

Пользоваться этим: `python check.py`.
"""
from collections import Counter

RESOURCES = ("еда", "вода", "топливо", "лекарства", "материалы", "патроны", "мясо")
SCALES_100 = ("satiety", "hydration", "warmth", "rest", "mood", "health", "panic")


# ---------------------------------------------------------------- инварианты

def invariants(h):
    """Что обязано быть верно всегда. Возвращает список нарушений."""
    bad = []

    def say(text):
        bad.append(f"день {h.day}: {text}")

    for p in h.people.values():
        живой = p.alive and not p.exiled

        for res, v in p.stock.items():
            if v < -1e-9:
                say(f"{p.short}: запас «{res}» ушёл в минус ({v:.3f})")
        for field in SCALES_100:
            v = getattr(p, field)
            if not (-1e-9 <= v <= 100 + 1e-9):
                say(f"{p.short}: {field} = {v:.2f}, а должно быть 0..100")
        for who, v in p.trust.items():
            if not (-1e-9 <= v <= 10 + 1e-9):
                say(f"{p.short}: доверие к {who} = {v:.2f}, а должно быть 0..10")
        for scale, name in ((p.hate, "ненависть"), (p.aware, "осведомлённость")):
            for who, v in scale.items():
                if not (-1e-9 <= v <= 100 + 1e-9):
                    say(f"{p.short}: {name} к {who} = {v:.2f}, а должно быть 0..100")
        if p.time_left < -0.01:
            say(f"{p.short}: часов в дне осталось {p.time_left:.3f}")

        if not живой and sum(p.stock.values()) > 1e-9:
            say(f"{p.short} выбыл, но запасы при нём: {p.stock}")
        if not живой and p.allies:
            say(f"{p.short} выбыл, но числится в союзе с {sorted(p.allies)}")

        if живой and p.living_with:
            host = h.get(p.living_with)
            if host is None:
                say(f"{p.short} живёт у несуществующего «{p.living_with}»")
            elif not host.alive or host.exiled:
                say(f"{p.short} живёт у выбывшего {host.short} — топить свою печь он уже не может")
            elif p.id not in host.guests:
                say(f"{p.short} живёт у {host.short}, а тот об этом не знает")
        for gid in sorted(p.guests):
            g = h.get(gid)
            if g is None:
                say(f"у {p.short} в гостях несуществующий «{gid}»")
            elif g.living_with != p.id:
                say(f"{p.short} считает гостем {g.short}, а тот живёт у «{g.living_with}»")
            elif not (g.alive and not g.exiled):
                say(f"у {p.short} в гостях выбывший {g.short}")
        for other in h.people.values():
            if other.id != p.id and p.id in other.allies and other.id not in p.allies:
                say(f"союз односторонний: {other.short} считает {p.short} союзником, а тот нет")

    # квартиры
    apts = [f.apt for f in h.empty]
    for apt in {a for a in apts if apts.count(a) > 1}:
        say(f"квартира {apt} числится пустой дважды")
    for f in h.empty:
        for p in h.alive():
            if p.apt == f.apt and not p.living_with:
                say(f"кв.{f.apt} числится пустой, хотя {p.short} в ней живёт")

    return bad


# ---------------------------------------------------------------- баланс ресурсов

def ledger(h, start):
    """Сошёлся ли приход с расходом. start — снимок мира до первого дня.

    Правило: было + пришло == осталось + израсходовано + потеряно.
    Всё, что не сходится, — либо забытый счётчик, либо утечка.
    """
    bad = []
    for res in RESOURCES:
        было = start.get(res, 0.0)
        пришло = (h.stats.get("принесено_" + res, 0.0)
                  + h.stats.get("наразобрано_" + res, 0.0)
                  + h.stats.get("натоплено_" + res, 0.0))
        ушло = (h.stats.get("израсходовано_" + res, 0.0)
                + h.stats.get("потеряно_" + res, 0.0))
        осталось = world_total(h, res)
        расхождение = (было + пришло) - (осталось + ушло)
        if abs(расхождение) > 0.01:
            bad.append(f"{res}: было {было:.1f} + пришло {пришло:.1f} "
                       f"≠ осталось {осталось:.1f} + ушло {ушло:.1f} "
                       f"(расхождение {расхождение:+.2f})")
    return bad


def world_total(h, res):
    """Сколько ресурса есть в доме — у живых, у мёртвых и в пустых квартирах."""
    return (sum(p.stock.get(res, 0.0) for p in h.people.values())
            + sum(f.stock.get(res, 0.0) for f in h.empty))


def snapshot(h):
    return {res: world_total(h, res) for res in RESOURCES}


# ---------------------------------------------------------------- покрытие

class Coverage:
    """Считает, сколько раз каждое действие предлагалось и выполнялось.

    Ветка, которая ни разу не предложена, — это мёртвый код, и в симуляции
    его никак иначе не заметить: исключения он не бросает.

    Пользоваться так:
        with Coverage() as cov:
            ...прогоны...
        cov.report()
    """

    def __init__(self):
        self.offered = Counter()
        self.done = Counter()
        self.siege = Counter()
        self.night = Counter()

    def __enter__(self):
        from . import actions, conflict
        self._actions, self._conflict = actions, conflict
        self._gather, self._execute = actions.gather, actions.execute
        self._siege = conflict.run_siege

        def gather(h, npc):
            opts = self._gather(h, npc)
            for (key, _t), _s in opts:
                self.offered[key] += 1
            return opts

        def execute(h, npc, key, target):
            self.done[key] += 1
            return self._execute(h, npc, key, target)

        def run_siege(h, leader, target):
            crew = conflict.recruit(h, leader, target)
            self.siege["состав всего"] += len(crew)
            self.siege["осад"] += 1
            жива = target.alive
            r = self._siege(h, leader, target)
            self.siege["исход: " + r] += 1
            if жива and not target.alive:
                self.siege["цель погибла"] += 1
            return r

        actions.gather, actions.execute = gather, execute
        conflict.run_siege = run_siege
        return self

    def __exit__(self, *exc):
        self._actions.gather, self._actions.execute = self._gather, self._execute
        self._conflict.run_siege = self._siege
        return False

    def dead_branches(self):
        from .actions import COST
        return sorted(k for k in COST if not self.offered.get(k))

    def report(self, w=print):
        from .actions import COST
        w("Действия — предложено / выполнено:")
        for key in sorted(COST, key=lambda k: -self.done.get(k, 0)):
            o, d = self.offered.get(key, 0), self.done.get(key, 0)
            метка = "   ← мёртвая ветка" if o == 0 else ("   ← ни разу не выбрано" if d == 0 else "")
            w(f"  {key:<14} {o:>8} / {d:<6}{метка}")
        if self.siege:
            осад = self.siege.get("осад", 0)
            w("")
            w(f"Осады: {осад}, средний состав "
              f"{self.siege.get('состав всего', 0) / max(1, осад):.2f} человека")
            for k in sorted(self.siege):
                if k.startswith("исход: ") or k == "цель погибла":
                    w(f"  {k:<22} {self.siege[k]:>4}  ({100 * self.siege[k] / max(1, осад):.0f}%)")
