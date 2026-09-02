# -*- coding: utf-8 -*-
"""Метель, инфраструктура и события (GDD 3, 21).

Три типа событий, как в документе:
  1) скриптовые по дням — одинаковы в каждой жизни;
  2) порождённые симуляцией — их делают сами люди (это не здесь, это в actions/conflict);
  3) внешние случайные — меняют условия, но никогда не убивают сами по себе.
"""
from .util import clamp


def start_of_day(h, events_data):
    """Новое утро: погода, отключения, события."""
    h.day += 1
    b = h.B

    _tick_mods(h)

    # --- температура (GDD 3: падает с каждым днём) ---
    base = b["температура_день1"] - b["температура_падение_в_день"] * (h.day - 1)
    h.outside = base + h.mods.get("температура_сдвиг", 0.0)

    # --- расписание отключений (GDD 21, скриптовые события) ---
    if h.day >= b["день_отключения_отопления"]:
        h.heating = False
    if h.day >= b["день_отключения_воды"]:
        h.water_on = False
    if h.day >= b["день_отключения_электричества"]:
        h.power_on = False
    if h.day >= b["день_потери_связи"]:
        h.network = 0.0
    elif h.day >= b["день_потери_связи"] - 4:
        h.network = clamp(1.0 - 0.25 * (h.day - (b["день_потери_связи"] - 4)), 0.0, 1.0)

    # --- скриптовое событие дня ---
    for ev in events_data.get("скриптовые", []):
        if ev["день"] == h.day:
            h.journal.event(ev["текст"], scripted=True)
            apply_effects(h, ev.get("эффекты", {}))

    # --- случайное внешнее событие ---
    _roll_random_event(h, events_data)


def _tick_mods(h):
    """Уменьшить срок действия временных модификаторов."""
    for key in ("температура_дней", "опасность_дней"):
        if h.mods.get(key, 0) > 0:
            h.mods[key] -= 1
            if h.mods[key] <= 0:
                if key == "температура_дней":
                    h.mods["температура_сдвиг"] = 0.0
                else:
                    h.mods["опасность_множитель"] = 1.0


def _roll_random_event(h, events_data):
    pool = []
    last = h.mods.setdefault("последние_события", {})
    for ev in events_data.get("случайные", []):
        lo, hi = ev.get("окно", [1, 99])
        if not (lo <= h.day <= hi):
            continue
        if h.day - last.get(ev["id"], -99) < 6:
            continue
        pool.append((ev, ev.get("вес", 5)))
    if not pool:
        return
    if not h.rng.chance(h.B.get("шанс_случайного_события", 0.45)):
        return
    ev = h.rng.weighted(pool)
    last[ev["id"]] = h.day
    h.journal.event(ev["текст"])
    apply_effects(h, ev.get("эффекты", {}))


def apply_effects(h, eff):
    """Применить эффекты события ко всему дому."""
    from .social import spread_panic

    if "паника" in eff:
        for p in h.alive():
            spread = eff["паника"] * (0.7 + 0.6 * p.t01("вспыльчивость"))
            from .social import add_panic
            add_panic(p, spread)
    if "настроение" in eff:
        for p in h.alive():
            p.mood = clamp(p.mood + eff["настроение"])
    if "богатство" in eff:
        h.scav_richness = clamp(h.scav_richness + eff["богатство"], 0.0, 1.6)
    if "связь" in eff:
        h.network = clamp(h.network + eff["связь"], 0.0, 1.0)
    if "температура" in eff:
        t = eff["температура"]
        h.mods["температура_сдвиг"] = t["градусов"]
        h.mods["температура_дней"] = t["дней"]
        h.outside += t["градусов"]
    if "опасность_вылазки" in eff:
        d = eff["опасность_вылазки"]
        h.mods["опасность_множитель"] = d["множитель"]
        h.mods["опасность_дней"] = d["дней"]
    if eff.get("укрепление_порыв"):
        h.mods["укрепление_порыв"] = h.day
    if "болезнь_шанс" in eff:
        for p in h.alive():
            risk = eff["болезнь_шанс"] * (1.4 if p.warmth < 40 else 1.0) * (1.3 if p.satiety < 35 else 1.0)
            if not p.sick and h.rng.chance(risk):
                p.sick = "простуда"
                h.journal.line(f"{p.label()} слёг: жар, кашель.", 1)
    if eff.get("кража_в_доме"):
        _scripted_theft(h)
    if eff.get("смерть_от_холода"):
        _scripted_cold_death(h)
    spread_panic(h)


def _scripted_theft(h):
    """GDD 21: «день 5 — первая кража в доме».

    Скриптовое событие должно уметь запускать происшествие, а не только двигать
    шкалы: смысл этого слоя по документу — «игрок учит расписание и вмешивается»,
    а вмешаться в прибавку паники нельзя. Если дом уже обворовали сам собой,
    сценарий молчит — расписание задаёт первый раз, а не лишний.
    """
    from . import conflict
    if h.stats.get("краж") or h.stats.get("попыток_кражи"):
        return
    люди = h.alive()
    if len(люди) < 2:
        return
    вор = max(люди, key=lambda p: p.trait("жадность") - p.trait("лояльность") + p.desperation() * 3)
    цели = [p for p in люди if p.id != вор.id]
    if not цели:
        return
    жертва = max(цели, key=lambda p: вор.loot_value(p.id))
    conflict.steal(h, вор, жертва)


def _scripted_cold_death(h):
    """GDD 21: «день 14 — смерть первого соседа от холода».

    Тоже только если дом ещё никого не потерял: расписание — это то, что
    случается «если игрок не вмешался», а не добавка к уже случившемуся.
    """
    from . import conflict
    from .util import vb
    if h.stats.get("смертей"):
        return
    люди = [p for p in h.alive() if not p.dependents]
    if len(люди) < 3:
        return
    жертва = min(люди, key=lambda p: p.warmth + p.health * 0.5)
    жертва.health = 0.0
    жертва.cause = "холод"
    жертва.died_day = h.day
    h.journal.line(f"† {жертва.name} не {vb(жертва.sex, 'проснулся')}. "
                   f"В квартире было минус четыре.", 2)
    conflict.on_death(h, жертва)


def outing_danger(h):
    """Общий множитель опасности вылазки (собаки, чужие, мороз)."""
    m = h.mods.get("опасность_множитель", 1.0) or 1.0
    if h.outside < -22:
        m *= 1.3
    return m
