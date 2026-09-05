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
ДЕТИ = []                             # (умер ли ребёнок) по каждому ребёнку
КОНТАКТЫ = collections.defaultdict(list)   # вид -> список (свой, богат) цели

# к кому ходят по доброй воле, а к кому по расчёту (этап 7)
ДОБРАЯ_ВОЛЯ = {"разговор", "поделиться", "вернуть", "лечить", "позвать"}
РАСЧЁТ = {"наблюдение", "кража_днём", "отнять", "подкараулить", "подбросить"}

_gather = actions.gather
_execute = actions.execute


def execute(h, npc, key, target):
    ХОДЫ[key] += 1
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
    h = sim.run()
    if ЭТА_ЖИЗНЬ:
        СОН_ПО_ЖИЗНЯМ.append(st.mean(ЭТА_ЖИЗНЬ))
    живые = [p for p in h.people.values() if p.alive and not p.exiled]
    ВЫЖИЛО.append(len(живые))
    for p in h.people.values():
        if (not p.alive or p.exiled) and not p.ушёл:
            ПРИЧИНЫ[(p.cause or "?").split(" (")[0]] += 1
        for д in p.дети:
            ДЕТИ.append(not д.get("жив", True))
    ПАРЫ.append(h.stats.get("вылазок_вдвоём", 0))
    КРИКИ.append(h.stats.get("криков", 0))
    ОСАДЫ.append(h.stats.get("осад", 0) or h.stats.get("налётов", 0))
    НОЧЁВКИ.append(h.stats.get("ночей_у_чужих", 0))


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
print(f"   совместных вылазок за жизнь: {st.mean(ПАРЫ):.2f}")
print(f"   криков о помощи за жизнь:    {st.mean(КРИКИ):.2f}")
print(f"   ночей ребёнка у чужих:       {st.mean(НОЧЁВКИ):.2f}")
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
print(f"   детская смертность {st.mean(ДЕТИ) if ДЕТИ else 0.0:.2f} "
      f"({sum(ДЕТИ)} из {len(ДЕТИ)})")
