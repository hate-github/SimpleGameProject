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


def take_from(h, victim, taker, greed, limit=None):
    """Перекладывание чужого себе. greed 0..1 — какая доля запаса уходит.

    limit — сколько единиц одного ресурса можно унести за раз. Ночью вор уносит
    то, что влезает в сумку, а не весь шкаф; при осаде выносят без ограничений.
    """
    moved = {}
    for res in ("еда", "топливо", "лекарства", "вода", "патроны", "материалы"):
        have = victim.stock.get(res, 0.0)
        if have <= 0:
            continue
        amount = have * greed
        if limit is not None:
            amount = min(amount, limit)
        amount = float(int(amount)) if res != "лекарства" else round(amount)
        if amount <= 0 and have >= 1 and h.rng.chance(greed):
            amount = 1.0
        amount = min(amount, have)
        if amount > 0:
            victim.stock[res] = have - amount
            taker.stock[res] = taker.stock.get(res, 0.0) + amount
            moved[res] = moved.get(res, 0.0) + amount
    return moved


def household(h, person):
    """Кто физически живёт в этой квартире: хозяин и его гости.

    К одной двери приходят за всем, что за ней лежит. Пока налёт брал только
    у хозяина, переезд к соседу делал человека неприкосновенным.
    """
    люди = [person]
    for gid in sorted(person.guests):
        g = h.get(gid)
        if g and g.alive and not g.exiled:
            люди.append(g)
    return люди


def take_household(h, victim, taker, greed, limit=None):
    """Вынести квартиру целиком — со всем, что принесли в неё жильцы."""
    moved = {}
    for кто in household(h, victim):
        m = take_from(h, кто, taker, greed, limit)
        for k, v in m.items():
            moved[k] = moved.get(k, 0.0) + v
    return moved


def take_carried(h, victim, taker, limit):
    """Отнять то, что человек несёт в руках, а не весь его шкаф.

    На лестнице у него пакет, а не квартира: забирают несколько единиц,
    начиная с самого ценного.
    """
    moved = {}
    left = int(max(1, round(limit)))
    for res in ("еда", "топливо", "лекарства", "вода", "патроны", "материалы"):
        if left <= 0:
            break
        have = int(victim.stock.get(res, 0.0))
        if have <= 0:
            continue
        amount = min(have, left, max(1, round(have * 0.3)))
        if amount <= 0:
            continue
        victim.stock[res] = victim.stock.get(res, 0.0) - amount
        taker.stock[res] = taker.stock.get(res, 0.0) + amount
        moved[res] = amount
        left -= amount
    return moved


def _fmt(moved):
    if not moved:
        return "ничего"
    return ", ".join(f"{k} {int(v) if float(v).is_integer() else round(v,1)}" for k, v in moved.items() if v)


# ---------------------------------------------------------------- кража

def theft_chance(h, thief, target, известно=True):
    """Шанс унести чужое незамеченным: дверь, дежурство, собственная ловкость.

    `известно=False` — это прикидка заранее, днём. Тогда про сегодняшнюю ночь
    вор ещё ничего не знает и судит по привычке: сколько ночей за метель у соседа
    горел свет. Пока эта разница не была проведена, вор читал `tonight` — то есть
    решение, которое сосед примет только вечером, — да ещё и через раз, потому
    что значение зависело от порядка обхода.
    """
    b = h.B
    p = b["кража_база_успеха"]
    if target.apt in thief.ключи:
        p += b["кража_по_ключам"]          # своим ключом, без шума и следов
    else:
        p += b["кража_за_уровень_двери"] * target.shelter.get("дверь", 0)
    if target.away and not [g for g in household(h, target)[1:] if not g.away]:
        p += b["кража_хозяин_ушёл"]      # ушёл, и дома никого не оставил
    if известно:
        if target.tonight == "дежурить":
            p += b["кража_дежурство"]
    else:
        привычка = target.stats.get("ночей_дежурства", 0) / max(1.0, float(h.day))
        p += b["кража_дежурство"] * clamp(привычка, 0.0, 1.0)
    p += (stealth(thief) - 0.5) * 0.7
    return clamp(p, 0.05, 0.93)


def steal(h, thief, target):
    """Ночная кража (GDD 12.5). Тихий вариант отъёма."""
    b = h.B
    p = theft_chance(h, thief, target)

    thief.bump("попыток_кражи")
    h.bump("попыток_кражи")
    if h.rng.chance(p):
        moved = take_from(h, target, thief, greed=h.rng.uni(0.25, 0.5),
                          limit=b["кража_унос_макс"])
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
        scare = (0.62 + 0.03 * (10 - thief.trait("храбрость"))) / aggr(h)
        if armed and h.rng.chance(scare):
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
        видели = [w for w in h.others(target) if w.id != thief.id and h.rng.chance(0.5)]
        social.judge(h, thief, "воровство", hate=12.0, trust=-1.2, witnesses=видели)
        return ("пойман", {})
    else:
        social.register_incident(h, "кража", f"{target.short} {vb(target.sex, 'проснулся')} от возни в прихожей. Кто-то убежал по лестнице.")
        target.panic = clamp(target.panic + b["паника_от_кражи_у_себя"])
        suspect(h, target, exclude=None)
        return ("сорвалось", {})


