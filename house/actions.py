# -*- coding: utf-8 -*-
"""Что человек делает днём и как он это выбирает.

Каждое действие занимает время (GDD 5) и имеет громкость (GDD 13).
Выбор — мягкий: обычно берётся лучшее по оценке, но чем выше паника,
тем чаще человек делает глупость.
"""
from .util import clamp, norm, vb, gform
from . import social, conflict

# ключ: (часы, громкость, вид шума)
COST = {
    "поесть":       (0.5, 2, "готовка"),
    "попить":       (0.3, 1, "шаги"),
    "топить_снег":  (1.5, 2, "готовка"),
    "топить":       (0.5, 1, "буржуйка"),
    "генератор":    (0.5, 5, "генератор"),
    "утепление":    (1.5, 3, "ремонт"),
    "дверь":        (3.0, 3, "дверь"),
    "буржуйка":     (4.0, 3, "ремонт"),
    "разбор":       (3.0, 3, "разбор"),
    "вылазка":      (6.0, 2, "возвращение"),
    "наблюдение":   (1.0, 0, "шаги"),
    "разговор":     (0.5, 1, "шаги"),
    "попросить":    (0.5, 1, "шаги"),
    "поделиться":   (0.5, 1, "шаги"),
    "обмен":        (0.7, 1, "шаги"),
    "лечить":       (1.0, 1, "шаги"),
    "отдых":        (2.0, 0, None),
    "быт":          (1.5, 1, "шаги"),
    "переехать":    (2.0, 2, "шаги"),
    "кража_днём":   (1.5, 3, "взлом"),
    "тело":         (2.0, 2, "разбор"),
    "поесть_мясо":  (0.7, 2, "готовка"),
    "вынести":      (3.0, 2, "разбор"),
}


def used_day(h, npc, key):
    return h.mods.get("сделано", {}).get((npc.id, key), 0)


def used_pair(h, npc, target, key):
    return h.mods.get("контакты", {}).get((npc.id, target.id, key), 0)


def mark(h, npc, key, target=None):
    """Отметить, что действие сегодня уже делалось. Без этого один человек
    за день двадцать раз просит еды у соседа — проверено на первом прогоне."""
    d = h.mods.setdefault("сделано", {})
    d[(npc.id, key)] = d.get((npc.id, key), 0) + 1
    if target is not None and getattr(target, "id", None):
        c = h.mods.setdefault("контакты", {})
        c[(npc.id, target.id, key)] = c.get((npc.id, target.id, key), 0) + 1


# ресурсы в винительном падеже: «обменял воду на топливо»
RES_ACC = {"еда": "еду", "вода": "воду"}
RES_GEN = {"еда": "еды", "вода": "воды", "топливо": "топлива"}


def most_needed(npc):
    """Чего у человека меньше всего в днях — то и пойдёт просить."""
    return min(("еда", "вода", "топливо"), key=lambda r: npc.days_of(r))


# Насколько поступок «не принят», пока жизнь ещё похожа на обычную.
# 0 — делают и в мирное время, 1 — только когда всё уже рухнуло.
НОРМА = {
    "разбор": 1.22,       # разобрать чужую пустую квартиру на доски
    "дверь": 1.00,        # заколотить дверь от соседей
    "наблюдение": 0.95,   # следить за соседом
    "кража": 1.10,
    "тело": 1.10,
    "дежурить": 0.85,     # не спать, слушая лестницу
    "буржуйка": 0.62,     # признать, что отопления больше не будет
    "генератор": 0.45,
    "утепление": 0.28,    # это и в обычную зиму делают
    "переехать": 0.88,    # признать, что в своей квартире не выжить
    "вылазка": 0.18,
}


def norm_gate(npc, key, b):
    """Множитель к оценке: чем нормальнее ещё кажется жизнь, тем немыслимее поступок."""
    w = НОРМА.get(key, 0.0)
    if not w:
        return 1.0
    return clamp(b["нормальность_порог"] - npc.normalcy * w, 0.02, 1.0)


# что стоит показывать в обычном режиме, а что — только с --подробно
NOTABLE = {"буржуйка": 1, "дверь": 1, "утепление": 1, "генератор": 1, "разбор": 1,
           "попросить": 1, "поделиться": 1, "обмен": 1, "лечить": 1}


def hours(key, npc):
    h = COST[key][0]
    if key == "буржуйка" and ("слесарь" in npc.skills or "электрик" in npc.skills):
        h -= 1.5
    if key == "вылазка":
        h = 6.0
    return h


# ---------------------------------------------------------------- выбор

