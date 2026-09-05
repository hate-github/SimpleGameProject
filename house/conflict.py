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


# ---------------------------------------------------------------- оружие как вещь

def вооружиться(h, npc, чем, куда_старое=None, вслух=True, текст=None):
    """Взять оружие в руки. Прежнее остаётся там, откуда взято новое.

    Слот один, поэтому «взял топор» всегда значит «нож положил», а класть его
    некуда, кроме стен: это то же правило, что у построек — вещь остаётся там,
    где её оставили, а не исчезает вместе с решением. Иначе дом к третьей неделе
    вооружён целиком, и численное превосходство (GDD 17) перестаёт значить что-либо.
    """
    from .model import WEAPONS
    старое = npc.weapon
    if WEAPONS.get(чем, 0.0) <= WEAPONS.get(старое, 0.0):
        return None
    npc.weapon = чем
    if старое and старое != "нет":
        flat = куда_старое if куда_старое is not None else h.flats[npc.apt]
        flat.оружие.append(старое)
    h.bump("оружие_сменило_руки")
    npc.bump("вооружался")
    # с оружием в руках человека видят: это и есть «свидетель инвентаря»
    # из GDD 12.3, и с него же начинается страх (GDD 17)
    if вслух:
        from .model import ОРУЖИЕ_ВИН
        видели = [o for o in h.others(npc) if h.rng.chance(h.B["оружие_заметно"])]
        social.увидел_оружие(h, None, npc, свидетели=видели,
                             доля=h.B["оружие_страх_доля"])
        h.journal.line(текст or (f"{npc.short} {vb(npc.sex, 'взял')} "
                                 f"{ОРУЖИЕ_ВИН.get(чем, чем)} себе."), 1)
    return чем


def подобрать_оружие(h, npc, flat, вслух=True):
    """Взять из этих стен то, что лучше своего. Возвращает название или None.

    Топор в углу и ружьё над дверью остаются в квартире так же, как буржуйка
    и заклеенные окна (GDD 15): кто занял квартиру или разобрал её, тот
    и вооружился. До сих пор это была единственная вещь, которой в наследство
    не доставалось никому.
    """
    from .model import WEAPONS
    if not flat.оружие:
        return None
    лучшее = max(flat.оружие, key=lambda w: WEAPONS.get(w, 0.0))
    if WEAPONS.get(лучшее, 0.0) <= WEAPONS.get(npc.weapon, 0.0):
        return None
    flat.оружие.remove(лучшее)
    if вооружиться(h, npc, лучшее, куда_старое=flat, вслух=вслух) is None:
        flat.оружие.append(лучшее)
        return None
    return лучшее


def сложить_оружие(h, person, flat):
    """Выбывший оставляет оружие в стенах: у него оно больше не в руках."""
    if person.weapon and person.weapon != "нет":
        flat.оружие.append(person.weapon)
        person.weapon = "нет"


def отнять_оружие(h, victim, taker, шанс):
    """Забрать чужое оружие силой. Только если своё хуже: второго в руках не унести.

    Самый злой из всех источников: за топором теперь можно прийти. И самый
    страшный для потерпевшего — его не просто обобрали, его обезоружили,
    а завтра тот же человек придёт снова.
    """
    from .model import WEAPONS
    if victim.weapon == "нет" or not victim.alive:
        return None
    if WEAPONS.get(victim.weapon, 0.0) <= WEAPONS.get(taker.weapon, 0.0):
        return None
    if not h.rng.chance(шанс):
        return None
    чем = victim.weapon
    victim.weapon = "нет"
    if вооружиться(h, taker, чем) is None:
        victim.weapon = чем
        return None
    b = h.B
    social.adjust(victim, taker.id, hate=b["ненависть_за_оружие"], trust=-2.0)
    social.испугался(h, victim, taker, b["страх_за_оружие"] * WEAPONS.get(чем, 1.0))
    h.bump("оружия_отнято")
    return чем


def унести_оружие(h, victim, taker, шанс):
    """Вынести чужое оружие из квартиры — при краже или при осаде.

    Отличие от `отнять_оружие` в том, что здесь никто никому в лицо не смотрит:
    берут не потому, что нужнее, а потому, что плохо лежит. Если своё лучше —
    несут к себе в угол, а не в руках: две вещи в руках не унести, но топор
    в чужой прихожей никто не оставит.
    """
    from .model import WEAPONS, ОРУЖИЕ_ВИН
    if victim.weapon == "нет" or not h.rng.chance(шанс):
        return None
    чем = victim.weapon
    victim.weapon = "нет"
    h.bump("оружия_вынесено")
    if WEAPONS.get(чем, 0.0) > WEAPONS.get(taker.weapon, 0.0):
        вооружиться(h, taker, чем, вслух=False)
    else:
        h.flats[taker.apt].оружие.append(чем)
    # злость и подозрение здесь не наводят: ночью хозяин спит и наутро гадает,
    # кто это был, — за это отвечает notice_theft и suspect. При осаде злость
    # на пришедших уже начислена, и второй раз её начислять не за что
    return ОРУЖИЕ_ВИН.get(чем, чем)


def take_from(h, victim, taker, greed, limit=None):
    """Перекладывание чужого себе. greed 0..1 — какая доля запаса уходит.

    limit — сколько единиц одного ресурса можно унести за раз. Ночью вор уносит
    то, что влезает в сумку, а не весь шкаф; при осаде выносят без ограничений.
    """
    moved = {}
    for res in ("еда", "топливо", "лекарства", "вода", "патроны", "материалы", "деньги"):
        have = victim.stock.get(res, 0.0)
        if have <= 0:
            continue
        amount = have * greed
        # деньги — не банки с тушёнкой: сколько нашёл, столько и в карман,
        # предел на «сколько влезет в сумку» к ним не относится
        if limit is not None and res != "деньги":
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
    # в квартиру, которую уже вскрыли осадой, входят через ту же дыру: пролом
    # в стене отменяет любой засов, и это самая долгая цена налёта
    p += b["дыра_кража"] * h.where(target).дыр()
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
    # свою первую кражу человек себе не забывает: после неё чужая дверь
    # перестаёт быть чужой (GDD 12.4)
    social.переступил(h, thief, "кража")
    # и если он обещал к этой двери не подходить — слово нарушено
    social.нарушил(h, thief, target, "не_делать", target.id)
    if h.rng.chance(p):
        moved = take_from(h, target, thief, greed=h.rng.uni(0.25, 0.5),
                          limit=b["кража_унос_макс"])
        # топор стоит в прихожей, а хозяин спит в комнате. Редко, но это
        # самая злая пропажа из всех: наутро человек безоружен и не знает,
        # у кого теперь его топор
        унёс = унести_оружие(h, target, thief, b["оружие_при_краже"])
        if унёс:
            moved = dict(moved)
            moved[унёс] = 1
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
            # и это он запомнит надолго: к этой двери он больше не подойдёт
            # не потому, что раскаялся, а потому что видел ствол (GDD 17)
            social.увидел_оружие(h, thief, target)
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
    # на мороз выставляют с пустыми руками: и запасы, и оружие, и ключи
    # от погреба остаются в доме
    сложить_оружие(h, person, flat)
    flat.ключи |= person.ключи_кладовых
    person.ключи_кладовых = set()
    if person.dependents:
        _orphan(h, person)