def cut_ties(h, person):
    """Разорвать все связи выбывшего: союзы, сожительство в обе стороны.

    Одно место на смерть и на изгнание. Пока это было только в on_death,
    гость изгнанного навсегда оставался с living_with — а значит, по правилу
    «печку топит хозяин», не мог затопить собственную и замерзал.
    """
    for other in h.people.values():
        other.allies.discard(person.id)
    person.allies.clear()
    for gid in sorted(person.guests):
        g = h.get(gid)
        if g and g.alive and not g.exiled:
            g.living_with = None
            g.warmth = clamp(g.warmth - 15)
            occupy_flat(h, g)
            h.journal.line(f"{g.short} {vb(g.sex, 'вернулся')} в свою выстывшую квартиру.", 1)
        elif g:
            g.living_with = None
    person.guests.clear()
    if person.living_with:
        host = h.get(person.living_with)
        if host:
            host.guests.discard(person.id)
        person.living_with = None


def exile(h, person, by=None, reason="воровство"):
    """Дом выставляет человека за дверь. Почти всегда — смертный приговор,
    но руки формально чистые (GDD 12.5: изгнание в списке действий NPC)."""
    person.exiled = True
    person.cause = f"{vb(person.sex, 'изгнан')} из дома ({reason})"
    person.died_day = h.day
    h.bump("изгнаний")
    who = f"{by.short} и остальные" if by else "соседи"
    h.journal.line(f"{who} вывели {person.form('acc')} на улицу и закрыли дверь подъезда.", 2)
    h.note(f"{person.short} изгнан ({reason})")
    social.house_shock(h, panic=10, mood=-12)
    cut_ties(h, person)
    flat = release_flat(h, person)
    for res, v in person.stock.items():
        flat.stock[res] = flat.stock.get(res, 0.0) + v
    person.stock = {}
    if person.dependents:
        _orphan(h, person)


def release_flat(h, person):
    """Квартира выбывшего. Заводить её больше не нужно — она была всегда.

    Пустой её делает не запись в списке, а то, что в ней никто не живёт
    (House.пустые). Здесь только помечается, чей это был дом: соседи разбирают
    жильё умершего иначе, чем брошенное.
    """
    flat = h.flats[person.apt]
    flat.owner_died = flat.owner_died or person.id
    return flat


def occupy_flat(h, person):
    """Человек вернулся в свою квартиру: забирает то, что в ней осталось."""
    flat = h.flats.get(person.apt)
    if flat and not (flat.body and flat.body.get("порций", 0) > 0):
        for res, v in list(flat.stock.items()):
            if v:
                person.stock[res] = person.stock.get(res, 0.0) + v
                flat.stock[res] = 0.0


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
    social.judge(h, eater, "табу", hate=b["людоедство_ненависть"] * 0.4, trust=-2.0)
    social.house_shock(h, panic=b["людоедство_паника_дома"], mood=-22)
    _verdict_taboo(h, eater)


def приговор_дома(h, кого, повод, причина_изгнания):
    """Что дом делает с тем, про кого узнал. Решает быстро — если есть кому.

    Одно место на два случая: людоед и убийца соседа. Разница только в словах.
    """
    if not кого.alive or кого.exiled:
        return
    judges = [p for p in h.others(кого) if p.health > 35]
    if len(judges) >= 2 and sum(p.power() for p in judges) > кого.power() * 1.2:
        if h.rng.chance(0.55):
            exile(h, кого, by=judges[0], reason=причина_изгнания)
        elif h.rng.chance(0.45):
            h.journal.line(f"За {кого.form('ins')} пришли ночью.", 2)
            fight(h, judges, [кого], place=f"кв.{кого.apt}", reason="приговор дома")
    elif len(judges) >= 1:
        # сил выгнать нет — просто перестают существовать друг для друга
        for p in judges:
            social.adjust(p, кого.id, trust=-3.0, hate=15)
        h.journal.line(f"С {кого.form('ins')} больше никто не разговаривает.", 1)


