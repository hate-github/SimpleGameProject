# -*- coding: utf-8 -*-
"""Сборка и главный цикл: день — ночь — расчёт.

День: у каждого 16 часов, он тратит их на действия (GDD 5).
Ночь: сон, дежурство, кражи и налёты (GDD 4.3 «Ночью происходит основной риск»).
Утро: сводка (GDD 4.4) — что заметили соседи, что пропало, кто что слышал.
"""
from .util import Rng
from .model import House
from .hooks import Хуки
from .schema import load_json, validate_data
from . import (world, social, actions, conflict, report, meeting, замысел, chat, assembly,
               psyche, household, services, discoveries, night, physiology, child)


class Simulation:
    def __init__(self, seed=1, days=30, verbosity=1, secrets=False, stream=None,
                 overrides=None, hooks=None):
        """overrides — {ключ: значение} поверх balance.json.

        Нужно, чтобы сравнивать две настройки одной ручки, не правя файл:
        без этого A/B по параметру технически невозможен.

        hooks — наблюдатели (`hooks.Хуки`), общие на сколько угодно жизней:
        линейка подписывается один раз и гоняет зёрна, дом зовёт её сам.
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
        self.h = House(rng=Rng(seed), B=self.balance,
                       hooks=hooks if hooks is not None else Хуки())
        self.h.реплики_быт = self.lines.get("быт", [])
        self.h.journal = report.Journal(verbosity=verbosity, secrets=secrets, stream=stream)
        assembly.build_house(self.h, self.npcs_data)

    # ------------------------------------------------------------ цикл
    def run(self):
        """Прожить все дни. В конце каждого зовёт наблюдателей `h.hooks.on_day`.

        Так check.py снимает отпечаток дома по дням, а линейки — свои мерки,
        и никто не подменяет `one_day`. Наблюдатель ничего не меняет и ничего
        не тянет из rng: с пустым списком прогон тот же до последнего слова.
        """
        h = self.h
        world.build_calendar(h, self.events, self.days)
        for _ in range(self.days):
            self.one_day()
            h.hooks.зов("on_day", h)
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
        report.отдых_за_день(h)
        report.первые_дни(h)
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
                if p.time_left < 0.3 or p.health <= 0 or not p.здесь():
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