def уйти_из_дома(h, person):
    """Человек ушёл к пункту обогрева и не вернулся (GDD 21).

    Дошёл он до школы №7 или замёрз на объездной — дом не узнаёт никогда,
    и это правильно: изнутри подъезда оба исхода выглядят одинаково. Третьего
    исхода нет — «дошёл» и «замёрз по дороге» для дома одно и то же: пропал.
    """
    b = h.B
    person.exiled = True          # для всего кода это обычное выбытие
    person.ушёл = True
    person.died_day = h.day
    с_кем = (f" с {person.dependent_ins or person.dependent_name}"
             if person.dependents else "")
    person.cause = (vb(person.sex, "ушёл")
                    + (с_кем if с_кем else " к пункту обогрева")
                    + ", не " + vb(person.sex, "вернулся"))
    h.bump("ушедших")
    свои = ", ".join(f"{k} {v:.1f}".rstrip("0").rstrip(".")
                     for k, v in sorted(person.stock.items()) if v >= 0.05)
    h.journal.line(f"{person.short} {vb(person.sex, 'ушёл')}{с_кем} к школе №7. "
                   f"{'Взяла' if person.sex == 'ж' else 'Взял'} что {vb(person.sex, 'смог')} унести"
                   + (f": {свои}." if свои else "."), 2)
    h.note(f"{person.short} ушёл к пункту обогрева" + с_кем)
    # дом видел, как он уходил с мешком: это все шесть окон разом
    social.house_shock(h, panic=b["уйти_паника_дома"], mood=b["уйти_настроение_дома"])
    cut_ties(h, person)
    # то, что унёс, уходит из мира — иначе учёт не сойдётся с первого же ухода
    несёт = b["уйти_унесёт"] * (1.0 + 0.5 * person.dependents)
    осталось = dict(person.stock)
    взял = {}
    for res in sorted(осталось):
        if несёт <= 0:
            break
        v = min(осталось[res], несёт)
        if v > 0:
            взял[res] = v
            осталось[res] -= v
            несёт -= v
    for res, v in взял.items():
        h.stats["унесено_" + res] = h.stats.get("унесено_" + res, 0.0) + v
    # квартира остаётся открытой и брошенной. `owner_died` не ставим: никто
    # здесь не умирал, и знание о смерти эту дверь не запирает
    flat = release_flat(h, person, чья_смерть=False)
    for res, v in осталось.items():
        if v > 0:
            flat.stock[res] = flat.stock.get(res, 0.0) + v
    person.stock = {}
    # оружие он забирает с собой: в такую дорогу без ножа не выходят.
    # Учитывается отдельным счётчиком, иначе оружие «исчезает» из баланса
    if person.weapon and person.weapon != "нет":
        h.stats["оружия_унесено"] = h.stats.get("оружия_унесено", 0) + 1
        person.weapon = "нет"
    # а ключ от погреба остаётся на гвозде: ему он больше ни к чему
    flat.ключи |= person.ключи_кладовых
    person.ключи_кладовых = set()
    # ребёнок уходит с ним: мать и уходит-то не за себя
    дошёл = h.rng.chance(b["пункт_дошёл"])
    h.journal.secret(f"{person.short} " + (vb(person.sex, "дошёл") + " до школы №7" if дошёл
                                           else vb(person.sex, "замёрз") + " на объездной, не дойдя"))
    h.bump("дошли_до_пункта" if дошёл else "замёрзли_по_дороге")
    person.stats["дошёл"] = 1 if дошёл else 0


def release_flat(h, person, чья_смерть=True):
    """Квартира выбывшего. Заводить её больше не нужно — она была всегда.

    Пустой её делает не запись в списке, а то, что в ней никто не живёт
    (House.пустые). Здесь только помечается, чей это был дом: соседи разбирают
    жильё умершего иначе, чем брошенное.
    """
    flat = h.flats[person.apt]
    if чья_смерть:
        flat.owner_died = flat.owner_died or person.id
    # после того как оттуда вынесли человека, дверь так и остаётся открытой:
    # ломать её больше не нужно никому
    flat.открыта = True
    return flat


def occupy_flat(h, person):
    """Человек вернулся в свою квартиру: забирает то, что в ней осталось."""
    flat = h.flats.get(person.apt)
    if flat and not (flat.body and flat.body.get("порций", 0) > 0):
        for res, v in list(flat.stock.items()):
            if v:
                person.stock[res] = person.stock.get(res, 0.0) + v
                flat.stock[res] = 0.0
        подобрать_оружие(h, person, flat)
        # и связку с гвоздя: погреб приписан к квартире, а не к человеку,
        # и достаётся тому, кто в этих стенах теперь живёт
        if flat.ключи:
            person.ключи_кладовых |= flat.ключи
            for kid in sorted(flat.ключи):
                к = h.кладовые.get(kid)
                if к is not None:
                    h.journal.line(f"   Вместе со стенами {person.form('dat')} "
                                   f"{vb(person.sex, 'достался')} и ключ от {к.имя_род}.", 1)
            flat.ключи = set()
            h.bump("ключей_от_кладовых_по_наследству")


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
    """Что дом делает с тем, про кого узнал. Одно место на два случая:
    людоед и убийца соседа — разница только в словах.

    Решают люди, а не код. Дом выносит вопрос на площадку (meeting.провести),
    и там он может не собраться, не договориться и сорваться в ссору: это
    прямая проверка тезиса ГДД про «дом, который сумел договориться».
    Автоматика осталась на случай, когда собирать уже некого — иначе людоед
    в доме на двоих стал бы неприкосновенным.
    """
    if not кого.alive or кого.exiled:
        return
    judges = [p for p in h.others(кого) if p.health > 35]
    for p in judges:
        social.adjust(p, кого.id, trust=-3.0, hate=15)
    if len(judges) >= h.B["собрание_минимум"]:
        h.mods["приговор_нужен"] = {"кто": кого.id, "день": h.day}
        h.journal.line(f"С {кого.form('ins')} больше никто не разговаривает. "
                       f"Дом молчит и ждёт, что скажут все.", 2)
        return
    if len(judges) >= 2 and sum(p.power() for p in judges) > кого.power() * 1.2:
        if h.rng.chance(0.55):
            exile(h, кого, by=judges[0], reason=причина_изгнания)
        elif h.rng.chance(0.45):
            h.journal.line(f"За {кого.form('ins')} пришли ночью.", 2)
            fight(h, judges, [кого], place=f"кв.{кого.apt}", reason="приговор дома")
    elif len(judges) >= 1:
        # сил выгнать нет — просто перестают существовать друг для друга
        h.journal.line(f"С {кого.form('ins')} больше никто не разговаривает.", 1)


def _verdict_taboo(h, eater):
    приговор_дома(h, eater, "людоедство", "то, что нашли у него в квартире")


def house_verdict(h, thief, victim):
    """После второй-третьей поимки дом решает, что с вором делать.

    Пока есть кому собраться — решают на площадке (meeting), а не тут:
    выгнать человека на мороз должно быть решением людей, которые могут
    и не согласиться. Автоматика остаётся на дом, в котором собирать некого.
    """
    caught = thief.stats.get("поймали", 0)
    if caught < 2:
        return False
    if len([p for p in h.others(thief) if p.health > 35]) >= h.B["собрание_минимум"]:
        h.mods["приговор_нужен"] = {"кто": thief.id, "день": h.day}
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
            # чем убили — тем дом и будет это помнить: выстрел пугает иначе,
            # чем кулаки. Ружьё без патронов в драке не оружие, а палка
            чем = shooter.weapon if (gun or shooter.weapon not in FIREARMS) else "нет"
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
                on_death(h, victim, killer=shooter, оружие=чем, свидетели=весь_дом(h))
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
                    on_death(h, victim, killer=shooter, оружие=чем, свидетели=весь_дом(h))
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
    # `place` до сих пор принимался и не использовался: возились всегда
    # «на площадке», даже когда свалка шла в комнате, где один из двоих спал
    где = f"в {place}" if place else "на площадке"
    h.journal.line(f"Возились {где}. {weak.short} {vb(weak.sex, 'ушёл')} с разбитым лицом.", 1)
    # проигравший теперь знает, чем это кончается, и с кем
    social.испугался(h, weak, strong, h.B["страх_за_насилие"])
    social.увидел_оружие(h, weak, strong)
    # и остаётся без того, чем мог бы ответить в следующий раз. Это единственное
    # место, где насилие меняет расклад сил надолго, а не на один вечер
    отнял = отнять_оружие(h, weak, strong, h.B["оружие_после_драки"])
    if отнял:
        from .model import ОРУЖИЕ_ВИН
        h.journal.line(f"   {strong.short} {vb(strong.sex, 'забрал')} "
                       f"{ОРУЖИЕ_ВИН.get(отнял, отнял)} {weak.form('gen')}.", 2)
    return strong is a


