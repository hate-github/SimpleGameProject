# -*- coding: utf-8 -*-
"""Шум, осведомлённость, слухи, отношения и группы.

Сердце социальной части. Ровно то, что в GDD:
  · 13 — шум и наблюдение, из которых растёт осведомлённость;
  · 12.3 — три шкалы: осведомлённость, паника, ненависть, плюс доверие 0-10;
  · 12.4 — группы не заданы заранее, они складываются после первого инцидента.
"""
from .util import clamp

# Что именно выдаёт тот или иной сигнал (GDD 13).
# (ресурс, насколько подрастает оценка, как это выглядит со стороны, заметность)
#
# Заметность — вторая ось из GDD 13 («каждое действие имеет громкость
# и заметность»), и устроена она иначе, чем громкость: дым из окна и пакеты
# в руках видны со двора и с площадки всему дому, по этажам не глохнут,
# зато их скрывают занавески — то есть утепление окон.
NOISE_MEANING = {
    "готовка":     (None, 0.0, "в кв.{apt} гремят посудой", 0.0),
    "буржуйка":    ("топливо", 1.1, "из окна кв.{apt} идёт дым", 0.45),
    "генератор":   ("топливо", 2.2, "в кв.{apt} гудит генератор", 0.55),
    "ремонт":      ("материалы", 1.2, "в кв.{apt} стучат", 0.0),
    "дверь":       ("материалы", 1.4, "в кв.{apt} что-то тяжёлое волокут к двери", 0.15),
    "разбор":      ("материалы", 1.0, "с пустой квартиры тащат доски", 0.35),
    "возвращение": ("еда", 1.6, "{кто} вернулся с улицы с пакетами", 0.65),
    "взлом":       (None, 0.0, "в подъезде скрипела чужая дверь", 0.2),
    "ссора":       (None, 0.0, "на лестнице кричали", 0.3),
    "выстрел":     (None, 0.0, "в доме стреляли", 0.0),
    "шаги":        (None, 0.0, "на лестнице шаги", 0.0),
}


# ---------------------------------------------------------------- шум

def emit(h, src, level, kind, night=False, text_for=None):
    """Издать шум. Возвращает список тех, кто услышал.

    level 1..5 (GDD 13: тихо / средне / громко).
    """
    if level <= 0:
        return []
    b = h.B
    base = b["шум_слышимость"].get(str(int(level)), 0.5)
    # дальность зависит от громкости: выстрел слышит весь стояк, шаги — соседняя
    # площадка. Раньше затухание было одинаковым для шёпота и для выстрела,
    # и «громко» из GDD 13 на практике означало меньше половины дома
    дальность = b["шум_дальность"].get(str(int(level)), 1.0)
    видно = NOISE_MEANING.get(kind, (None, 0.0, "", 0.0))[3]
    heard = []
    for other in h.others(src):
        if other.away:
            continue
        p = base
        p *= b["шум_затухание_на_этаж"] ** (h.floor_gap(src, other) / max(0.35, дальность))
        if night:
            # ночью фон тише: звук идёт дальше, но спящий может его пропустить,
            # а громкое (4-5) будит всех
            p *= b["шум_ночью_множитель"]
            if other.tonight == "спать" and level < 4:
                p *= b["шум_спящий"]
        if src.shelter.get("звукоизоляция"):
            p *= b["шум_звукоизоляция"]
        # закрытые окна из GDD 13: утеплённые окна и тепло держат, и скрывают
        окна = max(0.35, 1.0 - b["шум_за_утепление"] * src.shelter.get("утепление", 0))
        p *= окна
        # пустая квартира МЕЖДУ источником и слушателем глушит (GDD 13).
        # Раньше проверялось только соседство с источником, поэтому множитель
        # был одинаков для всех и к третьей неделе включён всегда
        лестница = range(min(src.floor, other.floor), max(src.floor, other.floor) + 1)
        между = sum(1 for f in h.empty if f.floor in лестница)
        if между:
            p *= b["шум_пустая_квартира"] ** между
        # заметность идёт своим каналом: по этажам не глохнет, прячут занавески
        замечено = видно > 0 and h.rng.chance(clamp(видно * окна, 0.0, 0.9))
        if h.rng.chance(clamp(p, 0.0, 0.97)) or замечено:
            heard.append(other)
            _hear(h, other, src, kind, level)
    if heard and level >= 4:
        for p in heard:
            p.panic = clamp(p.panic + b["паника_от_громкого_шума"])
    return heard


