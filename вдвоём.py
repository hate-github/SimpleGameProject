# -*- coding: utf-8 -*-
"""Линейка к седьмому плану: что дом делает вместе, а что поодиночке.

Меряет то, чего не меряет ни одна из прежних линеек:

  1. отдых — сколько его, у кого и при какой беде (этап 3);
  2. совместные вылазки: сколько, с кем, и что они меняют (этап 1);
  3. крик о помощи и кто на него вышел (этап 2);
  4. ребёнок в чужой квартире и невозвраты (этап 4);
  5. контакты доброй воли против контактов расчёта (этап 7).

    python вдвоём.py            — 40 жизней
    python вдвоём.py 80
    python вдвоём.py 40 отдых_отступает=1.5
"""
import io, sys, collections, statistics as st

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from house import actions, conflict
from house.engine import Simulation

ЗЕРНО = [0]

ЗАМЫСЛЫ = []
ПАРНАЯ_СУДЬБА = []
ПАРЫ_ЗАМЫСЛА = collections.defaultdict(set)
ВИД = []                              # (действие, как человек выглядит) — этап 5
ЖЕРТВЫ = []                           # как выглядела жертва отъёма
ХОДЫ = collections.Counter()          # действие -> сколько раз за все жизни
ОТДЫХ = []                            # по записи на каждый отдых
СОН_ПО_ДНЯМ = collections.defaultdict(list)   # день -> все rest вечером
ПРИЧИНЫ = collections.Counter()
СОН_ПО_ЖИЗНЯМ = []                    # средний сон за всю жизнь, по прогону
ВЫЖИЛО = []
ПАРЫ = []                             # совместные вылазки
ОДИНОЧКИ = [0]
ДВОР = collections.Counter()          # (вдвоём?) -> отняли / не отняли
КРИКИ = []
ОСАДЫ = []
НОЧЁВКИ = []
СТАТ = collections.Counter()          # сумма h.stats по всем прогонам
БЛИЗОСТЬ = []                         # разброс близости в конце жизни
ДЕТИ = []                             # (умер ли ребёнок) по каждому ребёнку
КОНТАКТЫ = collections.defaultdict(list)   # вид -> список (свой, богат) цели

# к кому ходят по доброй воле, а к кому по расчёту (этап 7)
ДОБРАЯ_ВОЛЯ = {"разговор", "поделиться", "вернуть", "лечить", "позвать"}
РАСЧЁТ = {"наблюдение", "кража_днём", "отнять", "подкараулить", "подбросить"}

_gather = actions.gather
_execute = actions.execute
_outing = actions._outing

ХОДИЛИ_ВМЕСТЕ = set()                 # (зерно, кто, кто) — были общие вылазки
ДОВЕРИЕ = []                          # (зерно, кто, о_ком, доверие, близость)


def outing(h, npc, dur, м, спутник=None):
    if спутник is not None:
        ХОДИЛИ_ВМЕСТЕ.add((ЗЕРНО[0], npc.id, спутник.id))
        ХОДИЛИ_ВМЕСТЕ.add((ЗЕРНО[0], спутник.id, npc.id))
    return _outing(h, npc, dur, м, спутник)


actions._outing = outing

import house.замысел as замысел_м
_выбрать = замысел_м.выбрать


def выбрать(h, npc):
    было = npc.замысел
    r = _выбрать(h, npc)
    з = npc.замысел
    if з is not None and з is not было and з["вид"] == "вместе":
        ПАРЫ_ЗАМЫСЛА[ЗЕРНО[0]].add(tuple(sorted((npc.id, з["друг"]))))
    return r


замысел_м.выбрать = выбрать

ВЫХОД = []                            # (близость к жертве, вышел ли) по каждому соседу
_defenders = conflict.defenders_of


def defenders_of(h, target, crew_ids, предупреждён=False, поднял=None, крик=False):
    d = _defenders(h, target, crew_ids, предупреждён, поднял, крик)
    вышли = {p.id for p in d}
    for p in h.others(target):
        if p.id not in crew_ids:
            ВЫХОД.append((p.свой(target.id), p.id in вышли, крик))
    return d


conflict.defenders_of = defenders_of


def execute(h, npc, key, target):
    ХОДЫ[key] += 1
    if key == "отнять" and target is not None and getattr(target, "id", None):
        ЖЕРТВЫ.append(target.каким_кажусь())
    if key == "отдых":
        ОТДЫХ.append({
            "сон": npc.rest,
            "невмоготу": npc.невмоготу(),
            "отчаяние": npc.desperation(),
            "настроение": npc.mood,
            "день": h.day,
            "кто": (ЗЕРНО[0], npc.id, h.day),
            "поправимо": поправимо(h, npc),
        })
    if key in ("лестница", "попросить", "отнять"):
        ВИД.append((key, npc.каким_кажусь()))
    if key in ДОБРАЯ_ВОЛЯ or key in РАСЧЁТ:
        if target is not None and getattr(target, "id", None) and target.id != npc.id:
            вид = "добрая воля" if key in ДОБРАЯ_ВОЛЯ else "расчёт"
            КОНТАКТЫ[вид].append((npc.свой(target.id), богатство(h, target)))
    return _execute(h, npc, key, target)


