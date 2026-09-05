# -*- coding: utf-8 -*-
"""Самопроверка симуляции: инварианты, баланс ресурсов, покрытие веток.

В симуляции ошибка почти никогда не выглядит как исключение. Она выглядит как
ветка, которая молча не выполняется, или как еда, которая молча исчезла.
Поэтому здесь три инструмента:

  · инварианты      — то, что обязано быть верно в конце каждого дня;
  · баланс ресурсов — приход минус расход должен сходиться до последней банки;
  · покрытие        — сколько раз каждое действие предлагалось и выполнялось.

Пользоваться этим: `python check.py`.
"""
import re
from collections import Counter

# «движок» считается наравне с банками по той же причине, по какой считается
# оружие: он один на весь дом, он ходит из рук в руки и он расходуется, когда
# из него собирают генератор. Заводиться сам он не должен
RESOURCES = ("еда", "вода", "топливо", "лекарства", "материалы", "патроны", "мясо",
             "деньги", "движок")
SCALES_100 = ("satiety", "hydration", "warmth", "rest", "mood", "health", "panic")


# ---------------------------------------------------------------- инварианты

def invariants(h):
    """Что обязано быть верно всегда. Возвращает список нарушений."""
    bad = []

    def say(text):
        bad.append(f"день {h.day}: {text}")

    for p in h.people.values():
        живой = p.alive and not p.exiled

        for res, v in p.stock.items():
            if v < -1e-9:
                say(f"{p.short}: запас «{res}» ушёл в минус ({v:.3f})")
        for field in SCALES_100:
            v = getattr(p, field)
            if not (-1e-9 <= v <= 100 + 1e-9):
                say(f"{p.short}: {field} = {v:.2f}, а должно быть 0..100")
        # знать можно только о том, кто и правда умер: знание о смерти живого
        # было бы ошибкой, а не слухом
        for who in sorted(p.знает_о_смерти):
            кто = h.get(who)
            if кто is None:
                say(f"{p.short} знает о смерти несуществующего «{who}»")
            elif кто.alive and not кто.ушёл:
                say(f"{p.short} считает {кто.short} мёртвым, а тот жив")
        for who, v in p.trust.items():
            if not (-1e-9 <= v <= 10 + 1e-9):
                say(f"{p.short}: доверие к {who} = {v:.2f}, а должно быть 0..10")
        for scale, name in ((p.hate, "ненависть"), (p.aware, "осведомлённость"),
                            (p.страх, "страх")):
            for who, v in scale.items():
                if not (-1e-9 <= v <= 100 + 1e-9):
                    say(f"{p.short}: {name} к {who} = {v:.2f}, а должно быть 0..100")
        for who, v in p.близость.items():
            if not (-1e-9 <= v <= 10 + 1e-9):
                say(f"{p.short}: близость с {who} = {v:.2f}, а должно быть 0..10")
        if p.time_left < -0.01:
            say(f"{p.short}: часов в дне осталось {p.time_left:.3f}")

        # ребёнок: число рук, которые он связывает, и его состояние — одно и то же
        if живой and len(p.дети) != p.dependents:
            say(f"{p.short}: иждивенцев {p.dependents}, а состояний детей {len(p.дети)}")
        for р in p.дети:
            for поле in ("сытость", "тепло", "здоровье"):
                if not (-1e-9 <= р[поле] <= 100 + 1e-9):
                    say(f"{р['имя']} у {p.short}: {поле} = {р[поле]:.2f}, а должно быть 0..100")

        # выбывший — это мёртвый, изгнанный ИЛИ ушедший к пункту обогрева.
        # Третье состояние для этих двух проверок ничем не отличается
        # от первых двух: человека в подъезде нет, и ничего его здесь не ждёт
        if not живой and sum(p.stock.values()) > 1e-9:
            say(f"{p.short} выбыл, но запасы при нём: {p.stock}")
        if not живой and p.allies:
            say(f"{p.short} выбыл, но числится в союзе с {sorted(p.allies)}")
        if p.ушёл and p.alive is False:
            say(f"{p.short} ушёл и одновременно числится мёртвым")
        if p.ушёл and p.id in {x for o in h.people.values() for x in o.знает_о_смерти}:
            say(f"{p.short} ушёл, но дом считает, что он умер")

        if живой and p.living_with:
            host = h.get(p.living_with)
            if host is None:
                say(f"{p.short} живёт у несуществующего «{p.living_with}»")
            elif not host.alive or host.exiled:
                say(f"{p.short} живёт у выбывшего {host.short} — топить свою печь он уже не может")
            elif p.id not in host.guests:
                say(f"{p.short} живёт у {host.short}, а тот об этом не знает")
        for gid in sorted(p.guests):
            g = h.get(gid)
            if g is None:
                say(f"у {p.short} в гостях несуществующий «{gid}»")
            elif g.living_with != p.id:
                say(f"{p.short} считает гостем {g.short}, а тот живёт у «{g.living_with}»")
            elif not (g.alive and not g.exiled):
                say(f"у {p.short} в гостях выбывший {g.short}")
        for other in h.people.values():
            if other.id != p.id and p.id in other.allies and other.id not in p.allies:
                say(f"союз односторонний: {other.short} считает {p.short} союзником, а тот нет")

    # квартиры. Список пустых теперь считается, а не ведётся руками, поэтому
    # «дважды пустая» и «пустая под живым» стали невозможны по устройству —
    # вместо них проверяем то, что теперь может сломаться
    for p in h.people.values():
        if p.apt not in h.flats:
            say(f"{p.short} прописан в кв.{p.apt}, которой нет в доме")
    # проломы: их можно заделать, но нельзя заделать больше, чем было
    for f in h.flats.values():
        if not (-1e-9 <= f.вентиляция <= 1.0 + 1e-9):
            say(f"кв.{f.apt}: вентиляция = {f.вентиляция:.3f}, а должно быть 0..1")
        for вид, n in f.дыры.items():
            if n < 0:
                say(f"кв.{f.apt}: проломов «{вид}» {n}, а должно быть 0 и больше")
            if вид not in ("окно", "стена", "пол", "потолок"):
                say(f"кв.{f.apt}: пролом неизвестного вида «{вид}»")
    занято = {}
    for p in h.alive():
        if p.living_with:
            continue
        if p.apt in занято:
            say(f"кв.{p.apt} считают своей двое: {занято[p.apt]} и {p.short}")
        занято[p.apt] = p.short

    # кладовые: имущество за порогом квартиры живёт по тем же правилам,
    # что и всё остальное имущество
    for к in h.кладовые.values():
        for res, v in к.stock.items():
            if v < -1e-9:
                say(f"{к.имя}: запас «{res}» ушёл в минус ({v:.3f})")
        if к.apt not in h.flats:
            say(f"{к.имя} приписана к кв.{к.apt}, которой нет в доме")
    for p in h.people.values():
        for kid in sorted(p.ключи_кладовых):
            if kid not in h.кладовые:
                say(f"{p.short}: ключ от несуществующей кладовой «{kid}»")
        if not (p.alive and not p.exiled) and p.ключи_кладовых:
            say(f"{p.short} выбыл, но ключи от кладовых при нём: {sorted(p.ключи_кладовых)}")

    return bad


