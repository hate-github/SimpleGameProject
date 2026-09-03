# -*- coding: utf-8 -*-
"""Модель данных: жилец, пустая квартира, дом.

Состояние и производные от него величины. «Простые» они не все: здесь же
живут боевая сила (power), тепловая модель квартиры (room_temp) и то,
как состояние тела превращается в скорость работы (speed) и шанс успеха
(success) — потому что это свойства человека и дома, а не решения.

Решения — в actions.py, social.py, conflict.py и engine.py.
"""
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Any

from .util import clamp, norm

# Ресурсы (GDD 12.1: «Запасы: еда, вода, топливо в днях»)
RESOURCES = ["еда", "вода", "топливо", "лекарства", "материалы", "патроны"]
# «мясо» намеренно не в списке: это не ресурс, который меняют и просят

# Оружие и его вес в бою (GDD 17: бой намеренно простой и смертельный)
WEAPONS = {
    "нет": 0.0,
    "нож": 1.0,
    "дубина": 1.3,
    "топор": 1.8,
    "пистолет": 2.4,
    "дробовик": 3.0,
    "винтовка": 3.4,
}
FIREARMS = {"пистолет", "дробовик", "винтовка"}


@dataclass
class Flat:
    """Квартира — вещь, а не приложение к жильцу (GDD 12, 15).

    Утепление, дверь, буржуйка и арматура принадлежат стенам. Пока они висели
    на человеке, «занять квартиру получше» было нечем выразить: переехавший
    уносил буржуйку на спине, а после смерти хозяина всё, что он построил
    за месяц, исчезало вместе с ним.

    Отсюда же берётся жильё как имущество: его можно занять, за него можно
    прийти с ломом, и его можно испортить, ломая дверь.
    """
    apt: int
    floor: int
    shelter: Dict[str, Any] = field(default_factory=dict)
    stock: Dict[str, float] = field(default_factory=dict)
    stripped: int = 0
    owner_died: Optional[str] = None
    body: Optional[Dict[str, Any]] = None   # тело хозяина, если он умер дома
    вложено: float = 0.0                    # сколько материалов в неё вбито
    костёр: int = -99                       # день, когда в ней жгли костёр
    тулуп: bool = False                     # зимняя одежда хозяина осталась здесь

    @property
    def id(self) -> str:
        """Чтобы квартира годилась в цели действия наравне с человеком."""
        return f"кв{self.apt}"

    def door_strength(self) -> float:
        """Прочность двери для стадии осады «Дверь» (GDD 16).

        Стальные листы — уровень 3 убежища, они добавляются к засову.
        """
        return 1.0 + 1.6 * self.shelter.get("дверь", 0) + 1.2 * self.shelter.get("листы", 0)

    def защита(self) -> float:
        """Насколько за этой дверью можно отсидеться (GDD 15, 16)."""
        return self.door_strength() + 1.2 * self.shelter.get("стены", 0)


# старое имя оставлено: в паре мест «пустая квартира» читается лучше
EmptyFlat = Flat