def gather(h, npc):
    """Собрать все доступные действия с оценками. Оценка примерно 0..10."""
    b = h.B
    opts = []
    hungry = 1.0 - norm(npc.satiety, 15, 75)
    thirsty = 1.0 - norm(npc.hydration, 15, 75)
    cold = 1.0 - norm(npc.warmth, 20, 70)
    tired = 1.0 - norm(npc.rest, 20, 80)
    des = npc.desperation()
    room = h.room_temp(npc, burning=True)
    fear = npc.panic / 100.0 + social.recent_incidents(h) * 0.25

    lim_day = b.get("лимит_в_день", {})
    lim_pair = b.get("лимит_на_пару_в_день", {})

    def add(key, score, target=None):
        if score <= 0:
            return
        if hours(key, npc) > npc.time_left + 0.001:
            return
        if used_day(h, npc, key) >= lim_day.get(key, 99):
            return
        if target is not None and used_pair(h, npc, target, key) >= lim_pair.get(key, 99):
            return
        opts.append(((key, target), score * norm_gate(npc, key, b)))

    # --- быт ---
    food_days = npc.days_of("еда")
    if npc.stock.get("мясо", 0) > 0 and npc.satiety < 70 and npc.stock.get("еда", 0) <= 0:
        add("поесть_мясо", hungry * 8.0)
    if npc.stock.get("еда", 0) > 0 and npc.satiety < 88:
        s = hungry * 9.0
        if food_days < 2.5 and npc.satiety > 45:
            s *= 0.45            # режим экономии: терпит, пока может
        if npc.dependents:
            s += 1.2             # ребёнка кормят раньше себя
        add("поесть", s)

    if npc.hydration < 88:
        if h.water_on:
            add("попить", thirsty * 9.5)
        elif npc.stock.get("вода", 0) > 0:
            add("попить", thirsty * 9.5)
    # снег топят только там, где есть на чём (GDD 15: буржуйка — улучшение 2 уровня).
    # без печки вода становится настоящей проблемой: только запас, обмен или просьба
    can_melt = npc.shelter.get("буржуйка") or (h.power_on and npc.shelter.get("обогреватель"))
    # топить снег можно и на мебели — раз уж печка всё равно горит
    есть_чем = (npc.stock.get("топливо", 0) >= b["снег_топливо"]
                or npc.stock.get("материалы", 0) >= b["мебель_за_топку"])
    if not h.water_on and can_melt and npc.stock.get("вода", 0) < 4 and есть_чем:
        # снег топят охотнее, когда печка и так нужна: одно действие закрывает
        # и жажду, и холод
        together = cold * 3.0 if not npc.burning and npc.shelter.get("буржуйка") else 0.0
        add("топить_снег", 2.0 + thirsty * 6.5 + together)

    # когда топливо кончилось, в печку идёт мебель — и тогда материалы
    # перестают быть строительным ресурсом. Это настоящий выбор: утеплиться или дожить
    can_burn = npc.stock.get("топливо", 0) >= 1 or npc.stock.get("материалы", 0) >= b["мебель_за_топку"]
    if npc.living_with:
        can_burn = False           # печку топит хозяин
    if npc.shelter.get("буржуйка") and can_burn and not npc.burning:
        need = cold * 8.0 + max(0.0, (14 - room)) * 0.35
        if npc.days_of("топливо") < 3:
            need *= 0.7
        add("топить", need)

    if npc.shelter.get("генератор") and npc.stock.get("топливо", 0) >= 2 and not h.power_on:
        add("генератор", cold * 3.0 + (1.0 - norm(npc.mood, 20, 80)) * 3.0 - fear * 3.0)

    # --- убежище (GDD 15) ---
    mats = npc.stock.get("материалы", 0)
    # смотрят не только на сегодня: метель с каждым днём злее, и это все знают
    room_soon = room - b["температура_падение_в_день"] * 3
    cold_pressure = clamp((b["комфортная_температура"] + 4 - room_soon) / 14.0, 0.0, 1.6)
    if not npc.shelter.get("буржуйка") and mats >= b["буржуйка_материалы"]:
        add("буржуйка", 4.0 + cold_pressure * 6.0)
    if npc.shelter.get("утепление", 0) < b["максимум_утепления"] and mats >= b["утепление_материалы"]:
        add("утепление", 1.0 + cold_pressure * 5.0)
    if npc.shelter.get("дверь", 0) < b["максимум_двери"] and mats >= b["дверь_материалы"]:
        s = fear * 4.5 + npc.t01("жадность") * 1.5
        if h.mods.get("укрепление_порыв") == h.day:
            s += 2.5
        add("дверь", s)
    # квартиру можно разобрать лишь несколько раз — потом там голые стены
    strippable = [f for f in h.empty
                  if f.stripped < b["разбор_максимум"] or sum(f.stock.values()) > 0]
    if strippable:
        want_mats = 0.0
        if not npc.shelter.get("буржуйка"):
            want_mats += 3.0
        if npc.shelter.get("утепление", 0) < 2:
            want_mats += 2.0
        if npc.shelter.get("дверь", 0) < 1:
            want_mats += 1.5 * fear
        want_mats += npc.t01("жадность") * 1.5
        # в квартире умершего может остаться еда — и это все понимают
        best_flat = max(strippable, key=lambda f: sum(f.stock.values()))
        if sum(best_flat.stock.values()) > 2:
            want_mats += 2.0 + des * 3.0
        add("разбор", want_mats - mats * 0.25)

    # --- то, до чего доходят на третьей неделе ---
    bodies = [f for f in h.empty if f.body and f.body["порций"] > 0
              and h.day - f.body["день"] <= b["тело_порча_дней"]]
    if bodies:
        # вынести тело: так поступают те, кто ещё держится за человеческое
        if npc.trait("лояльность") >= 6 and npc.health > 45:
            add("вынести", npc.t01("лояльность") * 3.0 + social.recent_incidents(h) * 0.4 - 1.0)
        # и то, до чего доходят от голода
        hungry_enough = (npc.satiety < b["людоедство_порог_сытости"]
                         or npc.days_of("еда") < 0.8)
        if hungry_enough and not npc.dependents_only_child(h):
            s = des * 10.0 + (1.0 - norm(npc.satiety, 0, 45)) * 4.0
            s -= npc.t01("лояльность") * 13.0
            s -= b["людоедство_порог_решимости"]
            s += npc.panic / 100.0 * 2.5
            near = min(bodies, key=lambda f: abs(f.floor - npc.floor))
            if near.owner_died and near.owner_died in npc.allies:
                s -= 5.0
            add("тело", s)

    # --- улица (GDD 20) ---
    if npc.health > 45 and npc.warmth > 32 and len(npc.injuries) < 2 and not npc.sick:
        # за запасами идут не когда умирают, а когда боятся, что не хватит:
        # тревожный человек выходит на улицу ещё сытым — и в этом всё дело,
        # потому что район выгрызается досуха уже ко второй неделе
        drive = max(des, npc.insecurity() * 0.85)
        s = drive * 7.0 + (1.0 - norm(food_days, 1, 7)) * 3.0
        s += npc.t01("храбрость") * 2.0 - 1.5
        s -= npc.dependents * 1.2
        s -= max(0.0, (-h.outside - 20) * 0.12)
        s *= (1.0 - 0.25 * (h.mods.get("опасность_множитель", 1.0) - 1.0))
        add("вылазка", s)

    # --- люди ---
    for t in h.others(npc):
        if t.away:
            continue
        trust = npc.trust.get(t.id, 3.0)
        # наблюдение
        curiosity = npc.t01("жадность") * 2.0 + npc.panic / 40.0 + npc.hate.get(t.id, 0) / 30.0
        curiosity += des * 2.0 - npc.t01("лояльность") * 1.5 - npc.confidence(t.id) * 3.0
        add("наблюдение", curiosity, t)
        # разговор
        talk = npc.t01("общительность") * 3.0 + trust * 0.3
        talk += (1.0 - norm(npc.mood, 20, 80)) * 2.0
        talk -= npc.hate.get(t.id, 0) / 25.0
        talk -= npc.panic / 100.0
        add("разговор", talk, t)
        # просьба — о том, чего не хватает: еда, вода или топливо.
        # человек помнит, кто ему давал, а кто отказывал
        need_res = most_needed(npc)
        rec = npc.ask_record(t.id)
        can_ask = True
        if h.day - rec["последняя"] < b["просьба_перерыв_дней"]:
            can_ask = False                      # только что уже просил
        if rec["подряд"] >= 2 and h.day - rec["последняя"] < b["просьба_обида_дней"]:
            can_ask = False                      # дважды отказал — больше не унижаюсь
        if h.day - rec["я_дал"] < b["просьба_после_помощи"]:
            can_ask = False                      # сам ему на днях отдал последнее
        if can_ask and des > 0.35 and npc.believed(t.id, need_res) > 1.5:
            ask = des * 6.0 * (0.3 + trust / 10.0) - npc.t01("храбрость") * 0.8
            # к щедрому идут первым, к скупому не идут вовсе
            ask *= b["просьба_вес_памяти"] + (2.0 - b["просьба_вес_памяти"]) * npc.generosity(t.id)
            add("попросить", ask, t)
        # поделиться
        gave_me_today = h.mods.get("контакты", {}).get((t.id, npc.id, "поделиться"), 0)
        if (npc.secure("еда") > b["порог_излишка"] and t.desperation() > 0.5
                and npc.confidence(t.id) > 0.3 and not gave_me_today
                and npc.days_of("еда") > t.days_of("еда") + 1.0):
            give = npc.t01("лояльность") * 4.0 + trust * 0.5 - npc.t01("жадность") * 2.0 - des * 4.0
            if t.id in npc.allies:
                give += 2.0
            if t.dependents:
                give += 1.5
            add("поделиться", give, t)
        # с раскрытым людоедом дом дел не имеет
        if t.stats.get("раскрыт"):
            continue
        # съехаться: когда своя квартира больше не держит тепло
        # отказ в переезде помнят: второй раз в ту же дверь не стучатся
        if (not npc.living_with and not npc.guests and not t.living_with
                and h.day - npc.ask_record(t.id).get("отказ_переезд", -99) >= b["переезд_обида_дней"]
                and npc.warmth < b["переезд_порог_тепла"]
                and h.room_temp(t, burning=True) > h.room_temp(npc, burning=True) + 4
                and trust >= b["переезд_доверие"]):
            move = (1.0 - norm(npc.warmth, 10, 55)) * 7.0 + trust * 0.4
            move -= npc.t01("храбрость") * 1.5      # гордость
            move += 1.5 if t.id in npc.allies else 0.0
            add("переехать", move, t)
        # обмен
        add("обмен", _trade_score(h, npc, t), t)
        # лечение
        if "медик" in npc.skills and npc.stock.get("лекарства", 0) >= 1 and (t.injuries or t.sick):
            add("лечить", 5.0 + npc.t01("лояльность") * 3.0 + trust * 0.3, t)
        # кража днём, пока хозяина нет
        if t.away and npc.confidence(t.id) > 0.2:
            greed = npc.loot_value(t.id) * (0.4 + npc.t01("жадность") * 0.8)
            score = greed * (0.4 + des) - npc.t01("лояльность") * 4.0 - (1.0 - conflict.stealth(npc)) * 3.0
            score += npc.hate.get(t.id, 0) / 25.0
            add("кража_днём", score, t)

    # --- себя ---
    if npc.stock.get("лекарства", 0) >= 1 and (npc.injuries or npc.sick):
        add("лечить", 6.0, npc)
    add("отдых", tired * 6.0 + (1.0 - norm(npc.mood, 20, 70)) * 2.0 + (2.0 if npc.injuries else 0.0))
    # обычная жизнь: пока всё ещё похоже на нормальное, человек живёт, а не выживает
    # обычная жизнь заполняет то, что осталось, но не спорит с настоящими делами
    add("быт", 1.25 + npc.normalcy * 1.4 + (0.4 if h.network > 0 else 0.0))

    return opts