def _verdict_taboo(h, eater):
    приговор_дома(h, eater, "людоедство", "то, что нашли у него в квартире")


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
        w += other.stats.get("поймали", 0) * 2.5       # репутация вора: того,
        # кого уже ловили. Раньше здесь стояло число удавшихся краж — то есть
        # ровно то, чего дом про человека не знает: чем чище он работал,
        # тем охотнее его подозревали
        # кого называли в чате, того и подозревают: слово в общем чате
        # работает как наговор (GDD 14, тема «подозрения»)
        назван = h.mods.get("названы_в_чате", {}).get(other.id, -99)
        if h.day - назван <= b["чат_подозрение_дней"]:
            w += b["чат_подозрение_вес"]
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
    """Короткий и смертельный бой (GDD 17). Возвращает ('a'|'b'|'ничья').

    Численное превосходство решает объёмом ударов, а не поправкой к меткости:
    бьёт каждый, кто пришёл. Пока за раунд от стороны бил ровно один человек,
    трое голыми руками забивали одного в 0.4% боёв — при том что документ
    обещает «два-три попадания убивают любого».
    """
    b = h.B
    ранены_сейчас = set()
    rounds = 0
    while rounds < 4:
        rounds += 1
        a_alive = [p for p in side_a if p.alive and p.health > 0]
        b_alive = [p for p in side_b if p.alive and p.health > 0]
        if not a_alive or not b_alive:
            break
        pa = sum(p.power() for p in a_alive)
        pb = sum(p.power() for p in b_alive)
        # кто попадает в этом раунде: бросок у каждого бойца свой
        удары = []
        for att, dfn, pw_att, pw_dfn in ((a_alive, b_alive, pa, pb), (b_alive, a_alive, pb, pa)):
            if not att or not dfn:
                continue
            hit_p = clamp(0.35 + 0.4 * (pw_att / max(0.3, pw_att + pw_dfn)), 0.15, 0.9)
            for боец in att:
                if h.rng.chance(hit_p):
                    удары.append((боец, dfn))
        for shooter, dfn in удары:
            dfn = [x for x in dfn if x.alive and x.health > 0]
            if not dfn:
                break
            victim = h.rng.pick(dfn)
            from .model import FIREARMS
            gun = shooter.weapon in FIREARMS and shooter.stock.get("патроны", 0) > 0
            if gun:
                shooter.stock["патроны"] = shooter.stock.get("патроны", 0) - 1
                h.stats["израсходовано_патроны"] = h.stats.get("израсходовано_патроны", 0) + 1
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
                injury = "огнестрел" if gun else h.rng.pick(["ушиб руки", "порез руки", "перелом ноги"])
                victim.injuries.append(injury)
                ранены_сейчас.add(victim.id)
                h.journal.line(f"{victim.short} {vb(victim.sex, 'получил')} {injury} ({shooter.short}). {place}", 1)
                добьёт = b["бой_шанс_смерти_огнестрел"] if gun else b["бой_шанс_смерти_холодное"]
                # третье попадание почти всегда последнее (GDD 17)
                добьёт *= 1.0 + 0.6 * max(0, len(victim.injuries) - 2)
                if h.rng.chance(добьёт):
                    victim.health = 0.0
                    victim.alive = False
                    victim.cause = vb(victim.sex, "убит") + (" выстрелом" if gun else " в драке")
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
            # раненым считается тот, кому досталось СЕЙЧАС, а не тот, у кого
            # ушиб с прошлой недели: старые травмы уже учтены в power()
            hurt = [p for p in side if p.id in ранены_сейчас]
            if not hurt:
                continue
            nerve = sum(p.t01("храбрость") for p in side) / len(side)
            nerve += 0.2 * (len(side) - len(other)) - 0.25 * len(hurt) / len(side)
            if h.rng.chance(clamp(b["бой_порог_морали"] - nerve * 0.6, 0.1, 0.9)):
                return tag
    return "ничья"


def scuffle(h, a, b_npc, place=""):
    """Потасовка из-за пакета: не бой из GDD 17, а короткая свалка.

    Кто-то получает по рёбрам, кто-то отпускает сумку. Насмерть — только
    если в ход пошло оружие, и то редко.
    """
    strong, weak = (a, b_npc) if a.power() >= b_npc.power() else (b_npc, a)
    weak.injuries.append(h.rng.pick(["ушиб", "порез"]))
    weak.health = clamp(weak.health - h.rng.uni(8, 18))
    if h.rng.chance(0.35):
        strong.injuries.append("ушиб")
        strong.health = clamp(strong.health - h.rng.uni(4, 10))
    social.emit(h, a, 4, "ссора", night=False)
    h.journal.line(f"Возились на площадке. {weak.short} {vb(weak.sex, 'ушёл')} с разбитым лицом.", 1)
    return strong is a


def on_death(h, dead, killer=None, quiet=False):
    """Смерть в доме: паника, настроение, осиротевший ребёнок, пустая квартира."""
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
    # мёртвый выпадает из всех союзов и из чужих квартир
    cut_ties(h, dead)
    social.register_incident(h, "смерть", None)
    h.note(f"{dead.short}: {dead.cause}")

    # ребёнок остаётся один (GDD 12.6: семья как моральный центр)
    if dead.dependents > 0:
        _orphan(h, dead)

    # квартира становится пустой и доступной (GDD 12.2)
    flat = release_flat(h, dead)
    for res, v in dead.stock.items():
        flat.stock[res] = flat.stock.get(res, 0.0) + v
    flat.body = {"кто": dead.short, "вин": dead.form("acc"), "падеж": dead.form("gen"), "день": h.day,
                 "порций": h.B["тело_порций"], "тронуто": False}
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