def засада(h, кто, жертва):
    """Он дождался её на площадке (GDD 16, 17).

    Не осада и не случайная встреча: человек сидел здесь три часа именно ради
    этого. Отсюда и разница с обычным отъёмом — врасплох, и потому чаще
    получается; и не только за пакет: за ключи от квартиры, а если счёты
    достаточно тяжелы и он сильнее — то и насмерть.
    """
    b = h.B
    h.bump("засад_сработало")
    social.обидели(h, жертва, кто, b["обида_за_засаду"])
    social.встретились(h, кто, жертва)
    # врасплох: жертва не успевает ни развернуться, ни позвать
    сила = кто.power() * b["засада_врасплох"]
    ненависть = кто.hate.get(жертва.id, 0.0)
    насмерть = (ненависть >= b["засада_насмерть_злость"]
                and сила > жертва.power() * b["засада_насмерть_превосходство"]
                and кто.weapon != "нет"
                and h.rng.chance(b["засада_насмерть_шанс"]))
    if насмерть:
        # не «убил», а «бросился»: дальше решает бой, и на площадке он может
        # кончиться в любую сторону. Врасплох — это преимущество, а не приговор
        h.journal.line(f"{кто.short} {vb(кто.sex, 'ждал')} {жертва.form('acc')} "
                       f"на площадке и не {vb(кто.sex, 'дал')} даже развернуться.", 2)
        social.переступил(h, кто, "убить_соседа")
        h.bump("покушений_на_соседа")
        fight(h, [кто], [жертва], place="на площадке", reason="ждал у двери")
        social.judge(h, кто, "насилие", hate=25.0, trust=-4.0,
                     witnesses=[жертва], участники=[жертва])
        return "убит" if not жертва.alive else "отбился"
    if сила > жертва.power() * b["засада_превосходство"] or h.rng.chance(0.7):
        moved = take_carried(h, жертва, кто, limit=b["отъём_максимум"])
        h.journal.line(f"{кто.short} {vb(кто.sex, 'вышел')} из-за угла навстречу "
                       f"{жертва.form('dat')}: {_fmt(moved)}.", 2)
        жертва.mood = clamp(жертва.mood - 16)
        жертва.panic = clamp(жертва.panic + 20)
        исход = "обобран"
    else:
        h.journal.line(f"{кто.short} {vb(кто.sex, 'ждал')} на площадке, но "
                       f"{жертва.short} не {vb(жертва.sex, 'отдал')} ничего.", 2)
        scuffle(h, кто, жертва, place="на площадке")
        исход = "отбился"
    # ключи — то, ради чего на площадке и ждут: они дороже банки
    if (not жертва.guests and not жертва.living_with
            and h.rng.chance(b["засада_ключи_шанс"])):
        кто.ключи.add(жертва.apt)
        h.journal.line(f"   Ключи от кв.{жертва.apt} {vb(кто.sex, 'забрал')} тоже.", 2)
        h.bump("ключей_отнято")
        for kid in sorted(жертва.ключи_кладовых):
            жертва.ключи_кладовых.discard(kid)
            кто.ключи_кладовых.add(kid)
            h.bump("ключей_от_кладовых_отнято")
    social.испугался(h, жертва, кто, b["страх_за_насилие"] * 1.3)
    social.adjust(жертва, кто.id, hate=b["ненависть_за_налёт"], trust=-5.0, aware=20)
    social.отдалились(жертва, кто.id, b["близость_за_обиду"])
    social.register_incident(h, "засада", None)
    social.emit(h, кто, 3, "ссора", night=False)
    return исход


def свидетели_смерти(h, dead, killer=None):
    """Кто узнаёт о смерти сразу, без слухов.

    Убийца знает, потому что убил. Тот, кто спал за той же дверью, знает,
    потому что утром не смог его добудиться. Остальные не знают ничего:
    человек умер один в своей квартире на пятом этаже, и узнать об этом
    соседу с первого не от кого и неоткуда.
    """
    кто = {killer.id} if killer is not None else set()
    for p in h.alive():
        if p.id != dead.id and social.под_одной_крышей(h, p, dead):
            кто.add(p.id)
    return кто


def весь_дом(h):
    """Слышали все: выстрел на лестнице, выломанная дверь, драка в подъезде."""
    return {p.id for p in h.alive()}


def on_death(h, dead, killer=None, quiet=False, оружие=None, свидетели=None):
    """Смерть в доме: паника, настроение, осиротевший ребёнок, пустая квартира.

    `свидетели` — кто видел это сам. Всё, что смерть делает с домом — шок,
    привычное, происшествие, ненависть к убийце, — теперь достаётся им, а не
    всем шестерым разом. Остальные узнают слухом (`social.gossip`) или не
    узнают вовсе.
    """
    b = h.B
    dead.alive = False
    if dead.died_day is None:
        dead.died_day = h.day
    h.bump("смертей")
    if свидетели is None:
        свидетели = свидетели_смерти(h, dead, killer)
    видевшие = [p for p in h.alive() if p.id in свидетели]
    if killer:
        for p in видевшие:
            if p.id == killer.id:
                continue
            social.adjust(p, killer.id, trust=-2.5, hate=25 + 15 * p.t01("лояльность"))
            # ненависть к убийце дом чувствует весь; страх — отдельно и сильнее,
            # и зависит от того, чем убили. Выстрел на лестнице и драка
            # на кулаках оставляют после себя разный дом (GDD 11, 17)
            social.видел_убийство(h, p, killer,
                                  оружие if оружие is not None else killer.weapon)
    # мёртвый выпадает из всех союзов и из чужих квартир
    cut_ties(h, dead)
    for p in видевшие:
        social.узнал_о_смерти(h, p, dead)
    h.note(f"{dead.short}: {dead.cause}")

    # ребёнок остаётся один (GDD 12.6: семья как моральный центр)
    if dead.dependents > 0:
        _orphan(h, dead)

    # квартира становится пустой и доступной (GDD 12.2)
    flat = release_flat(h, dead)
    for res, v in dead.stock.items():
        flat.stock[res] = flat.stock.get(res, 0.0) + v
    # его тулуп остался висеть в прихожей: вещь, которая ему уже не нужна,
    # а кому-то откроет улицу ещё на неделю
    if dead.одежда >= h.B["одежда_максимум"]:
        flat.тулуп = True
    # и оружие — там же, в углу за дверью. Тулуп с мёртвого снимали и раньше,
    # а топор исчезал вместе с телом, и самая опасная вещь в доме пропадала
    # ровно тогда, когда за неё стоило бы прийти
    сложить_оружие(h, dead, flat)
    # и ключ от погреба — на гвозде в прихожей. С собой его не уносят,
    # иначе запас в подвале выпадает из игры вместе с хозяином
    flat.ключи |= dead.ключи_кладовых
    dead.ключи_кладовых = set()
    flat.body = {"кто": dead.short, "вин": dead.form("acc"), "падеж": dead.form("gen"), "день": h.day,
                 "порций": h.B["тело_порций"], "тронуто": False}
    dead.stock = {}


def смерть_ребёнка(h, кто, р):
    """Единственное честное завершение детской шкалы (GDD 12.6).

    Правило ГДД остаётся в силе: ребёнок никогда не становится едой и его тела
    в мире не появляется. Но смерть от холода и болезни возможна — и она ломает
    мать сильнее любого другого события: у неё опускается даже пол нормальности,
    то есть она перестаёт быть тем человеком, каким была.
    """
    b = h.B
    if р in кто.дети:
        кто.дети.remove(р)
    кто.dependents = max(0, кто.dependents - 1)
    if not кто.дети:
        кто.dependent_name = ""
        кто.dependent_acc = ""
    h.bump("смертей_детей")
    кто.bump("потерял_ребёнка")
    причина = "от болезни" if р["болен"] else ("от голода" if р["сытость"] <= р["тепло"]
                                               else "от холода")
    h.journal.line(f"† {р['имя']} умер {причина}. {кто.short} "
                   f"{'сидела' if кто.sex == 'ж' else 'сидел'} рядом до утра.", 2)
    h.note(f"{р['имя']} умер {причина} ({кто.form('gen')})")
    кто.mood = clamp(кто.mood - b["ребёнок_смерть_настроение"])
    social.add_panic(кто, b["ребёнок_смерть_паника"])
    кто.нормальность_пол = max(0.0, кто.нормальность_пол - b["ребёнок_смерть_пол"])
    кто.normalcy = clamp(кто.normalcy - b["ребёнок_смерть_нормальность"],
                         кто.нормальность_пол, 1.0)
    # и то, чего ей уже не забыть: кто отказал, когда она просила
    for o in h.others(кто):
        отказов = (кто.asking.get(o.id) or {}).get("отказали", 0.0)
        if отказов > 0:
            social.adjust(кто, o.id, hate=b["ребёнок_смерть_ненависть"] * min(2.0, отказов),
                          trust=-2.0)
    social.house_shock(h, panic=b["ребёнок_смерть_дом_паника"],
                       mood=b["ребёнок_смерть_дом_настроение"])
    social.register_incident(h, "смерть_ребёнка", None)
    for p in h.alive():
        social.видел(h, p, b["нормальность_за_смерть"])


def _orphan(h, dead):
    """Кого-то надо взять к себе. Или не взять.

    Ребёнок переходит вместе со своей шкалой: он тот же самый, промёрзший
    и голодный ровно настолько, насколько был при матери.
    """
    дети = dead.дети or [{"имя": dead.dependent_name or "ребёнок",
                          "вин": dead.dependent_acc or dead.dependent_name or "ребёнка",
                          "род": dead.dependent_gen or dead.dependent_name or "ребёнка",
                          "твор": dead.dependent_ins or dead.dependent_name or "ребёнком",
                          "сытость": 60.0, "тепло": 55.0, "здоровье": 80.0, "болен": None}]
    dead.дети = []
    dead.dependents = 0
    for р in дети:
        candidates = []
        for p in h.alive():
            w = p.trait("лояльность") * 1.8 + p.trust.get(dead.id, 3.0) * 0.8 - p.desperation() * 3.5
            w += 2.0 if "медик" in p.skills else 0.0
            if w > 0:
                candidates.append((p, w))
        if candidates:
            taker = h.rng.weighted(candidates)
            taker.dependents += 1
            taker.дети.append(р)
            taker.dependent_name = р["имя"]
            taker.mood = clamp(taker.mood + 6)
            # все четыре формы, а не две: без родительного и творительного
            # у нового родителя выходило «просидела рядом с Ваня»
            taker.dependent_acc = р["вин"]
            taker.dependent_gen = р.get("род") or р["имя"]
            taker.dependent_ins = р.get("твор") or р["имя"]
            h.journal.line(f"{р['имя']} остался один. {taker.short} "
                           f"{vb(taker.sex, 'забрал')} его к себе.", 2)
            h.note(f"{taker.short} {vb(taker.sex, 'взял')} {р['вин']}")
            for p in h.alive():
                social.adjust(p, taker.id, trust=1.0)
        else:
            h.journal.line(f"{р['имя']} остался один. Никто не взял.", 2)
            h.note(f"{р['имя']} остался один — никто не взял")
            social.house_shock(h, panic=10, mood=-14)
            h.bump("детей_брошено")


