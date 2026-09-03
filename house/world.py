# -*- coding: utf-8 -*-
"""Метель, инфраструктура и события (GDD 3, 21).

Три типа событий, как в документе:
  1) скриптовые по дням — одинаковы в каждой жизни;
  2) порождённые симуляцией — их делают сами люди (это не здесь, это в actions/conflict);
  3) внешние случайные — меняют условия, но никогда не убивают сами по себе.
"""
from .util import clamp


def build_calendar(h, events_data, days):
    """Составить календарь внешних событий на всю жизнь заранее (GDD 21).

    Один раз, своим потоком случайности, до первого действия. Тогда вмешательство
    игрока меняет дом, но не погоду — и повтор жизни ощущается честным.
    """
    rng = h.rng.branch("мир")
    календарь = {}
    последние = {}
    for день in range(1, days + 1):
        pool = []
        for ev in events_data.get("случайные", []):
            lo, hi = ev.get("окно", [1, 99])
            if not (lo <= день <= hi):
                continue
            if день - последние.get(ev["id"], -99) < h.B["событие_перерыв_дней"]:
                continue
            pool.append((ev, ev.get("вес", 5)))
        if not pool or not rng.chance(h.B["шанс_случайного_события"]):
            continue
        ev = rng.weighted(pool)
        последние[ev["id"]] = день
        календарь[день] = ev["id"]
    h.mods["календарь"] = календарь


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
    # GDD 19: «После дня 0 связь деградирует… к дню 10 сеть исчезает совсем».
    # Раньше первые шесть дней связь была идеальной, и деградация начиналась
    # только с седьмого — то есть «после дня 0» не выполнялось
    if h.day >= b["день_потери_связи"]:
        h.network = 0.0
    else:
        h.network = clamp(1.0 - h.day / float(b["день_потери_связи"]), 0.0, 1.0)

    # --- скриптовое событие дня ---
    # GDD 21: «одинаковы в каждой жизни, ЕСЛИ ИГРОК НЕ ВМЕШАЛСЯ». Значит,
    # у события может быть условие отмены — иначе «эпидемия, если игрок
    # не помог медику» непроверяема, а именно она и названа в документе
    for ev in events_data.get("скриптовые", []):
        if ev["день"] != h.day:
            continue
        if _отменено(h, ev.get("отменяется_если")):
            h.journal.line(ev.get("текст_отмены", "…обошлось."), 1)
            h.bump("событий_отменено")
            continue
        h.journal.event(ev["текст"], scripted=True)
        apply_effects(h, ev.get("эффекты", {}))

    # --- случайное внешнее событие ---
    _roll_random_event(h, events_data)


def _отменено(h, условие):
    """Сработало ли условие отмены скриптового события."""
    if not условие:
        return False
    вид = условие.get("вид")
    if вид == "жив_с_умением":
        return any(условие["умение"] in p.skills and p.health > условие.get("здоровье", 40)
                   for p in h.alive())
    if вид == "нет_происшествий":
        from .social import recent_incidents
        return recent_incidents(h, условие.get("дней", 5)) == 0
    if вид == "все_в_тепле":
        return all(p.warmth > условие.get("тепло", 45) for p in h.alive())
    return False


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
    """Событие дня берётся из календаря, составленного до начала жизни."""
    eid = h.mods.get("календарь", {}).get(h.day)
    if not eid:
        return
    for ev in events_data.get("случайные", []):
        if ev["id"] == eid:
            h.journal.event(ev["текст"])
            apply_effects(h, ev.get("эффекты", {}))
            return


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
        # GDD 21: внешнее событие «никогда не убивает само по себе». Поэтому
        # похолодание бьёт по улице, но не проламывает то, что человек успел
        # построить: у сдвига есть предел, и он тем меньше, чем лучше утеплена
        # квартира. Раньше одно похолодание стоило дому полчеловека выживших
        t = eff["температура"]
        сдвиг = t["градусов"]
        if сдвиг < 0:
            сдвиг = max(сдвиг, -h.B["событие_холод_потолок"])
        h.mods["температура_сдвиг"] = сдвиг
        h.mods["температура_дней"] = t["дней"]
        h.outside += сдвиг
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
    # в свою же квартиру не влезают: у соседа по комнате не крадут, к нему
    # просто протягивают руку — а это уже другой поступок, и его в игре нет
    from .social import под_одной_крышей
    цели = [p for p in люди if p.id != вор.id and not под_одной_крышей(h, вор, p)]
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