def богатство(h, t):
    """Насколько цель богата по сравнению с домом: 0..1 по еде и топливу."""
    живые = [p for p in h.alive()]
    if not живые:
        return 0.0
    все = sorted(p.days_of("еда") + p.days_of("топливо") for p in живые)
    моё = t.days_of("еда") + t.days_of("топливо")
    ниже = sum(1 for x in все if x < моё)
    return ниже / max(1, len(все) - 1)


def поправимо(h, npc):
    """Есть ли у беды, которая на человеке, ответ руками. Только для замера:
    настоящая мерка живёт в actions.поправимая_беда."""
    f = getattr(actions, "поправимая_беда", None)
    return f(h, npc, h.B) if f else None


actions.execute = execute

_day = Simulation.one_day


ЭТА_ЖИЗНЬ = []


def one_day(self):
    r = _day(self)
    for p in self.h.alive():
        СОН_ПО_ДНЯМ[self.h.day].append(p.rest)
        ЭТА_ЖИЗНЬ.append(p.rest)
    return r


Simulation.one_day = one_day

N = 40
ПОДМЕНА = {}
for arg in sys.argv[1:]:
    if "=" in arg:
        k, v = arg.split("=", 1)
        ПОДМЕНА[k] = float(v)
    else:
        N = int(arg)

for seed in range(1, N + 1):
    ЗЕРНО[0] = seed
    sim = Simulation(seed=seed, days=30, verbosity=0, stream=io.StringIO(),
                     overrides=ПОДМЕНА or None)
    ЭТА_ЖИЗНЬ.clear()
    ДЕТИ.append(sum(len(p.дети) for p in sim.h.people.values()))
    h = sim.run()
    if ЭТА_ЖИЗНЬ:
        СОН_ПО_ЖИЗНЯМ.append(st.mean(ЭТА_ЖИЗНЬ))
    живые = [p for p in h.people.values() if p.alive and not p.exiled]
    ВЫЖИЛО.append(len(живые))
    for p in h.people.values():
        if (not p.alive or p.exiled) and not p.ушёл:
            ПРИЧИНЫ[(p.cause or "?").split(" (")[0]] += 1

    ПАРЫ.append(h.stats.get("вылазок_вдвоём", 0))
    ЗАМЫСЛЫ.append(h.stats.get("замысел_вместе", 0))
    for a, b_ in ПАРЫ_ЗАМЫСЛА.get(seed, set()):
        x, y = h.get(a), h.get(b_)
        if x is not None and y is not None:
            ПАРНАЯ_СУДЬБА.append((x.alive and not x.exiled, y.alive and not y.exiled))
    КРИКИ.append(h.stats.get("криков", 0))
    ОСАДЫ.append(h.stats.get("осад", 0) or h.stats.get("налётов", 0))
    НОЧЁВКИ.append(h.stats.get("ночей_у_чужих", 0))
    for a in h.people.values():
        for c in h.people.values():
            if a.id != c.id:
                ДОВЕРИЕ.append((seed, a.id, c.id, a.trust.get(c.id, 3.0),
                                a.близость.get(c.id, 0.0)))
    for k, v in h.stats.items():
        if isinstance(v, (int, float)):
            СТАТ[k] += v
    for a in h.people.values():
        for c in h.people.values():
            if a.id != c.id:
                БЛИЗОСТЬ.append(a.близость.get(c.id, 0.0))


def доля(a, b):
    return f"{100.0 * a / b:.1f}%" if b else "  —"


всего_ходов = sum(ХОДЫ.values())
print(f"═══ {N} жизней ═══")
print()
print("1. ЧЕМ ЗАНЯТ ДОМ")
for k, v in ХОДЫ.most_common(12):
    print(f"   {k:<16} {доля(v, всего_ходов):>7}   ({v})")