# метки поступка для суда дома: взято чужое и сломано доверие сразу.
# Лежат здесь, а не читаются из actions.ТЕГИ, чтобы не тянуть импорт по кругу
ТЕГИ_ОБОБРАТЬ = ("воровство", "предательство")


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
        # квартира достанется ему целиком — но он в ней уже греется, и убийство
        # даёт не тепло, а лишь то, что отсюда больше не выставят. Полный вес
        # этой разницы делал убийство привлекательным ровно для того, кому
        # переезд и был выгоден, то есть для всех подряд
        добыча += max(0.0, h.ценность_жилья(h.flats[victim.apt], a)
                      - h.ценность_жилья(h.flats[a.apt], a)) * b["убийство_вес_жилья"]
    хочу = добыча * (0.25 + a.t01("жадность") * 0.8)
    хочу += a.hate.get(victim.id, 0.0) / 30.0
    # того, кто вынес его квартиру, он не простит и случая ждать не станет:
    # мстят за такое любым способом, и нож ночью — один из них
    хочу += b["обида_вес_мести"] if victim.id in a.не_прощу else 0.0
    хочу += a.desperation() * b["убийство_за_отчаяние"]
    # доверие — главный тормоз, и его тут не было вовсе. Человек не режет того,
    # кому верит: в замере 43% убийц доверяли жертве выше 7 из 10, а 48% жертв
    # были им союзниками. Считать это надо до совести, а не после
    хочу -= a.trust.get(victim.id, 3.0) * b["убийство_за_доверие"]
    # и того, у кого на руках ребёнок, нож в спину не касается: ему есть
    # что терять и есть ради кого не рисковать. Раньше в формуле стояли только
    # ЧУЖИЕ иждивенцы, и половина убийц оказывалась матерями
    if a.dependents:
        хочу -= b["убийство_свой_ребёнок"]
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
    # и просто страшно: за нож против того, кого боишься, берутся тяжелее
    # всего — он может проснуться, а дальше в комнате останется один
    хочу -= a.боится(victim.id) * b["страх_вес_насилия"]
    хочу -= b["убийство_союзник"] if victim.id in a.allies else 0.0
    хочу -= 2.5 if victim.dependents else 0.0
    # рядом был он один, и дом это поймёт: чем больше свидетелей вокруг,
    # тем страшнее. Скрытному страшно меньше
    хочу -= b["убийство_страх_раскрытия"] * (1.0 + 0.15 * len(h.alive())) * (1.4 - stealth(a))
    # даже спящий сильный человек может проснуться
    хочу -= max(0.0, victim.power() - a.power()) * 0.8
    return хочу


def оценка_обобрать(h, гость, хозяин):
    """Стоит ли ночью собрать хозяйское и уйти к себе.

    Середина, которой в модели не было. Мотив «переехать не греться, а
    посмотреть» был давно — `переезд_расчёт` в оценке переезда прямо считает
    чужой шкаф против совести гостя, — а расплаты за него не существовало:
    втереться было можно, а вынести нельзя, и единственным выходом из чужой
    квартиры оставался нож. Отсюда и перекос: человек, которому нужны были
    чужие банки, шёл убивать за них хозяина, потому что другого способа
    уйти с ними не было.

    Отличий от кражи два, и оба в пользу вора. Замка нет — он внутри. И шкаф
    он видел своими глазами, а не по дыму из окна: здесь `est` не нужен, здесь
    правда мира. Отличие от убийства одно, и оно против него: хозяин остаётся
    жив и наутро точно знает, кто у него ночевал.
    """
    b = h.B
    from .model import вещи
    своя = h.flats[гость.apt]
    добыча = вещи(хозяин.stock) * b["обобрать_вес_добычи"]
    хочу = добыча * (0.25 + гость.t01("жадность") * 0.9)
    хочу += гость.desperation() * b["обобрать_за_отчаяние"]
    хочу += гость.hate.get(хозяин.id, 0.0) / 25.0
    хочу += b["обида_вес_мести"] if хозяин.id in гость.не_прощу else 0.0
    # уходить надо в ту же ночь, и уходить есть смысл только туда, где
    # не замёрзнешь. Унесённые дрова свою же квартиру и протопят — но только
    # если в ней есть чем топить и если её ещё не разобрали на доски
    тепло = h.flat_temp(своя, burning=bool(своя.shelter.get("буржуйка")),
                        powered=h.power_on)
    хочу -= max(0.0, b["комфортная_температура"] - тепло) * b["обобрать_за_холод"]
    # и всё, что держит руку: доверие к тому, кто пустил, история пары,
    # совесть, страх и простая мысль, что он может проснуться
    хочу -= гость.trust.get(хозяин.id, 3.0) * b["обобрать_за_доверие"]
    хочу -= гость.свой(хозяин.id) * b["обобрать_за_близость"]
    хочу -= гость.t01("лояльность") * b["обобрать_совесть"]
    хочу -= гость.боится(хозяин.id) * b["страх_вес_кражи"]
    хочу -= max(0.0, хозяин.power() - гость.power()) * b["обобрать_за_силу"]
    # наутро об этом узнает весь подъезд, и объяснить себя будет нечем
    хочу -= b["обобрать_решимость"]
    return хочу


def обобрать_и_уйти(h, гость, хозяин):
    """Ночь в общей квартире, второй её исход. Возвращает True, если вышло."""
    b = h.B
    гость.bump("обобрал")
    h.bump("попыток_обобрать")
    social.переступил(h, гость, "обобрать")
    тихо = clamp(b["обобрать_тихо"] + (stealth(гость) - 0.5) * 0.6, 0.2, 0.95)
    if not h.rng.chance(тихо):
        # хозяин проснулся, а он стоит посреди комнаты с его мешком
        social.emit(h, гость, 4, "ссора", night=True)
        h.journal.line(f"{хозяин.short} {vb(хозяин.sex, 'проснулся')} от того, что "
                       f"{гость.short} {vb(гость.sex, 'собирал')} "
                       f"{'её' if хозяин.sex == 'ж' else 'его'} шкаф в мешок.", 2)
        social.adjust(хозяин, гость.id, trust=-8.0, hate=b["ненависть_за_кражу"], aware=25)
        social.обидели(h, хозяин, гость, b["обида_за_обобрать"] * 0.6)
        social.register_incident(h, "кража", None)
        social.judge(h, гость, ТЕГИ_ОБОБРАТЬ, hate=15.0, trust=-3.0)
        scuffle(h, хозяин, гость, place=f"кв.{хозяин.apt}")
        cut_ties(h, гость)
        occupy_flat(h, гость)
        h.bump("обобрать_сорвалось")
        return False

    # получилось: он знает эту квартиру и берёт всё, что унесёт
    moved = take_from(h, хозяин, гость, greed=b["обобрать_доля"], limit=b["обобрать_предел"])
    # и дрова, которые сносил к этой печке сам, — их он считает своими
    дрова = min(гость.stats.get("снёс_дров", 0.0), хозяин.stock.get("топливо", 0.0))
    if дрова > 0:
        хозяин.stock["топливо"] -= дрова
        гость.stock["топливо"] = гость.stock.get("топливо", 0.0) + дрова
        moved["топливо"] = moved.get("топливо", 0.0) + дрова
    гость.stats["снёс_дров"] = 0.0
    гость.living_with = None
    хозяин.guests.discard(гость.id)
    occupy_flat(h, гость)
    гость.mood = clamp(гость.mood - b["обобрать_настроение"])
    хозяин.mood = clamp(хозяин.mood - 20)
    хозяин.panic = clamp(хозяин.panic + 18)
    h.bump("обобрал_хозяина")
    h.journal.line(f"{гость.short} {vb(гость.sex, 'ушёл')} ночью и {vb(гость.sex, 'унёс')} "
                   f"всё, что было в шкафу у {хозяин.form('gen')}: {_fmt(moved)}. "
                   f"{хозяин.short} {vb(хозяин.sex, 'пустил')} {гость.form('acc')} "
                   f"к своей печке.", 2)
    h.note(f"{гость.short} обобрал {хозяин.form('acc')} и ушёл")
    # хозяин знает наверняка: у него ночевал ровно один человек. И говорит он
    # об этом всем — это не ночная кража через дверь, где виноватого гадают
    social.adjust(хозяин, гость.id, trust=-10.0, hate=b["ненависть_за_кражу"] * 1.5, aware=30)
    social.обидели(h, хозяин, гость, b["обида_за_обобрать"], непрощаемо=True)
    social.испугался(h, хозяин, гость, b["страх_за_насилие"] * 0.4)
    social.register_incident(h, "кража", None)
    social.judge(h, гость, ТЕГИ_ОБОБРАТЬ, hate=b["суд_обобрать_злость"],
                 trust=-b["суд_обобрать_доверие"], witnesses=h.others(гость),
                 участники=[хозяин])
    гость.stats["под_подозрением"] = 1
    приговор_дома(h, гость, "обобрал", "то, что он вынес у того, кто его пустил")
    return True


