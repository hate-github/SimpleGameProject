# -*- coding: utf-8 -*-
"""Разбор поведения NPC: не «что дом делает», а «как он решает».

Меряет то, чего не меряет ни одна из четырёх линеек: сколько у человека
на самом деле выбора, насколько выбор близкий, насколько люди отличаются
друг от друга и насколько день похож на вчерашний.
"""
import io, sys, collections, statistics as st

sys.stdout.reconfigure(encoding="utf-8")
from house import actions
from house.engine import Simulation

ВАРИАНТОВ = []          # сколько вариантов прошло порог
ОТРЫВ = []              # (лучший - второй) / лучший
ДЕНЬ = collections.defaultdict(list)     # (зерно, id, день) -> список действий
ПО_ЛЮДЯМ = collections.defaultdict(collections.Counter)
ЦЕЛИ = collections.defaultdict(collections.Counter)   # id -> к кому ходил
ВЫБРАН_ЛУЧШИЙ = [0, 0]
ЗЕРНО = [0]
ОШИБКА = []          # |вид − правда|, усреднённая по трём полям
СПИСАН = []          # по одной записи на приговор «не жилец»: выжил ли он
ПОМОЩЬ_ПЛОХОМУ = collections.Counter()
ПОМОЩЬ_ВСЕГО = collections.Counter()
БЫТ = collections.defaultdict(collections.Counter)   # кто -> какой вариант быта
РУКА = []          # (привычно ли оружие, чьё оно было) в конце жизни

_gather = actions.gather
_choose = actions.choose_and_do
_execute = actions.execute


def gather(h, npc):
    opts = _gather(h, npc)
    прошли = sorted([s for _o, s in opts if s > h.B["порог_действия"]], reverse=True)
    ВАРИАНТОВ.append(len(прошли))
    if len(прошли) >= 2 and прошли[0] > 0:
        ОТРЫВ.append((прошли[0] - прошли[1]) / прошли[0])
    npc._последние = opts
    return opts


def execute(h, npc, key, target):
    opts = getattr(npc, "_последние", None)
    if opts:
        лучший = max(opts, key=lambda x: x[1])[0][0]
        ВЫБРАН_ЛУЧШИЙ[0 if key == лучший else 1] += 1
    if key == "поделиться" and target is not None and getattr(target, "id", None):
        когда = "рано" if h.day <= 5 else ("поздно" if h.day >= 20 else "середина")
        ПОМОЩЬ_ВСЕГО[когда] += 1
        if npc.плох(target.id) >= 0.45:
            ПОМОЩЬ_ПЛОХОМУ[когда] += 1
    ДЕНЬ[(ЗЕРНО[0], npc.id, h.day)].append(key)
    ПО_ЛЮДЯМ[npc.short][key] += 1
    if target is not None and getattr(target, "short", None):
        ЦЕЛИ[npc.short][target.short] += 1
    return _execute(h, npc, key, target)


actions.gather = gather
actions.execute = execute

_day = Simulation.one_day


def one_day(self):
    r = _day(self)
    h = self.h
    if h.day in (7, 14, 21, 28):
        for a in h.alive():
            for c in h.others(a):
                v = a.вид.get(c.id)
                if v:
                    ОШИБКА.append((abs(v.get("сыт", 80) - c.satiety)
                                   + abs(v.get("цел", 100) - c.health)
                                   + abs(v.get("тепло", 80) - c.warmth)) / 3.0)
    return r


Simulation.one_day = one_day