def smell(h, src, hot=False):
    """Запах готовки идёт по подъезду. Возвращает тех, кто учуял.

    Отличия от звука (GDD 13 — «каждое действие имеет громкость и заметность»):
      · вверх по стояку доходит лучше, чем вниз;
      · звукоизоляция не помогает;
      · спящий учует утром — запах не исчезает вместе со звуком.
    """
    b = h.B
    caught = []
    for other in h.others(src):
        if other.away:
            continue
        gap = other.floor - src.floor
        p = b["запах_база"] * (1.15 if hot else 1.0)
        p *= (b["запах_вверх"] ** gap) if gap >= 0 else (b["запах_вниз"] ** abs(gap))
        if not h.rng.chance(clamp(p, 0.0, 0.95)):
            continue
        caught.append(other)
        adjust(other, src.id, aware=b["запах_осведомлённость"])
        cur = other.believed(src.id, "еда")
        note_signal(other, src.id, "еда", min(cur + 1.8, 7.0), 0.5)
        other.memory.append(f"д{h.day}:учуял:{src.id}")
        # голодный человек, которому пахнет чужим ужином, злится по-настоящему
        hunger = clamp((55 - other.satiety) / 55.0, 0.0, 1.0)
        if hunger > 0.1:
            adjust(other, src.id, hate=b["запах_зависть"] * hunger)
            other.mood = clamp(other.mood - 2.0 * hunger)
            other.panic = clamp(other.panic + 1.5 * hunger)
    # в журнале это событие, а не бытовой шум: пары строк за день достаточно
    seen = h.mods.setdefault("запах_журнал", {})
    if seen.get("день") != h.day:
        seen.clear()
        seen["день"] = h.day
    hungry = [p for p in caught if p.satiety < 55]
    if caught and seen.get("строк", 0) < 2 and (hungry or h.rng.chance(0.35)):
        seen["строк"] = seen.get("строк", 0) + 1
        who = ", ".join(p.short for p in caught)
        tail = " — и это слышно по их лицам" if len(hungry) >= 2 else ""
        h.journal.line(f"По подъезду тянет едой из кв.{src.apt}. Учуяли: {who}.{tail}", 1)
    return caught


def _hear(h, listener, src, kind, level):
    """Услышал — значит узнал. Осведомлённость растёт (GDD 12.3)."""
    b = h.B
    res, hint, _, _видно = NOISE_MEANING.get(kind, (None, 0.0, "", 0.0))
    gain = b["осведомлённость_за_шум"] * (0.6 + 0.15 * level)
    adjust(listener, src.id, aware=gain)
    if res:
        cur = listener.believed(src.id, res)
        # звук говорит «у него это есть», но не «у него этого гора»:
        # без потолка оценка растёт от каждого чиха и весь дом идёт грабить
        note_signal(listener, src.id, res, min(cur + hint, 6.0), 0.35)
    listener.memory.append(f"д{h.day}:слышал:{kind}:{src.id}")


def note_signal(a, target_id, res, hint_value, weight):
    """Сдвинуть оценку чужих запасов в сторону нового сигнала."""
    e = a.est.setdefault(target_id, {})
    cur = e.get(res, 2.0)
    e[res] = max(0.0, cur * (1.0 - weight) + hint_value * weight)


def observe(h, watcher, target):
    """Наблюдение: подсмотреть, как живёт сосед. Точнее любого шума.

    Заодно это единственный способ увидеть то, что сосед прячет.
    """
    b = h.B
    if target.stock.get("мясо", 0) > 0 and h.rng.chance(0.5):
        from . import conflict
        conflict.reveal_taboo(h, target, witness=watcher)
    adjust(watcher, target.id, aware=b["осведомлённость_за_наблюдение"])
    for res in ("еда", "топливо", "лекарства"):
        true_v = target.stock.get(res, 0.0)
        noise = h.rng.uni(0.75, 1.25)
        note_signal(watcher, target.id, res, true_v * noise, 0.75)
    watcher.memory.append(f"д{h.day}:смотрел:{target.id}")


