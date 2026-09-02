# -*- coding: utf-8 -*-
"""Кражи, осада, бой, изгнание.

GDD 16: рейд — не бой, а осада из стадий, и на каждой стадии есть выход.
GDD 17: бой намеренно простой и смертельный; численное превосходство решает;
        угроза оружием часто ценнее выстрела.
"""
from .util import clamp, vb
from . import social


# ---------------------------------------------------------------- вспомогательное

def stealth(npc):
    """Скрытность выводится из черт и возраста — отдельной черты в GDD нет."""
    s = 0.42 + 0.025 * (10 - npc.trait("вспыльчивость"))
    if "ловкий" in npc.skills:
        s += 0.16
    if npc.age > 55:
        s -= 0.10
    if npc.age < 25:
        s += 0.05
    s -= npc.panic / 100.0 * 0.18
    s -= 0.12 * len(npc.injuries)
    return clamp(s, 0.08, 0.95)


def take_from(h, victim, taker, greed):
    """Перекладывание чужого себе. greed 0..1 — какая доля запаса уходит."""
    moved = {}
    for res in ("еда", "топливо", "лекарства", "вода", "патроны", "материалы"):
        have = victim.stock.get(res, 0.0)
        if have <= 0:
            continue
        amount = have * greed
        amount = float(int(amount)) if res != "лекарства" else round(amount)
        if amount <= 0 and have >= 1 and h.rng.chance(greed):
            amount = 1.0
        amount = min(amount, have)
        if amount > 0:
            victim.stock[res] = have - amount
            taker.stock[res] = taker.stock.get(res, 0.0) + amount
            moved[res] = moved.get(res, 0.0) + amount
    return moved


def _fmt(moved):
    if not moved:
        return "ничего"
    return ", ".join(f"{k} {int(v) if float(v).is_integer() else round(v,1)}" for k, v in moved.items() if v)


# ---------------------------------------------------------------- кража

def theft_chance(h, thief, target):
    """Шанс унести чужое незамеченным. Считается одинаково и вором, и игрой:
    человек прикидывает дверь, дежурство и собственную ловкость до того,
    как полезет."""
    b = h.B
    p = b["кража_база_успеха"]
    p += b["кража_за_уровень_двери"] * target.shelter.get("дверь", 0)
    if target.away:
        p += b["кража_хозяин_ушёл"]
    if target.tonight == "дежурить":
        p += b["кража_дежурство"]
    p += (stealth(thief) - 0.5) * 0.7
    return clamp(p, 0.05, 0.93)


