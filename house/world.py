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
    spread_panic(h)


def outing_danger(h):
    """Общий множитель опасности вылазки (собаки, чужие, мороз)."""
    m = h.mods.get("опасность_множитель", 1.0) or 1.0
    if h.outside < -22:
        m *= 1.3
    return m
