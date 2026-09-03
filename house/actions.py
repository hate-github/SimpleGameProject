# -*- coding: utf-8 -*-
"""Что человек делает днём и как он это выбирает.

Каждое действие занимает время (GDD 5) и имеет громкость (GDD 13).
Выбор — мягкий: обычно берётся лучшее по оценке, но чем выше паника,
тем чаще человек делает глупость.
"""
from .util import clamp, norm, vb, gform
from . import social, conflict

# ключ: (громкость, вид шума). Часы лежат в balance.json -> "часы_действий":
# держать их в двух местах — верный способ поменять одно и забыть другое.
COST = {
    "поесть":       (1, "готовка"),
    "попить":       (1, "шаги"),
    "топить_снег":  (1, "готовка"),
    "топить":       (1, "буржуйка"),
    "генератор":    (5, "генератор"),
    "утепление":    (3, "ремонт"),
    "дверь":        (3, "дверь"),
    "буржуйка":     (3, "ремонт"),
    "разбор":       (3, "разбор"),
    "вылазка":      (2, "возвращение"),
    "наблюдение":   (0, "шаги"),
    "разговор":     (1, "шаги"),
    "попросить":    (1, "шаги"),
    "поделиться":   (1, "шаги"),
    "обмен":        (1, "шаги"),
    "лечить":       (1, "шаги"),
    "отдых":        (0, None),
    "быт":          (1, "шаги"),
    "переехать":    (2, "шаги"),
    "занять":       (2, "разбор"),
    "съехать":      (2, "шаги"),
    "выгнать":      (3, "ссора"),
    "отнять":       (3, "ссора"),
    "кража_днём":   (3, "взлом"),
    "тело":         (2, "разбор"),
    "поесть_мясо":  (1, "готовка"),
    "вынести":      (3, "разбор"),
    "генератор_собрать": (3, "ремонт"),
    "звукоизоляция":     (3, "ремонт"),
    "стены":             (3, "ремонт"),
    "листы":             (3, "дверь"),
}

# GDD 15: у третьего и четвёртого уровня есть требования к умениям.
# «Смекалки» в прототипе нет — её заменяют умения жильцов.
ПОСТРОЙКИ = {
    # ключ: (уровень, ресурс-ключ материалов, нужные умения, потолок)
    "генератор_собрать": (3, "генератор_материалы", ("электрик",), 1),
    "листы":             (3, "листы_материалы", ("слесарь", "электрик"), 2),
    "звукоизоляция":     (4, "звукоизоляция_материалы", ("слесарь",), 1),
    "стены":             (4, "стены_материалы", ("слесарь",), 2),
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
    """Чего у человека меньше всего в днях — то и пойдёт просить.

    Переехавший к соседу дрова не просит: печку топит хозяин, а всё, что гость
    приносит, к этой же печке и уходит. Пока топливо считалось и ему, треть
    всех просьб о дровах в доме шла от людей, которые сидят у чужой горячей печи.
    """
    ресурсы = ("еда", "вода", "топливо") if npc.топит_сам() else ("еда", "вода")
    return min(ресурсы, key=lambda r: npc.days_of(r))


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
    "занять": 0.55,      # перебраться в квартиру, которая тебе не принадлежит
    "выгнать": 0.80,      # выставить из своей квартиры на мороз
    "отнять": 1.15,       # в мирное время это называется разбоем
    "вылазка": 0.18,
}


def norm_gate(npc, key, b):
    """Множитель к оценке: чем нормальнее ещё кажется жизнь, тем немыслимее поступок."""
    w = НОРМА.get(key, 0.0)
    if not w:
        return 1.0
    return clamp(b["нормальность_порог"] - npc.normalcy * w, 0.02, 1.0)


# действия, у которых есть чем провалиться: тратится материал, страдает рука
СТРОЙКА = {"утепление": "окна", "дверь": "дверь", "буржуйка": "буржуйку",
           "генератор_собрать": "генератор", "звукоизоляция": "звукоизоляцию",
           "стены": "стены", "листы": "листы на дверь"}

# что стоит показывать в обычном режиме, а что — только с --подробно
NOTABLE = {"буржуйка": 1, "дверь": 1, "утепление": 1, "генератор": 1, "разбор": 1,
           "попросить": 1, "поделиться": 1, "обмен": 1, "лечить": 1}


def порция(h, npc, b):
    """Сколько сытости даёт одна порция еды.

    GDD 12.1: умения полезны игроку и группе. Повар вытягивает из той же банки
    больше — и кормит не только себя, но и всех, кто с ним под одной крышей.
    """
    cooks = "повар" in npc.skills
    if not cooks:
        for other_id in ([npc.living_with] if npc.living_with else []) + sorted(npc.guests):
            o = h.get(other_id)
            if o and o.alive and not o.exiled and "повар" in o.skills:
                cooks = True
                break
    return b["еда_за_порцию"] * ((1.0 + b["повар_прибавка"]) if cooks else 1.0)


def вложить(h, npc, материалов):
    """Материалы, ушедшие в стены, остаются в стенах.

    Это и есть привязанность к своему углу: человек не бросает квартиру,
    в которую вбил месяц работы, даже если рядом стоит лучше. Числом, а не
    отдельной чертой характера, — и потому у каждого своя.
    """
    h.where(npc).вложено += материалов


def spend(h, npc, res, amount):
    """Израсходовать ресурс безвозвратно и записать это.

    Съеденное, сожжённое и потраченное на стройку уходит из мира. Пока это
    не считалось, баланс дома не сходился, и настоящую утечку было не отличить
    от нормальной траты.
    """
    have = npc.stock.get(res, 0.0)
    used = min(amount, have)
    npc.stock[res] = have - used
    h.stats["израсходовано_" + res] = h.stats.get("израсходовано_" + res, 0.0) + used
    return used