def _trade_score(h, a, b_npc):
    """Обмен по GDD 18: сделка идёт, если обе стороны считают её выгодной."""
    deal = find_deal(h, a, b_npc)
    if not deal:
        return 0.0
    give, get_ = deal
    gain = social.value_of(a, get_[0], get_[1]) - social.value_of(a, give[0], give[1])
    return clamp(gain * 1.2 + a.trust.get(b_npc.id, 3.0) * 0.2, 0.0, 9.0)


def find_deal(h, a, b_npc):
    """Найти сделку, выгодную обоим. Возвращает ((что отдаю, сколько), (что беру, сколько))."""
    best = None
    for give in ("еда", "топливо", "лекарства", "материалы", "вода"):
        if a.stock.get(give, 0) < 2:
            continue
        for get_ in ("еда", "топливо", "лекарства", "материалы", "вода"):
            if get_ == give:
                continue
            if a.believed(b_npc.id, get_) < 1.5 and b_npc.stock.get(get_, 0) < 1:
                continue
            if b_npc.stock.get(get_, 0) < 1:
                continue
            n = 1.0
            mine_gain = social.value_of(a, get_, n) - social.value_of(a, give, n)
            their_gain = social.value_of(b_npc, give, n) - social.value_of(b_npc, get_, n)
            if mine_gain > 0.2 and their_gain > 0.2:
                score = mine_gain + their_gain
                if best is None or score > best[0]:
                    best = (score, (give, n), (get_, n))
    if best:
        return best[1], best[2]
    return None


