# -*- coding: utf-8 -*-
"""Наблюдатели: линейки и check.py слушают дом, не подменяя его функций.

До задачи 7 каждая линейка подменяла `actions.execute`, `conflict.run_siege`
и ещё десяток имён прямо в модулях, и любое переименование ломало пять
скриптов молча (аудит §5 п.10, §7). Теперь домен сам зовёт наблюдателей
в нескольких точках, а наблюдатель — просто функция в списке `h.hooks`.

Договор наблюдателя: он смотрит и записывает, но ничего в доме не меняет
и не трогает `h.rng`. Вызов хука — не вызов случайности: прогон с пустыми
списками и с полными — один и тот же до последнего слова, и эталон это
проверяет (check.py снимает дом через `on_day`). Наблюдатели живут в том
процессе, где подписаны: `runner.many` с ними считает в один поток.
"""
from dataclasses import dataclass, field, fields
from functools import wraps
from typing import Callable, List

Список = List[Callable]


@dataclass
class Хуки:
    """Точки, в которых дом зовёт наблюдателей, и кто в них подписан.

    `on_<имя>` зовётся перед функцией домена с её аргументами, `after_<имя>` —
    после, с теми же аргументами и `итог=` (что функция вернула). Пары ставит
    декоратор `наблюдаемо`; одиночные точки домен зовёт сам через `зов`.
    """
    on_day: Список = field(default_factory=list)              # (h) — день кончился, всё записано
    # выбор и исполнение дня (actions)
    on_gather: Список = field(default_factory=list)           # (h, npc)
    after_gather: Список = field(default_factory=list)        # (h, npc, итог=варианты) — до порога и выбора
    on_execute: Список = field(default_factory=list)          # (h, npc, key, target)
    after_execute: Список = field(default_factory=list)       # (h, npc, key, target, итог=None)
    on_реплика: Список = field(default_factory=list)          # (h, текст) — реплика быта или чата, до подстановки рода
    # улица (street)
    on_outing: Список = field(default_factory=list)           # (h, npc, dur, м, спутник=None)
    after_outing: Список = field(default_factory=list)        # (…, итог=None)
    # ночь и осада (conflict)
    on_siege: Список = field(default_factory=list)            # (h, leader, target)
    after_siege: Список = field(default_factory=list)         # (…, итог=исход строкой); состав — h.сутки.состав_налёта
    on_steal: Список = field(default_factory=list)            # (h, thief, target)
    after_steal: Список = field(default_factory=list)         # (…, итог=(что унёс, сколько))
    on_убить_соседа: Список = field(default_factory=list)     # (h, killer, victim)
    after_убить_соседа: Список = field(default_factory=list)  # (…, итог=True, если получилось)
    on_defenders: Список = field(default_factory=list)        # (h, target, crew_ids, предупреждён=, поднял=, крик=)
    after_defenders: Список = field(default_factory=list)     # (…, итог=кто вышел к двери)
    # отношения, знание, замысел (social, замысел)
    on_adjust: Список = field(default_factory=list)           # (a, b_id, trust, hate, aware, страх) — до затухания
    on_узнал_о_смерти: Список = field(default_factory=list)   # (h, кто, умерший)
    after_узнал_о_смерти: Список = field(default_factory=list)   # (…, итог=True, если это была новость)
    on_замысел: Список = field(default_factory=list)          # (h, npc, замысел) — человек взялся за новый замысел

    def зов(self, имя, *args, **kw):
        for f in getattr(self, имя):
            f(*args, **kw)


ТОЧКИ = {f.name for f in fields(Хуки)}


def наблюдаемо(имя):
    """Обернуть функцию домена, у которой `h` — первый аргумент.

    До неё зовётся `on_<имя>` с её аргументами, после — `after_<имя>`
    с теми же аргументами и `итог=`. Имя проверяется при импорте: точка
    без списка в `Хуки` — ошибка сборки, а не молчание.
    """
    assert "on_" + имя in ТОЧКИ and "after_" + имя in ТОЧКИ, имя

    def обернуть(f):
        @wraps(f)
        def вызов(h, *args, **kw):
            h.hooks.зов("on_" + имя, h, *args, **kw)
            итог = f(h, *args, **kw)
            h.hooks.зов("after_" + имя, h, *args, итог=итог, **kw)
            return итог
        return вызов
    return обернуть