print()
print("2. ОТДЫХ: ПРИ КАКОЙ БЕДЕ ЛОЖАТСЯ")
if ОТДЫХ:
    n = len(ОТДЫХ)
    print(f"   всего отдыхов {n} — {доля(n, всего_ходов)} всех ходов")
    выспались = [x for x in ОТДЫХ if x["сон"] > 70]
    print(f"   при сне выше 70:        {доля(len(выспались), n)}")
    print(f"   при сне ниже 40:        {доля(sum(1 for x in ОТДЫХ if x['сон'] < 40), n)}")
    print(f"   при невмоготу > 0.5:    {доля(sum(1 for x in ОТДЫХ if x['невмоготу'] > 0.5), n)}")
    print(f"   при отчаянии > 0.5:     {доля(sum(1 for x in ОТДЫХ if x['отчаяние'] > 0.5), n)}")
    поправ = [x for x in ОТДЫХ if x["поправимо"] is not None]
    if поправ:
        п = [x for x in поправ if x["поправимо"] > 0.4]
        print(f"   при беде, поправимой руками: {доля(len(п), len(поправ))}")
    print(f"   медиана сна в минуту отдыха: {st.median([x['сон'] for x in ОТДЫХ]):.0f}")
    за_день = collections.Counter(x["кто"] for x in ОТДЫХ)
    расклад = collections.Counter(за_день.values())
    д = sum(расклад.values())
    print("   отдыхов за один день у одного человека: "
          + ", ".join(f"{k}×{доля(v, д)}" for k, v in sorted(расклад.items())))
print()
print("3. СОН: НЕ ОТНЯЛИ ЛИ ЕГО")
for д in (5, 10, 20, 30):
    v = СОН_ПО_ДНЯМ.get(д)
    if v:
        print(f"   день {д:>2}: медиана {st.median(v):>5.1f}, "
              f"ниже 30 у {доля(sum(1 for x in v if x < 30), len(v))}")
if СОН_ПО_ЖИЗНЯМ:
    import math
    м = st.mean(СОН_ПО_ЖИЗНЯМ)
    ci = 1.96 * st.stdev(СОН_ПО_ЖИЗНЯМ) / math.sqrt(len(СОН_ПО_ЖИЗНЯМ)) if len(СОН_ПО_ЖИЗНЯМ) > 1 else 0.0
    print(f"   средний сон по всем человеко-дням: {м:.2f} ± {ci:.2f}")
print(f"   смертей от истощения: {ПРИЧИНЫ.get('истощение', 0)} из {sum(ПРИЧИНЫ.values())}")
print()
print("4. ВДВОЁМ")
вдвоём_н, один_н = СТАТ["вылазок_вдвоём"], ХОДЫ["вылазка"]
print(f"   совместных вылазок за жизнь: {st.mean(ПАРЫ):.2f}; "
      f"одиночных {один_н / max(1, N):.2f} — вдвоём {доля(вдвоём_н, вдвоём_н + один_н)} всех ходок")
print(f"   звали {(ХОДЫ['позвать']) / max(1, N):.2f} раз за жизнь, "
      f"отказали в {доля(СТАТ['отказов_идти'], ХОДЫ['позвать'])}")
print(f"   отняли во дворе: у одиночек {доля(СТАТ['отнято_во_дворе'], один_н)}, "
      f"у пар {доля(СТАТ['отнято_во_дворе_вдвоём'], вдвоём_н)}")
print(f"   травм на вылазке: {СТАТ['травм_на_вылазке'] / max(1, N):.2f} за жизнь")
print(f"   близость: средняя {st.mean(БЛИЗОСТЬ):.2f}, разброс {st.pstdev(БЛИЗОСТЬ):.2f}")
print(f"   криков о помощи за жизнь:    {st.mean(КРИКИ):.2f}")
print(f"   ночей ребёнка у чужих:       {СТАТ['ночей_у_чужих'] / max(1, N):.2f}"
      f" (просили {(СТАТ['ночей_у_чужих'] + СТАТ['отказов_взять_ребёнка']) / max(1, N):.2f}, "
      f"отказали в {доля(СТАТ['отказов_взять_ребёнка'], СТАТ['ночей_у_чужих'] + СТАТ['отказов_взять_ребёнка'])})")
print(f"   не вернули утром: {доля(СТАТ['детей_не_вернули'], СТАТ['ночей_у_чужих'])}")
print()
print("4б. ДОВЕРИЕ У ТЕХ, КТО ХОДИЛ ВМЕСТЕ")
# репутация цели — среднее доверие к ней по всем, кто её знал; личное — то,
# что осталось сверх репутации. Считается порознь для пар, у которых были
# общие дела, и для тех, у кого их не было
репутация = collections.defaultdict(list)
for з, a, c, t, _б in ДОВЕРИЕ:
    репутация[(з, c)].append(t)
сред_реп = {k: st.mean(v) for k, v in репутация.items()}
вместе, порознь = [], []
бл_вместе, бл_порознь = [], []
for з, a, c, t, б in ДОВЕРИЕ:
    ост = t - сред_реп[(з, c)]
    (вместе if (з, a, c) in ХОДИЛИ_ВМЕСТЕ else порознь).append(ост)
    (бл_вместе if (з, a, c) in ХОДИЛИ_ВМЕСТЕ else бл_порознь).append(б)