# ---------------------------------------------------------------- ночью, в одной комнате

def оценка_убийства(h, a, victim):
    """Стоит ли ночью убить того, с кем живёшь.

    Самый тёмный поступок в доме после людоедства, и устроен он иначе, чем
    налёт: не сила решает, а то, что жертва спит в двух метрах и доверяет.
    Отсюда и стратегия «втереться»: сначала переехать, потом дождаться ночи.
    """
    b = h.B
    добыча = a.loot_value(victim.id)
    if a.living_with == victim.id:
        # хозяйская квартира достанется ему целиком — со стенами и печкой
        добыча += max(0.0, h.ценность_жилья(h.flats[victim.apt], a)
                      - h.ценность_жилья(h.flats[a.apt], a))
    хочу = добыча * (0.25 + a.t01("жадность") * 0.8)
    хочу += a.hate.get(victim.id, 0.0) / 30.0
    хочу += a.desperation() * b["убийство_за_отчаяние"]
    # главное: за нож берутся не от ссоры, а когда разойтись нельзя. Хозяин
    # может выставить гостя, гость может уйти — если ему есть куда вернуться.
    # А вот тот, чью квартиру разобрали на доски, пока он грелся у соседа,
    # заперт с ним в одной комнате, и это совсем другой расчёт
    if a.living_with == victim.id:
        своя = h.flats[a.apt]
        есть_куда = h.flat_temp(своя, burning=social.своя_топится(h, a),
                                powered=h.power_on) > b["комфортная_температура"] - 8
    else:
        есть_куда = a.power() >= victim.power() * b["выгнать_превосходство"]
    if есть_куда:
        хочу -= b["убийство_есть_выход"]
    хочу -= a.t01("лояльность") * b["убийство_совесть"]
    хочу -= 3.0 if victim.id in a.allies else 0.0
    хочу -= 2.5 if victim.dependents else 0.0
    # рядом был он один, и дом это поймёт: чем больше свидетелей вокруг,
    # тем страшнее. Скрытному страшно меньше
    хочу -= b["убийство_страх_раскрытия"] * (1.0 + 0.15 * len(h.alive())) * (1.4 - stealth(a))
    # даже спящий сильный человек может проснуться
    хочу -= max(0.0, victim.power() - a.power()) * 0.8
    return хочу


def убить_соседа(h, killer, victim):
    """Ночь в общей квартире. Возвращает True, если получилось."""
    b = h.B
    killer.bump("покушений")
    h.bump("покушений_на_соседа")
    шанс = b["убийство_база"] + (stealth(killer) - 0.5) * 0.4
    if killer.weapon in ("нож", "топор"):
        шанс += 0.10
    шанс -= victim.power() * 0.05
    шанс -= 0.12 if victim.tonight == "дежурить" else 0.0
    if not h.rng.chance(clamp(шанс, 0.30, 0.95)):
        # проснулся
        social.emit(h, killer, 5, "ссора", night=True)
        h.journal.line(f"{victim.short} {vb(victim.sex, 'проснулся')} от того, что "
                       f"{killer.short} {'стояла' if killer.sex == 'ж' else 'стоял'} над "
                       f"{'ней' if victim.sex == 'ж' else 'ним'}.", 2)
        social.adjust(victim, killer.id, trust=-10.0, hate=b["ненависть_за_убийство_соседа"])
        social.register_incident(h, "покушение", None)
        social.judge(h, killer, "насилие", hate=25.0, trust=-4.0)
        cut_ties(h, killer if killer.living_with else victim)
        fight(h, [killer], [victim], place=f"кв.{victim.apt}", reason="ночью в одной квартире")
        приговор_дома(h, killer, "покушение", "то, что он сделал ночью")
        return False

    # получилось
    гость_был = killer.living_with == victim.id
    for res, v in list(victim.stock.items()):
        if v:
            killer.stock[res] = killer.stock.get(res, 0.0) + v
            victim.stock[res] = 0.0
    victim.health = 0.0
    victim.alive = False
    victim.cause = vb(victim.sex, "убит") + " ночью, в собственной квартире"
    victim.died_day = h.day
    killer.bump("убийств")
    h.bump("убийств")
    h.bump("убийств_соседа")
    killer.mood = clamp(killer.mood - b["убийство_настроение"])
    killer.panic = clamp(killer.panic + 10)
    if гость_был:
        # он остаётся здесь: ради этих стен всё и было
        killer.apt, killer.floor = victim.apt, victim.floor
    h.journal.secret(f"ночью {killer.short} убил {victim.form('gen')} и забрал всё")
    on_death(h, victim)          # без killer: дом ещё не знает, кто это
    if гость_был:
        killer.living_with = None
    h.note(f"{victim.short}: {victim.cause}")

    # дом видит тело с раной и понимает, кто был рядом
    подозрение = clamp(b["убийство_подозрение"] * (1.4 - stealth(killer)), 0.05, 0.95)
    узнали = [w for w in h.others(killer) if h.rng.chance(подозрение)]
    for w in узнали:
        social.adjust(w, killer.id, trust=-5.0, hate=b["ненависть_за_убийство_соседа"], aware=20)
    if узнали:
        killer.stats["под_подозрением"] = 1
        h.journal.line(f"{victim.short} {vb(victim.sex, 'умер')} ночью, а рядом был "
                       f"только {killer.short}. Дом это сложил.", 2)
        social.register_incident(h, "убийство", None)
        social.judge(h, killer, "насилие", hate=20.0, trust=-3.0, witnesses=узнали)
        приговор_дома(h, killer, "убийство", "смерть соседа по квартире")
    else:
        h.journal.line(f"{victim.short} не {vb(victim.sex, 'проснулся')}. "
                       f"{killer.short} {vb(killer.sex, 'сказал')}, что ночью было тихо.", 2)
    return True