def steal(h, thief, target):
    """Ночная кража (GDD 12.5). Тихий вариант отъёма."""
    b = h.B
    p = theft_chance(h, thief, target)

    thief.bump("попыток_кражи")
    h.bump("попыток_кражи")
    if h.rng.chance(p):
        moved = take_from(h, target, thief, greed=h.rng.uni(0.25, 0.5))
        thief.memory.append(f"д{h.day}:украл:{target.id}")
        thief.bump("краж")
        h.bump("краж")
        thief.mood = clamp(thief.mood - 4 * thief.t01("лояльность"))
        h.journal.secret(f"ночью {thief.short} вынес из кв.{target.apt}: {_fmt(moved)}")
        # хозяин обнаружит пропажу утром (GDD 4.4 — сводка дня)
        if moved:
            h.mods.setdefault("пропажи", []).append((thief.id, target.id))
            target.stats["обокрали"] = target.stats.get("обокрали", 0) + 1
        return ("успех", moved)

    # поймали
    social.emit(h, target, 4, "ссора", night=True)
    caught_seen = h.rng.chance(0.7 + 0.2 * (1 if target.tonight == "дежурить" else 0))
    h.bump("краж_сорвано")
    if caught_seen:
        social.adjust(target, thief.id, trust=-4.0, hate=b["ненависть_за_кражу"], aware=20)
        target.memory.append(f"д{h.day}:поймал_вора:{thief.id}")
        social.register_incident(h, "кража", f"{target.short} {vb(target.sex, 'застал')} {thief.form('acc')} у себя в квартире.")
        thief.stats["поймали"] = thief.stats.get("поймали", 0) + 1
        if house_verdict(h, thief, target):
            return ("изгнан", {})
        # GDD 17: угроза оружием часто ценнее выстрела. Хозяин со стволом
        # обычно просто выставляет вора, а не убивает его.
        from .model import FIREARMS
        armed = target.weapon in FIREARMS and target.stock.get("патроны", 0) > 0
        if armed and h.rng.chance(0.62 + 0.03 * (10 - thief.trait("храбрость"))):
            h.journal.line(f"{target.short} {vb(target.sex, 'вышел') if target.sex != 'ж' else 'вышла'} "
                           f"со стволом. {thief.short} {vb(thief.sex, 'ушёл')} без разговоров.", 1)
            thief.panic = clamp(thief.panic + 18)
            social.adjust(thief, target.id, hate=12, aware=15)
            return ("отпугнули", {})
        # вор в первую очередь бежит, а не дерётся — драка тут крайний случай
        escaped = h.rng.chance(clamp(0.35 + stealth(thief) * 0.5 - target.t01("храбрость") * 0.3, 0.1, 0.9))
        if escaped:
            h.journal.line(f"{thief.short} {vb(thief.sex, 'вырвался')} и {vb(thief.sex, 'убежал')} по лестнице.", 1)
        elif target.trait("вспыльчивость") >= 6 or target.power() > thief.power() * 1.3:
            fight(h, [target], [thief], place=f"кв.{target.apt}", reason="вор в квартире")
        for w in h.others(target):
            if w.id != thief.id and h.rng.chance(0.5):
                social.adjust(w, thief.id, trust=-1.2, hate=12)
        return ("пойман", {})
    else:
        social.register_incident(h, "кража", f"{target.short} {vb(target.sex, 'проснулся')} от возни в прихожей. Кто-то убежал по лестнице.")
        target.panic = clamp(target.panic + b["паника_от_кражи_у_себя"])
        suspect(h, target, exclude=None)
        return ("сорвалось", {})


def exile(h, person, by=None, reason="воровство"):
    """Дом выставляет человека за дверь. Почти всегда — смертный приговор,
    но руки формально чистые (GDD 12.5: изгнание в списке действий NPC)."""
    from .model import EmptyFlat
    person.exiled = True
    person.cause = f"{vb(person.sex, 'изгнан')} из дома ({reason})"
    person.died_day = h.day
    h.bump("изгнаний")
    who = f"{by.short} и остальные" if by else "соседи"
    h.journal.line(f"{who} вывели {person.form('acc')} на улицу и закрыли дверь подъезда.", 2)
    h.note(f"{person.short} изгнан ({reason})")
    social.house_shock(h, panic=10, mood=-12)
    flat = EmptyFlat(apt=person.apt, floor=person.floor, stock=dict(person.stock), owner_died=person.id)
    h.empty.append(flat)
    person.stock = {}
    if person.dependents:
        _orphan(h, person)


def reveal_taboo(h, eater, witness=None):
    """Дом узнал. Дальше человек в этом доме не жилец — так или иначе."""
    if eater.stats.get("раскрыт"):
        # уже знают; но пока он продолжает, дом снова и снова возвращается к вопросу
        _verdict_taboo(h, eater)
        return
    eater.stats["раскрыт"] = 1
    b = h.B
    if witness:
        h.journal.line(f"{witness.short} {vb(witness.sex, 'увидел')}, что у {eater.form('gen')} "
                       f"в кастрюле. Дом узнал к вечеру.", 2)
    else:
        h.journal.line(f"К вечеру весь подъезд знал, чем питается {eater.short}.", 2)
    h.note(f"дом узнал про {eater.form('acc')}")
    h.bump("раскрытых_людоедов")
    social.register_incident(h, "людоедство", None)
    for p in h.others(eater):
        social.adjust(p, eater.id, trust=-9.0, hate=b["людоедство_ненависть"])
        p.allies.discard(eater.id)
        eater.allies.discard(p.id)
    social.house_shock(h, panic=b["людоедство_паника_дома"], mood=-22)
    _verdict_taboo(h, eater)