ПАРЫ_ДОВЕРИЕ = collections.defaultdict(list)   # (кто, о_ком) -> доверие в конце
ЦЕЛЬ_ДОВЕРИЕ = collections.defaultdict(list)   # о_ком -> все доверия
ПАРЫ_БЛИЗОСТЬ = collections.defaultdict(list)
ВЫРОСЛО = [0, 0, 0]
СОЮЗЫ = []
ПРИГОВОРОВ = []
ЛУЧШИЙ_ДРУГ = collections.Counter()            # (кто, кому) — с кем близость наибольшая

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
    старт = {(a.id, c.id): a.trust.get(c.id, 3.0)
             for a in sim.h.people.values() for c in sim.h.people.values() if a.id != c.id}
    h = sim.run()
    СОЮЗЫ.append(h.stats.get("союзов_заключено", 0))
    ПРИГОВОРОВ.append(h.stats.get("списан_со_счетов", 0))
    for a in h.people.values():
        близкие = []
        for c in h.people.values():
            if a.id == c.id:
                continue
            t = a.trust.get(c.id, 3.0)
            ПАРЫ_ДОВЕРИЕ[(a.short, c.short)].append(t)
            ЦЕЛЬ_ДОВЕРИЕ[c.short].append(t)
            ПАРЫ_БЛИЗОСТЬ[(a.short, c.short)].append(a.близость.get(c.id, 0.0))
            близкие.append((a.близость.get(c.id, 0.0), c.short))
            было = старт[(a.id, c.id)]
            ВЫРОСЛО[0 if t > было + 0.2 else (1 if t < было - 0.2 else 2)] += 1
        for c in h.people.values():
            if a.id != c.id and c.id in a.не_жилец:
                СПИСАН.append(not (c.alive and not c.exiled))
        if a.weapon and a.weapon != "нет":
            РУКА.append((a.short, a.weapon, a.привычка()))
        if близкие:
            лучший = max(близкие)
            if лучший[0] > 0.5:
                ЛУЧШИЙ_ДРУГ[(a.short, лучший[1])] += 1

def доля(a, b):
    return f"{100.0 * a / b:.0f}%" if b else "  —"


print(f"═══ {N} жизней ═══")
print()
print("1. СКОЛЬКО У ЧЕЛОВЕКА ВЫБОРА")
c = collections.Counter(ВАРИАНТОВ)
всего = sum(c.values())
print(f"   вариантов выше порога: медиана {st.median(ВАРИАНТОВ):.0f}, "
      f"среднее {st.mean(ВАРИАНТОВ):.1f}")
for k in range(0, 9):
    if c[k]:
        print(f"     {k:>2} вариантов: {100 * c[k] / всего:>5.1f}%")
print(f"     9 и больше: {100 * sum(v for k, v in c.items() if k >= 9) / всего:>5.1f}%")
print(f"   выбран НЕ лучший вариант: {100 * ВЫБРАН_ЛУЧШИЙ[1] / max(1, sum(ВЫБРАН_ЛУЧШИЙ)):.0f}% ходов")
if ОТРЫВ:
    print(f"   отрыв лучшего от второго: медиана {100 * st.median(ОТРЫВ):.0f}%, "
          f"в {100 * sum(1 for x in ОТРЫВ if x < 0.1) / len(ОТРЫВ):.0f}% ходов — меньше 10%")
print()
print("2. ПОХОЖ ЛИ ДЕНЬ НА ВЧЕРАШНИЙ")
пары = collections.Counter()
подряд = []
по_дням = collections.defaultdict(dict)
for (seed, pid, день), acts in ДЕНЬ.items():
    по_дням[(seed, pid)][день] = acts
for ключ, дни in по_дням.items():
    номера = sorted(дни)
    for a, b in zip(номера, номера[1:]):
        if b - a != 1:
            continue
        было, стало = set(дни[a]), set(дни[b])
        if было | стало:
            подряд.append(len(было & стало) / len(было | стало))
if подряд:
    print(f"   совпадение набора действий с вчерашним: медиана {100 * st.median(подряд):.0f}%")
    print(f"   дней, полностью повторяющих вчерашний: "
          f"{100 * sum(1 for x in подряд if x == 1.0) / len(подряд):.0f}%")
