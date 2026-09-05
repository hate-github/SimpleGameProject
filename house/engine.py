# -*- coding: utf-8 -*-
"""Сборка и главный цикл: день — ночь — расчёт.

День: у каждого 16 часов, он тратит их на действия (GDD 5).
Ночь: сон, дежурство, кражи и налёты (GDD 4.3 «Ночью происходит основной риск»).
Утро: сводка (GDD 4.4) — что заметили соседи, что пропало, кто что слышал.
"""
import json
import os

from .util import Rng, clamp, norm, vb
from .checks import _разделы as checks_разделы
from .model import NPC, House, Flat, Кладовая
from . import world, social, actions, conflict, report, meeting, замысел

DATA = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data")


def load_json(name):
    with open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


# Эффекты, которые умеет применять world.apply_effects. Всё остальное в events.json —
# опечатка, и лучше узнать о ней при запуске, чем не заметить никогда.
ЭФФЕКТЫ = {"паника", "настроение", "богатство", "связь", "температура", "опасность_вылазки",
           "укрепление_порыв", "болезнь_шанс", "кража_в_доме", "смерть_от_холода",
           "нормальность", "пункт_обогрева"}

# Условия событий, которые умеет проверять world.условие_верно. Условие,
# которого никто не понимает, тихо считалось бы невыполненным — а значит,
# событие никогда бы не случилось и никто бы этого не заметил.
УСЛОВИЯ = {"жив_с_умением", "нет_происшествий", "было_происшествие", "все_в_тепле",
           "была_смерть", "есть_раненый", "пусто_снаружи", "есть_пустая_квартира",
           "холоднее", "мало_живых", "есть_тело", "режим", "подъезд_открыт",
           "отключено", "работает", "до_дня", "деньги_ничего_не_стоят"}

ЧЕРТЫ = {"жадность", "храбрость", "лояльность", "общительность", "вспыльчивость",
         "сообразительность"}


def _проверить_условие(условие, где, bad):
    """Условие события или реплики: вид известен и поля на месте.

    Условие с опечаткой считается невыполненным и потому никогда не срабатывает
    молча — ровно тот случай, который в симуляции не заметить.
    """
    if isinstance(условие, list):
        for у in условие:
            _проверить_условие(у, где, bad)
        return
    вид = условие.get("вид")
    if вид not in УСЛОВИЯ:
        bad.append(f"{где}: условие «{вид}» никто не проверяет")
        return
    if вид in ("отключено", "работает") and условие.get("что") not in world.КОММУНАЛКИ:
        bad.append(f"{где}: «{вид}» без понятного «что» ({условие.get('что')})")
    if вид == "до_дня" and not isinstance(условие.get("день"), int):
        bad.append(f"{где}: «до_дня» без номера дня")


