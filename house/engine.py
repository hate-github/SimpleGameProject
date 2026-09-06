# -*- coding: utf-8 -*-
"""Сборка и главный цикл: день — ночь — расчёт.

День: у каждого 16 часов, он тратит их на действия (GDD 5).
Ночь: сон, дежурство, кражи и налёты (GDD 4.3 «Ночью происходит основной риск»).
Утро: сводка (GDD 4.4) — что заметили соседи, что пропало, кто что слышал.
"""
from .util import Rng, clamp, norm, vb
from .model import House
from .schema import load_json, validate_data
from . import (world, social, actions, conflict, report, meeting, замысел, character, chat,
               assembly, psyche, household, services, discoveries, night, physiology, child)


class Simulation:
    def __init__(self, seed=1, days=30, verbosity=1, secrets=False, stream=None,
                 overrides=None):
        """overrides — {ключ: значение} поверх balance.json.

        Нужно, чтобы сравнивать две настройки одной ручки, не правя файл:
        без этого A/B по параметру технически невозможен.
        """
        self.seed = seed
        self.days = days
        self.balance = load_json("balance.json")
        if overrides:
            unknown = [k for k in overrides if k not in self.balance]
            if unknown:
                raise KeyError("нет такой ручки в balance.json: " + ", ".join(sorted(unknown)))
            self.balance.update(overrides)
        self.npcs_data = load_json("npcs.json")
        self.events = load_json("events.json")
        try:
            self.lines = load_json("lines.json")
        except FileNotFoundError:
            self.lines = {}
        validate_data(self.balance, self.npcs_data, self.events, self.lines)
        self.h = House(rng=Rng(seed), B=self.balance)
        self.h.mods["реплики_быт"] = self.lines.get("быт", [])
        self.h.journal = report.Journal(verbosity=verbosity, secrets=secrets, stream=stream)
        assembly.build_house(self.h, self.npcs_data)

    # ------------------------------------------------------------ цикл
    def run(self, on_day=None):
        """on_day(h) — наблюдатель, которого зовут в конце каждого дня.

        Нужен check.py, чтобы снимать отпечаток дома по дням, не подменяя
        `one_day`, как это делают линейки. Сам ничего не меняет и ничего
        не тянет из rng: с on_day=None прогон тот же до последнего слова.
        """
        h = self.h
        world.build_calendar(h, self.events, self.days)
        for _ in range(self.days):
            self.one_day()
            if on_day is not None:
                on_day(h)
            if not h.alive():
                h.journal.line("В подъезде не осталось никого.", 2)
                h.journal.flush_day(h)
                break
        return h

    def one_day(self):
        h = self.h
        world.start_of_day(h, self.events)
        self._morning(h)
        self._day(h)
        # то, чего человек не простил за сегодняшний день, — одной строкой
        # на всех, а не по строке на каждого (discoveries.огласить_непрощённых)
        discoveries.огласить_непрощённых(h)
        chat.daily_chat(h, self.lines)
        night.ночь(h)
        discoveries.огласить_непрощённых(h)
        self._upkeep(h)
        social.проверить_обещания(h)
        social.alliance_check(h)
        social.update_groups(h)
        social.daily_decay(h)
        social.spread_panic(h)
        # чем кончился день у тех, кто его пролежал: одна строка вместо восьми
        for p in h.alive():
            if p.stats.get("отдых_день") != h.day:
                continue
            раз = p.stats.get("отдых_раз", 0)
            # и то, отчего этот день прошёл впустую. У матери, которой нечем
            # помочь ребёнку, «лежала и ничего не делала» — неправда журнала,
            # а не поведения: делать ей и правда нечего, но она не лежит
            с_ребёнком = (p.дети
                          and p.ребёнку_плохо() >= h.B["быт_ребёнку_плохо"])
            если_ребёнок = f" рядом с {p.dependent_ins or p.dependent_name or 'ребёнком'}"
            if раз >= h.B["отдых_весь_день"]:
                h.journal.line(f"{p.short} почти весь день "
                               + (f"{vb(p.sex, 'просидел')}{если_ребёнок}." if с_ребёнком
                                  else f"{vb(p.sex, 'пролежал')} и ничего не {vb(p.sex, 'делал')}."), 1)
            elif раз >= 2:
                h.journal.line(f"{p.short} подолгу "
                               + (f"{vb(p.sex, 'сидел')}{если_ребёнок}." if с_ребёнком
                                  else f"{vb(p.sex, 'лежал')} и ничего не {vb(p.sex, 'делал')}."), 0)
            else:
                h.journal.line(f"{p.short} "
                               + (f"{vb(p.sex, 'сидел')}{если_ребёнок}." if с_ребёнком
                                  else f"{vb(p.sex, 'лежал')} и ничего не {vb(p.sex, 'делал')}."), 0)
        self._firsts(h)
        h.journal.сводка_дня(h)
        h.journal.flush_day(h)
        h.journal.panel(h)

    # ------------------------------------------------------------ утро
    def _morning(self, h):
        h.новый_день()
        # утро после ночи у чужих: ребёнка забирают. Или не отдают
        conflict.вернуть_детей(h)
        for p in h.alive():
            psyche.горизонт(h, p)
            psyche.дрейф_нормальности(h, p)
            # замысел решает своё до всего остального: спит он сегодня или нет,
            # не пора ли его бросить и не пора ли взяться за новый
            замысел.утро(h, p)
            p.новый_день(h.B)
        household.дрова_к_печке(h)

        services.готовые_заказы(h)

        # вышел ли вчерашний дежурный: перед домом, а не перед соседом
        meeting.проверить_дежурство(h)

        discoveries.вскрытые_кладовые(h)

        discoveries.подброшенное(h)

        # не пора ли дому сложить одно к одному про того, кто ведёт свою игру
        for p in h.alive():
            замысел.напор_виден(h, p)

        discoveries.пропажи(h)

    # ------------------------------------------------------------ день
    def _day(self, h):
        guard = 0
        while guard < 200:
            guard += 1
            acted = False
            for p in h.rng.shuffled(h.alive()):
                # список составлен в начале прохода, а за это время человека
                # могли выставить на мороз или убить — проверяем ещё раз
                if p.time_left < 0.3 or p.health <= 0 or not p.alive or p.exiled:
                    continue
                if actions.choose_and_do(h, p):
                    acted = True
            if not acted:
                break
        # ночью все дома: «хозяин ушёл» не должно перетекать в ночную кражу
        for p in h.alive():
            p.away = False

    # ------------------------------------------------------------ расчёт суток
    def _upkeep(self, h):
        # сколько всего в доме отключено: одно число на настроение и на панику
        infra = ((not h.heating) + (not h.water_on) + (not h.power_on) + (h.network <= 0))
        for p in list(h.alive()):
            room = physiology.расход_и_тепло(h, p, infra)

            household.теснота_ночи(h, p)

            child.сутки(h, p, room)

            physiology.износ(h, p, infra)

        # вытяжка обмерзает от сегодняшнего пара: то, что натопили за день,
        # аукнется не этой ночью, а следующими
        world.вытяжка_мёрзнет(h)
        world.запах_по_стояку(h)

    # ------------------------------------------------------------ диагностика
    def _firsts(self, h):
        s = h.stats
        if s.get("налётов") and "первый_налёт_день" not in s:
            s["первый_налёт_день"] = h.day
        if s.get("смертей") and "первая_смерть_день" not in s:
            s["первая_смерть_день"] = h.day
        if s.get("союзов_заключено") and "первый_союз_день" not in s:
            s["первый_союз_день"] = h.day
        if s.get("краж") and "первая_кража_день" not in s:
            s["первая_кража_день"] = h.day
        if (s.get("ушедших") or s.get("возвратов_с_полпути")) and "первая_попытка_уйти" not in s:
            s["первая_попытка_уйти"] = h.day