def gossip(h, a, b_npc):
    """Разговор: обмен сведениями. Так осведомлённость расползается по дому."""
    bal = h.B
    # вес слуха — это доверие СЛУШАТЕЛЯ к рассказчику, а не наоборот. Раньше он
    # считался один раз и подставлялся в обе ветки: чем больше я доверял
    # собеседнику, тем охотнее ОН верил моим рассказам
    вес_для_b = 0.25 + 0.05 * b_npc.trust.get(a.id, 3.0)
    вес_для_a = 0.25 + 0.05 * a.trust.get(b_npc.id, 3.0)
    потолок = bal["слух_потолок_оценки"]
    for third in h.others(a):
        if third.id == b_npc.id:
            continue
        # рассказываю то, в чём уверен сам
        if a.aware.get(third.id, 0) > b_npc.aware.get(third.id, 0) + 12:
            adjust(b_npc, third.id, aware=bal["осведомлённость_за_слух"])
            for res in ("еда", "топливо", "лекарства"):
                # у пересказа есть потолок: разговоры не могут перевесить личный
                # опыт, иначе дом считает соседей вдвое богаче, чем они есть
                note_signal(b_npc, third.id, res,
                            min(a.believed(third.id, res), потолок), вес_для_b * 0.6)
        # и наоборот
        if b_npc.aware.get(third.id, 0) > a.aware.get(third.id, 0) + 12:
            adjust(a, third.id, aware=bal["осведомлённость_за_слух"])
            for res in ("еда", "топливо", "лекарства"):
                note_signal(a, third.id, res,
                            min(b_npc.believed(third.id, res), потолок), вес_для_a * 0.6)
    # обсуждают и то, у кого можно попросить, а у кого бесполезно
    for third in h.others(a):
        if third.id == b_npc.id:
            continue
        for teller, listener in ((a, b_npc), (b_npc, a)):
            mine = teller.asking.get(third.id)
            if not mine or (mine["дали"] + mine["отказали"]) < 1:
                continue
            theirs = listener.ask_record(third.id)
            # у слухов есть потолок: разговоры не могут перевесить личный опыт
            cap = bal["просьба_память_потолок"]
            if mine["отказали"] > mine["дали"]:
                theirs["отказали"] = min(theirs["отказали"] + 0.5, cap)
            elif mine["дали"] > mine["отказали"]:
                theirs["дали"] = min(theirs["дали"] + 0.5, cap)

    # чужая ненависть тоже заразна, если доверяешь рассказчику
    for third in h.others(a):
        if third.id == b_npc.id:
            continue
        if a.hate.get(third.id, 0) > 45 and a.trust.get(b_npc.id, 0) >= 5:
            b_npc.hate[third.id] = clamp(b_npc.hate.get(third.id, 0.0) + 5)


# ---------------------------------------------------------------- отношения

def adjust(a, b_npc_id, trust=0.0, hate=0.0, aware=0.0):
    """Все изменения шкал идут через эту дверь.

    Рост затухает: последние проценты доверия и осведомлённости даются тяжело.
    Иначе за пару дней все всё знают и всем доверяют — проверено.
    """
    if trust:
        cur = a.trust.get(b_npc_id, 3.0)
        if trust > 0:
            trust *= max(0.12, 1.0 - cur / 11.0)
        a.trust[b_npc_id] = clamp(cur + trust, 0.0, 10.0)
    if hate:
        a.hate[b_npc_id] = clamp(a.hate.get(b_npc_id, 0.0) + hate)
    if aware:
        cur = a.aware.get(b_npc_id, 0.0)
        if aware > 0:
            aware *= max(0.08, 1.0 - cur / 105.0)
        a.aware[b_npc_id] = clamp(cur + aware)


def add_panic(p, delta):
    """Паника растёт с затуханием: последние проценты набрать трудно.

    Иначе к десятому дню у всех ровно 100 и шкала перестаёт что-либо значить.
    """
    if delta > 0:
        delta *= max(0.15, 1.0 - p.panic / 130.0)
    p.panic = clamp(p.panic + delta)