# ---------------------------------------------------------------- баланс ресурсов

def ledger(h, start):
    """Сошёлся ли приход с расходом. start — снимок мира до первого дня.

    Правило: было + пришло == осталось + израсходовано + потеряно.
    Всё, что не сходится, — либо забытый счётчик, либо утечка.
    """
    bad = []
    for res in RESOURCES:
        было = start.get(res, 0.0)
        пришло = (h.stats.get("принесено_" + res, 0.0)
                  + h.stats.get("наразобрано_" + res, 0.0)
                  + h.stats.get("натоплено_" + res, 0.0))
        ушло = (h.stats.get("израсходовано_" + res, 0.0)
                + h.stats.get("потеряно_" + res, 0.0)
                # ушедший унёс свою еду с собой: для дома она ушла из мира
                # ровно так же, как съеденная
                + h.stats.get("унесено_" + res, 0.0))
        осталось = world_total(h, res)
        расхождение = (было + пришло) - (осталось + ушло)
        if abs(расхождение) > 0.01:
            bad.append(f"{res}: было {было:.1f} + пришло {пришло:.1f} "
                       f"≠ осталось {осталось:.1f} + ушло {ушло:.1f} "
                       f"(расхождение {расхождение:+.2f})")
    было_о = start.get("_оружие", 0)
    стало_о = оружие_всего(h)
    найдено = h.stats.get("оружия_найдено", 0)
    унесено = h.stats.get("оружия_унесено", 0)
    if стало_о != было_о + найдено - унесено:
        bad.append(f"оружие: было {было_о} + найдено {найдено} − унесено {унесено} "
                   f"≠ осталось {стало_о}. "
                   f"Оно не должно ни исчезать вместе с человеком, ни заводиться само")
    return bad


def world_total(h, res):
    """Сколько ресурса есть в доме — у живых, у мёртвых, в квартирах и в кладовых.

    Кладовая — третье место, где лежат банки, и не считать её нельзя: тогда
    первая же поднятая из погреба банка выглядит приходом из ниоткуда. Учтённая,
    она сходится сама собой, без единого счётчика: снимок мира делается до
    первого дня и уже включает подвал, а подъём наверх — честное перекладывание.
    """
    return (sum(p.stock.get(res, 0.0) for p in h.people.values())
            + sum(f.stock.get(res, 0.0) for f in h.flats.values())
            + sum(k.stock.get(res, 0.0) for k in h.кладовые.values()))


def оружие_всего(h):
    """Сколько единиц оружия в доме — в руках и в стенах.

    Тот же учёт, что и у банок, и по той же причине: оружие теперь ходит
    из рук в руки, а значит, может начать исчезать (забыли положить в квартиру
    за мёртвым) или заводиться само (взяли, не отдав своё). И то и другое
    ломает единственное правило, на котором эта механика стоит: почти всё,
    что есть в доме, было в доме с первого дня.
    """
    в_руках = sum(1 for p in h.people.values() if p.weapon and p.weapon != "нет")
    return (в_руках + sum(len(f.оружие) for f in h.flats.values())
            + sum(len(k.оружие) for k in h.кладовые.values()))