# работа, а не быт: только на неё влияет состояние тела
РАБОТА = {"утепление", "дверь", "буржуйка", "разбор", "вылазка", "топить_снег", "вынести", "тело"}


def hours(key, npc, b):
    """Сколько часов занимает действие (GDD 5), с поправкой на состояние (GDD 6.1).

    Голодный, промёрзший и не спавший человек делает ту же работу дольше.
    Разговор и еда от этого не растягиваются — растягивается труд.
    """
    v = b["часы_действий"][key]
    if key == "буржуйка" and ("слесарь" in npc.skills or "электрик" in npc.skills):
        v -= b["часы_ремонта_за_умение"]
    if key in РАБОТА:
        v *= npc.speed(b)
    return v


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
        if key in СТРОЙКА and npc.hurt("руки"):
            return                    # разбитой рукой не строят (GDD 6.2)
        if hours(key, npc, b) > npc.time_left + 0.001:
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
    can_melt = npc.shelter.get("буржуйка") or (h.powered(npc) and npc.shelter.get("обогреватель"))
    # печка в квартире одна, и дрова к ней общие: за снег гостя платит то же
    # хозяйство, у которого он греется. Иначе гость топил снег даром — своего
    # топлива у него нет по устройству
    очаг = h.хозяин_жилья(npc)
    есть_чем = (очаг.stock.get("топливо", 0) >= b["снег_топливо"]
                or npc.stock.get("материалы", 0) >= b["мебель_за_топку"])
    if not h.water_on and can_melt and npc.stock.get("вода", 0) < 4 and есть_чем:
        # снег топят охотнее, когда печка и так нужна: одно действие закрывает
        # и жажду, и холод
        together = cold * 3.0 if not очаг.burning and npc.shelter.get("буржуйка") else 0.0
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

    if npc.shelter.get("генератор") and очаг.stock.get("топливо", 0) >= 2 and not h.power_on:
        # генератор — это не грелка, а электричество на сутки: от него работает
        # обогреватель и на нём топят снег (GDD 15, уровень 3)
        нужен = cold * 4.0 + (1.0 - norm(npc.mood, 20, 80)) * 2.0
        if npc.shelter.get("питание") == h.day:
            нужен = 0.0            # он уже работает, второй раз заводить нечего
        if npc.shelter.get("обогреватель"):
            нужен += cold * 4.0
        if not h.water_on and npc.stock.get("вода", 0) < 3:
            нужен += thirsty * 3.0
        add("генератор", нужен - fear * 3.0 * (0.4 if npc.shelter.get("звукоизоляция") else 1.0))

    # --- убежище (GDD 15) ---
    mats = npc.stock.get("материалы", 0)
    # смотрят не только на сегодня: метель с каждым днём злее, и это все знают
    room_soon = room - b["температура_падение_в_день"] * 3
    cold_pressure = clamp((b["комфортная_температура"] + 4 - room_soon) / 14.0, 0.0, 1.6)
    if not npc.shelter.get("буржуйка") and mats >= b["буржуйка_материалы"]:
        add("буржуйка", 4.0 + cold_pressure * 6.0)
    if npc.shelter.get("утепление", 0) < b["максимум_утепления"] and mats >= b["утепление_материалы"]:
        add("утепление", 1.0 + cold_pressure * 5.0)
    # --- убежище уровней 3 и 4 (GDD 15): их не построить без человека с руками
    for ключ, (уровень, мат_ключ, умения, потолок) in ПОСТРОЙКИ.items():
        если_есть = npc.shelter.get({"генератор_собрать": "генератор"}.get(ключ, ключ), 0)
        if (если_есть or 0) >= потолок:
            continue
        if not any(u in npc.skills for u in умения):
            continue
        if mats < b[мат_ключ]:
            continue
        s = {"генератор_собрать": 2.0 + cold * 4.0,
             "листы": fear * 4.0,
             "звукоизоляция": fear * 2.0 + (3.0 if npc.shelter.get("генератор") else 0.0),
             "стены": fear * 5.0 - 1.0}[ключ]
        add(ключ, s)

    if npc.shelter.get("дверь", 0) < b["максимум_двери"] and mats >= b["дверь_материалы"]:
        s = fear * 4.5 + npc.t01("жадность") * 1.5
        if h.mods.get("укрепление_порыв") == h.day:
            s += 2.5
        add("дверь", s)
    # квартиру можно разобрать лишь несколько раз — потом там голые стены
    strippable = [f for f in h.пустые()
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

    # --- вместе или порознь (GDD «Съехаться») ---
    # то же число, которым считают переезд, каждый день пересчитывается заново.
    # Пока выхода из сожительства не было, пары доживали до конца с взаимной
    # ненавистью 86 из 100 и продолжали делить одну комнату
    if npc.living_with:
        host = h.get(npc.living_with)
        if host and host.alive and not host.exiled:
            add("съехать", -social.выгода_соседства(h, npc, host) - b["съехать_порог"], host)
    for g_id in sorted(npc.guests):
        g = h.get(g_id)
        if not (g and g.alive and not g.exiled):
            continue
        # выставить может только тот, кто сильнее: слабый хозяин терпит
        if npc.power() < g.power() * b["выгнать_превосходство"]:
            continue
        add("выгнать", -social.выгода_соседства(h, npc, g) - b["выгнать_порог"], g)

    # --- занять пустую квартиру (GDD 12): жильё — вещь, и его занимают ---
    # человек уходит не туда, где просто пусто, а туда, где заметно лучше:
    # целая дверь, заклеенные окна, чужая буржуйка. И тем тяжелее ему уйти,
    # чем больше он вложил в собственные стены — привязанность считается
    # материалами, а не отдельной чертой характера
    if not npc.living_with:
        своя = h.flats[npc.apt]
        моя_цена = h.ценность_жилья(своя, npc)
        держит = своя.вложено * b["жильё_привязанность"]
        for f in h.пустые():
            if f.apt == npc.apt:
                continue
            выгода = h.ценность_жилья(f, npc) - моя_цена - держит - b["занять_порог"]
            if f.body and f.body.get("порций", 0) > 0:
                выгода -= b["занять_тело_штраф"] * (0.5 + npc.t01("лояльность"))
            if h.чей(f) is not None:
                # угол живого человека: он сейчас у соседа, но возвращаться
                # ему будет некуда
                выгода -= b["занять_чужую_штраф"] * (0.5 + npc.t01("лояльность"))
            add("занять", выгода, f)

    # --- то, до чего доходят на третьей неделе ---
    bodies = [f for f in h.пустые() if f.body and f.body["порций"] > 0
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

    # --- улица (GDD 20): ближе и беднее или дальше и опаснее ---
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
            # хозяина нет дома: поговорить не с кем, но квартира стоит пустая.
            # Раньше эта ветка была ниже по циклу, за `continue`, и не достигалась
            # никогда — дневная кража из GDD 12.5 была мёртвым кодом
            if (npc.confidence(t.id) > 0.2 and not t.living_with
                    and not social.под_одной_крышей(h, npc, t)):
                greed = npc.loot_value(t.id) * (0.4 + npc.t01("жадность") * 0.8)
                score = (greed * (0.4 + des) - npc.t01("лояльность") * 4.0
                         - (1.0 - conflict.stealth(npc)) * 3.0)
                score += npc.hate.get(t.id, 0) / 25.0
                score -= t.shelter.get("дверь", 0) * 1.4
                # днём ломать чужую дверь страшнее, чем ночью: в подъезде люди,
                # шум слышно всем, и объяснить себя нечем
                score -= b["кража_днём_решимость"]
                score -= len([o for o in h.others(npc) if not o.away]) * 0.5
                add("кража_днём", score * norm_gate(npc, "кража", b), t)
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
        # видно не чужой шкаф, а чужое лицо: делятся с тем, кто КАЖЕТСЯ голодным
        его_еда = social.believed_days(npc, t, "еда")
        if (npc.secure("еда") > b["порог_излишка"]
                and npc.confidence(t.id) > 0.3 and not gave_me_today
                and его_еда < b["помощь_порог_дней"]
                and npc.days_of("еда") > его_еда + 1.0):
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
            move = social.выгода_соседства(h, npc, t) * b["переезд_вес_выгоды"]
            move += (1.0 - norm(npc.warmth, 10, 55)) * 4.0
            move -= npc.t01("храбрость") * 1.5      # гордость
            add("переехать", move, t)
        # отнять на лестнице: без двери, без замка, лицом к лицу
        A = conflict.aggr(h)
        # человека, с которым делишь одну комнату, на лестнице не зажимают:
        # вечером возвращаться в неё же
        if social.под_одной_крышей(h, npc, t):
            continue
        ratio = npc.power() / max(0.2, t.power())
        # ловят того, кого видели с пакетами; случайная встреча на лестнице — редкость
        met = (t.stats.get("день_вылазки") == h.day) or h.rng.chance(0.08)
        trigger = des > 0.40 or npc.hate.get(t.id, 0) > 35
        if met and trigger and ratio > b["отъём_порог_силы"] / A and npc.loot_value(t.id) > 1.5:
            take = (des * 3.5 + npc.hate.get(t.id, 0) / 18.0) * (0.4 + npc.t01("жадность"))
            take *= min(2.2, ratio)
            take -= b["отъём_решимость"]
            take -= npc.t01("лояльность") * 5.0 / A
            take -= len([o for o in h.others(npc) if t.id in o.allies]) * 1.6
            take -= 2.0 if t.id in npc.allies else 0.0
            take -= 1.5 if t.dependents else 0.0
            # дом закрывается против того, кто уже отнимал: это страшнее совести
            take -= len([o for o in h.others(npc) if o.hate.get(npc.id, 0) > 50]) * 1.8
            add("отнять", take, t)
        # обмен
        add("обмен", _trade_score(h, npc, t), t)
        # лечение
        # GDD 6.2: «помощь NPC-медика (если есть доверие)»
        if ("медик" in npc.skills and npc.stock.get("лекарства", 0) >= 1
                and (t.injuries or t.sick) and t.trust.get(npc.id, 3.0) >= b["лечение_доверие"]):
            add("лечить", 5.0 + npc.t01("лояльность") * 3.0 + trust * 0.3, t)

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


TRADABLE = ("еда", "топливо", "лекарства", "материалы", "вода")


def find_deal(h, a, b_npc):
    """Найти сделку, которую ОБЕ стороны сочтут выгодной (GDD 18).

    Главное: инициатор не знает правды о чужом складе. И что у соседа есть,
    и насколько соседу это нужно, он прикидывает по своей оценке — той самой,
    ради которой построены шум, запах и слухи. Раньше здесь трижды читался
    настоящий `b_npc.stock`, и осведомлённость в обмене не участвовала вовсе.

    Стоимость единицы считается по одному разу на человека: `value_of` линейна
    по количеству, а количество в сделке всегда единица. Это самое горячее
    место симуляции, через него шло 60% всего времени.
    """
    b = h.B
    va = {r: social.value_of(a, r, 1.0) for r in TRADABLE}
    # как, по мнению a, живёт b_npc — а не как он живёт на самом деле
    vb = {r: social.value_of(b_npc, r, 1.0, days=social.believed_days(a, b_npc, r))
          for r in TRADABLE}
    best = None
    for give in TRADABLE:
        if a.stock.get(give, 0) < 2:
            continue
        for get_ in TRADABLE:
            if get_ == give:
                continue
            # предлагают за то, что у соседа, по-твоему, есть
            if a.believed(b_npc.id, get_) < b["обмен_порог_веры"]:
                continue
            n = 1.0
            mine_gain = va[get_] - va[give]
            their_gain = vb[give] - vb[get_]
            if mine_gain > 0.2 and their_gain > 0.2:
                score = mine_gain + their_gain
                if best is None or score > best[0]:
                    best = (score, (give, n), (get_, n))
    if best:
        return best[1], best[2]
    return None


def choose_and_do(h, npc):
    """Один ход одного человека. Возвращает True, если действие совершено."""
    npc.away = False          # раз он снова берётся за дело — значит, он дома
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
    spent = hours(key, npc, b)
    место = None
    if key == "вылазка":
        # куда идти, решают до выхода, и дорога стоит времени: без этого
        # «магазин на Заречной» отнимал ровно столько же дня, сколько мусорка
        # во дворе, и выбор «ближе и беднее или дальше и опаснее» был бесплатным
        место = выбрать_место(h, npc)
        spent = min(h.rng.uni(b["вылазка_часы_мин"], b["вылазка_часы_макс"]) * место[1],
                    npc.time_left)
    npc.time_left -= spent
    mark(h, npc, key, target)
    npc.stats["часы_работы"] = npc.stats.get("часы_работы", 0) + (spent if key in ("утепление", "дверь", "буржуйка", "разбор", "вылазка") else 0)
    lvl, kind = COST[key]
    said = None

    # у работы есть шанс провала (GDD 7: «провал — потеря материалов, травма руки»)
    if key in СТРОЙКА and not h.rng.chance(npc.success(b)):
        мат_ключ = (ПОСТРОЙКИ[key][1] if key in ПОСТРОЙКИ
                    else {"утепление": "утепление_материалы", "дверь": "дверь_материалы",
                          "буржуйка": "буржуйка_материалы"}[key])
        потеря = b[мат_ключ] * b["провал_доля_материалов"]
        spend(h, npc, "материалы", потеря)
        npc.mood = clamp(npc.mood - 5)
        текст = f"{npc.short} {vb(npc.sex, 'взялся')} за {СТРОЙКА[key]}, но {vb(npc.sex, 'испортил')} материал"
        if h.rng.chance(b["провал_шанс_травмы"]):
            npc.injuries.append(h.rng.pick(["ушиб руки", "порез руки"]))
            npc.health = clamp(npc.health - h.rng.uni(4, 10))
            текст += f" и {vb(npc.sex, 'рассадил')} руку"
        h.journal.line(текст + ".", 1)
        if lvl and kind:
            social.emit(h, npc, lvl, kind, night=False)
        return

    if key == "поесть":
        need = npc.eaters()
        have = npc.stock.get("еда", 0.0)
        used = spend(h, npc, "еда", need)
        npc.satiety = clamp(npc.satiety + порция(h, npc, b) * (used / need))
        h.stats["съедено"] = h.stats.get("съедено", 0) + used
        # горячая еда пахнет сильнее — и выдаёт хозяина всему подъезду
        social.smell(h, npc, hot=npc.burning or (h.powered(npc) and npc.shelter.get("обогреватель")))
        said = f"{npc.short} {vb(npc.sex, 'поел')}" + (f" и {vb(npc.sex, 'покормил')} {npc.dependent_name}" if npc.dependents else "")

    elif key == "поесть_мясо":
        need = npc.eaters()
        used = spend(h, npc, "мясо", need)
        npc.satiety = clamp(npc.satiety + порция(h, npc, b) * (used / need))
        npc.mood = clamp(npc.mood - b["людоедство_настроение_за_раз"])
        social.smell(h, npc, hot=True)
        said = None

    elif key == "попить":
        if h.water_on:
            npc.hydration = clamp(npc.hydration + b["вода_за_порцию"])
            # из-под крана вода в запас не идёт, но и из мира не уходит
            said = f"{npc.short} {vb(npc.sex, 'набрал')} воды из-под крана"
        else:
            spend(h, npc, "вода", npc.eaters())
            npc.hydration = clamp(npc.hydration + b["вода_за_порцию"])
            said = f"{npc.short} {vb(npc.sex, 'достал')} воду из запаса"

    elif key == "топить_снег":
        # на электроплитке, пока есть свет, вода достаётся даром
        очаг = h.хозяин_жилья(npc)
        on_power = (not npc.shelter.get("буржуйка")) and h.powered(npc) and npc.shelter.get("обогреватель")
        if on_power:
            cost = 0.0
            how = "на плитке"
        else:
            # печка уже топится — доплачиваем немного; холодная — платим как за топку,
            # но тогда и квартира прогревается, топливо не выброшено
            already = очаг.burning
            cost = b["снег_топливо"] * (b["снег_на_горящей_печке"] if already else 1.0)
            очаг.burning = True
            how = "на горячей печке" if already else "затопив печку"
        if очаг.stock.get("топливо", 0) < cost and npc.stock.get("материалы", 0) >= b["мебель_за_топку"]:
            spend(h, npc, "материалы", b["мебель_за_топку"])   # в ход пошла мебель
            cost = 0.0
            how = "на мебели"
        spend(h, очаг, "топливо", cost)
        npc.stock["вода"] = npc.stock.get("вода", 0) + b["снег_вода"]
        h.stats["натоплено_вода"] = h.stats.get("натоплено_вода", 0) + b["снег_вода"]
        said = (f"{npc.short} {vb(npc.sex, 'натопил')} снега {how}"
                + (f" (-{cost:g} топлива)" if cost else ""))

    elif key == "топить":
        if npc.stock.get("топливо", 0) >= 1:
            spend(h, npc, "топливо", 1)
            said = f"{npc.short} {vb(npc.sex, 'затопил')} буржуйку"
        else:
            spend(h, npc, "материалы", b["мебель_за_топку"])
            said = f"{npc.short} {vb(npc.sex, 'разломал')} мебель и {vb(npc.sex, 'затопил')} ею"
        npc.burning = True

    elif key == "генератор":
        spend(h, h.хозяин_жилья(npc), "топливо", 2)
        npc.shelter["питание"] = h.day     # свет в квартире на сутки (GDD 15)
        npc.mood = clamp(npc.mood + 10)
        npc.warmth = clamp(npc.warmth + 4)
        for g in sorted(npc.guests):
            o = h.get(g)
            if o and o.alive:
                o.mood = clamp(o.mood + 6)
        said = f"{npc.short} {vb(npc.sex, 'запустил')} генератор — на весь подъезд гул и свет в окне"

    elif key in ПОСТРОЙКИ:
        уровень, мат_ключ, _умения, _потолок = ПОСТРОЙКИ[key]
        spend(h, npc, "материалы", b[мат_ключ])
        вложить(h, npc, b[мат_ключ])
        поле = {"генератор_собрать": "генератор"}.get(key, key)
        if поле == "генератор":
            npc.shelter["генератор"] = True
            said = f"{npc.short} {vb(npc.sex, 'собрал')} генератор из того, что было в подвале"
        else:
            npc.shelter[поле] = npc.shelter.get(поле, 0) + 1
            said = {"листы": f"{npc.short} {vb(npc.sex, 'прибил')} стальные листы на дверь",
                    "звукоизоляция": f"{npc.short} {vb(npc.sex, 'заглушил')} генератор — гула больше не слышно",
                    "стены": f"{npc.short} {vb(npc.sex, 'протянул')} арматуру по стене и потолку",
                    }[поле]
        h.note(said)

    elif key == "утепление":
        spend(h, npc, "материалы", b["утепление_материалы"])
        вложить(h, npc, b["утепление_материалы"])
        npc.shelter["утепление"] = npc.shelter.get("утепление", 0) + 1
        said = f"{npc.short} {vb(npc.sex, 'утеплил')} окна (уровень {npc.shelter['утепление']})"

    elif key == "дверь":
        spend(h, npc, "материалы", b["дверь_материалы"])
        вложить(h, npc, b["дверь_материалы"])
        npc.shelter["дверь"] = npc.shelter.get("дверь", 0) + 1
        said = f"{npc.short} {vb(npc.sex, 'укрепил')} дверь (уровень {npc.shelter['дверь']})"

    elif key == "буржуйка":
        spend(h, npc, "материалы", b["буржуйка_материалы"])
        вложить(h, npc, b["буржуйка_материалы"])
        npc.shelter["буржуйка"] = True
        said = f"{npc.short} {vb(npc.sex, 'собрал')} буржуйку"
        h.note(f"{npc.short} {vb(npc.sex, 'собрал')} буржуйку")

    elif key == "разбор":
        пустые = h.пустые()
        pool = [f for f in пустые
                if f.stripped < b["разбор_максимум"] or sum(f.stock.values()) > 0] or пустые
        if not pool:
            return
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
            # доски из стен — это приход в мир, а не перекладывание; считаем,
            # иначе баланс материалов в доме не сходится
            h.stats["наразобрано_материалы"] = (h.stats.get("наразобрано_материалы", 0)
                                                + b["разбор_материалов"])
        flat.stripped += 1
        # доски берут не из воздуха: сначала выламывают то, чем квартира
        # утеплена, потом дверь. Запасная комната и топливо для буржуйки —
        # один и тот же ресурс, и дом проедает сам себя
        если_было = None
        if flat.shelter.get("утепление", 0) > 0:
            flat.shelter["утепление"] -= 1
            если_было = "рамы"
        elif flat.shelter.get("дверь", 0) > 0:
            flat.shelter["дверь"] -= 1
            если_было = "дверь"
        flat.вложено = max(0.0, flat.вложено - b["разбор_материалов"])
        if flat.owner_died and sum(v for k, v in got.items() if k != "материалы") > 0:
            npc.mood = clamp(npc.mood - 5 * npc.t01("лояльность"))
        took = ", ".join(f"{k} {int(v)}" for k, v in got.items() if v)
        said = f"{npc.short} {vb(npc.sex, 'разобрал')} часть кв.{flat.apt}" + (f": {vb(npc.sex, 'взял')} {took}" if took else "")
        if если_было == "рамы":
            said += "; окна там теперь голые"
        elif если_было == "дверь":
            said += "; дверь снял с петель"

    elif key == "вылазка":
        _outing(h, npc, spent, место)
        said = None  # текст пишет сам _outing

    elif key == "наблюдение":
        social.observe(h, npc, target)
        said = (f"{npc.short} {vb(npc.sex, 'присматривался')} "
                f"к кв.{social.место(h, target).apt}")

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
            social.judge(h, npc, "щедрость", trust=0.35)
            said = f"{npc.short} {'сама занесла' if npc.sex == 'ж' else 'сам занёс'} еду {target.form('dat')}"

    elif key == "обмен":
        said = _trade(h, npc, target)

    elif key == "лечить":
        # медик умеет то, чего не умеет сам себе перевязывающий (GDD 12.6:
        # «единственная возможность лечить серьёзные травмы и болезни»)
        медик = "медик" in npc.skills
        spend(h, npc, "лекарства", 1)
        if медик:
            target.injuries.clear()
            target.sick = None
            target.health = clamp(target.health + b["лечение_медиком"])
        else:
            if target.injuries:
                target.injuries.pop()
            if target.sick and h.rng.chance(b["самолечение_болезнь"]):
                target.sick = None
            target.health = clamp(target.health + b["лечение_самому"])
        if target.id != npc.id:
            social.adjust(target, npc.id, trust=b["доверие_за_лечение"], hate=-12)
            target.mood = clamp(target.mood + 10)
            npc.mood = clamp(npc.mood + b["настроение_от_помощи"])
            target.favors[npc.id] = target.favors.get(npc.id, 0) + 1
            said = f"{npc.short} {vb(npc.sex, 'перевязал')} {target.form('acc')}"
        else:
            said = f"{npc.short} {vb(npc.sex, 'обработал')} раны"

    elif key == "тело":
        flat = min([f for f in h.пустые() if f.body and f.body["порций"] > 0],
                   key=lambda f: abs(f.floor - npc.floor))
        take = min(flat.body["порций"], 4.0)
        flat.body["порций"] -= take
        flat.body["тронуто"] = True
        npc.stock["мясо"] = npc.stock.get("мясо", 0) + take
        h.stats["принесено_мясо"] = h.stats.get("принесено_мясо", 0) + take
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
        flat = min([f for f in h.пустые() if f.body and f.body["порций"] > 0],
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

    elif key == "отнять":
        victim = target
        npc.bump("отъёмов")
        h.bump("отъёмов")
        # смысл разбоя в том, что слабый не сопротивляется
        scared = victim.t01("храбрость") * 3.0 + victim.power() * 1.2 < npc.power() * 2.6
        if scared or h.rng.chance(0.75):
            moved = conflict.take_carried(h, victim, npc, limit=b["отъём_максимум"])
            h.journal.line(f"{npc.short} {vb(npc.sex, 'зажал')} {victim.form('acc')} на лестнице "
                           f"и {vb(npc.sex, 'забрал')} {conflict._fmt(moved)}.", 2)
            victim.mood = clamp(victim.mood - 14)
            victim.panic = clamp(victim.panic + 16)
        else:
            h.journal.line(f"{npc.short} {vb(npc.sex, 'полез')} к {victim.form('dat')} на лестнице — "
                           f"{victim.short} не {vb(victim.sex, 'отдал')}.", 2)
            won = conflict.scuffle(h, npc, victim, place="на лестнице")
            if won:
                conflict.take_carried(h, victim, npc, limit=b["отъём_максимум"] * 0.5)
        social.adjust(victim, npc.id, trust=-5.0, hate=b["ненависть_за_налёт"] * 0.8, aware=15)
        social.register_incident(h, "отъём", None)
        # это видят и слышат: разбой в подъезде не спрячешь
        видели = [w for w in h.others(npc) if w.id != victim.id and h.rng.chance(0.8)]
        for w in видели:
            social.adjust(w, npc.id, aware=8)
            w.panic = clamp(w.panic + 7)
        # разбой каждый мерит своей меркой (GDD 12.1, «Ценности»)
        social.judge(h, npc, "насилие", hate=20 + 12 * 0.5, trust=-2.5, witnesses=видели)
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
        # и то же самое, чем он потом будет мерить, не выгнать ли его обратно
        yes += social.выгода_соседства(h, host, npc) * b["переезд_вес_выгоды"]
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
            # своя квартира остаётся пустой сама — в ней просто никто не живёт.
            # И её начнут разбирать: вернуться потом может быть некуда
        else:
            social.adjust(npc, host.id, trust=-1.0, hate=10)
            npc.ask_record(host.id)["отказ_переезд"] = h.day
            npc.mood = clamp(npc.mood - 8)
            h.journal.line(f"{npc.short} {vb(npc.sex, 'просил')} пустить к себе. {host.short} не {vb(host.sex, 'пустил')}.", 1)
        said = None

    elif key == "съехать":
        host = target
        дров = 0.0
        if host.hate.get(npc.id, 0.0) < 40:
            дров = min(b["выгнать_дров_на_дорогу"], host.stock.get("топливо", 0.0))
            host.stock["топливо"] = host.stock.get("топливо", 0.0) - дров
            npc.stock["топливо"] = npc.stock.get("топливо", 0.0) + дров
        npc.living_with = None
        host.guests.discard(npc.id)
        conflict.occupy_flat(h, npc)
        npc.warmth = clamp(npc.warmth - 8)
        social.adjust(npc, host.id, trust=-0.5)
        social.adjust(host, npc.id, trust=-0.5)
        h.bump("съездов")
        h.journal.line(f"{npc.short} {vb(npc.sex, 'вернулся')} к себе в кв.{npc.apt}"
                       + (f" — {host.short} {vb(host.sex, 'дал')} дров на первое время." if дров
                          else ". Ушёл молча."), 2)
        h.note(f"{npc.short} {vb(npc.sex, 'съехал')} от {host.form('gen')}")
        said = None

    elif key == "выгнать":
        гость = target
        гость.living_with = None
        npc.guests.discard(гость.id)
        conflict.occupy_flat(h, гость)
        гость.warmth = clamp(гость.warmth - 14)
        гость.mood = clamp(гость.mood - 14)
        гость.panic = clamp(гость.panic + 12)
        social.adjust(гость, npc.id, trust=-4.0, hate=b["ненависть_за_выселение"])
        social.adjust(npc, гость.id, trust=-1.0)
        h.bump("выселений")
        h.journal.line(f"{npc.short} {vb(npc.sex, 'выставил')} {гость.form('acc')} обратно "
                       f"в кв.{гость.apt}. Разговаривать не о чем.", 2)
        h.note(f"{npc.short} {vb(npc.sex, 'выгнал')} {гость.form('acc')}")
        social.judge(h, npc, "жестокость", hate=6.0, trust=-0.8)
        said = None

    elif key == "занять":
        flat = target
        старая = h.flats[npc.apt]
        прежний = h.чей(flat)
        if прежний is not None and not прежний.living_with:
            return                     # пока он собирался, туда уже въехали
        npc.apt, npc.floor = flat.apt, flat.floor      # и гости переезжают с ним
        if прежний is not None:
            # у него был этот угол, но сам он жил у соседа. Меняются местами:
            # человек не остаётся без адреса, он получает брошенную дыру
            прежний.apt, прежний.floor = старая.apt, старая.floor
        conflict.occupy_flat(h, npc)                   # забрать то, что лежало
        h.bump("занято_квартир")
        npc.bump("занял_квартиру")
        чем_лучше = []
        if flat.shelter.get("буржуйка") and not старая.shelter.get("буржуйка"):
            чем_лучше.append("там буржуйка")
        if flat.shelter.get("утепление", 0) > старая.shelter.get("утепление", 0):
            чем_лучше.append("окна заклеены")
        if flat.shelter.get("дверь", 0) > старая.shelter.get("дверь", 0):
            чем_лучше.append("дверь целее")
        хвост = (" — " + ", ".join(чем_лучше)) if чем_лучше else ""
        h.journal.line(f"{npc.short} {vb(npc.sex, 'перебрался')} в кв.{flat.apt}{хвост}. "
                       f"Свою {vb(npc.sex, 'бросил')}.", 2)
        h.note(f"{npc.short} {vb(npc.sex, 'занял')} кв.{flat.apt}"
               + (f" (была {прежний.form('gen')})" if прежний else ""))
        if flat.body and flat.body.get("порций", 0) > 0:
            npc.mood = clamp(npc.mood - b["занять_тело_штраф"])
        for w in h.others(npc):
            social.adjust(w, npc.id, aware=12)
        if прежний is not None and прежний.alive:
            прежний.mood = clamp(прежний.mood - 15)
            прежний.panic = clamp(прежний.panic + 12)
            social.adjust(прежний, npc.id, trust=-3.0, hate=b["ненависть_за_захват"])
            h.journal.line(f"{прежний.short} {vb(прежний.sex, 'остался')} без своего угла.", 2)
            social.register_incident(h, "захват", None)
            social.judge(h, npc, "воровство", hate=10.0, trust=-1.0)
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
    # сам факт просьбы: «просят — значит, скоро будут отбирать» (GDD 14).
    # И третье следствие оттуда же, которого не было: у чужого человека просить
    # стыдно, и доверие к просящему падает тем сильнее, чем меньше его было
    target.panic = clamp(target.panic + 4)
    # человек, пришедший просить, сам о себе всё и рассказал: теперь сосед знает,
    # что у него пусто. Раньше здесь стояло aware=-5 — просьба почему-то делала
    # просящего менее понятным, хотя та же просьба в чате (report.daily_chat)
    # правильно поднимала осведомлённость и роняла оценку запасов
    social.adjust(target, npc.id, aware=b["просьба_осведомлённость"])
    social.note_signal(target, npc.id, res, 0.5, 0.35)
    social.adjust(npc, target.id, aware=12)
    цена = b["просьба_цена_доверия"] * (1.0 - min(1.0, trust / 8.0))
    social.adjust(target, npc.id, trust=-цена)

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
    if a.stock.get(give, 0) < gn:
        return None
    if b_npc.stock.get(get_, 0) < tn:
        # пришёл менять на то, чего у соседа нет: сходил зря и понял, что ошибся
        social.note_signal(a, b_npc.id, get_, 0.0, 0.6)
        social.adjust(a, b_npc.id, aware=6)
        h.bump("сделок_сорвалось")
        return (f"{a.short} {vb(a.sex, 'предложил')} {b_npc.form('dat')} мену, "
                f"но {RES_GEN.get(get_, get_)} у {b_npc.form('gen')} не оказалось")
    # сосед считает выгоду по СВОИМ настоящим запасам — и может отказаться
    их = social.value_of(b_npc, give, gn) - social.value_of(b_npc, get_, tn)
    if их <= 0:
        social.adjust(a, b_npc.id, aware=4)
        h.bump("сделок_отклонено")
        return f"{b_npc.short} не {vb(b_npc.sex, 'стал')} меняться с {a.form('ins')}"
    a.stock[give] -= gn
    b_npc.stock[give] = b_npc.stock.get(give, 0) + gn
    b_npc.stock[get_] -= tn
    a.stock[get_] = a.stock.get(get_, 0) + tn
    social.adjust(a, b_npc.id, trust=0.7, aware=10)
    social.adjust(b_npc, a.id, trust=0.7, aware=10)
    h.bump("обменов")
    return (f"{a.short} {vb(a.sex, 'обменял')} {RES_ACC.get(give, give)} "
            f"на {RES_ACC.get(get_, get_)} с {b_npc.form('ins')}")


# GDD 20: «выбор ведёт в отдельную небольшую локацию». Отдельных локаций
# в прототипе нет, но выбор «ближе и беднее или дальше и опаснее» — есть,
# и именно он и был главным, чего вылазке не хватало.
МЕСТА = [
    ("двор и мусорки", 0.6, 0.55, 0.5),
    ("соседние подъезды", 1.0, 1.0, 1.0),
    ("гаражи за домом", 1.5, 1.5, 1.9),
    ("магазин на Заречной", 2.1, 2.1, 3.0),
]


def выбрать_место(h, npc):
    """Куда пойти: смелость и нужда толкают дальше, мороз и раны — ближе."""
    b = h.B
    лучшее, оценка = МЕСТА[0], -99.0
    for место in МЕСТА:
        имя, часы, богатство, риск = место
        s = богатство * (1.0 + npc.desperation() * b["вылазка_дальше_за_нужду"])
        s -= риск * (b["вылазка_осторожность"] * (1.4 - npc.t01("храбрость")))
        s -= риск * max(0.0, (-h.outside - 18)) * 0.05
        s -= часы * 0.25 if npc.dependents else 0.0
        if s > оценка:
            лучшее, оценка = место, s
    return лучшее


def _outing(h, npc, dur, выбор=None):
    """Вылазка (GDD 20): единственный источник новых ресурсов.

    Время дороги уже учтено в `dur` — его посчитали до выхода, вместе с выбором
    места (см. execute).
    """
    from .world import outing_danger
    b = h.B
    npc.away = True
    место, часы_к, богатство_к, риск_к = выбор or выбрать_место(h, npc)
    danger = outing_danger(h) * риск_к

    take = b["вылазка_добыча_база"] * богатство_к * h.scav_richness * h.rng.uni(0.5, 1.4)
    take *= 0.8 + npc.t01("храбрость") * 0.5
    # охотник умеет ходить по зимнему лесу и знает, где смотреть (GDD 12.1, 12.6)
    if "охотник" in npc.skills:
        take *= 1.0 + b["охотник_добыча"]
    got = {}
    for _ in range(int(take) + (1 if h.rng.chance(take % 1) else 0)):
        res = h.rng.weighted([(r, w) for r, w in b["вылазка_состав"].items()])
        got[res] = got.get(res, 0) + 1
        npc.stock[res] = npc.stock.get(res, 0) + 1
        h.stats["принесено_" + res] = h.stats.get("принесено_" + res, 0) + 1
    h.scav_richness = max(b["вылазка_минимум_богатства"],
                          h.scav_richness - b["вылазка_истощение_за_ход"] * богатство_к)

    npc.warmth = clamp(npc.warmth - b["вылазка_холод_за_час"] * dur * (1.0 + max(0.0, (-h.outside - 12)) * 0.03))
    npc.rest = clamp(npc.rest - 6)

    txt = f"{npc.short} {vb(npc.sex, 'ходил')} в {место} ({dur:.0f} ч): "
    txt += ", ".join(f"{k} {v}" for k, v in got.items()) if got else "пусто"

    hurt_risk = b["вылазка_шанс_травмы"] * danger
    if "охотник" in npc.skills:
        hurt_risk *= b["охотник_травма"]
    if h.rng.chance(hurt_risk):
        inj = h.rng.pick(["ушиб ноги", "порез руки", "перелом ноги", "ушиб руки"])
        npc.injuries.append(inj)
        npc.health = clamp(npc.health - h.rng.uni(8, 20))
        txt += f"; {vb(npc.sex, 'вернулся') if npc.sex != 'ж' else 'вернулась'} с травмой ({inj})"
    if h.rng.chance(b["вылазка_шанс_встречи"] * danger):
        if h.rng.chance(0.5):
            lost = {k: v for k, v in list(npc.stock.items()) if v > 0}
            res = h.rng.pick(list(lost)) if lost else None
            if res:
                had = npc.stock[res]
                npc.stock[res] = max(0, had - h.rng.rint(1, 2))
                # это единственное место, где ресурс уходит из мира насовсем;
                # без счётчика баланс дома не сходится и утечку не отличить от бага
                h.stats["потеряно_" + res] = h.stats.get("потеряно_" + res, 0) + (had - npc.stock[res])
            npc.panic = clamp(npc.panic + 15)
            txt += "; во дворе отняли часть добычи"
        else:
            npc.panic = clamp(npc.panic + 8)
            txt += "; на обратном пути кто-то шёл следом"

    # квартира стоит пустой, пока хозяин не вернулся к своему следующему делу.
    # Раньше away снимался здесь же, поэтому «пока хозяина нет» не видел никто
    # и дневная кража была недостижима (GDD 12.5).
    npc.stats["день_вылазки"] = h.day      # его видели с пакетами
    h.journal.line(txt, 1)
    # возвращение с пакетами видно всем (GDD 13)
    social.emit(h, npc, 2, "возвращение", night=False)
    if got:
        for other in h.others(npc):
            if h.rng.chance(0.35 + 0.1 * (2 - min(2, h.floor_gap(npc, other)))):
                social.adjust(other, npc.id, aware=14)
                social.note_signal(other, npc.id, "еда", other.believed(npc.id, "еда") + got.get("еда", 0), 0.5)