def _verdict_taboo(h, eater):
    """Что дом делает с тем, про кого узнал. Решает быстро — если есть кому."""
    if not eater.alive or eater.exiled:
        return
    judges = [p for p in h.others(eater) if p.health > 35]
    if len(judges) >= 2 and sum(p.power() for p in judges) > eater.power() * 1.2:
        if h.rng.chance(0.55):
            exile(h, eater, by=judges[0], reason="то, что нашли у него в квартире")
        elif h.rng.chance(0.45):
            h.journal.line(f"За {eater.form('ins')} пришли ночью.", 2)
            fight(h, judges, [eater], place=f"кв.{eater.apt}", reason="приговор дома")
    elif len(judges) >= 1:
        # сил выгнать нет — просто перестают существовать друг для друга
        for p in judges:
            social.adjust(p, eater.id, trust=-3.0, hate=15)
        h.journal.line(f"С {eater.form('ins')} больше никто не разговаривает.", 1)


def house_verdict(h, thief, victim):
    """После второй-третьей поимки дом решает, что с вором делать."""
    caught = thief.stats.get("поймали", 0)
    if caught < 2:
        return False
    # выгнать человека на мороз — решение, которое дом принимает тяжело:
    # нужно, чтобы злы были почти все и чтобы сил хватило
    judges = [p for p in h.others(thief) if p.hate.get(thief.id, 0) > 55 and p.health > 45]
    if len(judges) < 3 and not (len(judges) == 2 and len(h.alive()) <= 3):
        return False
    if sum(p.power() for p in judges) < thief.power() * 1.8:
        return False
    hard = sum(1 for p in judges if p.trait("лояльность") < 5 or p.trait("вспыльчивость") > 7)
    if not h.rng.chance(0.18 + 0.12 * hard):
        return False
    exile(h, thief, by=victim, reason="воровство")
    return True


def notice_theft(h, victim, thief_id=None):
    """Утреннее обнаружение пропажи и поиск виноватого."""
    b = h.B
    victim.panic = clamp(victim.panic + b["паника_от_кражи_у_себя"])
    victim.mood = clamp(victim.mood - 12)
    social.register_incident(h, "кража", f"{victim.label()} {vb(victim.sex, 'обнаружил')}, что запасы стали меньше.")
    suspect(h, victim, exclude=None, real=thief_id)


def suspect(h, victim, exclude=None, real=None):
    """Кого обвинят. Здесь и рождаются несправедливые обиды."""
    b = h.B
    pool = []
    for other in h.others(victim):
        w = 1.0
        w += victim.hate.get(other.id, 0.0) / 20.0
        w += (5.0 - victim.trust.get(other.id, 3.0)) * 0.4
        w += other.stats.get("краж", 0) * 2.5          # репутация вора
        w += max(0.0, 1.0 - other.days_of("еда") / 4.0) * victim.confidence(other.id) * 2.0
        if any(f"слышал" in m and other.id in m and f"д{h.day}" in m for m in victim.memory):
            w += 1.5
        w = max(0.05, w)
        pool.append((other, w))
    if not pool:
        return None
    pool.sort(key=lambda x: -x[1])
    # если никто не выделяется — человек просто не знает, на кого думать
    if len(pool) > 1 and pool[0][1] < pool[1][1] * 1.6:
        h.journal.line(f"{victim.short} не {vb(victim.sex, 'понял')}, кто это был.", 1)
        for other, _ in pool[:2]:
            social.adjust(victim, other.id, trust=-0.6, hate=5)
        return None
    accused = h.rng.weighted(pool)
    social.adjust(victim, accused.id, trust=-2.5, hate=b["ненависть_за_подозрение"])
    right = (real is not None and accused.id == real)
    h.journal.line(f"{victim.short} {vb(victim.sex, 'уверен')}, что это {accused.short}." + ("" if right else " (а это был не он)"), 1)
    if not right:
        h.bump("ложных_обвинений")
        h.note(f"{victim.short} обвинил {accused.short} напрасно")
    # обвинение расходится по дому
    for w in h.others(victim):
        if w.id == accused.id:
            continue
        if h.rng.chance(0.4 + 0.05 * victim.trait("общительность")):
            social.adjust(w, accused.id, trust=-0.8, hate=8)
    social.adjust(accused, victim.id, trust=-1.5, hate=10)   # обвинённому тоже обидно
    return accused


# ---------------------------------------------------------------- бой