# ---------------------------------------------------------------- рейд как осада

def aggr(h):
    """Общий множитель злости дома. Одно число вместо шести порогов."""
    return max(0.2, h.B.get("агрессивность_дома", 1.0))


def хочу_его_квартиру(h, npc, t):
    """Насколько чужая квартира лучше своей — с поправкой на то,
    что ломая дверь, ты портишь то, ради чего пришёл (GDD 16).

    Ноль, если своя не хуже или если идти всё равно некуда: тот, кто сам
    живёт у соседа, чужую квартиру занять не может.
    """
    if npc.living_with:
        return 0.0
    b = h.B
    выгода = (h.ценность_жилья(h.flats[t.apt], npc)
              - h.ценность_жилья(h.flats[npc.apt], npc))
    if t.apt not in npc.ключи:
        # выломанная дверь — минус к призу. С ключами ломать нечего
        выгода -= b["дверь_ломается_за"] * 1.6 * b["жильё_вес_защиты"]
    return max(0.0, выгода)


def consider_raid(h, npc):
    """Условие запуска рейда ровно по GDD 16.

    «Рейд запускается, когда паника выше половины и высока либо ненависть,
    либо осведомлённость.»

    Пороги из документа больше не делятся на агрессивность дома: когда они
    делились, «паника выше половины» на практике означала «выше четверти»,
    и записанное в GDD условие переставало описывать игру. Агрессивность
    теперь двигает только поведение — совесть, вербовку, готовность откупиться
    и осторожность вора, — а не саму формулу запуска.
    """
    b = h.B
    A = aggr(h)
    # ворота нормальности стоят первыми: пока жизнь ещё похожа на обычную,
    # человек до чужой двери просто не додумывается — какой бы ни была паника.
    # Раньше эта проверка стояла после паники и не срабатывала ни разу
    if npc.normalcy > b["нормальность_потолок_налёта"]:
        return None
    if npc.panic < b["налёт_порог_паники"]:
        return None
    if npc.health < 35 or len(npc.injuries) >= 2:
        return None
    # налёт — это всегда либо нужда, либо личное
    if npc.desperation() < 0.30 / A and max(list(npc.hate.values()) or [0]) < 50 / A:
        return None
    best = None
    for t in h.others(npc):
        # к тому, с кем живёшь под одной крышей, не идут с ломом
        if social.под_одной_крышей(h, npc, t):
            continue
        # и к двери переехавшего тоже: за ней пусто, а сам он у хозяина —
        # если нужны его запасы, идти надо к хозяину, он в этом же списке
        if t.living_with:
            continue
        hate = npc.hate.get(t.id, 0.0)
        aware = npc.aware.get(t.id, 0.0)
        if hate < b["налёт_порог_ненависти"] and aware < b["налёт_порог_осведомлённости"]:
            continue
        want = npc.loot_value(t.id) * (0.5 + npc.t01("жадность"))
        want *= 0.6 + npc.desperation() * 1.4
        want += hate / 25.0
        # за дверью не только банки: за ней стены, печка и целая дверь.
        # Это второй мотив осады, и он не кончается, в отличие от еды
        want += хочу_его_квартиру(h, npc, t) * b["налёт_вес_жилья"]
        # если тихо взять не выйдет, дверь начинает притягивать людей с ломом:
        # укреплённая квартира защищает от вора и приманивает налёт (GDD 16)
        want += (1.0 - theft_chance(h, npc, t, известно=False)) * 2.6
        # страх перед хозяином: чем сильнее сосед, тем меньше желания.
        # человек считает не себя одного, а тех, кого реально может привести.
        # Считаем тем же кодом, что и собирает группу, — иначе вожак идёт туда,
        # куда его группа не пойдёт, и остаётся у двери один
        crew_size = len(recruit(h, npc, t))
        # за дверью не один человек, а квартира: сожители дерутся за неё
        # по определению (см. defenders_of). Пока страх считался по одному
        # хозяину, «вместе безопаснее» было правдой только в момент драки,
        # а в голове у налётчика этого не было — и съезжаться не защищало
        fear = sum(p.power() for p in household(h, t)) * (1.4 - npc.t01("храбрость")) * 1.5
        fear /= 1.0 + 0.45 * (crew_size - 1)
        fear += t.shelter.get("дверь", 0) * 0.8
        from .model import FIREARMS
        if t.weapon in FIREARMS and npc.aware.get(t.id, 0) > 30:
            fear += 2.2
        # совесть
        conscience = npc.t01("лояльность") * 5.5 * (1.0 - npc.desperation() * 0.5) / A
        conscience += 3.5 if t.id in npc.allies else 0.0   # на своего идти тяжело
        conscience += 1.2 if t.dependents else 0.0   # к матери с ребёнком идут последними
        # в одиночку к чужой двери идут только те, кто явно сильнее хозяина:
        # GDD 16 говорит о группе, а один человек у двери — это не осада,
        # а разговор через цепочку
        if crew_size < b["налёт_минимум_группы"] and npc.power() < t.power() * b["налёт_соло_превосходство"]:
            continue
        score = want - fear - conscience
        if best is None or score > best[1]:
            best = (t, score)
    if best and best[1] >= b["налёт_порог_желания"] / A:
        return best[0]
    return None


