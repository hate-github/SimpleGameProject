# -*- coding: utf-8 -*-
"""Линейка к целям второго плана — «поводы для вражды» (ПЛАН.md).

    python поводы.py            — 60 прогонов
    python поводы.py 150

`замер.py` меряет цели первого плана (ранние кражи, нормальность, слово,
собрания); здесь — шесть механик второго: ходит ли оружие по рукам, чем
кончается зов в налёт, слышит ли дежурный лестницу, сколько работы идёт
на заказ, откуда берётся злость между сожителями и чем в доме делятся.

Ничего не патчится насовсем: наблюдатели ставятся на время прогонов тем же
приёмом, что `checks.Coverage`.
"""
import io, sys, collections, inspect, statistics as st
sys.stdout.reconfigure(encoding="utf-8")
from house import social, conflict, actions
from house.engine import Simulation

ИСТ = collections.Counter(); ПАРЫ = collections.defaultdict(float)
ИСХОД = collections.Counter(); ПРЕД = collections.Counter(); ДЕЖ = collections.Counter()
СОСТАВ = collections.Counter(); С = collections.Counter()
ОРУЖИЕ = collections.Counter(); ЖИЗНЕЙ_С_ОРУЖИЕМ = 0

_adjust = social.adjust
def adjust(a, b_id, trust=0.0, hate=0.0, aware=0.0, страх=0.0):
    if hate > 0 and (a.living_with == b_id or b_id in a.guests):
        ИСТ[inspect.stack()[1].function] += hate
    return _adjust(a, b_id, trust=trust, hate=hate, aware=aware, страх=страх)
social.adjust = adjust

_siege = conflict.run_siege
def siege(h, leader, target):
    п = h.stats.get("предупреждений", 0); д = h.stats.get("дежурный_поднял_дом", 0)
    r = _siege(h, leader, target)
    ИСХОД[r] += 1
    СОСТАВ[min(4, len(h.mods.get("состав_налёта", [])))] += 1
    if h.stats.get("предупреждений", 0) > п: ПРЕД[r] += 1
    if h.stats.get("дежурный_поднял_дом", 0) > д: ДЕЖ[r] += 1
    return r
conflict.run_siege = siege

_day = Simulation.one_day
def one_day(self):
    r = _day(self); h = self.h
    for p in h.alive():
        if p.living_with:
            host = h.get(p.living_with)
            if host and host.alive:
                k = (h.seed_, p.id, host.id)
                ПАРЫ[k] = max(ПАРЫ[k], p.hate.get(host.id,0), host.hate.get(p.id,0))
    return r
Simulation.one_day = one_day

N = int(sys.argv[1]) if len(sys.argv)>1 else 60
for seed in range(1, N+1):
    s = Simulation(seed=seed, days=30, verbosity=0, stream=io.StringIO()); s.h.seed_=seed
    h = s.run()
    if h.stats.get("оружие_сменило_руки"): ЖИЗНЕЙ_С_ОРУЖИЕМ += 1
    for k in ("оружие_сменило_руки","оружия_отнято","оружия_вынесено","оружия_найдено",
              "зовов_отказано","предупреждений","дежурный_поднял_дом","налётов",
              "работ_на_заказ","лечений_за_плату","лечений_в_долг","съездов","выселений",
              "переездов","убийств_соседа","покушений_на_соседа","ненависть_от_тесноты"):
        С[k] += h.stats.get(k, 0)
    for k,v in h.stats.items():
        if isinstance(k,str) and (k.startswith("поделились_") or k.startswith("заказ_")):
            ОРУЖИЕ[k] += v
    for p in h.people.values():
        if p.stats.get("вооружался"): С["вооружался_" + p.short] += p.stats["вооружался"]

def хор(d):
    return 100.0*sum(d.get(k,0) for k in ("отбился","переубедил","откупился"))/max(1,sum(d.values()))
n = sum(ИСХОД.values()); всего_зла = sum(ИСТ.values())
print(f"════ {N} жизней ════")
print(f"1. ОРУЖИЕ: сменило руки в {100*ЖИЗНЕЙ_С_ОРУЖИЕМ/N:.0f}% жизней, "
      f"{С['оружие_сменило_руки']/N:.1f} раза за жизнь")
print(f"   отнято силой {С['оружия_отнято']}, вынесено {С['оружия_вынесено']}, "
      f"найдено на улице {С['оружия_найдено']}, остальное — из чужих стен")
print("   кто вооружался:", ", ".join(f"{k[11:]} {v}" for k,v in
      sorted(((k,v) for k,v in С.items() if k.startswith("вооружался_")), key=lambda x:-x[1])))
print(f"2. ЗОВ: отказов {С['зовов_отказано']} ({С['зовов_отказано']/N:.1f} за жизнь), "
      f"предупреждений {С['предупреждений']} — {100*С['предупреждений']/max(1,n):.0f}% осад")
print(f"3. ДЕЖУРНЫЙ поднял дом в {С['дежурный_поднял_дом']} осадах "
      f"({100*С['дежурный_поднял_дом']/max(1,n):.0f}%)")
print(f"   обошлось без прорыва: все {хор(ИСХОД):.0f}% | "
      f"предупреждённые {хор(ПРЕД):.0f}% | дом поднят {хор(ДЕЖ):.0f}%")
print("   состав осады 1/2/3/4+:", "/".join(str(СОСТАВ.get(i,0)) for i in (1,2,3,4)),
      f"— трое и больше {100*(СОСТАВ.get(3,0)+СОСТАВ.get(4,0))/max(1,n):.0f}%")
print(f"4. УСЛУГИ: работ на заказ {С['работ_на_заказ']} ({С['работ_на_заказ']/N:.1f} за жизнь), "
      + ", ".join(f"{k[6:]} {v}" for k,v in ОРУЖИЕ.items() if k.startswith("заказ_")))
пл, вд = С['лечений_за_плату'], С['лечений_в_долг']
print(f"   лечений за плату {пл}, в долг {вд}")
print(f"5. ТЕСНОТА: {100*ИСТ.get('_upkeep',0)/max(1,всего_зла):.0f}% всей злости между сожителями; "
      f"разъездов {С['съездов']+С['выселений']}, переездов {С['переездов']}, "
      f"убийств соседа {С['убийств_соседа']} (+{С['покушений_на_соседа']} покушений)")
v = list(ПАРЫ.values())
print(f"   медиана макс. ненависти в паре {st.median(v):.0f}, пар {len(v)}")
пд = {k[11:]: v for k,v in ОРУЖИЕ.items() if k.startswith("поделились_")}
всп = sum(пд.values())
print(f"6. ПОМОЩЬ: {всп} передач ({всп/N:.1f} за жизнь), "
      + ", ".join(f"{k} {100*v/max(1,всп):.0f}%" for k,v in sorted(пд.items(), key=lambda x:-x[1])))