def fight(h, side_a, side_b, place="", reason=""):
    """Короткий и смертельный бой (GDD 17). Возвращает ('a'|'b'|'ничья')."""
    b = h.B
    rounds = 0
    while rounds < 4:
        rounds += 1
        a_alive = [p for p in side_a if p.alive and p.health > 0]
        b_alive = [p for p in side_b if p.alive and p.health > 0]
        if not a_alive or not b_alive:
            break
        pa = sum(p.power() for p in a_alive)
        pb = sum(p.power() for p in b_alive)
        # кто попадает в этом раунде
        for att, dfn, pw_att, pw_dfn in ((a_alive, b_alive, pa, pb), (b_alive, a_alive, pb, pa)):
            if not att or not dfn:
                continue
            shooter = h.rng.pick(att)
            hit_p = clamp(0.35 + 0.4 * (pw_att / max(0.3, pw_att + pw_dfn)), 0.15, 0.9)
            if not h.rng.chance(hit_p):
                continue
            victim = h.rng.pick(dfn)
            gun = shooter.weapon in ("пистолет", "дробовик", "винтовка") and shooter.stock.get("патроны", 0) > 0
            if gun:
                shooter.stock["патроны"] = shooter.stock.get("патроны", 0) - 1
                social.emit(h, shooter, 5, "выстрел", night=True)
                social.house_shock(h, panic=b["паника_от_выстрела"], mood=-6)
                h.bump("выстрелов")
            dmg = h.rng.uni(b["бой_урон_мин"], b["бой_урон_макс"]) * (1.6 if gun else 1.0)
            victim.health = clamp(victim.health - dmg)
            if victim.health <= 0:
                victim.alive = False
                victim.cause = (f"{vb(victim.sex, 'убит')} в драке ({reason})" if reason
                                else vb(victim.sex, "убит") + " в драке")
                victim.died_day = h.day
                shooter.bump("убийств")
                h.bump("убийств")
                h.journal.line(f"{shooter.short} {vb(shooter.sex, 'убил')} {victim.form('acc')}. {place}", 2)
                h.note(f"{shooter.short} {vb(shooter.sex, 'убил')} {victim.form('acc')}")
                on_death(h, victim, killer=shooter)
            else:
                injury = "огнестрел" if gun else h.rng.pick(["ушиб", "порез", "перелом"])
                victim.injuries.append(injury)
                h.journal.line(f"{victim.short} {vb(victim.sex, 'получил')} {injury} ({shooter.short}). {place}", 1)
                if gun and h.rng.chance(b["бой_шанс_смерти_огнестрел"]):
                    victim.health = 0.0
                    victim.alive = False
                    victim.cause = vb(victim.sex, "убит") + " выстрелом"
                    victim.died_day = h.day
                    h.journal.line(f"{victim.short} не {vb(victim.sex, 'дожил')} до утра.", 2)
                    on_death(h, victim, killer=shooter)
        # мораль: получил — чаще всего выходит из драки (GDD 17: numbers decide,
        # но никто не бьётся до последнего за банку тушёнки)
        a_alive = [p for p in side_a if p.alive and p.health > 0]
        b_alive = [p for p in side_b if p.alive and p.health > 0]
        if not a_alive:
            return "b"
        if not b_alive:
            return "a"
        for side, other, tag in ((b_alive, a_alive, "a"), (a_alive, b_alive, "b")):
            hurt = [p for p in side if p.injuries]
            if not hurt:
                continue
            nerve = sum(p.t01("храбрость") for p in side) / len(side)
            nerve += 0.2 * (len(side) - len(other)) - 0.25 * len(hurt) / len(side)
            if h.rng.chance(clamp(0.75 - nerve * 0.6, 0.1, 0.9)):
                return tag
    return "ничья"


