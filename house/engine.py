# -*- coding: utf-8 -*-
"""Сборка и главный цикл: день — ночь — расчёт.

День: у каждого 16 часов, он тратит их на действия (GDD 5).
Ночь: сон, дежурство, кражи и налёты (GDD 4.3 «Ночью происходит основной риск»).
Утро: сводка (GDD 4.4) — что заметили соседи, что пропало, кто что слышал.
"""
from .util import Rng, clamp, norm, vb
from .model import House
from .schema import load_json, validate_data
from . import (world, social, actions, conflict, report, meeting, замысел, character, chat,
               assembly, psyche, household, services, discoveries)


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
        assembly.build_house(self.h, self.npcs_data)

    # ------------------------------------------------------------ цикл
    def run(self, on_day=None):
        """on_day(h) — наблюдатель, которого зовут в конце каждого дня.

        Нужен check.py, чтобы снимать отпечаток дома по дням, не подменяя
        `one_day`, как это делают линейки. Сам ничего не меняет и ничего
        не тянет из rng: с on_day=None прогон тот же до последнего слова.
        """
        h = self.h
        world.build_calendar(h, self.events, self.days)
        for _ in range(self.days):
            self.one_day()
            if on_day is not None:
                on_day(h)
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
        # то, чего человек не простил за сегодняшний день, — одной строкой
        # на всех, а не по строке на каждого (discoveries.огласить_непрощённых)
        discoveries.огласить_непрощённых(h)
        chat.daily_chat(h, self.lines)
        self._night(h)
        discoveries.огласить_непрощённых(h)
        self._upkeep(h)
        social.проверить_обещания(h)
        social.alliance_check(h)
        social.update_groups(h)
        social.daily_decay(h)
        social.spread_panic(h)
        # чем кончился день у тех, кто его пролежал: одна строка вместо восьми
        for p in h.alive():
            if p.stats.get("отдых_день") != h.day:
                continue
            раз = p.stats.get("отдых_раз", 0)
            # и то, отчего этот день прошёл впустую. У матери, которой нечем
            # помочь ребёнку, «лежала и ничего не делала» — неправда журнала,
            # а не поведения: делать ей и правда нечего, но она не лежит
            с_ребёнком = (p.дети
                          and p.ребёнку_плохо() >= h.B["быт_ребёнку_плохо"])
            если_ребёнок = f" рядом с {p.dependent_ins or p.dependent_name or 'ребёнком'}"
            if раз >= h.B["отдых_весь_день"]:
                h.journal.line(f"{p.short} почти весь день "
                               + (f"{vb(p.sex, 'просидел')}{если_ребёнок}." if с_ребёнком
                                  else f"{vb(p.sex, 'пролежал')} и ничего не {vb(p.sex, 'делал')}."), 1)
            elif раз >= 2:
                h.journal.line(f"{p.short} подолгу "
                               + (f"{vb(p.sex, 'сидел')}{если_ребёнок}." if с_ребёнком
                                  else f"{vb(p.sex, 'лежал')} и ничего не {vb(p.sex, 'делал')}."), 0)
            else:
                h.journal.line(f"{p.short} "
                               + (f"{vb(p.sex, 'сидел')}{если_ребёнок}." if с_ребёнком
                                  else f"{vb(p.sex, 'лежал')} и ничего не {vb(p.sex, 'делал')}."), 0)
        self._firsts(h)
        h.journal.сводка_дня(h)
        h.journal.flush_day(h)
        h.journal.panel(h)

    # ------------------------------------------------------------ утро
    def _morning(self, h):
        h.новый_день()
        # утро после ночи у чужих: ребёнка забирают. Или не отдают
        conflict.вернуть_детей(h)
        for p in h.alive():
            psyche.горизонт(h, p)
            psyche.дрейф_нормальности(h, p)
            # замысел решает своё до всего остального: спит он сегодня или нет,
            # не пора ли его бросить и не пора ли взяться за новый
            замысел.утро(h, p)
            p.новый_день(h.B)
        household.дрова_к_печке(h)

        services.готовые_заказы(h)

        # вышел ли вчерашний дежурный: перед домом, а не перед соседом
        meeting.проверить_дежурство(h)

        discoveries.вскрытые_кладовые(h)

        discoveries.подброшенное(h)

        # не пора ли дому сложить одно к одному про того, кто ведёт свою игру
        for p in h.alive():
            замысел.напор_виден(h, p)

        discoveries.пропажи(h)

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
            if c and c.alive and not c.exiled and h.под_одной_крышей(p, c):
                conflict.убить_соседа(h, p, c)

        # обобрать и уйти. После ножа и до краж: тот, кто на это решился,
        # этой ночью больше никуда не пойдёт, а дом наутро считает другое
        for p in h.rng.shuffled(h.alive()):
            if p.tonight != "обобрать" or not p.alive or p.exiled:
                continue
            c = targets.get(p.id)
            if c and c.alive and not c.exiled and p.living_with == c.id:
                conflict.обобрать_и_уйти(h, p, c)

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
            elif p.tonight in ("кража", "налёт", "убить_соседа", "обобрать"):
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

        gate = character.norm_gate
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
                     + character.своя_мерка(p, "убить_соседа", b))
            opts.append((("убить_соседа", c), ночью * gate(p, "убить_соседа", b)))
            # и то, что лежит между «съехать по-хорошему» и ножом: собрать
            # хозяйское и уйти к себе в ту же ночь. Только гостю — хозяину
            # уходить некуда, у него эта квартира и есть
            if p.living_with == c.id:
                обобрать = (conflict.оценка_обобрать(h, p, c)
                            + p.пунктик("обобрать")
                            + character.своя_мерка(p, "обобрать", b))
                opts.append((("обобрать", c), обобрать * gate(p, "обобрать", b)))

        for t in h.others(p):
            if not t.alive:
                continue
            if t.living_with:
                continue           # его нет дома, он у соседа
            if h.под_одной_крышей(p, t):
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
            score += p.пунктик("кража") + character.своя_мерка(p, "кража", b)
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

            household.теснота_ночи(h, p)

            # --- ребёнок: своя шкала, и она короче (GDD 12.6) ---
            for р in list(p.дети):
                р["сытость"] = clamp(р["сытость"] - b["ребёнок_расход_сытости"])
                # ту ночь, которую он провёл у соседа, он греется соседской
                # печкой: ради этого его туда и отнесли (actions.отдать_на_ночь)
                комната = room
                у_кого = h.get(р.get("у")) if р.get("у") else None
                if у_кого is not None and у_кого.alive and not у_кого.exiled:
                    комната = h.room_temp(у_кого, burning=у_кого.burning)
                # та же комната, но ребёнок остывает быстрее взрослого
                р["тепло"] = clamp(р["тепло"] + (комната - b["комфортная_температура"])
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
                h.journal.line(f"{p.label()} {vb(p.sex, 'закашлял')}.", 1)

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