def validate_data(balance, npcs, events, lines=None):
    """Проверить данные при загрузке.

    Без этого опечатка в id тихо стирает стартовые отношения, дубль id стирает
    жильца, а несуществующий эффект события не делает ничего и никто не замечает.
    """
    from .model import WEAPONS
    from .actions import ПУНКТИК_КЛЮЧИ, ВЕСА_КЛЮЧИ, ЦЕННОСТИ
    bad = []

    # характер в данных: пунктики и веса черт. Опечатка здесь — это молча
    # ничего не делающее правило, то есть ровно тот случай, который в симуляции
    # никак иначе не заметить
    пунктики = balance.get("пунктики", {})
    for имя, сдвиги in пунктики.items():
        for ключ in сдвиги:
            if ключ not in ПУНКТИК_КЛЮЧИ:
                bad.append(f"пунктик «{имя}» сдвигает «{ключ}», а такого места в коде нет")
    for решение, веса in balance.get("веса_черт", {}).items():
        if решение not in ВЕСА_КЛЮЧИ:
            bad.append(f"веса_черт: решение «{решение}» никто не спрашивает")
        for черта in веса:
            if черта not in ЧЕРТЫ:
                bad.append(f"веса_черт[{решение}]: нет такой черты «{черта}»")

    ids = [d["id"] for d in npcs["жильцы"]]
    dupes = {i for i in ids if ids.count(i) > 1}
    if dupes:
        bad.append("повторяющиеся id жильцов: " + ", ".join(sorted(dupes)))
    known = set(ids)
    for d in npcs["жильцы"]:
        for field in ("доверие_старт", "ненависть_старт", "осведомлённость_старт"):
            for k in d.get(field, {}):
                if k not in known:
                    bad.append(f"{d['id']}.{field} ссылается на несуществующего «{k}»")
        missing = ЧЕРТЫ - set(d.get("черты", {}))
        if missing:
            bad.append(f"{d['id']}: нет черт {', '.join(sorted(missing))}")
        for t, v in d.get("черты", {}).items():
            if not (0 <= v <= 10):
                bad.append(f"{d['id']}: черта {t} = {v}, а должна быть 0..10")
        if d.get("оружие", "нет") not in WEAPONS:
            bad.append(f"{d['id']}: неизвестное оружие «{d.get('оружие')}»")
        пол = d.get("нормальность_пол", 0.1)
        if not (0.0 <= пол <= 0.9):
            bad.append(f"{d['id']}: нормальность_пол = {пол}, а должен быть 0..0.9")
        скорость = d.get("нормальность_скорость", 1.0)
        if not (0.2 <= скорость <= 3.0):
            bad.append(f"{d['id']}: нормальность_скорость = {скорость}, "
                       f"а должна быть 0.2..3.0")
        for п in d.get("пунктики", []):
            if п not in пунктики:
                bad.append(f"{d['id']}: пунктика «{п}» нет в balance.json")
        # ценности — те же данные, что и пунктики, и та же беда: метка,
        # которую никто не ставит поступкам и никто не судит, лежит в файле
        # и не делает ничего. Так у Игоря «ценю силу» и «не терплю высокомерие»
        # не срабатывали ни разу за сорок жизней, а проверка этого не видела
        for поле in ("ценит", "не_терпит"):
            for метка in (d.get("ценности") or {}).get(поле, []):
                if метка not in ЦЕННОСТИ:
                    bad.append(f"{d['id']}.ценности.{поле}: метку «{метка}» "
                               f"никто не ставит поступкам и никто не судит")
        # деньги: наличные лежат в запасах, счёт — отдельно (GDD 18)
        if float(d.get("счёт", 0.0)) < 0 or float(d.get("запасы", {}).get("деньги", 0)) < 0:
            bad.append(f"{d['id']}: деньги в минусе")

    apts = [d["кв"] for d in npcs["жильцы"]] + [f["кв"] for f in npcs.get("пустые_квартиры", [])]
    if len(apts) != len(set(apts)):
        bad.append("две квартиры с одним номером в npcs.json")

    # кладовые: по той же причине, по какой проверяются пунктики и ценности.
    # Опечатка в виде («пoгреб» с латинской «o») дала бы кладовку, в которую
    # никто никогда не сходит, и понять это по логу невозможно
    from .actions import КЛАДОВЫЕ_ВИДЫ
    кл_ids = [k.get("id") for k in npcs.get("кладовые", [])]
    if len(кл_ids) != len(set(кл_ids)):
        bad.append("две кладовые с одним id в npcs.json")
    for k in npcs.get("кладовые", []):
        имя = k.get("id") or "?"
        if k.get("вид") not in КЛАДОВЫЕ_ВИДЫ:
            bad.append(f"кладовая «{имя}»: вид «{k.get('вид')}» никто не понимает "
                       f"(известны: {', '.join(sorted(КЛАДОВЫЕ_ВИДЫ))})")
        if k.get("кв") not in apts:
            bad.append(f"кладовая «{имя}» приписана к кв.{k.get('кв')}, которой нет в доме")
        for w in k.get("оружие", []):
            if w not in WEAPONS:
                bad.append(f"кладовая «{имя}»: неизвестное оружие «{w}»")

    ids = [ev["id"] for ev in events.get("случайные", [])]
    повторы = {i for i in ids if ids.count(i) > 1}
    if повторы:
        bad.append("повторяющиеся id событий: " + ", ".join(sorted(повторы)))
    for group in ("скриптовые", "случайные"):
        for ev in events.get(group, []):
            имя = ev.get("id") or ("день " + str(ev.get("день")))
            for k in ev.get("эффекты", {}):
                if k not in ЭФФЕКТЫ:
                    bad.append(f"событие «{имя}»: эффект «{k}» никто не применяет")
            for поле in ("условие", "отменяется_если"):
                if ev.get(поле):
                    _проверить_условие(ev[поле], f"событие «{имя}»", bad)
            if group == "случайные" and not ev.get("окно"):
                bad.append(f"событие «{имя}»: нет окна дней")

    # реплики: та же проверка, что и у событий. Реплика с условием-опечаткой
    # просто исчезла бы из чата, и понять это по логу невозможно
    for раздел, варианты in checks_разделы(lines or {}):
        for i, в in enumerate(варианты):
            где = f"реплика {раздел}[{i}]"
            if isinstance(в, str):
                continue
            if not isinstance(в, dict) or not в.get("текст"):
                bad.append(f"{где}: ни строка, ни {{текст, условие}}")
                continue
            if в.get("условие"):
                _проверить_условие(в["условие"], где, bad)

    if bad:
        raise ValueError("Данные не в порядке:\n  · " + "\n  · ".join(bad))