def on_death(h, dead, killer=None, quiet=False):
    """Смерть в доме: паника, настроение, осиротевший ребёнок, пустая квартира."""
    from .model import EmptyFlat
    b = h.B
    dead.alive = False
    if dead.died_day is None:
        dead.died_day = h.day
    h.bump("смертей")
    social.house_shock(h, panic=b["паника_от_смерти_в_доме"], mood=b["настроение_от_смерти"])
    if killer:
        for p in h.alive():
            if p.id == killer.id:
                continue
            social.adjust(p, killer.id, trust=-2.5, hate=25 + 15 * p.t01("лояльность"))
    # мёртвый выпадает из всех союзов — иначе он годами числится в списке
    for other in h.people.values():
        other.allies.discard(dead.id)
    dead.allies.clear()
    social.register_incident(h, "смерть", None)
    h.note(f"{dead.short}: {dead.cause}")

    # гости хозяина остаются на улице собственной пустой квартиры
    for gid in list(dead.guests):
        g = h.get(gid)
        if g and g.alive:
            g.living_with = None
            g.warmth = clamp(g.warmth - 15)
            h.journal.line(f"{g.short} {vb(g.sex, 'вернулся')} в свою выстывшую квартиру.", 1)
    dead.guests.clear()
    if dead.living_with:
        host = h.get(dead.living_with)
        if host:
            host.guests.discard(dead.id)

    # ребёнок остаётся один (GDD 12.6: семья как моральный центр)
    if dead.dependents > 0:
        _orphan(h, dead)

    # квартира становится пустой и доступной (GDD 12.2)
    flat = EmptyFlat(apt=dead.apt, floor=dead.floor, stock=dict(dead.stock), owner_died=dead.id)
    flat.body = {"кто": dead.short, "вин": dead.form("acc"), "падеж": dead.form("gen"), "день": h.day,
                 "порций": h.B["тело_порций"], "тронуто": False}
    h.empty.append(flat)
    dead.stock = {}


def _orphan(h, dead):
    """Кого-то надо взять к себе. Или не взять."""
    name = dead.dependent_name or "ребёнок"
    name_acc = dead.dependent_acc or name
    candidates = []
    for p in h.alive():
        w = p.trait("лояльность") * 1.8 + p.trust.get(dead.id, 3.0) * 0.8 - p.desperation() * 3.5
        w += 2.0 if "медик" in p.skills else 0.0
        if w > 0:
            candidates.append((p, w))
    if candidates:
        taker = h.rng.weighted(candidates)
        taker.dependents += 1
        taker.dependent_name = name
        taker.mood = clamp(taker.mood + 6)
        taker.dependent_acc = name_acc
        h.journal.line(f"{name} остался один. {taker.short} {vb(taker.sex, 'забрал')} его к себе.", 2)
        h.note(f"{taker.short} {vb(taker.sex, 'взял')} {name_acc}")
        for p in h.alive():
            social.adjust(p, taker.id, trust=1.0)
    else:
        h.journal.line(f"{name} остался один. Никто не взял.", 2)
        h.note(f"{name} остался один — никто не взял")
        social.house_shock(h, panic=10, mood=-14)
        h.bump("детей_брошено")


# ---------------------------------------------------------------- рейд как осада

def aggr(h):
    """Общий множитель злости дома. Одно число вместо шести порогов."""
    return max(0.2, h.B.get("агрессивность_дома", 1.0))