def choose_and_do(h, npc):
    """Один ход одного человека. Возвращает True, если действие совершено."""
    opts = gather(h, npc)
    # если ничего толкового не осталось — день на этом и заканчивается.
    # без этого порога человек от нечего делать идёт разбирать чужую квартиру
    opts = [(o, s) for o, s in opts if s > h.B["порог_действия"]]
    if not opts:
        return False
    temp = h.B["температура_выбора"] + (npc.panic / 100.0) * h.B["температура_выбора_паника"]
    temp += (1.0 - npc.rest / 100.0) * 0.4
    key_target = h.rng.softmax_pick(opts, temp)
    key, target = key_target
    execute(h, npc, key, target)
    return True


# ---------------------------------------------------------------- исполнение

def execute(h, npc, key, target):
    b = h.B
    spent = hours(key, npc)
    if key == "вылазка":
        spent = min(h.rng.uni(b["вылазка_часы_мин"], b["вылазка_часы_макс"]), npc.time_left)
    npc.time_left -= spent
    mark(h, npc, key, target)
    npc.stats["часы_работы"] = npc.stats.get("часы_работы", 0) + (spent if key in ("утепление", "дверь", "буржуйка", "разбор", "вылазка") else 0)
    lvl, kind = COST[key][1], COST[key][2]
    said = None

    if key == "поесть":
        need = npc.eaters()
        have = npc.stock.get("еда", 0.0)
        used = min(need, have)
        npc.stock["еда"] = have - used
        npc.satiety = clamp(npc.satiety + b["еда_за_порцию"] * (used / need))
        h.stats["съедено"] = h.stats.get("съедено", 0) + used
        # горячая еда пахнет сильнее — и выдаёт хозяина всему подъезду
        social.smell(h, npc, hot=npc.burning or (h.power_on and npc.shelter.get("обогреватель")))
        said = f"{npc.short} {vb(npc.sex, 'поел')}" + (f" и {vb(npc.sex, 'покормил')} {npc.dependent_name}" if npc.dependents else "")

    elif key == "поесть_мясо":
        need = npc.eaters()
        used = min(need, npc.stock.get("мясо", 0.0))
        npc.stock["мясо"] = npc.stock.get("мясо", 0.0) - used
        npc.satiety = clamp(npc.satiety + b["еда_за_порцию"] * (used / need))
        npc.mood = clamp(npc.mood - b["людоедство_настроение_за_раз"])
        social.smell(h, npc, hot=True)
        said = None

    elif key == "попить":
        if h.water_on:
            npc.hydration = clamp(npc.hydration + b["вода_за_порцию"])
            said = f"{npc.short} {vb(npc.sex, 'набрал')} воды из-под крана"
        else:
            npc.stock["вода"] = max(0.0, npc.stock.get("вода", 0.0) - npc.eaters())
            npc.hydration = clamp(npc.hydration + b["вода_за_порцию"])
            said = f"{npc.short} {vb(npc.sex, 'достал')} воду из запаса"

    elif key == "топить_снег":
        # на электроплитке, пока есть свет, вода достаётся даром
        on_power = (not npc.shelter.get("буржуйка")) and h.power_on and npc.shelter.get("обогреватель")
        if on_power:
            cost = 0.0
            how = "на плитке"
        else:
            # печка уже топится — доплачиваем немного; холодная — платим как за топку,
            # но тогда и квартира прогревается, топливо не выброшено
            already = npc.burning
            cost = b["снег_топливо"] * (b["снег_на_горящей_печке"] if already else 1.0)
            npc.burning = True
            how = "на горячей печке" if already else "затопив печку"
        if npc.stock.get("топливо", 0) < cost and npc.stock.get("материалы", 0) >= b["мебель_за_топку"]:
            npc.stock["материалы"] -= b["мебель_за_топку"]   # в ход пошла мебель
            cost = 0.0
            how = "на мебели"
        npc.stock["топливо"] = max(0.0, npc.stock.get("топливо", 0) - cost)
        npc.stock["вода"] = npc.stock.get("вода", 0) + b["снег_вода"]
        said = (f"{npc.short} {vb(npc.sex, 'натопил')} снега {how}"
                + (f" (-{cost:g} топлива)" if cost else ""))

    elif key == "топить":
        if npc.stock.get("топливо", 0) >= 1:
            npc.stock["топливо"] = npc.stock.get("топливо", 0) - 1
            said = f"{npc.short} {vb(npc.sex, 'затопил')} буржуйку"
        else:
            npc.stock["материалы"] = npc.stock.get("материалы", 0) - b["мебель_за_топку"]
            said = f"{npc.short} {vb(npc.sex, 'разломал')} мебель и {vb(npc.sex, 'затопил')} ею"
        npc.burning = True

    elif key == "генератор":
        npc.stock["топливо"] = npc.stock.get("топливо", 0) - 2
        npc.mood = clamp(npc.mood + 10)
        npc.warmth = clamp(npc.warmth + 8)
        said = f"{npc.short} {vb(npc.sex, 'запустил')} генератор — на весь подъезд гул и свет в окне"

    elif key == "утепление":
        npc.stock["материалы"] = npc.stock.get("материалы", 0) - b["утепление_материалы"]
        npc.shelter["утепление"] = npc.shelter.get("утепление", 0) + 1
        said = f"{npc.short} {vb(npc.sex, 'утеплил')} окна (уровень {npc.shelter['утепление']})"

    elif key == "дверь":
        npc.stock["материалы"] = npc.stock.get("материалы", 0) - b["дверь_материалы"]
        npc.shelter["дверь"] = npc.shelter.get("дверь", 0) + 1
        said = f"{npc.short} {vb(npc.sex, 'укрепил')} дверь (уровень {npc.shelter['дверь']})"

    elif key == "буржуйка":
        npc.stock["материалы"] = npc.stock.get("материалы", 0) - b["буржуйка_материалы"]
        npc.shelter["буржуйка"] = True
        said = f"{npc.short} {vb(npc.sex, 'собрал')} буржуйку"
        h.note(f"{npc.short} {vb(npc.sex, 'собрал')} буржуйку")

    elif key == "разбор":
        pool = [f for f in h.empty
                if f.stripped < b["разбор_максимум"] or sum(f.stock.values()) > 0] or h.empty
        flat = max(pool, key=lambda f: sum(f.stock.values()) + (b["разбор_максимум"] - min(b["разбор_максимум"], f.stripped)))
        got = {}
        for res, amount in list(flat.stock.items()):
            take = min(amount, 3.0)      # за один заход больше не утащить
            if take > 0:
                flat.stock[res] = amount - take
                npc.stock[res] = npc.stock.get(res, 0) + take
                got[res] = take
        if flat.stripped < b["разбор_максимум"]:
            npc.stock["материалы"] = npc.stock.get("материалы", 0) + b["разбор_материалов"]
            got["материалы"] = got.get("материалы", 0) + b["разбор_материалов"]
        flat.stripped += 1
        if flat.owner_died and sum(v for k, v in got.items() if k != "материалы") > 0:
            npc.mood = clamp(npc.mood - 5 * npc.t01("лояльность"))
        took = ", ".join(f"{k} {int(v)}" for k, v in got.items() if v)
        said = f"{npc.short} {vb(npc.sex, 'разобрал')} часть кв.{flat.apt}" + (f": {vb(npc.sex, 'взял')} {took}" if took else "")

    elif key == "вылазка":
        _outing(h, npc, spent)
        said = None  # текст пишет сам _outing

    elif key == "наблюдение":
        social.observe(h, npc, target)
        said = f"{npc.short} {vb(npc.sex, 'присматривался')} к кв.{target.apt}"

    elif key == "разговор":
        social.gossip(h, npc, target)
        social.adjust(npc, target.id, trust=b["доверие_за_разговор"])
        social.adjust(target, npc.id, trust=b["доверие_за_разговор"] * 0.8)
        npc.mood = clamp(npc.mood + b["настроение_от_общения"])
        target.mood = clamp(target.mood + b["настроение_от_общения"] * 0.7)
        npc.panic = clamp(npc.panic - 2)
        npc.stats["день_разговора"] = h.day
        target.stats["день_разговора"] = h.day
        said = f"{npc.short} {vb(npc.sex, 'зашёл')} к {target.form('dat')}"

    elif key == "попросить":
        said = _ask(h, npc, target)

    elif key == "поделиться":
        n = 1.0
        if npc.stock.get("еда", 0) >= n:
            npc.stock["еда"] -= n
            target.stock["еда"] = target.stock.get("еда", 0) + n
            social.adjust(target, npc.id, trust=b["доверие_за_помощь"] * 1.3, hate=-10)
            social.adjust(npc, target.id, trust=0.6)
            npc.mood = clamp(npc.mood + b["настроение_от_помощи"])
            target.mood = clamp(target.mood + 8)
            target.favors[npc.id] = target.favors.get(npc.id, 0) + 1
            target.ask_record(npc.id)["я_дал"] = h.day
            npc.ask_record(target.id)["дали"] += 0.5   # он мне не отказывал, я сам принёс
            said = f"{npc.short} {'сама занесла' if npc.sex == 'ж' else 'сам занёс'} еду {target.form('dat')}"

    elif key == "обмен":
        said = _trade(h, npc, target)

    elif key == "лечить":
        npc.stock["лекарства"] = npc.stock.get("лекарства", 0) - 1
        if target.injuries:
            target.injuries.pop()
        target.sick = None
        target.health = clamp(target.health + 18)
        if target.id != npc.id:
            social.adjust(target, npc.id, trust=b["доверие_за_лечение"], hate=-12)
            target.mood = clamp(target.mood + 10)
            npc.mood = clamp(npc.mood + b["настроение_от_помощи"])
            target.favors[npc.id] = target.favors.get(npc.id, 0) + 1
            said = f"{npc.short} {vb(npc.sex, 'перевязал')} {target.form('acc')}"
        else:
            said = f"{npc.short} {vb(npc.sex, 'обработал')} раны"

    elif key == "тело":
        flat = min([f for f in h.empty if f.body and f.body["порций"] > 0],
                   key=lambda f: abs(f.floor - npc.floor))
        take = min(flat.body["порций"], 4.0)
        flat.body["порций"] -= take
        flat.body["тронуто"] = True
        npc.stock["мясо"] = npc.stock.get("мясо", 0) + take
        npc.mood = clamp(npc.mood - b["людоедство_настроение"])
        npc.panic = clamp(npc.panic + 12)
        npc.stats["переступил"] = 1
        h.bump("людоедство")
        if npc.stats.get("раскрыт"):
            # прятаться больше не от кого
            h.journal.line(f"{npc.short} {vb(npc.sex, 'ходил')} в кв.{flat.apt}. "
                           f"Уже не таясь.", 1)
        else:
            h.journal.line(h.rng.pick([
                f"{npc.short} {vb(npc.sex, 'ходил')} в кв.{flat.apt} и {vb(npc.sex, 'вернулся')} "
                f"с чем-то тяжёлым, завёрнутым в простыню.",
                f"Ночью на лестнице долго возились. Утром дверь кв.{flat.apt} была приоткрыта.",
                f"{npc.short} {vb(npc.sex, 'провёл')} полдня в кв.{flat.apt} и не {vb(npc.sex, 'сказал')}, зачем.",
            ]), 1)
        h.journal.secret(f"{npc.short} взял тело {flat.body['падеж']} — {take:g} порц.")
        # мог кто-то увидеть на лестнице
        for other in h.others(npc):
            if other.away or other.id == npc.id:
                continue
            seen = 0.14 + (0.12 if abs(other.floor - flat.floor) <= 1 else 0.0)
            if h.rng.chance(seen):
                conflict.reveal_taboo(h, npc, witness=other)
                break
        said = None

    elif key == "вынести":
        flat = min([f for f in h.empty if f.body and f.body["порций"] > 0],
                   key=lambda f: abs(f.floor - npc.floor))
        name = flat.body.get("вин") or flat.body["кто"]
        flat.body["порций"] = 0.0
        npc.warmth = clamp(npc.warmth - 10)
        npc.mood = clamp(npc.mood - 6)
        for p in h.alive():
            social.adjust(p, npc.id, trust=1.0)
            p.mood = clamp(p.mood + 3)
        h.bump("тел_вынесено")
        h.journal.line(f"{npc.short} {vb(npc.sex, 'вынес')} {name} во двор и {vb(npc.sex, 'завалил')} снегом. "
                       f"Больше в той квартире брать нечего.", 2)
        said = None

    elif key == "переехать":
        host = target
        # хозяин решает: греть двоих дороже, но вдвоём не так страшно
        yes = host.t01("лояльность") * 5.0 + host.trust.get(npc.id, 3.0) * 0.8
        yes -= host.t01("жадность") * 2.0
        yes -= (1.0 - min(1.0, host.secure("топливо"))) * 6.0
        # пустить человека — значит пустить и его рот: в голод это решает
        yes -= (1.0 - min(1.0, host.secure("еда"))) * 5.5
        yes += 2.0 if npc.id in host.allies else 0.0
        yes += 1.5 if npc.dependents else 0.0
        # в однушке с буржуйкой больше двоих не помещается
        yes -= len(host.guests) * b["теснота_отказ"]
        if yes > 4.0 and len(host.guests) < b["переезд_максимум_гостей"]:
            npc.living_with = host.id
            host.guests.add(npc.id)
            fuel = npc.stock.get("топливо", 0.0)
            host.stock["топливо"] = host.stock.get("топливо", 0.0) + fuel
            npc.stock["топливо"] = 0.0
            social.adjust(npc, host.id, trust=2.0, hate=-20)
            social.adjust(host, npc.id, trust=1.0)
            npc.mood = clamp(npc.mood + 12)
            host.mood = clamp(host.mood + 6)
            h.bump("переездов")
            h.journal.line(f"{npc.short} {vb(npc.sex, 'перебрался')} к {host.form('dat')} "
                           f"в кв.{host.apt} — топят одну печку на двоих.", 2)
            h.note(f"{npc.short} {vb(npc.sex, 'переехал')} к {host.form('dat')}")
            # своя квартира остаётся пустой, и это все понимают
            from .model import EmptyFlat
            h.empty.append(EmptyFlat(apt=npc.apt, floor=npc.floor, stock={}))
        else:
            social.adjust(npc, host.id, trust=-1.0, hate=10)
            npc.ask_record(host.id)["отказ_переезд"] = h.day
            npc.mood = clamp(npc.mood - 8)
            h.journal.line(f"{npc.short} {vb(npc.sex, 'просил')} пустить к себе. {host.short} не {vb(host.sex, 'пустил')}.", 1)
        said = None

    elif key == "быт":
        npc.mood = clamp(npc.mood + 3.0 * npc.normalcy)
        npc.panic = clamp(npc.panic - 1.5)
        variants = h.mods.get("реплики_быт") or ["занимал{ся|ась} своими делами"]
        said = f"{npc.short} {gform(h.rng.pick(variants), npc.sex)}"

    elif key == "отдых":
        npc.mood = clamp(npc.mood + 5)
        npc.rest = clamp(npc.rest + 6)
        npc.panic = clamp(npc.panic - 3)
        said = f"{npc.short} {vb(npc.sex, 'лежал')} и ничего не {vb(npc.sex, 'делал')}"

    elif key == "кража_днём":
        res, moved = conflict.steal(h, npc, target)
        said = None

    if said:
        h.journal.line(said, NOTABLE.get(key, 0))
    if lvl and kind:
        heard = social.emit(h, npc, lvl, kind, night=False)
        if НОРМА.get(key, 0) >= 0.9:
            # сосед заколотил дверь — значит, так уже можно
            for w in heard:
                w.stats["видел_чужое"] = w.stats.get("видел_чужое", 0) + 1
        if heard and lvl >= 3:
            who = ", ".join(p.short for p in heard)
            h.journal.line(f"   (слышали: {who})", 0)