if вместе:
    print(f"   пар с общей ходкой {len(вместе)}, без неё {len(порознь)}")
    print(f"   доверие сверх репутации: у ходивших вместе {st.mean(вместе):+.2f}, "
          f"у остальных {st.mean(порознь):+.2f}")
    print(f"   близость: у ходивших вместе {st.mean(бл_вместе):.2f}, "
          f"у остальных {st.mean(бл_порознь):.2f}")
else:
    print("   ни одной совместной ходки")
print()
print("4в. КРИК О ПОМОЩИ")
осад = СТАТ["налётов"]
print(f"   осад за жизнь {осад / max(1, N):.2f}; кричали в {доля(СТАТ['криков'], осад)}")
print(f"   на лестницу вышел хоть кто-то: {доля(СТАТ['осад_с_защитой'], осад)}; "
      f"защитников на осаду {СТАТ['защитников'] / max(1, осад):.2f}")
print(f"   налётчики ушли на крик: {доля(СТАТ['исход_ушли_на_крик'], осад)}")
if ВЫХОД:
    в = [б for б, вышел, _к in ВЫХОД if вышел]
    н = [б for б, вышел, _к in ВЫХОД if not вышел]
    print(f"   близость к жертве: у вышедших {st.mean(в) if в else 0:.2f} "
          f"({len(в)} чел.), у оставшихся за дверью {st.mean(н) if н else 0:.2f} ({len(н)})")
    с_криком = [вышел for _б, вышел, к in ВЫХОД if к]
    без = [вышел for _б, вышел, к in ВЫХОД if not к]
    print(f"   выходит один сосед из: на крик {доля(sum(с_криком), len(с_криком))}, "
          f"без крика {доля(sum(без), len(без))}")
print()
print("4г. СВОЁ ЛИЦО")
if ВИД:
    по_ключу = collections.defaultdict(list)
    for k, v in ВИД:
        по_ключу[k].append(v)
    for k, v in sorted(по_ключу.items()):
        print(f"   {k:<12} {len(v):>5} раз, выглядел на {st.mean(v):.2f}")
if ЖЕРТВЫ:
    print(f"   жертва отъёма выглядела на {st.mean(ЖЕРТВЫ):.2f} ({len(ЖЕРТВЫ)} случаев)")
print()
print("4д. ЗАМЫСЕЛ ВМЕСТЕ")
жизней_с = sum(1 for x in ЗАМЫСЛЫ if x)
print(f"   брали в {доля(жизней_с, N)} жизней; {СТАТ['замысел_вместе'] / max(1, N):.2f} раз за жизнь")
for k in ("тепло", "запас", "угол", "рассорить", "уйти", "вместе"):
    print(f"     {k:<10} {СТАТ['замысел_' + k] / max(1, N):.2f}")
if ПАРНАЯ_СУДЬБА:
    оба = sum(1 for a, b_ in ПАРНАЯ_СУДЬБА if a and b_)
    никто = sum(1 for a, b_ in ПАРНАЯ_СУДЬБА if not a and not b_)
    порознь = len(ПАРНАЯ_СУДЬБА) - оба - никто
    # с чем сравнивать: если бы судьбы двоих не были связаны вовсе, доля
    # «одинаково» вышла бы из одной только выживаемости дома
    p1 = st.mean(ВЫЖИЛО) / 6.0
    случайно = p1 * p1 + (1 - p1) * (1 - p1)
    print(f"   судьба пары: выжили оба {оба}, не выжил никто {никто}, "
          f"порознь {порознь} — из {len(ПАРНАЯ_СУДЬБА)} пар")
    print(f"   одинаково {доля(оба + никто, len(ПАРНАЯ_СУДЬБА))}, "
          f"а при несвязанных судьбах было бы {100 * случайно:.0f}%")
print()
print("5. К КОМУ ХОДЯТ")
for вид, v in sorted(КОНТАКТЫ.items()):
    if v:
        свои = st.mean(x for x, _ in v)
        богат = st.mean(y for _, y in v)
        print(f"   {вид:<12} {len(v):>5} контактов: близость цели {свои:.2f}, "
              f"место цели по достатку {богат:.2f}")
print()
print(f"6. ВЫЖИВАЕМОСТЬ {st.mean(ВЫЖИЛО):.2f}")
детей = sum(ДЕТИ)
print(f"   детей в доме {детей / max(1, N):.1f}; умерло {СТАТ['смертей_детей'] / max(1, N):.2f} "
      f"за жизнь — {доля(СТАТ['смертей_детей'], детей)} детей")