def убить_соседа(h, killer, victim):
    """Ночь в общей квартире. Возвращает True, если получилось."""
    b = h.B
    killer.bump("покушений")
    h.bump("покушений_на_соседа")
    social.переступил(h, killer, "убить_соседа")
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
        # его он теперь боится по-настоящему: этот человек стоял над ним с ножом
        social.испугался(h, victim, killer, b["страх_за_покушение"])
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

    # убитый жил за одной дверью с убийцей, и наутро тот сам говорит, что
    # сосед не проснулся. Дом узнаёт о смерти — но не о том, отчего она.
    # Это и есть та цена, которую платит единственный источник известия
    for w in h.others(killer):
        social.узнал_о_смерти(h, w, victim)

    # дом видит тело с раной и понимает, кто был рядом
    подозрение = clamp(b["убийство_подозрение"] * (1.4 - stealth(killer)), 0.05, 0.95)
    узнали = [w for w in h.others(killer) if h.rng.chance(подозрение)]
    for w in узнали:
        social.adjust(w, killer.id, trust=-5.0, hate=b["ненависть_за_убийство_соседа"], aware=20)
        # тихое убийство пугает не меньше громкого: этот человек живёт
        # с ними в одном подъезде и однажды ночью уже вставал с ножом
        social.видел_убийство(h, w, killer, killer.weapon)
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
    # «нечего терять»: умирающий идёт и разбитым, и один — просто почти
    # наверняка неудачно. Пока это был запрет, 26% случаев «рядом сытый сосед,
    # а я умираю от голода» упирались именно в него
    if (npc.health < 35 or len(npc.injuries) >= 2) and npc.desperation() < b["налёт_нечего_терять"]:
        return None
    # к чужой двери с ребёнком на руках не идут — ни вожаком, ни в толпе
    if npc.dependents:
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
        crew_size = len(recruit(h, npc, t)[0])   # прикидка, а не зов: он ещё не пошёл
        # за дверью не один человек, а квартира: сожители дерутся за неё
        # по определению (см. defenders_of). Пока страх считался по одному
        # хозяину, «вместе безопаснее» было правдой только в момент драки,
        # а в голове у налётчика этого не было — и съезжаться не защищало
        fear = sum(p.power() for p in household(h, t)) * (1.4 - npc.t01("храбрость")) * 1.5
        fear /= 1.0 + 0.45 * (crew_size - 1)
        fear += t.shelter.get("дверь", 0) * 0.8
        # и то, кого он за этой дверью боится. Не «сильный», а именно
        # страшный: тот, кто на его глазах стрелял, зарубил соседа или
        # вышел на площадку со стволом. Сила забывается, страх помнится
        fear += max(npc.боится(p.id) for p in household(h, t)) * b["страх_вес_налёта"]
        from .model import FIREARMS
        if t.weapon in FIREARMS and npc.aware.get(t.id, 0) > 30:
            fear += 2.2
        # и то, каким его КАЖУТ: слабость видна и она приглашает. Не правда
        # мира — по правде мира у осаждающего нет способа узнать, сколько
        # у соседа здоровья, — а лицо, которое он видел на площадке
        # (social.разглядел). Тот, кто держится прямо, этим и защищается
        слабость = max((npc.плох(p.id) for p in household(h, t)), default=0.0)
        fear -= слабость * b["добить_за_слабость"]
        # к тому, кого не простил, идут и через страх: это не расчёт, а счёт
        if t.id in npc.не_прощу:
            fear -= b["обида_вес_мести"]
        # и свет на площадке. Кто-то в доме не спит — значит, их услышат
        # на лестнице и поднимут весь подъезд. До сих пор дежурство не значило
        # для налётчика ровно ничего: 96 осад из 115 приходились на ночи,
        # когда кто-то дежурил
        дежурят = sum(1 for o in h.others(npc) if o.tonight == "дежурить" and o.id != t.id)
        if дежурят:
            fear += b["налёт_страх_дежурства"] * min(2, дежурят)
        # совесть
        conscience = npc.вес_черт("налёт_совесть") * (1.0 - npc.desperation() * 0.5) / A
        conscience += 3.5 if t.id in npc.allies else 0.0   # на своего идти тяжело
        conscience += 1.2 if t.dependents else 0.0   # к матери с ребёнком идут последними
        # и тот, кто ещё пригодится: единственный человек в доме, который умеет
        # собрать генератор или сбить жар. Это, кажется, единственная защита
        # в этой игре, которая не из досок (GDD 18: «повод быть нужным»)
        if any(u in t.skills for u in ("слесарь", "электрик", "медик")):
            conscience += b["налёт_польза_мастера"]
            if h.mods.get("мастер_занят_" + t.id) == npc.id:
                conscience += b["налёт_польза_мастера"]   # он сейчас делает мне печь
        # в одиночку к чужой двери идут только те, кто явно сильнее хозяина:
        # GDD 16 говорит о группе, а один человек у двери — это не осада,
        # а разговор через цепочку
        соло = (crew_size < b["налёт_минимум_группы"]
                and npc.power() < t.power() * b["налёт_соло_превосходство"])
        if соло:
            if npc.desperation() < b["налёт_нечего_терять"]:
                continue
            fear *= b["налёт_соло_страх"]     # он понимает, на что идёт
        score = want - fear - conscience + npc.пунктик("налёт_вожак")
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

    Возвращает (кто пошёл, кого позвали и получили «нет»). Второй список
    важен не меньше первого: за сорок жизней вожаки получали по тридцать
    отказов за жизнь, и до сих пор после отказа не происходило ровно ничего.
    """
    b = h.B
    A = aggr(h)
    crew = [leader]
    отказали = []
    for p in h.others(leader):
        if p.id == target.id or p.health < 40 or len(p.injuries) >= 2:
            continue
        if p.dependents:
            continue                  # его руки связаны (GDD 12.6)
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
        причина = (ядро or злой or нейтрал
                   or (свой_кров and trust >= b["налёт_вербовка_доверие"]))
        # и те, про кого он просто надеется. Втроём ломают стену, а в одиночку
        # лезут в окно от отчаяния (GDD 16), и разница так велика, что вожак
        # обходит и сомнительных. Именно здесь и берутся отказы: пока «кого
        # звать» и «кто пойдёт» были одним условием, отказов не было вовсе —
        # вожак звал ровно тех, кто и так шёл. К союзнику жертвы он при этом
        # не пойдёт: дом маленький, и кто с кем держится, слышали все
        надежда = (trust >= b["зов_надежда_доверие"]
                   and p.hate.get(leader.id, 0.0) < b["зов_надежда_ненависть"]
                   and target.id not in p.allies)
        if not (причина or надежда):
            continue
        # к тому, кто уже отказал, второй раз в ту же дверь не стучатся:
        # звать человека, который на днях сказал «нет», значит признать,
        # что идти больше не с кем
        if h.day - leader.ask_record(p.id).get("отказ_налёт", -99.0) < b["зов_обида_дней"]:
            continue

        pull = p.desperation() * 2.0 + p.вес_черт("налёт_вербовка") + trust * 0.25
        pull += p.hate.get(target.id, 0.0) / 30.0
        pull += 1.0 if ядро else 0.0
        pull -= 1.2 if target.id in p.allies else 0.0
        pull -= 1.5
        # к страшной двери не идут даже за компанию…
        pull -= p.боится(target.id) * b["страх_вес_вербовки"]
        # …а страшному вожаку труднее отказать
        pull += p.боится(leader.id) * b["страх_вес_вербовки"] * 0.4
        pull += p.пунктик("налёт_вербовка")
        # и своя мерка того, что ему предлагают (GDD 12.1)
        from .actions import своя_мерка
        pull += своя_мерка(p, "налёт", b)
        # ворота нормальности — те же, что у вожака (consider_raid), и до сих пор
        # их у идущих следом не было вовсе: НОРМА["налёт"] = 1.25 стояла в коде
        # и применялась только к тому, кто позвал. Оттого отказов не случалось
        # ни одного за сорок жизней — человек, у которого жизнь ещё похожа
        # на обычную, шёл ломать чужую дверь ночью просто потому, что позвали
        порог = (b["налёт_порог_вербовки"] / A
                 + p.normalcy * b["налёт_вербовка_нормальность"])
        if pull > порог:
            crew.append(p)
        else:
            отказали.append(p)
    return crew, отказали


def зов(h, leader, target, отказали):
    """Что остаётся в доме после того, как вожак обошёл соседей.

    До сих пор вербовка была молчаливым фильтром: человеку предлагали пойти
    ломать соседскую дверь, он отказывался, и не происходило ничего — вожак
    не помнил, отказавшийся не шёл предупредить, жертва не узнавала, что за
    ней собирались. Это был самый дешёвый неиспользованный источник в модели,
    и даёт он сразу и вражду, и доверие.

    Возвращает того, кто предупредил жертву, или None.
    """
    b = h.B
    if отказали:
        h.bump("зовов_отказано", len(отказали))
    предупредил = None
    for p in отказали:
        # позвали — значит, рассказали: и про то, что за той дверью что-то
        # есть, и про то, что в доме уже собирают людей
        social.adjust(p, target.id, aware=b["зов_осведомлённость"])
        social.add_panic(p, b["зов_паника"])
        # и показали, что так теперь можно. Это не увиденный поступок,
        # а предложенный, и весит он меньше — но по той же шкале (GDD 12.4)
        social.видел(h, p, b["нормальность_за_зов"], кто=leader)
        # вожак помнит отказ. Не ненависть — обида: доверие вниз, и звать
        # этого человека он какое-то время больше не станет
        social.adjust(leader, p.id, trust=b["зов_отказ_доверие"])
        leader.ask_record(p.id)["отказ_налёт"] = h.day
        p.bump("отказов_идти")
        # и может пойти предупредить. Это единственный источник доверия
        # помимо помощи и лечения, который стоит дороже них обоих
        if предупредил is not None:
            continue
        # предупреждают не потому, что любят соседа, а потому, что в доме
        # собирают людей идти ночью к чужой двери, и это ещё не стало обычным
        # делом. Ненависть мешает, но не решает: к третьей неделе в этом доме
        # ненавидят все и всех, и если считать по ней, предупреждать некому
        хочу = (p.t01("лояльность") * 4.5
                + p.normalcy * b["предупредить_нормальность"]
                + p.trust.get(target.id, 3.0) * 0.5
                - p.hate.get(target.id, 0.0) / 40.0
                # страх держит: сказать вожаку «нет» можно, а пойти к жертве
                # за его спиной — это уже против него
                - p.боится(leader.id) * b["страх_вес_предупреждения"]
                - b["предупредить_решимость"])
        if target.id in p.allies:
            хочу += 3.0
        if p.dependents:
            хочу -= 1.0        # ему есть что терять, если узнают
        if хочу > 0 and h.rng.chance(clamp(хочу / 3.5, 0.05, 0.85)):
            предупредил = p
    if предупредил is None:
        return None

    h.bump("предупреждений")
    h.journal.line(f"{предупредил.short} {vb(предупредил.sex, 'успел')} шепнуть "
                   f"{target.form('dat')}, что за {target.form('ins')} сегодня придут.", 2)
    h.note(f"{предупредил.short} предупредил {target.form('acc')}")
    social.adjust(target, предупредил.id, trust=b["доверие_за_предупреждение"], hate=-15)
    social.adjust(предупредил, target.id, trust=0.5)
    target.panic = clamp(target.panic + b["паника_от_предупреждения"])
    # и дом наутро берётся за двери: не потому, что случилось, а потому,
    # что стало известно, чем тут теперь занимаются
    h.mods["укрепление_порыв"] = h.day + 1
    # слух идёт дальше жертвы: вожак теперь тот, кто собирает людей
    for w in h.others(leader):
        if w.id == предупредил.id:
            continue
        if h.rng.chance(b["зов_слух"]):
            social.adjust(w, leader.id, aware=6)
            social.испугался(h, w, leader, b["страх_за_сговор"])
    return предупредил


# ---------------------------------------------------------------- чем ещё войти

def инструменты(crew):
    """«Прочность двери против инструментов группы» (GDD 16).

    Инструменты — это не только руки: топор и слесарь в компании решают
    больше, чем лишний человек.
    """
    def вес(p):
        w = 0.75 + p.t01("храбрость") * 0.6
        if p.weapon == "топор" or "слесарь" in p.skills:
            w += 1.2
        elif p.weapon in ("дубина", "нож"):
            w += 0.55
        return w
    return sum(вес(p) for p in crew) * (1.0 + 0.22 * (len(crew) - 1))


def упорство(h, crew, target):
    """Уйдут ли они от целой двери или полезут другим путём (GDD 16).

    Решает не злость, а нужда: человеку, которому нечего есть, некуда идти
    от этой двери — дома его ждёт то же самое, только без еды. Он полезет
    в окно на четвёртом этаже; сытый в такое окно не полезет.

    До сих пор осада кончалась на первом же броске за дверь, и «дверь
    выдержала» означало «все разошлись» — независимо от того, кто пришёл
    и почему. Четверть всех осад в доме упиралась ровно в этот бросок.
    """
    b = h.B
    нужда = max(p.desperation() for p in crew)
    злость = max(p.hate.get(target.id, 0.0) for p in crew) / 100.0
    v = нужда * b["налёт_упорство_за_нужду"] + злость * b["налёт_упорство_за_злость"]
    v += (len(crew) - 1) * b["налёт_упорство_за_людей"]
    # и то, кого они слышат за дверью: от знакомого ружья уходят
    v -= max(p.боится(target.id) for p in crew) * b["налёт_упорство_за_страх"]
    return v


def _площадка(h, crew, flat, где):
    """Откуда ломать: своя квартира или пустая (GDD 16, стадия стен).

    Стену и перекрытие ломают, стоя в другой квартире, и это настоящее
    ограничение, а не бросок кубика: к Лиде на третьем спускаются сверху,
    потому что над ней кв.14 и кв.16, — а к Игорю на первом снизу не придёт
    никто, там подвал.
    """
    свои = {p.apt for p in crew} | {h.where(p).apt for p in crew}
    занято = h.занятые()
    for f in h.соседние(flat, где):
        if f.apt in свои or f.apt not in занято:
            return f
    return None


def пути_внутрь(h, crew, target, defenders, упор=0.0):
    """Чем ещё можно войти, если дверь выдержала (GDD 16).

    Список (вид, шанс, охота, площадка, откуда), отсортированный по охоте.
    Окно зависит от погоды и этажа, перекрытия — от того, есть ли рядом
    пустая квартира и хватает ли людей на объединённый рейд.
    """
    b = h.B
    flat = h.flats[target.apt]
    сила = clamp(0.5 + инструменты(crew) * b["пролом_за_инструменты"], 0.3, 1.6)
    арматура = 1.0 + b["стены_за_уровень"] * flat.shelter.get("стены", 0)
    за_жильём = хочу_его_квартиру(h, crew[0], target) > 0
    сырые = []

    # окно: со двора, с крыши или по балконам — и только если погода пускает.
    # В буран на карниз не выйдет никто, и на тридцатиградусном морозе тоже:
    # пальцы белеют раньше, чем поддастся рама
    if h.mods.get("режим") != "буран" and h.outside > b["окно_мороз"]:
        верх = max(f.floor for f in h.flats.values())
        откуда = ("низ" if flat.floor <= 1 else
                  "крыша" if flat.floor >= верх else "балкон")
        шанс = b["пролом_шанс"]["окно"] * сила * b["окно_доступ"][откуда]
        # заколоченные окна — та самая двойная выгода утепления (GDD 13:
        # тепло и тишина; теперь ещё и створка, которую снаружи не выставить).
        # Делитель, а не вычет: четвёртый слой утепления должен окно защищать,
        # а не закрывать эту стадию вовсе — с вычетом шанс уходил в ноль
        # у всех, к кому вообще ходят с ломом
        шанс /= 1.0 + b["окно_за_утепление"] * flat.shelter.get("утепление", 0)
        шанс -= max(0, len(defenders) - 1) * b["окно_за_защитника"]
        сырые.append(("окно", шанс, None, откуда))

    # стена и перекрытия — объединённым рейдом, трое и больше (GDD 16).
    # Арматура по стене и потолку (убежище 4 уровня) — единственная защита
    # от этой стадии, и ради неё её и строят.
    #
    # Или в одиночку, если терять уже нечего: трое ломают стену расчётливо,
    # а один — от отчаяния, и это ровно то же самое отчаяние, из-за которого
    # он вообще не ушёл от целой двери
    if len(crew) >= b["стены_нужно_людей"] or упор >= b["налёт_упорство_отчаяние"]:
        for где in ("стена", "потолок", "пол"):
            площадка = _площадка(h, crew, flat, где)
            if площадка is not None:
                сырые.append((где, b["пролом_шанс"][где] * сила / арматура, площадка, None))

    готовые = []
    for вид, шанс, площадка, откуда in сырые:
        шанс = clamp(шанс, 0.0, 0.95)
        охота = шанс
        if за_жильём:
            # ломая, портишь ровно то, ради чего пришёл (GDD 16): за квартирой
            # лезут через стену, а не в окно — выбитая рама в метель дороже
            охота -= b["дыра_градусов"][вид] * b["пролом_бережёт_приз"]
        готовые.append((вид, шанс, охота, площадка, откуда))
    готовые.sort(key=lambda x: -x[2])
    return готовые


def пролом(h, flat, вид, площадка=None):
    """Дыра остаётся в стенах — и сразу в двух квартирах, если это перекрытие.

    В этом вся цена такой победы: в пролом дует, за ним больше не спрятаться,
    и в следующий раз через него войдут уже без всякой осады.
    """
    flat.дыры[вид] = flat.дыры.get(вид, 0) + 1
    flat.открыта = True
    flat.вложено = max(0.0, flat.вложено - 2.0)
    if вид == "окно" and flat.shelter.get("утепление", 0) > 0:
        flat.shelter["утепление"] -= 1
    if площадка is not None:
        обратный = {"стена": "стена", "потолок": "пол", "пол": "потолок"}[вид]
        площадка.дыры[обратный] = площадка.дыры.get(обратный, 0) + 1
        площадка.открыта = True
    h.bump("проломов")
    h.bump("пролом_" + вид)


ОКНО_ТЕКСТ = {
    "низ": "Тогда выставили окно со двора и влезли внутрь.",
    "крыша": "Тогда спустились с крыши на верёвке и выдавили раму.",
    "балкон": "Тогда прошли по балконам от соседнего окна и выставили створку.",
}


def _войти_иначе(h, crew, target, defenders):
    """Дверь выдержала: разойтись или искать другой путь (GDD 16).

    Возвращает (вид пролома или None, что успели попробовать).
    """
    b = h.B
    упор = упорство(h, crew, target)
    if упор < b["налёт_упорство_порог"]:
        return None, []
    # чем отчаяннее группа, тем больше способов она успевает попробовать
    попыток = 1 + (1 if упор >= b["налёт_упорство_порог"] * 2 else 0)
    flat = h.flats[target.apt]
    пробовали = []
    for вид, шанс, _охота, площадка, откуда in пути_внутрь(
            h, crew, target, defenders, упор)[:попыток]:
        пробовали.append(вид)
        social.emit(h, target, 3 if вид == "окно" else 5,
                    "взлом" if вид == "окно" else "пролом", night=True)
        if not h.rng.chance(шанс):
            continue
        пролом(h, flat, вид, площадка)
        if вид == "окно":
            h.journal.line(f"Дверь кв.{target.apt} выдержала. {ОКНО_ТЕКСТ[откуда]}", 2)
        elif вид == "стена":
            h.journal.line(f"Дверь кв.{target.apt} выдержала — тогда пробили стену "
                           f"из кв.{площадка.apt}.", 2)
        elif вид == "потолок":
            h.journal.line(f"Дверь кв.{target.apt} выдержала. Разобрали пол "
                           f"в кв.{площадка.apt} и спустились сверху.", 2)
        else:
            h.journal.line(f"Дверь кв.{target.apt} выдержала. Вскрыли перекрытие "
                           f"снизу, из кв.{площадка.apt}.", 2)
        # ломать пришлось из собственных стен — и дыра осталась в них тоже
        свой = h.чей(площадка) if площадка is not None else None
        if свой is not None and свой in crew:
            h.journal.line(f"   Ломали из своей же кв.{площадка.apt}. "
                           f"Теперь дует и там.", 1)
        return вид, пробовали
    return None, пробовали


def дежурный_услышал(h, crew, target):
    """Кто из дежурных услышал их на лестнице и поднял дом. Или None.

    Услышать проще, чем решиться: на площадке трое с ломом, и они не крадутся.
    Мешает погода — в буран ветер глушит всё (GDD 21), — и мешает собственная
    усталость. Не мешает ничего больше: это и есть смысл не спать.
    """
    b = h.B
    дежурные = [p for p in h.alive()
                if p.tonight == "дежурить" and p.id not in {c.id for c in crew}
                and p.id != target.id]
    if not дежурные:
        return None
    # слышит тот, кто ближе к чужой двери: этажи считаются как для шума
    дежурные.sort(key=lambda p: (h.floor_gap(p, target), p.id))
    кто = дежурные[0]
    шанс = b["дежурный_слышит"] * social.слышимость(h)
    шанс *= b["дежурный_слышит_этаж"] ** h.floor_gap(кто, target)
    шанс *= 1.0 + 0.05 * len(crew)          # чем больше пришло, тем громче
    if not h.rng.chance(clamp(шанс, 0.02, 0.95)):
        return None
    h.bump("дежурный_поднял_дом")
    кто.bump("поднял_дом")
    h.journal.line(f"{кто.short} не {vb(кто.sex, 'спал')} и {vb(кто.sex, 'услышал')} "
                   f"их на лестнице. {vb(кто.sex, 'Поднял')} весь подъезд.", 2)
    # и дом узнаёт состав не по слухам, а глазами
    for p in crew:
        social.adjust(кто, p.id, aware=25)
        for w in h.alive():
            if w.id not in {c.id for c in crew}:
                social.adjust(w, p.id, aware=10)
    return кто


def defenders_of(h, target, crew_ids, предупреждён=False, поднял=None):
    """Кто придёт на помощь (союзники и просто порядочные).

    `предупреждён` — жертву успели предупредить, и она обошла площадку сама:
    выходят и те, кто спросонья не вышел бы. `поднял` — дежурный, который
    услышал их на лестнице и поднял дом; он и сам стоит у двери.
    """
    d = [target]
    if поднял is not None and поднял.id not in crew_ids and поднял.id != target.id:
        d.append(поднял)
    # те, кто живёт в этой квартире, дерутся за неё по определению
    for p in h.others(target):
        if p.id in crew_ids or p in d:
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
        # на лестницу против того, кого боишься, не выходят даже за своего
        will -= max((p.боится(c) for c in crew_ids), default=0.0) * h.B["страх_вес_защиты"]
        if target.id in p.allies:
            will += 3.0
        if p.dependents:
            will -= 2.0
        порог = 6.5
        if предупреждён:
            порог -= h.B["предупреждён_порог_защиты"]
        if поднял is not None:
            порог -= h.B["дежурный_порог_защиты"]   # его будят, а не он решает
        if will > порог:
            d.append(p)
    return d


def run_siege(h, leader, target):
    """Осада по стадиям (GDD 16). Возвращает исход строкой."""
    b = h.B
    crew, отказали = recruit(h, leader, target)
    # вожак обошёл соседей, и это не проходит бесследно, кто бы что ни ответил
    предупредил = зов(h, leader, target, отказали)
    crew_ids = {p.id for p in crew}
    # состав читает обвязка покрытия: пока она звала recruit сама, зов
    # выполнялся дважды
    h.mods["состав_налёта"] = [p.id for p in crew]
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
    # с этой минуты между ними счёты, кем бы ни кончилась ночь у двери.
    # Тяжесть уточнится по исходу (см. конец функции): разошлись миром —
    # забудется за пару дней, вынесли квартиру — не забудется
    for p in crew:
        social.обидели(h, target, p, b["обида_за_осаду"])
    for p in crew:
        social.переступил(h, p, "налёт")
        # тот, кто в прошлый раз пообещал не приходить, пришёл снова
        social.нарушил(h, p, target, "не_делать", target.id)
    names = ", ".join(p.short for p in crew)
    social.register_incident(h, "налёт", f"НАЛЁТ. {names} — к двери кв.{target.apt} ({target.short}).")
    social.emit(h, target, 3, "ссора", night=True)

    # тот, кто не спит и слушает лестницу, слышит троих с ломом. До сих пор
    # дежурство читали ровно два места — шанс кражи и шанс убийства соседа, —
    # и 96 осад из 115 случались в ночь, когда кто-то в доме дежурил, ничего
    # при этом не меняя. Дежурный будит дом: это единственное, ради чего
    # общее дежурство (решение собрания, GDD 12) вообще имеет смысл
    поднял = дежурный_услышал(h, crew, target)

    # ---- стадия 1: предупреждение ----
    # предупреждённый успел обойти площадку сам: выходят к нему и те, кто
    # спросонья не вышел бы. Это и есть цена утечки для вожака
    defenders = defenders_of(h, target, crew_ids,
                             предупреждён=предупредил is not None, поднял=поднял)
    attack_power = sum(p.power() for p in crew)
    def_power = sum(p.power() for p in defenders) * (1.0 + 0.2 * target.shelter.get("дверь", 0))
    # у двери с оружием в руках привыкают к нему быстрее, чем за месяц ухода
    # за ним: страшно, но руки запоминают
    from .model import СВОЙСКОЕ
    for p in crew + defenders:
        if p.weapon and p.weapon != "нет":
            было = p.рука.get(p.weapon, СВОЙСКОЕ.get(p.weapon, 0.0))
            p.рука[p.weapon] = clamp(было + h.B["рука_за_бой"], 0.0, 1.0)
    outnumbered = attack_power > def_power * 1.25

    if len(defenders) > 1:
        h.journal.line(f"На лестницу вышли: {', '.join(p.short for p in defenders[1:])} — за {target.form('acc')}.", 2)
        for d in defenders[1:]:
            social.adjust(target, d.id, trust=b["доверие_за_защиту"])
            # и близость: выйти ночью на лестницу за человека — самое сильное
            # событие пары в этой игре, и до сих пор оно в историю пары
            # не попадало вовсе. Симметрично, как разговор и общая печка:
            # запоминают это оба
            social.сблизились(h, target, d, b["близость_за_защиту"])
            # выйти за человека к его двери, когда за ней стоят с ломом, —
            # самое дорогое, чем в этом доме гасят обиду
            social.загладил(h, d, target, b["обида_за_защиту"])

    # у двери есть выходы: откупиться, переубедить, драться
    pay_ok = target.stock.get("еда", 0) + target.stock.get("топливо", 0) >= 2
    fear = clamp(0.25 + (attack_power / max(0.5, def_power)) * 0.35 - target.t01("храбрость") * 0.5, 0.0, 0.95)
    talk = target.t01("общительность") * 0.5 + sum(p.trust.get(target.id, 3.0) for p in crew) / (len(crew) * 20.0)
    talk += 0.25 if any("медик" in p.skills for p in defenders) else 0.0
    if предупредил is not None:
        # он ждал их весь вечер и знает, что сказать: это не растерянный
        # человек за дверью, а человек, который успел придумать слова
        talk += b["предупреждён_уговор"]
        fear -= b["предупреждён_смелее"]

    # кого именно он слышит за дверью: перед знакомым топором откупаются
    # охотнее, чем перед незнакомой злостью (GDD 17)
    страшно = max((target.боится(p.id) for p in crew), default=0.0)
    готов_платить = clamp(0.45 / aggr(h) * (1.0 + страшно * b["страх_вес_откупа"]), 0.0, 0.95)
    if (pay_ok and (fear > 0.5 or target.t01("храбрость") < 0.45 or страшно > 0.35)
            and h.rng.chance(готов_платить)):
        moved = {}
        for p in crew:
            m = take_household(h, target, p, greed=b["откуп_доля"] / len(crew))
            for k, v in m.items():
                moved[k] = moved.get(k, 0) + v
        h.journal.line(f"{target.short} {'откупилась: отдала' if target.sex == 'ж' else 'откупился: отдал'} {_fmt(moved)}. Ушли.", 2)
        for p in crew:
            social.adjust(p, target.id, hate=-12)
            social.adjust(target, p.id, hate=30, trust=-3.0)
            # уговор у двери: «больше не придём». Слово, которое можно нарушить
            social.обещать(h, p, target, "не_делать", target.id)
        _house_learns(h, target, crew)
        # откупился — значит, всё-таки отдал: это уже не «постояли и ушли»
        for p in crew:
            social.обидели(h, target, p, b["обида_за_откуп"])
        h.bump("исход_откупился")
        return "откупился"

    if h.rng.chance(clamp(talk * 0.55 / aggr(h), 0.0, 0.6)):
        h.journal.line(f"{target.short} {vb(target.sex, 'говорил')} с ними через дверь. Постояли и разошлись.", 2)
        for p in crew:
            social.adjust(p, target.id, hate=-6)
            social.adjust(target, p.id, hate=20, trust=-2.0)
            social.обещать(h, p, target, "не_делать", target.id)
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
            social.увидел_оружие(h, None, shooter, свидетели=crew)
            for p in crew:
                social.adjust(p, shooter.id, hate=15, aware=10)
                p.panic = clamp(p.panic + 8)
            h.bump("исход_отбился")
            return "отбился"

    # чем больше пришло и чем тяжелее в руках, тем меньше значит дверь
    # (GDD 17: численное превосходство решает почти всё)
    tools = инструменты(crew)
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
        # и то, кого именно он слышит за дверью: от знакомого топора бегут
        уйти += max((target.боится(p.id) for p in crew), default=0.0) * b["страх_вес_побега"]
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
            # он ушёл через окно, а квартиру вынесли. Это то самое, что
            # не прощается и о чём вспоминают до конца метели
            for p in crew:
                social.обидели(h, target, p, b["обида_за_разграбление"], непрощаемо=True)
            h.bump("исход_сбежал")
            return "сбежал"
        # засада: тот, кто ждёт за дверью с топором, встречает первого вошедшего
        if not держит and target.weapon != "нет" and target.t01("храбрость") > 0.55 and h.rng.chance(b["засада_шанс"]):
            первый = h.rng.pick(crew)
            первый.injuries.append(h.rng.pick(["ушиб", "порез"]))
            первый.health = clamp(первый.health - h.rng.uni(10, 22))
            h.journal.line(f"{target.short} {vb(target.sex, 'ждал')} за дверью. "
                           f"{первый.short} {vb(первый.sex, 'получил')} первым.", 2)
            social.увидел_оружие(h, None, target, свидетели=crew)
            social.испугался(h, первый, target, b["страх_за_насилие"])
            for p in crew:
                p.panic = clamp(p.panic + 10)

    door_broken = not держит
    вошли = None
    if not door_broken:
        # ---- стадия «другой путь»: окно, стена, перекрытия (GDD 16) ----
        # Дверь выдержала — это ещё не конец осады. Человек, у которого дома
        # пусто, не уходит от чужой двери с пустыми руками: он идёт вокруг —
        # в окно, если пускает погода, или сквозь стену, если пришли втроём
        # и рядом стоит пустая квартира
        вошли, пробовали = _войти_иначе(h, crew, target, defenders)
        door_broken = вошли is not None
        if not door_broken:
            хвост = ""
            if пробовали:
                хвост = (" Лезли в окно — не вышло." if пробовали == ["окно"]
                         else " Пробовали и обойти — не вышло.")
            h.journal.line(f"Дверь кв.{target.apt} выдержала. Били долго, "
                           f"ушли под утро.{хвост}", 2)
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
    if вошли is None:
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
        # выносят и то, чем он мог бы ответить в другой раз: топор из угла,
        # ружьё над дверью. Отсюда и берётся то, чего в модели не было вовсе, —
        # осада, после которой человек беззащитен не на вечер, а до конца метели
        унёс = унести_оружие(h, target, leader, b["оружие_при_осаде"])
        if унёс:
            moved = dict(moved)
            moved[унёс] = 1
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
            on_death(h, target, killer=leader, свидетели=весь_дом(h))
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
            # выставили из собственных стен на мороз. Тяжелее этого в доме
            # только смерть, и это не прощается никогда
            for p in crew:
                social.обидели(h, target, p, b["обида_за_изгнание"], непрощаемо=True)
            exile(h, target, by=leader, reason="налёт")
            h.bump("исход_изгнан")
            return "изгнан"
        for p in crew:
            social.обидели(h, target, p, b["обида_за_разграбление"], непрощаемо=True)
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
        for p in crew:
            social.обидели(h, target, p, b["обида_за_изгнание"], непрощаемо=True)
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
            for p in crew:
                social.обидели(h, target, p, b["обида_за_разграбление"], непрощаемо=True)
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
            # и главное, чего у дома не было: он теперь их боится. Ненависть
            # оседает за неделю, страх — нет, и следующей ночью к этой компании
            # никто уже не пойдёт ни просить, ни красть
            social.испугался(h, p, c, b["страх_за_налёт"])
            social.увидел_оружие(h, p, c)
    # тому, к чьей двери приходили, страшнее всех
    for c in crew:
        social.испугался(h, target, c, b["страх_за_насилие"])
    # и отдельно — по личной мерке каждого (GDD 12.1)
    for c in crew:
        social.judge(h, c, "насилие", hate=8.0, trust=-0.6, witnesses=судьи)