def recent_incidents(h, window=4):
    """Сколько происшествий было за последние дни. Дом забывает старое."""
    return len([d for d in h.mods.get("происшествия_дни", []) if h.day - d < window])


def spread_panic(h):
    """Чужая паника заражает (GDD 12.3: «паника растёт от чужой паники»)."""
    people = h.alive()
    if len(people) < 2:
        return
    avg = sum(p.panic for p in people) / len(people)
    k = h.B["паника_заражение"]
    for p in people:
        # общительные заражаются сильнее, замкнутые меньше
        p.panic = clamp(p.panic + (avg - p.panic) * k * (0.5 + 0.1 * p.trait("общительность")))


def house_shock(h, panic=0.0, mood=0.0, note=None):
    """Общая встряска дома: смерть, налёт, выстрел."""
    for p in h.alive():
        if panic:
            add_panic(p, panic * (0.7 + 0.6 * p.t01("вспыльчивость")))
        if mood:
            p.mood = clamp(p.mood + mood)
    if note:
        h.journal.line(note, 2)


def judge(h, actor, tag, hate=0.0, trust=0.0, witnesses=None):
    """Дом оценивает поступок. У каждого своя мерка (GDD 12.1, «Ценности»).

    Одно и то же — раздать последнее или отнять на лестнице — для Лиды и для
    Игоря весит по-разному. Без этого «ценности» лежали в данных строкой
    и не значили ничего.
    """
    k = h.B["ценности_вес"]
    for w in (witnesses if witnesses is not None else h.others(actor)):
        if w.id == actor.id:
            continue
        сила = 1.0
        v = w.values or {}
        if tag in (v.get("не_терпит") or ()):
            сила += k
        if tag in (v.get("ценит") or ()):
            сила -= k * 0.5 if hate > 0 else -k
        adjust(w, actor.id, hate=hate * сила, trust=trust * сила)


def register_incident(h, kind, text):
    """Происшествие в доме. После первого дом начинает делиться на группы (GDD 12.4)."""
    h.incidents += 1
    h.mods.setdefault("происшествия_дни", []).append(h.day)
    for p in h.alive():
        p.stats["видел_чужое"] = p.stats.get("видел_чужое", 0) + 1
    if h.first_incident_day is None:
        h.first_incident_day = h.day
        h.note(f"первое происшествие в доме ({kind}) — дом начал делиться")
    h.bump(f"происшествий_{kind}")
    if text:
        h.journal.line(text, 2)


# ---------------------------------------------------------------- союзы и группы

def alliance_check(h):
    """Союзы складываются из взаимного доверия (GDD 12.4)."""
    b = h.B
    for a in h.alive():
        for c in h.others(a):
            mutual = min(a.trust.get(c.id, 3.0), c.trust.get(a.id, 3.0))
            cooldown = h.mods.get("разрывы", {}).get(tuple(sorted((a.id, c.id))), -99)
            # мало доверять — надо, чтобы люди успели друг другу что-то сделать
            earned = a.favors.get(c.id, 0) or c.favors.get(a.id, 0) or h.day >= 6
            if (mutual >= b["доверие_порог_союза"] and earned
                    and c.id not in a.allies and h.day - cooldown >= 5):
                a.allies.add(c.id)
                c.allies.add(a.id)
                h.bump("союзов_заключено")
                h.journal.line(f"{a.short} и {c.short} договорились держаться вместе.", 2)
                h.note(f"союз: {a.short} + {c.short}")
            elif mutual < b["доверие_разрыв_союза"] and c.id in a.allies:
                a.allies.discard(c.id)
                c.allies.discard(a.id)
                h.mods.setdefault("разрывы", {})[tuple(sorted((a.id, c.id)))] = h.day
                h.bump("союзов_распалось")
                h.journal.line(f"{a.short} и {c.short} больше не разговаривают.", 2)
                h.note(f"союз распался: {a.short} + {c.short}")