длины = [len(v) for v in ДЕНЬ.values()]
print(f"   действий за день: медиана {st.median(длины):.0f}, среднее {st.mean(длины):.1f}")
print()
print("3. ОТЛИЧАЮТСЯ ЛИ ЛЮДИ ДРУГ ОТ ДРУГА")
ключи = [k for k, _ in collections.Counter(
    {k: sum(c[k] for c in ПО_ЛЮДЯМ.values()) for k in
     {x for c in ПО_ЛЮДЯМ for x in ПО_ЛЮДЯМ[c]}}).most_common(10)]
print(f"   {'':<9}" + "".join(f"{k[:9]:>10}" for k in ключи))
доли = {}
for имя, c in sorted(ПО_ЛЮДЯМ.items()):
    s = sum(c.values())
    доли[имя] = [c[k] / s for k in ключи]
    print(f"   {имя:<9}" + "".join(f"{100 * c[k] / s:>9.1f}%" for k in ключи))
# расстояние между людьми: максимум |доля_a - доля_b| по всем действиям
имена = sorted(доли)
макс = 0.0
пара = None
for i, a in enumerate(имена):
    for b in имена[i + 1:]:
        d = sum(abs(x - y) for x, y in zip(доли[a], доли[b])) / 2
        if d > макс:
            макс, пара = d, (a, b)
print(f"   самые непохожие: {пара[0]} и {пара[1]} — расходятся на {100 * макс:.0f}% времени")
средн = []
for i, a in enumerate(имена):
    for b in имена[i + 1:]:
        средн.append(sum(abs(x - y) for x, y in zip(доли[a], доли[b])) / 2)
print(f"   в среднем двое расходятся на {100 * st.mean(средн):.0f}% времени")
# быт, отдых, еда и питьё занимают половину дня у всех и одинаково: это фон,
# а не характер. Настоящее различие видно на том, что человек делает СВЕРХ него
ФОН = {"быт", "отдых", "поесть", "попить", "топить"}
дела = {и: {k: v for k, v in c.items() if k not in ФОН} for и, c in ПО_ЛЮДЯМ.items()}
ключи_д = sorted({k for c in дела.values() for k in c})
доли_д = {и: [c.get(k, 0) / max(1, sum(c.values())) for k in ключи_д] for и, c in дела.items()}
имена_д = sorted(доли_д)
раз_д = [sum(abs(x - y) for x, y in zip(доли_д[a], доли_д[b])) / 2
         for i, a in enumerate(имена_д) for b in имена_д[i + 1:]]
print(f"   а если не считать фон (быт, отдых, еда, питьё, топка) — "
      f"на {100 * st.mean(раз_д):.0f}%, максимум {100 * max(раз_д):.0f}%")
print()
print("4. ДОВЕРИЕ: ОТНОШЕНИЕ ИЛИ РЕПУТАЦИЯ")
меж_целями = st.pvariance([st.mean(ЦЕЛЬ_ДОВЕРИЕ[k]) for k in ЦЕЛЬ_ДОВЕРИЕ])
остатки = [st.mean(v) - st.mean(ЦЕЛЬ_ДОВЕРИЕ[c]) for (a, c), v in ПАРЫ_ДОВЕРИЕ.items()]
меж_парами = st.pvariance(остатки)
d = меж_целями / max(1e-9, меж_целями + меж_парами)
print(f"   репутацией объясняется {100*d:.0f}%, личным отношением {100*(1-d):.0f}%")
n_ = sum(ВЫРОСЛО)
print(f"   доверие выросло у {100*ВЫРОСЛО[0]/n_:.0f}% пар, упало у {100*ВЫРОСЛО[1]/n_:.0f}%")
print(f"   союзов за жизнь {st.mean(СОЮЗЫ):.2f}")
print()
print("5. БЛИЗОСТЬ (0–10) — у кого с кем сложилось")
имена_б = sorted({a for a, _ in ПАРЫ_БЛИЗОСТЬ})
print(f"   {'':<9}" + "".join(f"{n[:8]:>9}" for n in имена_б))
for a in имена_б:
    print(f"   {a:<9}" + "".join(
        f"{st.mean(ПАРЫ_БЛИЗОСТЬ[(a, c)]) if (a, c) in ПАРЫ_БЛИЗОСТЬ else 0.0:>9.1f}"
        for c in имена_б))