def consider_raid(h, npc):
    """Условие запуска рейда ровно по GDD 16.

    «Рейд запускается, когда паника выше половины и высока либо ненависть,
    либо осведомлённость.»
    """
    b = h.B
    A = aggr(h)
    if npc.panic < b["налёт_порог_паники"] / A:
        return None
    # пока жизнь ещё похожа на обычную, к чужой двери не идут
    if npc.normalcy > b["нормальность_потолок_налёта"] * A:
        return None
    if npc.health < 35 or len(npc.injuries) >= 2:
        return None
    # налёт — это всегда либо нужда, либо личное
    if npc.desperation() < 0.30 / A and max(list(npc.hate.values()) or [0]) < 50 / A:
        return None
    best = None
    for t in h.others(npc):
        # к тому, с кем живёшь под одной крышей, не идут с ломом
        if npc.living_with == t.id or t.living_with == npc.id:
            continue
        hate = npc.hate.get(t.id, 0.0)
        aware = npc.aware.get(t.id, 0.0)
        if hate < b["налёт_порог_ненависти"] / A and aware < b["налёт_порог_осведомлённости"] / A:
            continue
        want = npc.loot_value(t.id) * (0.5 + npc.t01("жадность"))
        want *= 0.6 + npc.desperation() * 1.4
        want += hate / 25.0
        # если тихо взять не выйдет, дверь начинает притягивать людей с ломом:
        # укреплённая квартира защищает от вора и приманивает налёт (GDD 16)
        want += (1.0 - theft_chance(h, npc, t)) * 2.6
        # страх перед хозяином: чем сильнее сосед, тем меньше желания.
        # но человек считает не себя одного, а тех, кого может привести —
        # поэтому вожак с двумя приятелями идёт туда, куда один бы не сунулся
        crew_size = 1 + sum(1 for p in h.others(npc)
                            if p.id != t.id and p.health > 40
                            and (p.trust.get(npc.id, 3.0) >= b["налёт_вербовка_доверие"] / A
                                 or p.group == "агрессивные"))
        fear = t.power() * (1.4 - npc.t01("храбрость")) * 1.5
        fear /= 1.0 + 0.45 * (crew_size - 1)
        fear += t.shelter.get("дверь", 0) * 0.8
        if t.weapon in ("винтовка", "дробовик", "пистолет") and npc.aware.get(t.id, 0) > 30:
            fear += 2.2
        # совесть
        conscience = npc.t01("лояльность") * 5.5 * (1.0 - npc.desperation() * 0.5) / A
        conscience += 3.5 if t.id in npc.allies else 0.0   # на своего идти тяжело
        conscience += 1.2 if t.dependents else 0.0   # к матери с ребёнком идут последними
        score = want - fear - conscience
        if best is None or score > best[1]:
            best = (t, score)
    if best and best[1] >= b["налёт_порог_желания"] / A:
        return best[0]
    return None


def recruit(h, leader, target):
    """Состав группы (GDD 16): агрессивные идут сразу, нейтралы — если обе шкалы высоки."""
    b = h.B
    crew = [leader]
    for p in h.others(leader):
        if p.id == target.id or p.health < 40:
            continue
        # свою же дверь не ломают: под одной крышей — значит на одной стороне
        if p.living_with == target.id or target.living_with == p.id:
            continue
        trust = p.trust.get(leader.id, 3.0)
        if trust < b["налёт_вербовка_доверие"] / aggr(h) and p.group != "агрессивные":
            continue
        A = aggr(h)
        aggressive = (p.group == "агрессивные") or p.hate.get(target.id, 0) > 35 / A
        both_high = (p.panic > b["налёт_нейтралы_порог"] / A
                     and p.aware.get(target.id, 0) > b["налёт_нейтралы_порог"] / A)
        if not (aggressive or both_high):
            continue
        pull = p.desperation() * 2.0 + p.t01("жадность") * 1.5 + trust * 0.25
        pull -= p.t01("лояльность") * 2.2 + (1.2 if target.id in p.allies else 0.0)
        pull -= (1.0 - p.t01("храбрость")) * 1.5
        if pull > 0.8 / A:
            crew.append(p)
    return crew


def defenders_of(h, target, crew_ids):
    """Кто придёт на помощь (союзники и просто порядочные)."""
    d = [target]
    # те, кто живёт в этой квартире, дерутся за неё по определению
    for p in h.others(target):
        if p.id in crew_ids:
            continue
        if p.living_with == target.id or target.living_with == p.id:
            d.append(p)
    for p in h.others(target):
        if p in d:
            continue
        if p.id in crew_ids or p.health < 40:
            continue
        will = p.trust.get(target.id, 3.0) * 0.6 + p.t01("лояльность") * 4.0 + p.t01("храбрость") * 3.0
        will -= p.panic / 100.0 * 3.0
        will -= p.hate.get(target.id, 0) / 20.0
        if target.id in p.allies:
            will += 3.0
        if p.dependents:
            will -= 2.0
        if will > 6.5:
            d.append(p)
    return d