def update_groups(h):
    """Группы складываются после первого инцидента (GDD 12.4).

    Ожидаемый рисунок из документа: мирное ядро вокруг самых лояльных,
    агрессивное — вокруг самых храбрых и жадных, остальные примыкают
    к тому, кому доверяют.
    """
    if h.first_incident_day is None:
        return
    people = h.alive()
    if len(people) < 3:
        return
    peace_leader = max(people, key=lambda p: p.trait("лояльность") + p.trait("общительность") * 0.4)
    aggr_leader = max(people, key=lambda p: p.trait("храбрость") + p.trait("жадность") - p.trait("лояльность") * 0.5)
    if peace_leader.id == aggr_leader.id:
        return
    b = h.B
    changed = []
    for p in people:
        old = p.group
        if p.id == peace_leader.id:
            p.group = "мирные"
        elif p.id == aggr_leader.id:
            p.group = "агрессивные"
        else:
            tp = p.trust.get(peace_leader.id, 3.0)
            ta = p.trust.get(aggr_leader.id, 3.0)
            # своя злость тоже тянет в агрессивное ядро
            ta += p.t01("жадность") * 1.5 + p.desperation() * 2.0 - p.t01("лояльность") * 1.5
            if max(tp, ta) < b["группа_порог_доверия"] - 1.5:
                p.group = None
            else:
                p.group = "мирные" if tp >= ta else "агрессивные"
        if p.group != old:
            changed.append(p)
    if changed and h.journal:
        for p in changed:
            if p.group:
                h.journal.line(f"{p.short} держится {'мирных' if p.group == 'мирные' else 'тех, кто готов брать своё'}.", 1)


def daily_decay(h):
    """Память притупляется, злость оседает, сведения устаревают."""
    b = h.B
    к = b["оценка_спад_в_день"]
    for p in h.alive():
        for k in list(p.aware):
            p.aware[k] = clamp(p.aware[k] - b["осведомлённость_спад_в_день"])
        for k in list(p.hate):
            p.hate[k] = clamp(p.hate[k] - b["ненависть_спад_в_день"])
        # без свежих сигналов оценка чужих запасов ползёт к «не знаю».
        # Пока этого не было, вчерашний дым из окна помнился до конца метели
        for оценка in p.est.values():
            for res in list(оценка):
                оценка[res] += (b["оценка_нейтральная"] - оценка[res]) * к


# ---------------------------------------------------------------- обмен

def value_of(npc, res, amount, days=None):
    """Сколько ресурс стоит лично для этого человека (GDD 18: с учётом дефицита).

    days можно передать снаружи — тогда считается не по настоящим запасам,
    а по тому, на сколько дней их, по чужому мнению, хватает. Это нужно,
    чтобы сделку оценивали по представлению о соседе, а не по его шкафу.
    """
    if days is None:
        days = npc.days_of(res) if res in ("еда", "вода", "топливо") else 5.0
    scarcity = 3.2 if days < 1.5 else (2.0 if days < 3 else (1.3 if days < 6 else 0.8))
    base = {"еда": 1.0, "вода": 0.85, "топливо": 0.9, "лекарства": 1.1, "материалы": 0.55, "патроны": 1.2}
    extra = 0.0
    if res == "лекарства" and (npc.injuries or npc.sick or npc.dependents):
        extra = 1.2
    if res == "материалы" and not npc.shelter.get("буржуйка"):
        extra = 0.9
    return amount * (base.get(res, 0.7) * scarcity + extra)


def believed_days(a, target, res):
    """На сколько дней, по мнению a, хватит ресурса у target.

    Это и есть осведомлённость в деле: чем меньше a знает, тем ближе оценка
    к «наверное, как у всех». Ради этой шкалы построены шум, запах и слухи —
    и до сих пор обмен ходил мимо неё, читая настоящий склад соседа.
    """
    сколько = a.believed(target.id, res)
    за_день = {"еда": 0.85, "вода": 0.9, "топливо": 1.0}.get(res, 1.0) * target.eaters()
    оценка = сколько / max(0.2, за_день)
    # чего не знаешь — то домысливаешь средним
    c = a.confidence(target.id)
    return оценка * c + 4.0 * (1.0 - c)