def recruit(h, leader, target):
    """Состав группы ровно по GDD 16.

    «Состав — агрессивная группа; при высоких обеих шкалах к ней присоединяются
    нейтралы.» Значит, к вожаку идут по трём разным причинам, и путать их нельзя:

      · свои          — те, кто уже в агрессивном ядре дома (GDD 12.4);
      · злые лично    — те, у кого своя ненависть к этой двери;
      · нейтралы      — те, у кого высоки ОБЕ шкалы: и паника, и осведомлённость
                        о запасах цели, и кто при этом доверяет вожаку.

    Раньше доверие к вожаку требовалось от всех, включая агрессивное ядро,
    и на дверь приходил один человек: средний состав налёта был 1.4.
    """
    b = h.B
    A = aggr(h)
    crew = [leader]
    for p in h.others(leader):
        if p.id == target.id or p.health < 40 or len(p.injuries) >= 2:
            continue
        # свою же дверь не ломают: под одной крышей — значит на одной стороне
        if p.living_with == target.id or target.living_with == p.id:
            continue
        if p.living_with == leader.id or leader.living_with == p.id:
            свой_кров = True          # с кем живёшь, за тем и идёшь
        else:
            свой_кров = False
        trust = p.trust.get(leader.id, 3.0)

        ядро = p.group == "агрессивные"
        злой = p.hate.get(target.id, 0.0) >= b["налёт_порог_ненависти"]
        нейтрал = (p.panic >= b["налёт_нейтралы_порог"]
                   and p.aware.get(target.id, 0.0) >= b["налёт_нейтралы_порог"]
                   and trust >= b["налёт_вербовка_доверие"])
        if not (ядро or злой or нейтрал or (свой_кров and trust >= b["налёт_вербовка_доверие"])):
            continue

        pull = p.desperation() * 2.0 + p.t01("жадность") * 1.5 + trust * 0.25
        pull += p.hate.get(target.id, 0.0) / 30.0
        pull += 1.0 if ядро else 0.0
        pull -= p.t01("лояльность") * 2.2 + (1.2 if target.id in p.allies else 0.0)
        pull -= (1.0 - p.t01("храбрость")) * 1.5
        if pull > b["налёт_порог_вербовки"] / A:
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
        social.judge(h, p, "предательство", hate=10.0, trust=-1.0)
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
            m = take_household(h, target, p, greed=b["откуп_доля"] / len(crew))
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
    from .model import FIREARMS
    armed = [d for d in defenders if d.weapon in FIREARMS and d.stock.get("патроны", 0) > 0]
    if armed:
        shooter = armed[0]
        scare = b["угроза_оружием_отпугивает"] * (1.0 - sum(p.t01("храбрость") for p in crew) / len(crew) * 0.6)
        scare /= aggr(h)
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

    # GDD 16, стадия «Дверь»: «время на подготовку, побег через окно/чердак, засада».
    # Пока дверь держат, у хозяина есть эти минуты — и это его выбор, а не бросок
    # на следующей стадии, куда побег был перенесён раньше
    прочность = target.door_strength()
    по_ключам = target.apt in leader.ключи
    if по_ключам:
        h.journal.line(f"{leader.short} {vb(leader.sex, 'открыл')} дверь кв.{target.apt} "
                       f"ключом. Ломать не пришлось.", 2)
    держит = (not по_ключам) and tools <= прочность * h.rng.uni(0.75, 1.35)
    if держит or прочность >= 2.0:
        уйти = (1.0 - target.t01("храбрость")) * b["побег_за_трусость"]
        уйти += b["побег_за_превосходство"] * clamp(attack_power / max(0.5, def_power) - 1.0, 0.0, 2.0)
        уйти -= b["побег_за_иждивенцев"] if target.dependents else 0.0
        if len(defenders) > 1:
            уйти *= 0.4                    # при своих не бегут
        if h.rng.chance(clamp(уйти, 0.0, 0.9)):
            moved = {}
            for p in crew:
                m = take_household(h, target, p, greed=b["налёт_доля_пустой_квартиры"] / len(crew))
                for k, v in m.items():
                    moved[k] = moved.get(k, 0) + v
            target.warmth = clamp(target.warmth - 25)
            h.journal.line(f"{target.short} {vb(target.sex, 'ушёл')} через окно на пожарную лестницу, "
                           f"пока били дверь. Квартиру вынесли: {_fmt(moved)}.", 2)
            _house_learns(h, target, crew)
            h.bump("исход_сбежал")
            return "сбежал"
        # засада: тот, кто ждёт за дверью с топором, встречает первого вошедшего
        if not держит and target.weapon != "нет" and target.t01("храбрость") > 0.55 and h.rng.chance(b["засада_шанс"]):
            первый = h.rng.pick(crew)
            первый.injuries.append(h.rng.pick(["ушиб", "порез"]))
            первый.health = clamp(первый.health - h.rng.uni(10, 22))
            h.journal.line(f"{target.short} {vb(target.sex, 'ждал')} за дверью. "
                           f"{первый.short} {vb(первый.sex, 'получил')} первым.", 2)
            for p in crew:
                p.panic = clamp(p.panic + 10)

    door_broken = not держит
    if not door_broken:
        # стены и потолок — отдельная стадия и только объединённой группой
        # (GDD 16: «при объединённом рейде группа ломает и их; тогда нужны
        # улучшения 4 уровня или побег»). Арматура в стене — та самая защита
        # четвёртого уровня, и до сих пор её не существовало
        стены = target.shelter.get("стены", 0)
        через_стену = h.rng.chance(b["стены_шанс"] / (1.0 + b["стены_за_уровень"] * стены))
        if len(crew) >= b["стены_нужно_людей"] and через_стену:
            h.journal.line(f"Дверь кв.{target.apt} выдержала — тогда полезли через стену из пустой квартиры.", 2)
            door_broken = True
        else:
            h.journal.line(f"Дверь кв.{target.apt} выдержала. Били долго, ушли под утро.", 2)
            target.panic = clamp(target.panic + 14)
            target.mood = clamp(target.mood - 10)
            # дверь повело
            target.shelter["дверь"] = max(0, target.shelter.get("дверь", 0) - b["дверь_ломается_за"])
            for p in crew:
                social.adjust(p, target.id, hate=8, aware=12)
            _house_learns(h, target, crew)
            h.bump("исход_отбился")
            return "отбился"

    # ---- стадия 3: прорыв ----
    h.journal.line(f"Дверь кв.{target.apt} вынесли.", 2)
    # GDD 16, «Прорыв»: сдаться, отбиваться или бежать. Бежать было поздно —
    # это решалось, пока дверь ещё держали
    surrender = (def_power < attack_power * b["сдаться_превосходство"]
                 or target.t01("храбрость") < b["сдаться_трусость"])

    if surrender:
        moved = {}
        for p in crew:
            m = take_household(h, target, p, greed=0.75 / len(crew))
            for k, v in m.items():
                moved[k] = moved.get(k, 0) + v
        h.journal.line(f"{target.short} не {vb(target.sex, 'стал')} драться. Забрали: {_fmt(moved)}.", 2)
        for p in crew:
            social.adjust(target, p.id, hate=b["ненависть_за_налёт"], trust=-5.0)
        # GDD 12.5 называет убийство отдельным действием NPC против NPC, а GDD 11
        # ставит «убийство безоружного» в самый тяжёлый вес. До сих пор смерть
        # могла случиться только как побочный результат драки
        решимость = (leader.t01("вспыльчивость") + leader.hate.get(target.id, 0) / 100.0
                     - leader.t01("лояльность") * 1.5)
        if (решимость > b["добить_решимость"] and not target.dependents
                and h.rng.chance(b["добить_шанс"])):
            target.health = 0.0
            target.alive = False
            target.cause = vb(target.sex, "убит") + " безоружным при налёте"
            target.died_day = h.day
            leader.bump("убийств")
            h.bump("убийств")
            h.bump("убийств_безоружных")
            h.journal.line(f"{target.short} не {vb(target.sex, 'сопротивлялся')}. "
                           f"{leader.short} {vb(leader.sex, 'убил')} {target.form('acc')} всё равно.", 2)
            h.note(f"{leader.short} {vb(leader.sex, 'убил')} безоружного {target.form('acc')}")
            social.judge(h, leader, "насилие", hate=b["ненависть_за_налёт"], trust=-4.0)
            on_death(h, target, killer=leader)
            _house_learns(h, target, crew)
            h.bump("исход_убит")
            return "убит"
        # если шли за квартирой — её и забирают. Хозяина меняют местами
        # с вожаком: тот перебирается в чужие стены, а бывшему остаётся дыра,
        # из которой пришли (GDD 16, «сдаться — потеря запасов»)
        if хочу_его_квартиру(h, leader, target) > 0 and h.rng.chance(b["занять_после_налёта"]):
            занять_силой(h, leader, target)
            _house_learns(h, target, crew)
            h.bump("исход_занял")
            return "занял"
        # изгнание — если вожак злой (GDD 16: «сдаться — потеря запасов,
        # возможно, изгнание»). Раньше эта ветка стояла за условием
        # «ни у кого из защитников нет даже ножа» и не случалась ни разу
        if ((leader.trait("вспыльчивость") >= 7 or leader.hate.get(target.id, 0) > 70)
                and h.rng.chance(b["изгнание_после_налёта"]) and not target.dependents):
            exile(h, target, by=leader, reason="налёт")
            h.bump("исход_изгнан")
            return "изгнан"
        _house_learns(h, target, crew)
        h.bump("исход_ограблен")
        return "ограблен"

    # драка
    res = fight(h, crew, defenders, place=f"кв.{target.apt}", reason="налёт")
    if not target.alive:
        # GDD 16 называет «убит» отдельным исходом осады. Раньше гибель цели
        # засчитывалась как «ограблен», и в статистике этого исхода не было вовсе.
        # Запасы убитого уже лежат в его квартире — она теперь пустая и открытая
        _house_learns(h, target, crew)
        h.bump("исход_убит")
        return "убит"
    if res == "a" and хочу_его_квартиру(h, leader, target) > 0 and leader.alive:
        занять_силой(h, leader, target)
        _house_learns(h, target, crew)
        h.bump("исход_занял")
        return "занял"
    if res == "a":
        moved = {}
        for p in [c for c in crew if c.alive]:
            m = take_household(h, target, p, greed=0.85 / max(1, len([c for c in crew if c.alive])))
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