def run_siege(h, leader, target):
    """Осада по стадиям (GDD 16). Возвращает исход строкой."""
    b = h.B
    crew = recruit(h, leader, target)
    crew_ids = {p.id for p in crew}
    # предательство: на дверь идут те, с кем ещё вчера держались вместе
    traitors = [p for p in crew if target.id in p.allies]
    for p in traitors:
        p.allies.discard(target.id)
        target.allies.discard(p.id)
        h.mods.setdefault("разрывы", {})[tuple(sorted((p.id, target.id)))] = h.day
        h.bump("предательств")
        h.bump("союзов_распалось")
        h.journal.line(f"{p.short} {vb(p.sex, 'пришёл')} к двери {target.form('gen')}, "
                       f"с кем ещё вчера {vb(p.sex, 'держался')} вместе.", 2)
        h.note(f"предательство: {p.short} против {target.form('gen')}")
        social.adjust(target, p.id, trust=-6.0, hate=35)
    h.bump("налётов")
    leader.bump("налётов")
    names = ", ".join(p.short for p in crew)
    social.register_incident(h, "налёт", f"НАЛЁТ. {names} — к двери кв.{target.apt} ({target.short}).")
    social.emit(h, target, 3, "ссора", night=True)

    # ---- стадия 1: предупреждение ----
    defenders = defenders_of(h, target, crew_ids)
    attack_power = sum(p.power() for p in crew)
    def_power = sum(p.power() for p in defenders) * (1.0 + 0.2 * target.shelter.get("дверь", 0))
    outnumbered = attack_power > def_power * 1.25

    if len(defenders) > 1:
        h.journal.line(f"На лестницу вышли: {', '.join(p.short for p in defenders[1:])} — за {target.form('acc')}.", 2)
        for d in defenders[1:]:
            social.adjust(target, d.id, trust=b["доверие_за_защиту"])

    # у двери есть выходы: откупиться, переубедить, драться
    pay_ok = target.stock.get("еда", 0) + target.stock.get("топливо", 0) >= 2
    fear = clamp(0.25 + (attack_power / max(0.5, def_power)) * 0.35 - target.t01("храбрость") * 0.5, 0.0, 0.95)
    talk = target.t01("общительность") * 0.5 + sum(p.trust.get(target.id, 3.0) for p in crew) / (len(crew) * 20.0)
    talk += 0.25 if any("медик" in p.skills for p in defenders) else 0.0

    if pay_ok and (fear > 0.5 or target.t01("храбрость") < 0.45) and h.rng.chance(0.45 / aggr(h)):
        moved = {}
        for p in crew:
            m = take_from(h, target, p, greed=b["откуп_доля"] / len(crew))
            for k, v in m.items():
                moved[k] = moved.get(k, 0) + v
        h.journal.line(f"{target.short} {'откупилась: отдала' if target.sex == 'ж' else 'откупился: отдал'} {_fmt(moved)}. Ушли.", 2)
        for p in crew:
            social.adjust(p, target.id, hate=-12)
            social.adjust(target, p.id, hate=30, trust=-3.0)
        _house_learns(h, target, crew)
        h.bump("исход_откупился")
        return "откупился"

    if h.rng.chance(clamp(talk * 0.55 / aggr(h), 0.0, 0.6)):
        h.journal.line(f"{target.short} {vb(target.sex, 'говорил')} с ними через дверь. Постояли и разошлись.", 2)
        for p in crew:
            social.adjust(p, target.id, hate=-6)
            social.adjust(target, p.id, hate=20, trust=-2.0)
        h.bump("исход_переубедил")
        return "переубедил"

    # ---- стадия 2: дверь ----
    # угроза оружием часто ценнее выстрела (GDD 17)
    armed = [d for d in defenders if d.weapon in ("винтовка", "дробовик", "пистолет") and d.stock.get("патроны", 0) > 0]
    if armed:
        shooter = armed[0]
        scare = b["угроза_оружием_отпугивает"] * (1.0 - sum(p.t01("храбрость") for p in crew) / len(crew) * 0.6)
        if h.rng.chance(clamp(scare, 0.05, 0.95)):
            h.journal.line(f"{shooter.short} {'вышла' if shooter.sex == 'ж' else 'вышел'} на площадку со стволом. Разошлись без слова.", 2)
            for p in crew:
                social.adjust(p, shooter.id, hate=15, aware=10)
                p.panic = clamp(p.panic + 8)
            h.bump("исход_отбился")
            return "отбился"

    # чем больше пришло и чем тяжелее в руках, тем меньше значит дверь
    # (GDD 17: численное превосходство решает почти всё)
    # «прочность двери против инструментов группы» (GDD 16). Инструменты — это
    # не только руки: топор и слесарь в компании решают больше, чем лишний человек
    def tool_weight(p):
        w = 0.75 + p.t01("храбрость") * 0.6
        if p.weapon == "топор" or "слесарь" in p.skills:
            w += 1.2
        elif p.weapon in ("дубина", "нож"):
            w += 0.55
        return w

    tools = sum(tool_weight(p) for p in crew) * (1.0 + 0.22 * (len(crew) - 1))
    social.emit(h, target, 5, "взлом", night=True)
    door_broken = tools > target.door_strength() * h.rng.uni(0.75, 1.35)
    if not door_broken:
        # стены и потолок — только объединённой группой (GDD 16)
        if len(crew) >= 3 and h.rng.chance(0.4):
            h.journal.line(f"Дверь кв.{target.apt} выдержала — тогда полезли через стену из пустой квартиры.", 2)
            door_broken = True
        else:
            h.journal.line(f"Дверь кв.{target.apt} выдержала. Побились и ушли.", 2)
            for p in crew:
                social.adjust(p, target.id, hate=8, aware=12)
            _house_learns(h, target, crew)
            h.bump("исход_отбился")
            return "отбился"

    # ---- стадия 3: прорыв ----
    h.journal.line(f"Дверь кв.{target.apt} вынесли.", 2)
    escape = target.t01("храбрость") < 0.4 and h.rng.chance(0.35)
    surrender = def_power < attack_power * 0.6 or target.t01("храбрость") < 0.35

    if escape:
        moved = {}
        for p in crew:
            m = take_from(h, target, p, greed=0.8 / len(crew))
            for k, v in m.items():
                moved[k] = moved.get(k, 0) + v
        target.warmth = clamp(target.warmth - 25)
        h.journal.line(f"{target.short} {vb(target.sex, 'ушёл')} через окно на пожарную лестницу. Квартиру вынесли: {_fmt(moved)}.", 2)
        _house_learns(h, target, crew)
        h.bump("исход_сбежал")
        return "сбежал"

    if surrender and not any(d.weapon != "нет" for d in defenders):
        moved = {}
        for p in crew:
            m = take_from(h, target, p, greed=0.75 / len(crew))
            for k, v in m.items():
                moved[k] = moved.get(k, 0) + v
        h.journal.line(f"{target.short} не {vb(target.sex, 'стал')} драться. Забрали: {_fmt(moved)}.", 2)
        for p in crew:
            social.adjust(target, p.id, hate=b["ненависть_за_налёт"], trust=-5.0)
        # изгнание — если вожак злой
        if ((leader.trait("вспыльчивость") >= 7 or leader.hate.get(target.id, 0) > 70)
                and h.rng.chance(0.35) and not target.dependents):
            exile(h, target, by=leader, reason="налёт")
            h.bump("исход_изгнан")
            return "изгнан"
        _house_learns(h, target, crew)
        h.bump("исход_ограблен")
        return "ограблен"

    # драка
    res = fight(h, crew, defenders, place=f"кв.{target.apt}", reason="налёт")
    if res == "a":
        moved = {}
        for p in [c for c in crew if c.alive]:
            m = take_from(h, target, p, greed=0.85 / max(1, len([c for c in crew if c.alive])))
            for k, v in m.items():
                moved[k] = moved.get(k, 0) + v
        if moved:
            h.journal.line(f"Квартиру кв.{target.apt} обобрали: {_fmt(moved)}.", 2)
        _house_learns(h, target, crew)
        h.bump("исход_ограблен")
        return "ограблен"
    else:
        h.journal.line(f"{target.short} {'отбилась' if target.sex == 'ж' else 'отбился'}.", 2)
        _house_learns(h, target, crew)
        h.bump("исход_отбился")
        return "отбился"


def _house_learns(h, target, crew):
    """После налёта весь дом узнаёт и о жертве, и о нападавших."""
    b = h.B
    social.house_shock(h, panic=b["паника_от_налёта_в_доме"], mood=-6)
    for p in h.alive():
        social.adjust(p, target.id, aware=18)
        for c in crew:
            if p.id == c.id:
                continue
            social.adjust(p, c.id, trust=-1.2 * p.t01("лояльность") * 2, hate=10 + 12 * p.t01("лояльность"), aware=8)