@dataclass
class NPC:
    # --- паспорт ---
    id: str
    name: str
    short: str
    apt: int
    floor: int
    age: int
    role: str
    sex: str = "м"
    gen: str = ""      # родительный: «у Лиды»
    dat: str = ""      # дательный: «занёс Лиде»
    acc: str = ""      # винительный: «убил Лиду»
    ins: str = ""      # творительный: «обменял с Лидой»
    skills: List[str] = field(default_factory=list)
    # GDD 12.1: «Ценности: что любит и ненавидит». Метки поступков, на которые
    # этот человек реагирует сильнее прочих — см. social.judge и своя_мерка
    values: Dict[str, Any] = field(default_factory=dict)
    # именованные правила поведения: список строк, как умения (см. пунктик)
    пунктики: List[str] = field(default_factory=list)

    # --- черты 0..10 (GDD 12.1) ---
    traits: Dict[str, float] = field(default_factory=dict)

    # --- имущество ---
    stock: Dict[str, float] = field(default_factory=dict)
    weapon: str = "нет"
    одежда: int = 0            # 0 — обычная куртка, 1 — утеплённая, 2 — тулуп
    места: Dict[str, float] = field(default_factory=dict)   # что я думаю о местах
    dependents: int = 0
    dependent_name: str = ""
    dependent_acc: str = ""    # «взял Ваню»

    # --- состояние (GDD 6.1). 100 = хорошо, 0 = критично ---
    satiety: float = 85.0
    hydration: float = 85.0
    warmth: float = 80.0
    rest: float = 90.0
    mood: float = 65.0
    health: float = 100.0

    panic: float = 10.0
    horizon: float = 10.0     # на сколько дней вперёд человек считает нужным иметь запас
    # 1.0 — «жизнь ещё обычная», 0.0 — «всё, началось». Это состояние, а не
    # формула: оно помнит себя, падает от увиденного и отрастает в тихие дни.
    normalcy: float = 1.0
    # у каждого свой пол и своя скорость — два числа в npcs.json, а не код.
    # Лида не сломается до конца (0.35), Игорь съедет за неделю (0.05)
    нормальность_пол: float = 0.1
    нормальность_скорость: float = 1.0
    injuries: List[str] = field(default_factory=list)
    sick: Optional[str] = None

    # --- отношение к каждому другому жильцу (GDD 12.3) ---
    aware: Dict[str, float] = field(default_factory=dict)
    hate: Dict[str, float] = field(default_factory=dict)
    trust: Dict[str, float] = field(default_factory=dict)
    est: Dict[str, Dict[str, float]] = field(default_factory=dict)

    # --- социальное ---
    group: Optional[str] = None
    living_with: Optional[str] = None   # к кому переехал
    guests: set = field(default_factory=set)
    allies: set = field(default_factory=set)
    favors: Dict[str, int] = field(default_factory=dict)
    дал: Dict[str, float] = field(default_factory=dict)   # кому и сколько я отдал
    ключи: set = field(default_factory=set)   # от каких квартир у него ключи
    asking: Dict[str, Dict[str, float]] = field(default_factory=dict)  # память о просьбах

    # --- служебное ---
    alive: bool = True
    cause: Optional[str] = None
    died_day: Optional[int] = None
    exiled: bool = False
    time_left: float = 16.0
    slept: float = 8.0
    tonight: str = "спать"
    away: bool = False
    burning: bool = False
    memory: List[str] = field(default_factory=list)
    stats: Dict[str, int] = field(default_factory=dict)
    # ссылка на дом: нужна, чтобы npc.shelter означал «стены, в которых он сейчас».
    # repr=False обязателен — иначе печать жильца утащит за собой весь дом
    _h: Any = field(default=None, repr=False, compare=False)

    # ---------- в каких он стенах ----------
    @property
    def shelter(self) -> Dict[str, Any]:
        """Убежище той квартиры, в которой человек сейчас живёт.

        Своей — или хозяйской, если он переехал. Свойство, а не поле: так все
        сорок мест в коде, которые спрашивают «а есть ли у него буржуйка»,
        сами собой стали спрашивать про правильные стены.
        """
        return self._h.where(self).shelter if self._h is not None else {}

    # ---------- производные величины ----------
    def trait(self, name: str) -> float:
        return float(self.traits.get(name, 5.0))

    def t01(self, name: str) -> float:
        """Черта, приведённая к 0..1."""
        return self.trait(name) / 10.0

    def вес_черт(self, key: str) -> float:
        """Сумма «черта × вес» для этого решения. Веса лежат в balance.json.

        Характер — это и есть эти веса: `лояльность × 4` в щедрости и
        `− лояльность × 4.5` в краже. Пока они были зашиты в код, крутить
        можно было темп и злость дома, но не человека.

        Выносятся только слагаемые вида «черта × число». Там, где черта входит
        множителем (жадность в отъёме, храбрость в бою), она осталась в коде:
        это не вес характера, а устройство самого действия.
        """
        if self._h is None:
            return 0.0
        таблица = self._h.B.get("веса_черт", {}).get(key, {})
        return sum(self.t01(черта) * вес for черта, вес in таблица.items())

    def пунктик(self, key: str) -> float:
        """Сдвиг от личных пунктиков (GDD 12.1: «ценности» как правила).

        Пунктик — это правило, а не сила склонности: «первым не ударит»
        нельзя выразить через храбрость, потому что человек может быть смелым
        и при этом не начинать. Список пунктиков человека лежит в npcs.json,
        сами пунктики — в balance.json; в коде один интерпретатор.
        """
        if not self.пунктики or self._h is None:
            return 0.0
        таблица = self._h.B.get("пунктики", {})
        return sum(float(таблица.get(п, {}).get(key, 0.0)) for п in self.пунктики)

    def eaters(self) -> float:
        """Сколько ртов кормит (GDD 12.1: дневное потребление)."""
        return 1.0 + 0.6 * self.dependents

    def days_of(self, res: str) -> float:
        """На сколько дней хватит ресурса при текущем потреблении."""
        per_day = {"еда": 0.85, "вода": 0.9, "топливо": 1.0}.get(res, 1.0) * self.eaters()
        return self.stock.get(res, 0.0) / max(0.2, per_day)

    def secure(self, res: str) -> float:
        """1.0 = запасов ровно на тот срок, который человек считает нужным.

        Ниже единицы — «мне не хватит», и это главный источник жадности:
        отказывают не злые, а те, кто посчитал.
        """
        return clamp(self.days_of(res) / max(3.0, self.horizon), 0.0, 2.5)

    def топит_сам(self) -> bool:
        """Отвечает ли этот человек за своё отопление.

        Переехавший к соседу — нет: его дрова ушли в общую печку, и топит
        её хозяин. Пока это не спрашивалось, у гостя топлива всегда было ноль,
        и он вечно числился в отчаянии, сидя в тепле и сытости.
        """
        return not self.living_with

    def мороз_предел(self, b: Dict[str, Any]) -> float:
        """Ниже какой температуры этот человек на улицу не выйдет.

        В обычной городской куртке при минус двадцати пяти на улице делать
        нечего — не «опасно», а нельзя: обморожение раньше, чем добыча.
        Каждый уровень одежды отодвигает предел (GDD 20).
        """
        return b["выход_предел"] - self.одежда * b["выход_за_одежду"]

    def могу_топить(self) -> float:
        """Насколько топливо для него вообще ресурс.

        Без буржуйки дрова не сжечь: обогреватель ест электричество, генератор
        надо сперва собрать. Считать их такой же нуждой, как еду, — значит
        заставить человека без печки паниковать из-за мёртвого груза
        и ходить просить дрова у соседей.
        """
        if not self.топит_сам():
            return 0.0
        if self.shelter.get("буржуйка"):
            return 1.0
        if self.stock.get("материалы", 0.0) >= 4.0:
            return 0.5      # есть из чего собрать — запасается не зря
        return 0.15

    def insecurity(self) -> float:
        """0..1 — «мне не хватит».

        Это не голод, а страх: человек считает запас против срока, который сам
        себе назначил, и не знает, когда кончится метель. Отсюда жадность
        и паника — но не преступление.
        """
        части = [1.0 - min(1.0, self.secure("еда")),
                 1.0 - min(1.0, self.secure("вода")),
                 (1.0 - min(1.0, self.secure("топливо"))) * 0.9 * self.могу_топить()]
        return clamp(max(части), 0.0, 1.0)

    def невмоготу(self) -> float:
        """0..1 — «мне сейчас физически плохо».

        Третье чувство, которого не хватало между «мне не хватит» и «я умираю».
        Это не про запасы вообще: холод в комнате, боль от травмы, жар. И лечится
        оно печкой, костром, просьбой, переездом и аптечкой — а не взломом.

        Пока этого не было, холод и ушиб входили слагаемыми в отчаяние, и человек
        с полным шкафом в нетопленой квартире читался как умирающий: у медианного
        ночного вора в первую неделю было еды на десять дней.
        """
        холод = 1.0 - norm(self.warmth, 12, 55)
        боль = 0.0
        if self.injuries:
            боль = min(1.0, 0.35 + 0.2 * (len(self.injuries) - 1))
        if self.sick:
            боль = max(боль, 0.45)
        слабость = 1.0 - norm(self.health, 20, 70)
        return clamp(max(холод, боль, слабость), 0.0, 1.0)

    def desperation(self) -> float:
        """0..1 — «я умираю». Считается по запасам: вот это толкает на кражу и налёт.

        Холод, боль и болезнь сюда не входят — они в `невмоготу`. Травма тоже:
        она уже отнимает силу в драке, скрытность и скорость, и этого довольно.
        """
        food = 1.0 - norm(min(self.days_of("еда"), 10), 0, 6)
        water = 1.0 - norm(min(self.days_of("вода"), 10), 0, 4)
        fuel = (1.0 - norm(min(self.days_of("топливо"), 10), 0, 6)) * self.могу_топить()
        # телесная часть — только настоящее истощение, ниже критического порога:
        # иначе человек «в отчаянии» каждый день перед ужином
        body = 1.0 - norm(min(self.satiety, self.hydration), 5, 30)
        return clamp(max(food * 0.95, water * 0.9, fuel * 0.75, body), 0.0, 1.0)

    def hurt(self, limb: str) -> bool:
        """Повреждена ли конечность (GDD 6.2: «нога снижает скорость, рука — ремонт»).

        Травмы хранятся строками вида «перелом ноги», «порез руки» — тип и место
        в одном тексте, чтобы журнал читался как журнал, а не как структура.
        """
        return any(limb in i for i in self.injuries)

    def speed(self, b: Dict[str, Any]) -> float:
        """Множитель ко времени действия (GDD 6.1).

        Голод, холод и недосып замедляют всё; повреждённая нога — переноску
        и вылазки. Меньше единицы человек не работает быстрее, только медленнее.
        """
        v = 1.0
        v += b["медленнее_за_голод"] * (1.0 - norm(self.satiety, 15, 70))
        v += b["медленнее_за_недосып"] * (1.0 - norm(self.rest, 15, 70))
        v += b["медленнее_за_холод"] * (1.0 - norm(self.warmth, 20, 65))
        if self.sick:
            v += b["медленнее_за_болезнь"]
        if self.hurt("ноги"):
            v += b["медленнее_за_ногу"]
        return v

    def success(self, b: Dict[str, Any]) -> float:
        """Шанс, что работа выйдет с первого раза (GDD 7: у действий есть провал)."""
        p = b["успех_база"]
        p -= b["успех_за_недосып"] * (1.0 - norm(self.rest, 15, 70))
        p -= b["успех_за_голод"] * (1.0 - norm(self.satiety, 15, 70))
        p -= b["успех_за_холод"] * (1.0 - norm(self.warmth, 20, 65))
        if self.hurt("руки"):
            p -= b["успех_за_руку"]
        return clamp(p, 0.15, 0.98)

    def power(self) -> float:
        """Боевая сила (GDD 17: оружие и численное превосходство решают почти всё)."""
        p = 0.6 + self.t01("храбрость") * 0.9
        p += WEAPONS.get(self.weapon, 0.0) * 0.8
        if self.weapon in FIREARMS and self.stock.get("патроны", 0) <= 0:
            p -= WEAPONS.get(self.weapon, 0.0) * 0.55
        p *= 0.55 + 0.45 * (self.health / 100.0)
        p *= max(0.35, 1.0 - 0.18 * len(self.injuries))
        if self.hurt("руки"):
            p *= 0.8            # оружие в разбитой руке держится плохо (GDD 6.2)
        if self.age > 58:
            p *= 0.88
        if self.sick:
            p *= 0.8
        return max(0.15, p)

    def door_strength(self) -> float:
        """Прочность двери для стадии осады «Дверь» (GDD 16).

        Стальные листы — уровень 3 убежища, они добавляются к засову.
        """
        return 1.0 + 1.6 * self.shelter.get("дверь", 0) + 1.2 * self.shelter.get("листы", 0)

    def знаю_место(self, имя: str) -> float:
        """Что я думаю о месте, пока сам не сходил и никто не рассказал.

        По умолчанию — «наверное, там как всегда»: до метели в магазине была
        еда, и человек идёт туда, пока не узнает обратного. Это и есть
        нормальный порядок вылазок: сначала очевидное, мусорки — потом.
        """
        return self.места.get(имя, 1.0)

    def believed(self, other_id: str, res: str) -> float:
        """Сколько, по мнению этого NPC, у другого есть ресурса."""
        return self.est.get(other_id, {}).get(res, 2.0)

    def confidence(self, other_id: str) -> float:
        """0..1 — насколько он уверен в сведениях. Это и есть осведомлённость."""
        return self.aware.get(other_id, 0.0) / 100.0

    def loot_value(self, other_id: str) -> float:
        """Насколько лакомой выглядит чужая квартира: сведения на уверенность."""
        e = self.est.get(other_id, {})
        raw = e.get("еда", 2.0) * 1.0 + e.get("топливо", 2.0) * 0.7 + e.get("лекарства", 0.0) * 1.1
        return raw * (0.35 + 0.65 * self.confidence(other_id))

    def form(self, case: str) -> str:
        """Имя в нужном падеже. Формы лежат в data/npcs.json, а не в коде."""
        return getattr(self, case, "") or self.short

    def dependents_only_child(self, h=None) -> bool:
        """Заглушка-запрет: ребёнок никогда не может стать едой.

        Тел детей в мире не появляется вовсе (см. conflict._orphan),
        этот метод оставлен как явная точка, чтобы правило было видно в коде.
        """
        return False

    def ask_record(self, other_id: str) -> Dict[str, float]:
        """Что я помню про просьбы к этому человеку."""
        return self.asking.setdefault(other_id, {
            "дали": 0.0, "отказали": 0.0, "подряд": 0.0,
            "последняя": -99.0, "я_дал": -99.0,
        })

    def generosity(self, other_id: str) -> float:
        """0..1 — насколько, по моему опыту, у этого человека можно выпросить.

        Пока опыта нет, оценка около половины: попробовать стоит.
        """
        r = self.asking.get(other_id)
        if not r:
            return 0.5
        return (r["дали"] + 1.0) / (r["дали"] + r["отказали"] + 2.0)

    def label(self) -> str:
        return f"{self.short} (кв.{self.apt})"

    def bump(self, key: str, n: int = 1):
        self.stats[key] = self.stats.get(key, 0) + n