все_б = [x for v in ПАРЫ_БЛИЗОСТЬ.values() for x in v]
print(f"   средняя {st.mean(все_б):.2f}, разброс {st.pstdev(все_б):.2f} — "
      f"важен разброс: без него все всем одинаково свои")
print("   чаще всего лучшим другом оказывается:")
for (a, c), n_д in ЛУЧШИЙ_ДРУГ.most_common(8):
    print(f"     {a:<8} → {c:<8} в {100*n_д/N:>3.0f}% жизней")
print()
print("6. РУКА: ПРИВЫЧНО ЛИ ОРУЖИЕ")
if РУКА:
    по_людям = collections.defaultdict(list)
    for имя, w, пр in РУКА:
        по_людям[(имя, w)].append(пр)
    for (имя, w), v in sorted(по_людям.items(), key=lambda x: -len(x[1]))[:8]:
        print(f"   {имя:<8} {w:<10} привычка {st.mean(v):.2f}  ({len(v)} жизней)")
    все_пр = [пр for _, _, пр in РУКА]
    свои = [x for x in все_пр if x > 0.85]
    print(f"   доля тех, у кого оружие своё: {100*len(свои)/len(все_пр):.0f}%; "
          f"средняя привычка {st.mean(все_пр):.2f}")
print()
print("7. ВЗГЛЯД: ЧТО ЧЕЛОВЕК ВИДИТ В СОСЕДЕ")
if ОШИБКА:
    print(f"   ошибка оценки: медиана {st.median(ОШИБКА):.0f} пунктов "
          f"(нужно 10–25: ноль — это телепатия, полсотни — шум)")
if СПИСАН:
    print(f"   «не жилец»: {st.mean(ПРИГОВОРОВ):.2f} приговоров за жизнь; "
          f"к концу висит {len(СПИСАН) / N:.1f} на дом, и в "
          f"{100 * (1 - sum(СПИСАН) / len(СПИСАН)):.0f}% из них осуждённый выжил")
else:
    print("   «не жилец» не ставится ни разу")
for когда in ("рано", "середина", "поздно"):
    в, п = ПОМОЩЬ_ВСЕГО[когда], ПОМОЩЬ_ПЛОХОМУ[когда]
    print(f"   помощь {когда:<9} всего {в:>4}, из них плохо выглядящему: {доля(п, в):>5}")
print()
print("8. К КОМУ ХОДЯТ (доля контактов, по строкам)")
люди = sorted(ЦЕЛИ)
print(f"   {'':<9}" + "".join(f"{n[:8]:>9}" for n in люди))
доли_строк = {}
for имя in люди:
    s_ = sum(ЦЕЛИ[имя].values())
    доли_строк[имя] = {n: ЦЕЛИ[имя][n] / max(1, s_) for n in люди}
    print(f"   {имя:<9}" + "".join(f"{100 * доли_строк[имя][n]:>8.0f}%" for n in люди))
# шесть одинаковых строк или шесть разных: среднее расхождение между строками,
# считая только чужие столбцы (диагональ — это он сам)
раз = []
for i, a in enumerate(люди):
    for b_ in люди[i + 1:]:
        общие = [n for n in люди if n not in (a, b_)]
        сумма_a = sum(доли_строк[a][n] for n in общие) or 1.0
        сумма_b = sum(доли_строк[b_][n] for n in общие) or 1.0
        раз.append(sum(abs(доли_строк[a][n] / сумма_a - доли_строк[b_][n] / сумма_b)
                       for n in общие) / 2)
print(f"   строки расходятся между собой на {100 * st.mean(раз):.0f}%  "
      f"(это и есть «есть ли у людей свои люди»)")