class Simulation:
    def __init__(self, seed=1, days=30, verbosity=1, secrets=False, stream=None,
                 overrides=None):
        """overrides — {ключ: значение} поверх balance.json.

        Нужно, чтобы сравнивать две настройки одной ручки, не правя файл:
        без этого A/B по параметру технически невозможен.
        """
        self.seed = seed
        self.days = days
        self.balance = load_json("balance.json")
        if overrides:
            unknown = [k for k in overrides if k not in self.balance]
            if unknown:
                raise KeyError("нет такой ручки в balance.json: " + ", ".join(sorted(unknown)))
            self.balance.update(overrides)
        self.npcs_data = load_json("npcs.json")
        self.events = load_json("events.json")
        try:
            self.lines = load_json("lines.json")
        except FileNotFoundError:
            self.lines = {}
        validate_data(self.balance, self.npcs_data, self.events, self.lines)
        self.h = House(rng=Rng(seed), B=self.balance)
        self.h.mods["реплики_быт"] = self.lines.get("быт", [])
        self.h.journal = report.Journal(verbosity=verbosity, secrets=secrets, stream=stream)
        self._build()

    # ------------------------------------------------------------ сборка
    def _build(self):
        h = self.h
        for f in self.npcs_data.get("пустые_квартиры", []):
            h.flats[f["кв"]] = Flat(apt=f["кв"], floor=f["этаж"],
                                    shelter=dict(f.get("убежище", {})),
                                    stock=dict(f.get("запасы", {})))
        for d in self.npcs_data["жильцы"]:
            # квартира заводится вместе с жильцом, но принадлежит дому, а не ему:
            # он может её бросить, её могут занять, и всё, что он в неё вложил,
            # останется в стенах
            h.flats[d["кв"]] = Flat(apt=d["кв"], floor=d["этаж"],
                                    shelter=dict(d.get("убежище", {})),
                                    вложено=float(d.get("вложено", 3.0)))
        # погреба и гаражи — то же самое, только за порогом квартиры
        for k in self.npcs_data.get("кладовые", []):
            h.кладовые[k["id"]] = Кладовая(
                id=k["id"], вид=k["вид"], apt=k["кв"],
                stock=dict(k.get("запасы", {})),
                оружие=list(k.get("оружие", [])),
                тулуп=bool(k.get("тулуп", False)))
        for d in self.npcs_data["жильцы"]:
            p = NPC(
                id=d["id"], name=d["имя"], short=d["коротко"], apt=d["кв"], floor=d["этаж"],
                age=d["возраст"], role=d["роль"], sex=d.get("пол", "м"), skills=list(d.get("умения", [])),
                values=dict(d.get("ценности") or {}),
                пунктики=list(d.get("пунктики") or []),
                gen=d.get("коротко_род", ""), dat=d.get("коротко_дат", ""),
                acc=d.get("коротко_вин", ""), ins=d.get("коротко_твор", ""),
                traits=dict(d["черты"]), stock=dict(d["запасы"]),
                weapon=d.get("оружие", "нет"), одежда=int(d.get("одежда", 0)),
                счёт=float(d.get("счёт", 0.0)),
                dependents=d.get("иждивенцы", 0), dependent_name=d.get("иждивенец_имя", ""),
                dependent_acc=d.get("иждивенец_вин", ""),
                dependent_ins=d.get("иждивенец_твор", ""),
                нормальность_пол=float(d.get("нормальность_пол", 0.1)),
                нормальность_скорость=float(d.get("нормальность_скорость", 1.0)),
            )
            p._h = h
            # ключ от своего погреба или гаража. Дальше он ходит только вместе
            # с имуществом: через смерть, отъём, осаду и занятую квартиру
            p.ключи_кладовых = {k.id for k in h.кладовые.values() if k.apt == p.apt}
            for _ in range(p.dependents):
                p.дети.append({"имя": p.dependent_name or "ребёнок",
                               "вин": p.dependent_acc or p.dependent_name or "ребёнка",
                               "сытость": 85.0, "тепло": 80.0, "здоровье": 100.0,
                               "болен": None})
            h.people[p.id] = p
        # стартовые отношения
        for d in self.npcs_data["жильцы"]:
            p = h.people[d["id"]]
            # оружие, с которым человек вошёл в метель, ему привычно: оно
            # у него годами. Охотнику привычно любое огнестрельное — это его
            # ремесло, а не эта конкретная винтовка
            if p.weapon and p.weapon != "нет":
                p.рука[p.weapon] = 1.0
            if "охотник" in p.skills:
                from .model import FIREARMS
                for w in sorted(FIREARMS):
                    p.рука[w] = max(p.рука.get(w, 0.0), h.B["рука_охотника"])
            for other in h.people.values():
                if other.id == p.id:
                    continue
                p.trust[other.id] = 3.0
                p.hate[other.id] = 0.0
                p.страх[other.id] = 0.0
                p.aware[other.id] = 15.0 + (10.0 if other.floor == p.floor else 0.0)
                p.est[other.id] = {"еда": 3.0, "топливо": 3.0, "лекарства": 0.5, "материалы": 1.0}
            for k, v in d.get("доверие_старт", {}).items():
                p.trust[k] = float(v)
                # до метели эти двое уже были знакомы, и близость начинается
                # не с нуля. Оксана с Лидой (8 и 7) входят в метель парой,
                # Игорь с Петром (1 и 2) — никем друг другу; дальше это или
                # выдержит, или нет, но начальная несимметрия у дома есть
                p.близость[k] = float(v) * h.B["близость_старт_от_доверия"]
            for k, v in d.get("ненависть_старт", {}).items():
                p.hate[k] = float(v)
            for k, v in d.get("осведомлённость_старт", {}).items():
                p.aware[k] = float(v)

    # ------------------------------------------------------------ цикл
    def run(self):
        h = self.h
        world.build_calendar(h, self.events, self.days)
        for _ in range(self.days):
            self.one_day()
            if not h.alive():
                h.journal.line("В подъезде не осталось никого.", 2)
                h.journal.flush_day(h)
                break
        return h

    def one_day(self):
        h = self.h
        world.start_of_day(h, self.events)
        self._morning(h)
        self._day(h)
        report.daily_chat(h, self.lines)
        self._night(h)
        self._upkeep(h)
        social.проверить_обещания(h)
        social.alliance_check(h)
        social.update_groups(h)
        social.daily_decay(h)
        social.spread_panic(h)
        self._firsts(h)
        h.journal.сводка_дня(h)
        h.journal.flush_day(h)
        h.journal.panel(h)

    # ------------------------------------------------------------ утро
    def _morning(self, h):
        b_n = h.B
        h.mods["контакты"] = {}      # сколько раз кто к кому подходил сегодня
        h.mods["сделано"] = {}       # сколько раз кто повторил одно действие
        # самая громкая злость в доме — одним числом на день. Считается с утра
        # и не пересчитывается внутри дня: это то, с чем дом проснулся
        # (используется в social.напряжение_дома)
        h.mods["злость_дома"] = max((a.hate.get(c.id, 0.0) for a in h.alive()
                                     for c in h.others(a)), default=0.0)
        for p in h.alive():
            # чем дольше метель, тем меньше веры, что она кончится (GDD 12.3:
            # паника — это «вера, что скоро всё кончится»)
            p.horizon = min(h.B["горизонт_потолок"],
                            h.B["горизонт_старт"] + h.day * h.B["горизонт_за_день"]
                            + p.panic * h.B["горизонт_за_панику"]
                            + p.trait("жадность") * h.B["горизонт_за_жадность"])
            # запасливый считает вперёд с самого начала, не дожидаясь страха
            p.horizon = max(p.horizon, min(h.B["горизонт_потолок"],
                                           p.пунктик("горизонт_пол")))
            # нормальность — состояние, а не формула. Падает в момент события
            # (social.видел: отключение, происшествие, смерть, чужой пример,
            # свой первый крайний поступок), а здесь только два медленных
            # движения: слабый календарь и отрастание в тихие дни.
            #
            # Пока она считалась каждое утро заново по формуле, где главным
            # слагаемым был номер дня, к седьмому дню она была нулём у всех
            # сразу и двадцать три дня из тридцати дом вёл себя как один
            # человек. Личность весила ±0.1 и тонула.
            n = p.normalcy - b_n["нормальность_за_день"]
            n -= (p.panic / 100.0) * b_n["нормальность_за_панику"]
            if social.тихо_ли(h):
                # ложная надежда, из которой распад потом бьёт сильнее
                n += b_n["нормальность_отрастает"]
            p.normalcy = clamp(n, p.нормальность_пол, 1.0)
            # замысел решает своё до всего остального: спит он сегодня или нет,
            # не пора ли его бросить и не пора ли взяться за новый
            замысел.утро(h, p)
            p.time_left = h.B["часов_бодрствования"]
            p.burning = False
            p.away = False
            # без сброса в поле висело вчерашнее решение, и вор прикидывал
            # шанс кражи по позавчерашнему дежурству жертвы
            p.tonight = "спать"
            p.stats["часы_работы"] = 0
        # всё, что гость принёс за вчера, идёт к общей печке: квартира одна,
        # дрова у неё общие. Пока этого не было, гость копил топливо, которое
        # по правилу «печку топит хозяин» не мог сжечь никогда, и просил ещё
        for p in h.alive():
            if not p.living_with:
                continue
            host = h.get(p.living_with)
            дрова = p.stock.get("топливо", 0.0)
            if host and host.alive and not host.exiled and дрова > 0:
                host.stock["топливо"] = host.stock.get("топливо", 0.0) + дрова
                p.stock["топливо"] = 0.0
                h.journal.line(f"{p.short} {vb(p.sex, 'снёс')} дрова к печке "
                               f"{host.form('gen')} ({дрова:g}).", 0)

        # готовые заказы: мастер отдаёт печь и берёт своё (GDD 18)
        for p in h.alive():
            заказ = h.mods.get("заказ_" + p.id)
            if not заказ or h.day < заказ["готово"]:
                continue
            мастер = h.get(заказ["мастер"])
            if not (мастер and мастер.alive and not мастер.exiled):
                h.mods.pop("заказ_" + p.id, None)
                h.mods.pop("мастер_занят_" + заказ["мастер"], None)
                continue
            что = заказ.get("что", "буржуйка")
            у = actions.УСЛУГИ[что]
            нужно = h.B[у["мат"]]
            # доски отложены в угол у мастера ещё при уговоре: их не сожгли
            # и не пустили в свои окна. Если угол растащили — работает из своего
            склад = h.flats[мастер.apt]
            отложено = min(нужно, склад.stock.get("материалы", 0.0))
            if отложено >= нужно:
                склад.stock["материалы"] -= нужно
                мастер.stock["материалы"] = мастер.stock.get("материалы", 0.0) + нужно
            elif мастер.stock.get("материалы", 0) < нужно:
                continue                       # нет материала — заказ ждёт
            if что == "генератор":
                # движок ждал в том же углу. Если угол растащили — работа
                # стоит: собирать больше не из чего
                if склад.stock.get("движок", 0.0) < 1.0:
                    continue
                склад.stock["движок"] -= 1.0
                мастер.stock["движок"] = мастер.stock.get("движок", 0.0) + 1.0
            # цена — та, о которой договорились, включая доски мастера
            # (actions.цена_услуги). Пока доплата за материал прибавлялась
            # здесь, при сдаче, объявленная цена не совпадала с взятой
            цена = заказ.get("цена", h.B["печь_цена"])
            плата = {}
            for res in ("еда", "топливо"):
                берём = min(цена - sum(плата.values()), p.stock.get(res, 0.0))
                if берём > 0:
                    p.stock[res] -= берём
                    мастер.stock[res] = мастер.stock.get(res, 0.0) + берём
                    плата[res] = берём
            if sum(плата.values()) < цена * 0.5:
                continue                       # заказчику нечем платить — ждёт
            actions.spend(h, мастер, "материалы", нужно)
            if что == "генератор":
                actions.spend(h, мастер, "движок", 1.0)
            flat = h.where(p)
            if что == "буржуйка" or что == "генератор":
                flat.shelter[у["поле"]] = True
            else:
                flat.shelter[у["поле"]] = flat.shelter.get(у["поле"], 0) + 1
            flat.вложено += нужно
            h.mods.pop("заказ_" + p.id, None)
            h.mods.pop("мастер_занят_" + мастер.id, None)
            social.adjust(p, мастер.id, trust=2.0, hate=-10)
            social.adjust(мастер, p.id, trust=1.0)
            social.сдержал(h, мастер, p, "сделать", что)
            h.bump("работ_на_заказ")
            h.bump("заказ_" + что)
            h.journal.line(f"{мастер.short} {vb(мастер.sex, 'сделал')} {p.form('dat')} "
                           f"{actions.УСЛУГА_РАБОТА[что]}. Расплатились: "
                           + ", ".join(f"{k} {v:g}" for k, v in плата.items()) + ".", 2)
            h.note(f"{мастер.short}: работа {p.form('dat')} ({что})")

        # вышел ли вчерашний дежурный: перед домом, а не перед соседом
        meeting.проверить_дежурство(h)

        # сорванный замок в подвале хозяин видит не сразу: он туда не каждый
        # день ходит. Тем же путём, что и пропажа из квартиры, — наутро
        for вор_id, kid in h.mods.pop("вскрытые_кладовые", []):
            к = h.кладовые.get(kid)
            вор = h.get(вор_id)
            хозяин = h.хозяин_кладовой(к) if к is not None else None
            if not (к and вор and хозяин) or not (хозяин.alive and not хозяин.exiled):
                continue
            if not h.rng.chance(h.B["кладовая_шанс_заметить"]):
                continue
            h.journal.line(f"{хозяин.short} {vb(хозяин.sex, 'спустился')} к своей "
                           f"кладовке — замок сорван, дверь настежь.", 2)
            хозяин.mood = clamp(хозяин.mood - 12)
            хозяин.panic = clamp(хозяин.panic + 14)
            social.register_incident(h, "вскрытие", None)
            # кто это был, он знает не всегда: подвал общий, следов на бетоне
            # не остаётся. Это тот же вопрос, что и с ночной кражей
            if h.rng.chance(h.B["кладовая_шанс_узнать_вора"]):
                social.adjust(хозяин, вор.id, trust=-3.0,
                              hate=h.B["кладовая_вскрытие_ненависть"], aware=20)
                social.испугался(h, хозяин, вор, h.B["страх_за_насилие"] * 0.5)
                h.journal.line(f"   {хозяин.short} {vb(хозяин.sex, 'уверен')}, "
                               f"что это {вор.short}.", 2)
                h.bump("вскрытий_раскрыто")
            else:
                for p in h.alive():
                    if p.id != хозяин.id:
                        social.adjust(хозяин, p.id, hate=3.0, aware=5)

        # то, что за ночь оказалось под чужой дверью (house/замысел.py).
        # Дом видит вещь и хозяина двери, а того, кто её положил, не видит
        # никто: в этом весь смысл подброса
        for apt, что in list(h.mods.pop("подброшено", {}).items()):
            хозяин = h.чей(h.flats[apt])
            if хозяин is None or not (хозяин.alive and not хозяин.exiled):
                continue
            нашли = [w for w in h.alive()
                     if w.id != хозяин.id and w.id != что["кто"]
                     and h.rng.chance(b_n["подброс_заметность"]
                                      * (1.6 if h.floor_gap(w, хозяин) == 0 else 1.0))]
            if not нашли:
                continue
            for w in нашли:
                social.adjust(w, хозяин.id, hate=b_n["подброс_ненависть"],
                              trust=-b_n["подброс_доверие"], aware=12)
                social.испугался(h, w, хозяин, b_n["подброс_страх"])
                social.отдалились(w, хозяин.id, b_n["близость_за_обиду"])
            social.register_incident(h, "подброс", None, witnesses=нашли)
            вещь = "кусок мяса" if что["что"] == "мясо" else "чужая аптечка"
            h.journal.line(f"У двери кв.{apt} с утра лежал{'' if что['что'] == 'мясо' else 'а'} "
                           f"{вещь}. {', '.join(w.short for w in нашли)} "
                           f"{'видел' if len(нашли) == 1 else 'видели'} "
                           f"и {'сделал' if len(нашли) == 1 else 'сделали'} свои выводы.", 2)
            h.journal.secret(f"положил{'а' if h.get(что['кто']).sex == 'ж' else ''} это "
                             f"{h.get(что['кто']).short}.")
            h.note(f"под дверью кв.{apt} нашли {вещь}")
            h.bump("подбросов_сработало")

        # не пора ли дому сложить одно к одному про того, кто ведёт свою игру
        for p in h.alive():
            замысел.напор_виден(h, p)

        # обнаружение ночных пропаж (GDD 4.4 — сводка утром)
        losses = h.mods.pop("пропажи", [])
        for thief_id, victim_id in losses:
            victim = h.get(victim_id)
            if victim and victim.alive and h.rng.chance(h.B["кража_шанс_заметить_пропажу"]):
                conflict.notice_theft(h, victim, thief_id=thief_id)

    # ------------------------------------------------------------ день
    def _day(self, h):
        guard = 0
        while guard < 200:
            guard += 1
            acted = False
            for p in h.rng.shuffled(h.alive()):
                # список составлен в начале прохода, а за это время человека
                # могли выставить на мороз или убить — проверяем ещё раз
                if p.time_left < 0.3 or p.health <= 0 or not p.alive or p.exiled:
                    continue
                if actions.choose_and_do(h, p):
                    acted = True
            if not acted:
                break
        # ночью все дома: «хозяин ушёл» не должно перетекать в ночную кражу
        for p in h.alive():
            p.away = False

    # ------------------------------------------------------------ ночь
    def _night(self, h):
        targets = {}
        for p in h.alive():
            mode, target = self._decide_night(h, p)
            p.tonight = mode
            targets[p.id] = target

        # налёт — максимум один за ночь и не каждую ночь: после осады дом
        # несколько дней отходит (GDD 16 — рейд это событие, а не быт)
        raid_done = h.day - h.mods.get("последний_налёт", -99) < h.B["налёт_перерыв_дней"]
        # вожаком налёта становится самый злой и жадный, а не просто самый смелый
        for p in sorted(h.alive(), key=lambda x: -(x.trait("жадность") + x.trait("вспыльчивость")
                                                   + x.trait("храбрость") * 0.5 - x.trait("лояльность"))):
            if raid_done or not p.alive or p.exiled:
                continue
            t = conflict.consider_raid(h, p)
            if t:
                p.tonight = "налёт"
                conflict.run_siege(h, p, t)
                h.mods["последний_налёт"] = h.day
                raid_done = True

        # ночь в общей квартире. До краж: тот, кто на это решился, уже не пойдёт
        # никуда лезть, а дом наутро будет считать совсем другое
        for p in h.rng.shuffled(h.alive()):
            if p.tonight != "убить_соседа" or not p.alive or p.exiled:
                continue
            c = targets.get(p.id)
            if c and c.alive and not c.exiled and social.под_одной_крышей(h, p, c):
                conflict.убить_соседа(h, p, c)

        # кражи. Список составлен до осады, а осада могла кого-то из него убить
        # или выставить на мороз — поэтому проверяем обоих ещё раз
        for p in h.rng.shuffled(h.alive()):
            if p.tonight != "кража" or not p.alive or p.exiled:
                continue
            t = targets.get(p.id)
            if t and t.alive and not t.exiled:
                conflict.steal(h, p, t)

        # сон
        for p in h.alive():
            if p.rest < 35 and p.tonight == "дежурить":
                p.tonight = "спать"      # человек просто не выдерживает ещё одну ночь
            if p.rest < 25 and p.tonight in ("кража", "дежурить"):
                p.tonight = "спать"     # на ногах уже не стоит
            if p.tonight == "дежурить":
                slept = 4.5
                p.bump("ночей_дежурства")
                p.stats["дежурил_ночь"] = h.day
            elif p.tonight in ("кража", "налёт", "убить_соседа"):
                slept = 5.5
            else:
                slept = 11.0 if p.rest < 35 else (9.5 if p.rest < 60 else 8.0)
            p.slept = slept
            # обезвоженный спит хуже — это единственное, что GDD 6.1 обещает
            # жажде помимо самой смерти, и до сих пор этого не было
            качество = 1.0 - h.B["сон_за_жажду"] * (1.0 - norm(p.hydration, 15, 70))
            p.rest = clamp(p.rest + slept * h.B["сон_за_час"] * качество
                           - h.B["часов_бодрствования"] * h.B["бодрствование_за_час"])

        # и то, чего в доме до сих пор не могло случиться: смерть, которой
        # никто не заметил. Считается после сна, потому что угорают спящие
        world.угар(h)

    def _decide_night(self, h, p):
        b = h.B
        tired = 1.0 - norm(p.rest, 20, 80)
        opts = [(("спать", None), 2.5 + tired * 6.0)]

        gate = actions.norm_gate
        wealth = p.stock.get("еда", 0) * 0.6 + p.stock.get("топливо", 0) * 0.3
        watch = p.panic / 100.0 * 3.0 + social.recent_incidents(h) * 0.8 + wealth * 0.10 - tired * 6.0
        watch += p.stats.get("обокрали", 0) * 2.0
        watch += p.вес_черт("дежурить")
        watch += p.пунктик("дежурить")
        # своя ночь по общему расписанию: это уже не желание, а обязательство
        # перед всеми (см. meeting.проверить_дежурство)
        дежурный = meeting.чья_ночь(h)
        if дежурный is not None and дежурный.id == p.id:
            watch += b["дежурство_обязанность"]
            h.mods["дежурил_вчера"] = p.id
        # с ребёнком всю ночь на лестнице не просидишь: он просыпается,
        # мёрзнет и его надо держать при себе (GDD 12.6)
        if not p.dependents:
            opts.append((("дежурить", None), watch * gate(p, "дежурить", b)))

        # тот, с кем он делит комнату: ночью до него два метра и никакой двери
        соседи = ([h.get(p.living_with)] if p.living_with
                  else [h.get(g) for g in sorted(p.guests)])
        for c in соседи:
            if not (c and c.alive and not c.exiled):
                continue
            ночью = (conflict.оценка_убийства(h, p, c)
                     + p.пунктик("убить_соседа")
                     + actions.своя_мерка(p, "убить_соседа", b))
            opts.append((("убить_соседа", c), ночью * gate(p, "убить_соседа", b)))

        for t in h.others(p):
            if not t.alive:
                continue
            if t.living_with:
                continue           # его нет дома, он у соседа
            if social.под_одной_крышей(h, p, t):
                continue           # это тот, у чьей печки я сплю
            # сытый и незлой человек ночью не лезет к соседу
            A = conflict.aggr(h)
            if p.desperation() < 0.30 / A and p.hate.get(t.id, 0) < 25 / A:
                continue
            # сначала магазин, к соседу — потом. Пока человек верит, что на
            # улице ещё есть что взять, чужая дверь почти ничего не стоит
            greed = (p.loot_value(t.id) * (0.35 + p.t01("жадность") * 0.9)
                     * (1.0 - actions.есть_куда_сходить(h, p, завтра=True)))
            score = greed * (0.35 + p.desperation() * 1.3)
            # «я же только что принёс»
            if h.day - p.stats.get("день_вылазки", -99) <= 1:
                score -= b["кража_только_принёс"]
            # и то, что в доме уже неспокойно: происшествия, отказы, чужая злость
            score += social.напряжение_дома(h, p) * b["кража_за_напряжение"]
            score += p.вес_черт("кража")
            score += p.пунктик("кража") + actions.своя_мерка(p, "кража", b)
            score -= (1.0 - conflict.stealth(p)) * 3.5
            score -= t.shelter.get("дверь", 0) * 1.2
            score += p.hate.get(t.id, 0) / 22.0
            score -= 2.0 if t.id in p.allies else 0.0
            score -= t.power() * 0.6
            # сила — это расчёт, а страх — память: к тому, кто на его глазах
            # уже стрелял, ночью не идут, как бы ни было пусто в шкафу
            score -= p.боится(t.id) * b["страх_вес_кражи"]
            # человек прикидывает шансы: в укреплённую дверь при дежурстве не лезут
            chance = conflict.theft_chance(h, p, t, известно=False)
            if chance < b["кража_порог_шанса"] / A:
                continue
            score *= 0.45 + chance
            # страх последствий: обжёгся сам, видел, как за это убивали и выгоняли
            score -= p.stats.get("поймали", 0) * 1.8 / A
            score -= h.stats.get("убийств", 0) * 0.9 / A
            score -= h.stats.get("изгнаний", 0) * 1.1 / A
            opts.append((("кража", t), score * gate(p, "кража", b)))

        # тот же порог, что и днём (actions.choose_and_do): от нечего делать
        # человек не идёт ночью к чужой двери. Пока порога здесь не было,
        # мягкий выбор поднимал со дна то, что оценено почти в ноль, — а дешевле
        # всего «спать» стоит как раз в первую ночь метели, когда все выспались.
        # Сон остаётся всегда: это не одно из дел, а то, чем ночь кончается
        стоящие = [(o, s) for o, s in opts
                   if o[0] == "спать" or s > b["порог_действия"]]
        temp = b["температура_выбора"] + (p.panic / 100.0) * b["температура_выбора_паника"]
        return h.rng.softmax_pick(стоящие, temp)

    # ------------------------------------------------------------ расчёт суток
    def _upkeep(self, h):
        b = h.B
        # сколько всего в доме отключено: одно число на настроение и на панику
        infra = ((not h.heating) + (not h.water_on) + (not h.power_on) + (h.network <= 0))
        for p in list(h.alive()):
            work = p.stats.get("часы_работы", 0) or 0
            drain = b["расход_сытости"] + work * b["расход_сытости_за_час_работы"]
            if p.warmth < 40:
                drain += b["расход_сытости_на_холоде"]
            p.satiety = clamp(p.satiety - drain)
            p.hydration = clamp(p.hydration - b["расход_жажды"])

            # к оружию привыкают тем, что носят его: каждый день понемногу
            if p.weapon and p.weapon != "нет":
                from .model import СВОЙСКОЕ
                было = p.рука.get(p.weapon, СВОЙСКОЕ.get(p.weapon, 0.0))
                p.рука[p.weapon] = clamp(было + b["рука_за_день"], 0.0, 1.0)

            room = h.room_temp(p)
            p.warmth = clamp(p.warmth + (room - b["комфортная_температура"]) * b["тепло_за_градус"])

            comfort = (p.satiety + p.hydration + p.warmth + p.rest) / 4.0
            p.mood = clamp(p.mood + (comfort - 55) * b["настроение_от_комфорта"]
                           - p.panic * 0.035 + b["настроение_метель"])
            if p.stats.get("день_разговора") != h.day:
                p.mood = clamp(p.mood + b["настроение_одиночество"])

            # у настроения есть потолок, и он опускается вместе с домом.
            # Без него шкала была двоичной: прибавки от разговоров, отдыха и быта
            # прижимали её к сотне, и на десятый день метели — без отопления,
            # воды и света — медиана держалась на 92. Устроено ровно как дно
            # паники ниже: не мгновенный вычет, а уровень, к которому тянет.
            #
            # Причины разделены нарочно. Отключения и холод в комнате — это про
            # быт; темнота и отсутствие связи — про одиночество, и потому идут
            # отдельными слагаемыми (свой генератор от темноты спасает).
            потолок = 100.0 - infra * b["настроение_потолок_отключение"]
            потолок -= max(0.0, b["настроение_терпимо_градусов"] - room) * b["настроение_потолок_холод"]
            if not h.powered(p):
                потолок -= b["настроение_потолок_темнота"]
            if h.network <= 0:
                потолок -= b["настроение_потолок_без_связи"]
            потолок = clamp(потолок)
            if p.mood > потолок:
                p.mood = clamp(p.mood - (p.mood - потолок) * b["настроение_потолок_притяжение"])

            # теснота: нервы, чужой кашель и общий котёл
            room_mates = len(p.guests) + (1 if p.living_with else 0)
            if room_mates:
                p.mood = clamp(p.mood - b["теснота_настроение"] * room_mates)
                # sorted, а не list: порядок множества строк зависит от PYTHONHASHSEED,
                # а внутри цикла бросается кубик — иначе зерно перестаёт быть зерном
                for other_id in sorted(p.guests) + ([p.living_with] if p.living_with else []):
                    o = h.get(other_id)
                    if o and o.alive:
                        # спать в одной комнате — это и есть история пары,
                        # даже если за день не сказано ни слова. Тем и тяжелее
                        # потом выставить его на мороз
                        social.сблизились(h, p, o, b["близость_под_крышей"])
                        # и видят друг друга каждый день, вплотную: от того,
                        # как выглядит человек напротив, в одной комнате
                        # не спрячешься
                        social.встретились(h, p, o)
                        # у соседства своя злость, и она копится: ГДД 15 обещает
                        # «накопленную злость», из которой растут разъезд,
                        # изгнание гостя и нож ночью
                        зло = social.теснота(h, p, o, b)
                        social.adjust(p, o.id, trust=-b["теснота_доверие"], hate=зло)
                        h.stats["ненависть_от_тесноты"] = (
                            h.stats.get("ненависть_от_тесноты", 0.0) + зло)
                        # в одной комнате болезнь переходит почти наверняка
                        if o.sick and not p.sick and h.rng.chance(b["теснота_зараза"]):
                            p.sick = "простуда"
                            h.journal.line(f"{p.label()} слёг вслед за соседом по комнате.", 1)
                # соседи видят, что в одной квартире собрались несколько запасов
                for w in h.others(p):
                    social.adjust(w, p.id, aware=2.5 * room_mates)

            # --- ребёнок: своя шкала, и она короче (GDD 12.6) ---
            for р in list(p.дети):
                р["сытость"] = clamp(р["сытость"] - b["ребёнок_расход_сытости"])
                # та же комната, но ребёнок остывает быстрее взрослого
                р["тепло"] = clamp(р["тепло"] + (room - b["комфортная_температура"])
                                   * b["тепло_за_градус"] * b["ребёнок_мёрзнет"])
                урон = 0.0
                for v in (р["сытость"], р["тепло"]):
                    if v < b["критичный_порог"]:
                        урон += ((b["критичный_порог"] - v) * b["здоровье_за_критичное"]
                                 * b["ребёнок_хрупкость"])
                if р["болен"]:
                    урон += b["болезнь_урон_в_день"] * b["ребёнок_хрупкость"]
                if урон > 0:
                    р["здоровье"] = clamp(р["здоровье"] - урон)
                elif min(р["сытость"], р["тепло"]) > b["порог_восстановления"]:
                    р["здоровье"] = clamp(р["здоровье"] + b["здоровье_восстановление"])
                if not р["болен"] and р["тепло"] < 40 and h.rng.chance(
                        b["болезнь_шанс_на_холоде"] * b["ребёнок_болеет"]):
                    р["болен"] = "простуда"
                    h.journal.line(f"{р['имя']} закашлял у {p.form('gen')} на руках.", 1)
                elif р["болен"] and h.rng.chance(b["болезнь_проходит"]
                                                 * b["ребёнок_болезнь_проходит"]
                                                 * (b["болезнь_проходит_в_тепле"]
                                                    if р["тепло"] > 55 else 1.0)):
                    р["болен"] = None
                if р["здоровье"] <= 0:
                    conflict.смерть_ребёнка(h, p, р)

            # обморожение — это про холод, а не про случайный пик на улице (GDD 6.2)
            if p.warmth < b["критичный_порог"] and not p.hurt("обморож")                     and h.rng.chance(b["обморожение_шанс"]):
                p.injuries.append(h.rng.pick(["обморожение рук", "обморожение ног"]))
                h.journal.line(f"{p.short} {vb(p.sex, 'отморозил')} пальцы.", 1)
            # болезнь от холода
            if not p.sick and p.warmth < 32 and h.rng.chance(b["болезнь_шанс_на_холоде"]):
                p.sick = "простуда"
                h.journal.line(f"{p.label()} закашлял.", 1)

            dmg = 0.0
            for v in (p.satiety, p.hydration, p.warmth, p.rest):
                if v < b["критичный_порог"]:
                    dmg += (b["критичный_порог"] - v) * b["здоровье_за_критичное"]
            dmg += len(p.injuries) * b["травма_урон_в_день"]
            if p.sick:
                dmg += b["болезнь_урон_в_день"]
            # урон и восстановление считаются отдельно, а не «или-или»: пока
            # регенерация стояла в elif, больной не мог поправиться в принципе,
            # потому что болезнь всегда даёт урон (GDD 6.2 обещает четыре пути
            # лечения, а работал один — аптечка)
            if dmg > 0:
                p.health = clamp(p.health - dmg)
            if min(p.satiety, p.hydration, p.warmth, p.rest) > b["порог_восстановления"]:
                p.health = clamp(p.health + b["здоровье_восстановление"])
            if p.injuries and p.health > 40 and h.rng.chance(b["рана_заживает"]):
                p.injuries.pop()      # раны всё-таки затягиваются
            # болезнь проходит сама — в тепле, в сытости и выспавшись (GDD 6.2)
            if p.sick:
                шанс = b["болезнь_проходит"]
                if p.warmth > 55 and p.rest > 60 and p.satiety > 45:
                    шанс *= b["болезнь_проходит_в_тепле"]
                if h.rng.chance(шанс):
                    p.sick = None
                    h.journal.line(f"{p.short} {vb(p.sex, 'отлежался')} — жар спал.", 1)

            # паника растёт и от настоящей нужды, и от веры, что не хватит
            # (GDD 12.3: паника — это «вера, что скоро всё кончится»)
            # спад глушится отчаянием: человек, который неделю не ел, не
            # успокаивается сам собой. Пока этого не было, голодный висел
            # на панике 45-50 и умирал спокойным — а рейд по разд. 16 требует
            # «паника выше половины», и потому не случался никогда
            # пугает и то, что кончается, и то, что сейчас плохо: холод, боль,
            # жар. Отчаяние теперь считает только запасы, поэтому физическую
            # часть приходится назвать здесь отдельно — иначе промёрзший человек
            # успокаивался бы ровно потому, что шкаф у него полон
            нужда = max(p.desperation(), p.невмоготу())
            спад = (b["паника_спад_в_день"] * (0.4 + 0.6 * p.mood / 100.0)
                    * (1.0 - b["паника_не_спадает_в_нужде"] * нужда))
            social.add_panic(p, b["паника_от_отчаяния"]
                             * max(нужда, p.insecurity() * 0.85) - спад)
            # у тревоги есть дно, и оно поднимается само: двадцатый день метели
            # без света, воды и новостей пугает независимо от того, что в шкафу.
            # Это и есть «вера, что скоро всё кончится» из GDD 12.3
            floor = min(b["паника_дно_потолок"],
                        h.day * b["паника_дно_за_день"] + infra * b["паника_дно_за_отключение"])
            floor *= 0.6 + 0.08 * p.trait("вспыльчивость")
            if p.panic < floor:
                p.panic += (floor - p.panic) * b["паника_дно_притяжение"]

            if p.health <= 0:
                p.cause = self._cause(p)
                p.died_day = h.day
                h.journal.line(f"† {p.name} {'умерла' if p.sex == 'ж' else 'умер'}. {p.cause}.", 2)
                conflict.on_death(h, p)

        # вытяжка обмерзает от сегодняшнего пара: то, что натопили за день,
        # аукнется не этой ночью, а следующими
        world.вытяжка_мёрзнет(h)
        world.запах_по_стояку(h)

    def _cause(self, p):
        """Отчего именно человек умер — по самой провалившейся потребности."""
        from .util import vb
        worst = min([(p.satiety, "голод"), (p.hydration, "обезвоживание"),
                     (p.warmth, "холод"), (p.rest, "истощение")], key=lambda x: x[0])
        if p.injuries and worst[0] > 20:
            return vb(p.sex, "умер") + " от невылеченных ран"
        if p.sick and worst[0] > 20:
            return vb(p.sex, "умер") + " от болезни без лечения"
        return worst[1]

    # ------------------------------------------------------------ диагностика
    def _firsts(self, h):
        s = h.stats
        if s.get("налётов") and "первый_налёт_день" not in s:
            s["первый_налёт_день"] = h.day
        if s.get("смертей") and "первая_смерть_день" not in s:
            s["первая_смерть_день"] = h.day
        if s.get("союзов_заключено") and "первый_союз_день" not in s:
            s["первый_союз_день"] = h.day
        if s.get("краж") and "первая_кража_день" not in s:
            s["первая_кража_день"] = h.day
        if (s.get("ушедших") or s.get("возвратов_с_полпути")) and "первая_попытка_уйти" not in s:
            s["первая_попытка_уйти"] = h.day