@dataclass
class House:
    """Дом и всё, что в нём происходит. Один подъезд пятиэтажки (GDD 25, «Ядро»)."""
    rng: Any = None
    B: Dict[str, Any] = field(default_factory=dict)
    day: int = 0
    people: Dict[str, NPC] = field(default_factory=dict)
    flats: Dict[int, Flat] = field(default_factory=dict)

    # погода и инфраструктура
    outside: float = -8.0
    heating: bool = True
    power_on: bool = True
    water_on: bool = True
    network: float = 1.0

    # среда
    scav_richness: float = 1.0
    места: Dict[str, float] = field(default_factory=dict)   # что ещё осталось где
    incidents: int = 0
    first_incident_day: Optional[int] = None
    mods: Dict[str, Any] = field(default_factory=dict)   # временные модификаторы событий

    # вывод
    journal: Any = None
    stats: Dict[str, Any] = field(default_factory=dict)
    chronicle: List[str] = field(default_factory=list)

    def богатство_места(self, имя: str) -> float:
        """Сколько ещё осталось в этом месте. 1.0 — как было до метели."""
        return self.места.get(имя, 1.0)

    def alive(self) -> List[NPC]:
        return [p for p in self.people.values() if p.alive and not p.exiled]

    def others(self, npc: NPC) -> List[NPC]:
        return [p for p in self.alive() if p.id != npc.id]

    def get(self, npc_id: str) -> Optional[NPC]:
        return self.people.get(npc_id)

    # ---------- жильё ----------
    def хозяин_жилья(self, npc: NPC) -> NPC:
        """Чья это квартира: своя или того, к кому он переехал."""
        if npc.living_with:
            host = self.people.get(npc.living_with)
            if host and host.alive and not host.exiled:
                return host
        return npc

    def where(self, npc: NPC) -> Flat:
        """В какой квартире человек физически находится."""
        return self.flats[self.хозяин_жилья(npc).apt]

    def занятые(self) -> set:
        """Номера квартир, в которых кто-то живёт прямо сейчас."""
        return {p.apt for p in self.people.values()
                if p.alive and not p.exiled and not p.living_with}

    def пустые(self) -> List[Flat]:
        """Квартиры, в которых никто не живёт.

        Считается, а не хранится: пока список вёлся руками, одна и та же
        квартира успевала попасть в него дважды, а жилая — остаться в нём
        и уйти на доски из-под живого человека.
        """
        занято = self.занятые()
        return [f for f in sorted(self.flats.values(), key=lambda x: x.apt)
                if f.apt not in занято]

    def floor_gap(self, a: NPC, b: NPC) -> int:
        return abs(a.floor - b.floor)

    def powered(self, npc: NPC) -> bool:
        """Есть ли в этой квартире электричество (GDD 15, уровень 3).

        Либо в доме ещё не отключили свет, либо человек завёл свой генератор —
        ради этого он его и собирал, платя за это шумом на весь подъезд.
        """
        if self.power_on:
            return True
        return self.where(npc).shelter.get("питание") == self.day

    def flat_temp(self, flat: Flat, burning: bool = False, powered: bool = False) -> float:
        """Температура в конкретной квартире. Считает стены, а не жильца —
        поэтому этим же можно оценить чужую и пустую (GDD 15)."""
        b = self.B
        t = self.outside + b["дом_базовый_прогрев"]
        if self.heating:
            t += b["отопление_градусов"]
        t += flat.shelter.get("утепление", 0) * b["утепление_градус_за_уровень"]
        if burning and flat.shelter.get("буржуйка"):
            t += b["буржуйка_градусов"]
        if powered and flat.shelter.get("обогреватель"):
            t += b["обогреватель_градусов"]
        # костёр посреди комнаты: греет хуже печки, дымит и может спалить дом
        if flat.костёр == self.day:
            t += b["костёр_градусов"]
        return t

    def чей(self, flat: Flat) -> Optional[NPC]:
        """Живой человек, который считает эту квартиру своей.

        Он может в ней сейчас и не жить — переехал к соседу, — но она его,
        и занять её значит оставить его без угла.
        """
        for p in self.people.values():
            if p.alive and not p.exiled and p.apt == flat.apt:
                return p
        return None

    def ценность_жилья(self, flat: Flat, для: NPC) -> float:
        """Чего эта квартира стоит вот этому человеку.

        Три слагаемых, и все три он может оценить снаружи: сколько она держит
        тепла (видно по окнам и по дыму), крепка ли дверь (видно с площадки)
        и что в ней лежит. Из разницы двух таких чисел растёт всё остальное —
        и мирный переезд, и налёт за квартирой.
        """
        b = self.B
        # свет в доме кончился — значит, обогреватель в расчёт больше не идёт
        тепло = self.flat_temp(flat, burning=True, powered=self.power_on)
        v = (тепло - b["комфортная_температура"]) * b["жильё_вес_тепла"]
        страх = min(1.5, для.panic / 100.0 + 0.2 * (0 if self.first_incident_day is None else 1))
        v += flat.защита() * b["жильё_вес_защиты"] * (0.4 + страх)
        v += sum(flat.stock.values()) * b["жильё_вес_запаса"] * (0.5 + для.t01("жадность"))
        return v

    def room_temp(self, npc: NPC, burning: Optional[bool] = None) -> float:
        """Температура в квартире (GDD 15: утепление, буржуйка, обогреватель)."""
        b = self.B
        # гость греется хозяйской печкой — в этом весь смысл съезжаться
        хозяин = self.хозяин_жилья(npc)
        flat = self.flats[хозяин.apt]
        if burning is None:
            burning = хозяин.burning
        return self.flat_temp(flat, burning=burning, powered=self.powered(npc))

    def bump(self, key: str, n: int = 1):
        self.stats[key] = self.stats.get(key, 0) + n

    def note(self, text: str):
        """Строка в хронику дома — то, что попадёт в финальный отчёт."""
        self.chronicle.append(f"день {self.day:>2}: {text}")