def snapshot(h):
    s = {res: world_total(h, res) for res in RESOURCES}
    s["_оружие"] = оружие_всего(h)
    return s


# ---------------------------------------------------------------- покрытие

def _разделы(lines):
    """Пары (путь, список реплик) по всем разделам lines.json."""
    for ключ, значение in sorted(lines.items()):
        if isinstance(значение, list):
            yield ключ, значение
        elif isinstance(значение, dict):
            for под, список in sorted(значение.items()):
                if isinstance(список, list):
                    yield f"{ключ}.{под}", список


class Coverage:
    """Считает, сколько раз каждое действие предлагалось и выполнялось.

    Ветка, которая ни разу не предложена, — это мёртвый код, и в симуляции
    его никак иначе не заметить: исключения он не бросает.

    Пользоваться так:
        with Coverage() as cov:
            ...прогоны...
        cov.report()
    """

    def __init__(self):
        self.offered = Counter()
        self.done = Counter()
        self.siege = Counter()
        self.night = Counter()
        self.реплики = Counter()

    def __enter__(self):
        from . import actions, conflict, report
        self._actions, self._conflict, self._report = actions, conflict, report
        self._gather, self._execute = actions.gather, actions.execute
        self._siege = conflict.run_siege
        self._gform, self._chat = actions.gform, report.Journal.chat

        def gather(h, npc):
            opts = self._gather(h, npc)
            for (key, _t), _s in opts:
                self.offered[key] += 1
            return opts

        def execute(h, npc, key, target):
            self.done[key] += 1
            return self._execute(h, npc, key, target)

        def run_siege(h, leader, target):
            # состав читается ПОСЛЕ осады, из того, что она сама записала.
            # Пока обвязка звала recruit сама, вербовка выполнялась дважды —
            # безобидно, пока она была чистым фильтром, и уже нет, когда у зова
            # появились последствия: осведомлённость, паника и утечка к жертве
            self.siege["осад"] += 1
            жива = target.alive
            r = self._siege(h, leader, target)
            self.siege["состав всего"] += len(h.mods.get("состав_налёта", []))
            self.siege["исход: " + r] += 1
            if жива and not target.alive:
                self.siege["цель погибла"] += 1
            return r

        # реплика, которая ни разу не прозвучала, — такой же мёртвый текст,
        # как ветка, которую ни разу не предложили. Условия у реплик умеют
        # не выполняться никогда: чат живёт до десятого дня, а паника доходит
        # до порога позже — и целая тема молча выпадает из игры
        def gform(text, sex):
            self.реплики[text] += 1
            return self._gform(text, sex)

        def chat(journal, who, text):
            self.реплики[text] += 1
            return self._chat(journal, who, text)

        actions.gather, actions.execute = gather, execute
        actions.gform = gform
        report.Journal.chat = chat
        conflict.run_siege = run_siege
        return self

    def __exit__(self, *exc):
        self._actions.gather, self._actions.execute = self._gather, self._execute
        self._actions.gform = self._gform
        self._report.Journal.chat = self._chat
        self._conflict.run_siege = self._siege
        return False

    def немые_реплики(self, lines):
        """Реплики из lines.json, которые ни разу не прозвучали.

        Сверяются по самому длинному куску текста без подстановок: в журнал
        реплика попадает уже с именем соседа и номером квартиры внутри,
        а реплика обычной жизни — ещё и с выбранным родом.
        """
        сказанные = list(self.реплики)
        немые = []
        for раздел, список in _разделы(lines):
            for в in список:
                текст = в if isinstance(в, str) else в.get("текст", "")
                куски = [к.strip() for к in re.split(r"\{[^}]*\}", текст)
                         if len(к.strip()) >= 6]
                якорь = max(куски, key=len) if куски else текст
                if not any(якорь in s for s in сказанные):
                    немые.append(f"{раздел}: «{текст[:52]}»")
        return немые

    def dead_branches(self):
        from .actions import COST
        return sorted(k for k in COST if not self.offered.get(k))

    def report(self, w=print):
        from .actions import COST
        w("Действия — предложено / выполнено:")
        for key in sorted(COST, key=lambda k: -self.done.get(k, 0)):
            o, d = self.offered.get(key, 0), self.done.get(key, 0)
            метка = "   ← мёртвая ветка" if o == 0 else ("   ← ни разу не выбрано" if d == 0 else "")
            w(f"  {key:<14} {o:>8} / {d:<6}{метка}")
        if self.siege:
            осад = self.siege.get("осад", 0)
            w("")
            w(f"Осады: {осад}, средний состав "
              f"{self.siege.get('состав всего', 0) / max(1, осад):.2f} человека")
            for k in sorted(self.siege):
                if k.startswith("исход: ") or k == "цель погибла":
                    w(f"  {k:<22} {self.siege[k]:>4}  ({100 * self.siege[k] / max(1, осад):.0f}%)")