def _ask(h, npc, target):
    """Просьба о припасах. GDD 14: просьба без доверия сама по себе опасна."""
    b = h.B
    trust = target.trust.get(npc.id, 3.0)
    give = target.t01("лояльность") * 5.0 + trust * 0.9
    give -= target.t01("жадность") * 2.5
    give -= target.hate.get(npc.id, 0.0) / 12.0
    if npc.id in target.allies:
        give += 2.5
    if npc.dependents:
        give += 1.8
    if target.favors.get(npc.id, 0):
        give += 1.0
    res = most_needed(npc)
    has = target.stock.get(res, 0)
    # главное: человек отдаёт только то, что считает лишним для себя.
    # он не знает, сколько ещё терпеть, и считает по своему горизонту
    shortfall = 1.0 - min(1.0, target.secure(res))
    give -= shortfall * h.B["жадность_от_нехватки"]
    give -= shortfall * target.t01("жадность") * 4.0
    # своим последним топливом делятся тяжелее, чем последней банкой
    if res == "топливо":
        give -= 1.0
    # сам факт просьбы: «просят — значит, скоро будут отбирать» (GDD 14)
    target.panic = clamp(target.panic + 4)
    social.adjust(target, npc.id, aware=-5)
    social.adjust(npc, target.id, aware=12)

    rec = npc.ask_record(target.id)
    rec["последняя"] = h.day
    if has >= 2 and give > 3.0:
        target.stock[res] = has - 1
        npc.stock[res] = npc.stock.get(res, 0) + 1
        social.adjust(npc, target.id, trust=b["доверие_за_помощь"], hate=-15)
        social.adjust(target, npc.id, trust=0.5)
        npc.mood = clamp(npc.mood + 8)
        target.mood = clamp(target.mood + 4)
        npc.favors[target.id] = npc.favors.get(target.id, 0) + 1
        rec["дали"] += 1
        rec["подряд"] = 0
        target.ask_record(npc.id)["я_дал"] = h.day     # я ему дал — теперь не прошу сам
        h.bump("помощи")
        return (f"{npc.short} {vb(npc.sex, 'попросил')} {RES_GEN.get(res, res)} "
                f"у {target.form('gen')} — дали")
    else:
        social.adjust(npc, target.id, trust=b["доверие_за_отказ"], hate=b["ненависть_за_отказ"] * (0.7 + npc.desperation()))
        npc.refused_by[target.id] = npc.refused_by.get(target.id, 0) + 1
        rec["отказали"] += 1
        rec["подряд"] += 1
        # «жмётся — значит есть». Отказ выдаёт запасы не хуже наблюдения
        social.adjust(npc, target.id, aware=b["отказ_осведомлённость"])
        npc.mood = clamp(npc.mood - 6)
        h.bump("отказов")
        return (f"{npc.short} {vb(npc.sex, 'попросил')} {RES_GEN.get(res, res)} "
                f"у {target.form('gen')} — отказали")


