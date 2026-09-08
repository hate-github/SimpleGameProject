# -*- coding: utf-8 -*-
"""Линейка к целям третьего плана — «своё и чужое» (ПЛАН.md).

    python своё.py            — 40 прогонов
    python своё.py 150

`замер.py` меряет первый план, `поводы.py` — второй; здесь третий: куда дом
ходит по дням, как живут кладовые, какими путями ходит ключ и когда в доме
появляется генератор.
"""
import io, sys, collections, statistics as st
sys.stdout.reconfigure(encoding="utf-8")
from house.engine import Simulation
from house.hooks import Хуки
from house.street import МЕСТА, чужое_место

ПО_ДНЯМ = collections.defaultdict(collections.Counter)
ВСЕГО = collections.Counter()
ДИКО = []
ПУСТА = collections.defaultdict(list)
ХОД = collections.Counter()
ВСКРЫТИЕ = []
ЗАКАЗ = collections.defaultdict(list)
С = collections.Counter()
ВЫЖИЛО = []

ЭТА = {}                     # за одну жизнь: день первой вылазки в чужое, когда опустела кладовая
_ЗАКАЗ_БЫЛ = {}


def выход(h, npc, dur, м, спутник=None):
    ПО_ДНЯМ[h.day][м.имя] += 1
    ВСЕГО[м.имя] += 1
    if чужое_место(м) and "дико" not in ЭТА:
        ЭТА["дико"] = h.day


def перед_делом(h, npc, key, target, детали=None):
    if key == "кладовая" and target is not None:
        ХОД[target.вид] += 1
        ПО_ДНЯМ[h.day]["кладовая"] += 1
    _ЗАКАЗ_БЫЛ[npc.id] = h.заказы.get(npc.id)


def после_дела(h, npc, key, target, итог=None, детали=None):
    стало = h.заказы.get(npc.id)
    if key == "заказать" and стало and стало is not _ЗАКАЗ_БЫЛ.get(npc.id):
        ЗАКАЗ[стало.что].append((h.day, стало.цена, h.power_on))


def конец_дня(h):
    for k, v in h.кладовые.items():
        if v.пусто() and k not in ЭТА.setdefault("_пусто", {}):
            ЭТА["_пусто"][k] = h.day


ХУКИ = Хуки(on_outing=[выход], on_execute=[перед_делом], after_execute=[после_дела],
            on_day=[конец_дня])

N = int(sys.argv[1]) if len(sys.argv) > 1 else 40
for seed in range(1, N + 1):
    ЭТА.clear()
    h = Simulation(seed=seed, days=30, verbosity=0, stream=io.StringIO(), hooks=ХУКИ).run()
    ДИКО.append(ЭТА.get("дико", 99))
    ВСКРЫТИЕ.append(h.stats.get("первое_вскрытие", 99))
    ВЫЖИЛО.append(len([p for p in h.people.values() if p.alive and not p.exiled]))
    for k, v in h.кладовые.items():
        ПУСТА[k].append(ЭТА.get("_пусто", {}).get(k, 99))
    for k in ("вскрытых_кладовых", "вскрытий_раскрыто", "вылазок_в_чужое",
              "ключей_от_кладовых_отнято", "ключей_от_кладовых_найдено",
              "ключей_от_кладовых_по_наследству", "ходок_в_кладовую",
              "происшествий_вскрытие", "тулупов_из_кладовой"):
        С[k] += h.stats.get(k, 0)

имена = [м.имя for м in МЕСТА]
print(f"═══ {N} жизней ═══")
print("1. КУДА ХОДЯТ")
print(f"{'день':>4} " + " ".join(f"{и[:14]:>15}" for и in имена) + f"{'кладовая':>10}{'дикое':>8}")
for д in range(1, 11):
    c = ПО_ДНЯМ[д]
    вылазок = sum(c[и] for и in имена)
    дик = sum(c[м.имя] for м in МЕСТА if чужое_место(м))
    print(f"{д:>4} " + " ".join(f"{c.get(и,0):>15}" for и in имена)
          + f"{c.get('кладовая',0):>10}{100*дик/max(1,вылазок):>7.0f}%")
всего = sum(ВСЕГО.values())
print(f"   вылазок {всего/N:.1f} за жизнь: "
      + ", ".join(f"{k} {100*v/всего:.0f}%" for k, v in ВСЕГО.most_common()))
ж = [d for d in ДИКО if d < 99]
print(f"   первая вылазка в чужое: медиана дня {st.median(ж):.0f} "
      f"({100*len(ж)/N:.0f}% жизней), всего {С['вылазок_в_чужое']/N:.1f} за жизнь")
print()
print("2. КЛАДОВЫЕ")
for k in sorted(ПУСТА):
    д = [x for x in ПУСТА[k] if x < 99]
    print(f"   {k:>9}: опустела в {100*len(д)/N:>3.0f}% жизней"
          + (f", медиана дня {st.median(д):.0f}" if д else ""))
print(f"   ходок {С['ходок_в_кладовую']/N:.1f} за жизнь "
      + ", ".join(f"{k} {100*v/max(1,sum(ХОД.values())):.0f}%" for k, v in ХОД.most_common()))
в = [d for d in ВСКРЫТИЕ if d < 99]
print(f"   вскрыто чужих: {С['вскрытых_кладовых']/N:.2f} за жизнь; "
      f"первое — медиана дня {st.median(в):.0f} ({100*len(в)/N:.0f}% жизней); "
      f"хозяин узнал вора в {С['вскрытий_раскрыто']} случаях")
print(f"   ключ сменил хозяина: отняли {С['ключей_от_кладовых_отнято']}, "
      f"нашли при разборе {С['ключей_от_кладовых_найдено']}, "
      f"достался со стенами {С['ключей_от_кладовых_по_наследству']}")
print()
print("3. ГЕНЕРАТОР И РАБОТА НА ЗАКАЗ")
for что in sorted(ЗАКАЗ):
    v = ЗАКАЗ[что]
    print(f"   {что:>13}: {len(v)/N:.2f} за жизнь, медиана дня {st.median(x[0] for x in v):>4.0f}, "
          f"цена {st.mean(x[1] for x in v):>5.1f}, при живом свете "
          f"{100*sum(1 for x in v if x[2])/len(v):>3.0f}%")
print()
print(f"ВЫЖИЛО {st.mean(ВЫЖИЛО):.2f}")