def занять_силой(h, кто, у_кого):
    """Вожак въезжает в чужую квартиру, хозяина отправляют в его прежнюю.

    Меняются местами, а не выбрасываются на мороз: дом маленький, углов
    хватает, и это делает захват расчётливым поступком, а не убийством.
    Гости обеих сторон переезжают вместе со своими — они привязаны к людям.
    """
    старая = h.flats[кто.apt]
    новая = h.flats[у_кого.apt]
    if у_кого.alive and not у_кого.exiled:
        у_кого.apt, у_кого.floor = старая.apt, старая.floor
        у_кого.mood = clamp(у_кого.mood - 18)
        у_кого.panic = clamp(у_кого.panic + 15)
        у_кого.warmth = clamp(у_кого.warmth - 10)
        social.adjust(у_кого, кто.id, trust=-6.0, hate=h.B["ненависть_за_захват"])
    кто.apt, кто.floor = новая.apt, новая.floor
    кто.ключи.discard(новая.apt)
    h.bump("захвачено_квартир")
    h.journal.line(f"{кто.short} {vb(кто.sex, 'перебрался')} в кв.{новая.apt}. "
                   + (f"{у_кого.short} {vb(у_кого.sex, 'ушёл')} в его прежнюю, кв.{старая.apt}."
                      if у_кого.alive and not у_кого.exiled else "Хозяина больше нет."), 2)
    h.note(f"{кто.short} {vb(кто.sex, 'отнял')} квартиру {у_кого.form('gen')}")
    social.judge(h, кто, "воровство", hate=12.0, trust=-1.5)


def _house_learns(h, target, crew):
    """После налёта весь дом узнаёт и о жертве, и о нападавших."""
    b = h.B
    social.house_shock(h, panic=b["паника_от_налёта_в_доме"], mood=-6)
    свои = {c.id for c in crew}
    # судит дом, а не соучастники: тот, кто сам стоял у этой двери, не злится
    # на того, с кем он туда пришёл. Пока состав не был исключён из перебора,
    # налёт добавлял +17 ненависти между подельниками, и агрессивное ядро
    # из GDD 12.4 съедало само себя за две ночи
    судьи = [p for p in h.alive() if p.id not in свои]
    for p in h.alive():
        social.adjust(p, target.id, aware=18)
        if p.id in свои:
            continue
        for c in crew:
            social.adjust(p, c.id, trust=-1.2 * p.t01("лояльность") * 2,
                          hate=10 + 12 * p.t01("лояльность"), aware=8)
    # и отдельно — по личной мерке каждого (GDD 12.1)
    for c in crew:
        social.judge(h, c, "насилие", hate=8.0, trust=-0.6, witnesses=судьи)