def _trade(h, a, b_npc):
    deal = find_deal(h, a, b_npc)
    if not deal:
        return None
    (give, gn), (get_, tn) = deal
    if b_npc.stock.get(get_, 0) < tn or a.stock.get(give, 0) < gn:
        return None
    a.stock[give] -= gn
    b_npc.stock[give] = b_npc.stock.get(give, 0) + gn
    b_npc.stock[get_] -= tn
    a.stock[get_] = a.stock.get(get_, 0) + tn
    social.adjust(a, b_npc.id, trust=0.7, aware=10)
    social.adjust(b_npc, a.id, trust=0.7, aware=10)
    h.bump("обменов")
    return (f"{a.short} {vb(a.sex, 'обменял')} {RES_ACC.get(give, give)} "
            f"на {RES_ACC.get(get_, get_)} с {b_npc.form('ins')}")


def _outing(h, npc, dur):
    """Вылазка (GDD 20): единственный источник новых ресурсов."""
    from .world import outing_danger
    b = h.B
    npc.away = True
    danger = outing_danger(h)

    take = b["вылазка_добыча_база"] * h.scav_richness * h.rng.uni(0.5, 1.4)
    take *= 0.8 + npc.t01("храбрость") * 0.5
    got = {}
    for _ in range(int(take) + (1 if h.rng.chance(take % 1) else 0)):
        res = h.rng.weighted([("еда", 31), ("топливо", 31), ("материалы", 19), ("вода", 9), ("лекарства", 4), ("патроны", 2)])
        got[res] = got.get(res, 0) + 1
        npc.stock[res] = npc.stock.get(res, 0) + 1
        h.stats["принесено_" + res] = h.stats.get("принесено_" + res, 0) + 1
    h.scav_richness = max(b["вылазка_минимум_богатства"], h.scav_richness - b["вылазка_истощение_за_ход"])

    npc.warmth = clamp(npc.warmth - b["вылазка_холод_за_час"] * dur * (1.0 + max(0.0, (-h.outside - 12)) * 0.03))
    npc.rest = clamp(npc.rest - 6)

    txt = f"{npc.short} {vb(npc.sex, 'ходил')} на улицу ({dur:.0f} ч): "
    txt += ", ".join(f"{k} {v}" for k, v in got.items()) if got else "пусто"

    if h.rng.chance(b["вылазка_шанс_травмы"] * danger):
        inj = h.rng.pick(["ушиб", "порез", "перелом", "обморожение"])
        npc.injuries.append(inj)
        npc.health = clamp(npc.health - h.rng.uni(8, 20))
        txt += f"; {vb(npc.sex, 'вернулся') if npc.sex != 'ж' else 'вернулась'} с травмой ({inj})"
    if h.rng.chance(b["вылазка_шанс_встречи"] * danger):
        if h.rng.chance(0.5):
            lost = {k: v for k, v in list(npc.stock.items()) if v > 0}
            res = h.rng.pick(list(lost)) if lost else None
            if res:
                npc.stock[res] = max(0, npc.stock[res] - h.rng.rint(1, 2))
            npc.panic = clamp(npc.panic + 15)
            txt += "; во дворе отняли часть добычи"
        else:
            npc.panic = clamp(npc.panic + 8)
            txt += "; на обратном пути кто-то шёл следом"

    npc.away = False
    h.journal.line(txt, 1)
    # возвращение с пакетами видно всем (GDD 13)
    social.emit(h, npc, 2, "возвращение", night=False)
    if got:
        for other in h.others(npc):
            if h.rng.chance(0.35 + 0.1 * (2 - min(2, h.floor_gap(npc, other)))):
                social.adjust(other, npc.id, aware=14)
                social.note_signal(other, npc.id, "еда", other.believed(npc.id, "еда") + got.get("еда", 0), 0.5)
