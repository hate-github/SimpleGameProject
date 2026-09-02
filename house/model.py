# -*- coding: utf-8 -*-
"""Модель данных: жилец, пустая квартира, дом.

Здесь только состояние и простые производные величины.
Всё, что «решает» и «происходит», лежит в actions.py, social.py, conflict.py, engine.py.
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
class EmptyFlat:
    """Пустая квартира: ресурс для разборки и укрытие (GDD 12)."""
    apt: int
    floor: int
    stock: Dict[str, float] = field(default_factory=dict)
    stripped: int = 0
    owner_died: Optional[str] = None
    body: Optional[Dict[str, Any]] = None   # тело хозяина, если он умер дома


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
    # этот человек реагирует сильнее прочих — см. social.judge
    values: Dict[str, Any] = field(default_factory=dict)

    # --- черты 0..10 (GDD 12.1) ---
    traits: Dict[str, float] = field(default_factory=dict)

    # --- имущество ---
    stock: Dict[str, float] = field(default_factory=dict)
    weapon: str = "нет"
    shelter: Dict[str, Any] = field(default_factory=dict)
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
    normalcy: float = 1.0     # 1.0 — «жизнь ещё обычная», 0.0 — «всё, началось»
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
    refused_by: Dict[str, int] = field(default_factory=dict)
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

    # ---------- производные величины ----------
    def trait(self, name: str) -> float:
        return float(self.traits.get(name, 5.0))

    def t01(self, name: str) -> float:
        """Черта, приведённая к 0..1."""
        return self.trait(name) / 10.0

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

    def insecurity(self) -> float:
        """0..1 — «мне не хватит».

        Это не голод, а страх: человек считает запас против срока, который сам
        себе назначил, и не знает, когда кончится метель. Отсюда жадность
        и паника — но не преступление.
        """
        return clamp(max(1.0 - min(1.0, self.secure("еда")),
                         1.0 - min(1.0, self.secure("вода")),
                         (1.0 - min(1.0, self.secure("топливо"))) * 0.9), 0.0, 1.0)

    def desperation(self) -> float:
        """0..1 — «я умираю». Физическая нужда: вот это толкает на кражу и налёт."""
        food = 1.0 - norm(min(self.days_of("еда"), 10), 0, 6)
        water = 1.0 - norm(min(self.days_of("вода"), 10), 0, 4)
        fuel = 1.0 - norm(min(self.days_of("топливо"), 10), 0, 6)
        # телесная часть включается только когда правда плохо, иначе
        # человек «в отчаянии» каждый день перед ужином
        body = 1.0 - norm(min(self.satiety, self.warmth, self.hydration), 5, 40)
        hurt = 0.3 if (self.injuries or self.sick) else 0.0
        return clamp(max(food * 0.95, water * 0.9, fuel * 0.75, body) + hurt, 0.0, 1.0)

    def power(self) -> float:
        """Боевая сила (GDD 17: оружие и численное превосходство решают почти всё)."""
        p = 0.6 + self.t01("храбрость") * 0.9
        p += WEAPONS.get(self.weapon, 0.0) * 0.8
        if self.weapon in FIREARMS and self.stock.get("патроны", 0) <= 0:
            p -= WEAPONS.get(self.weapon, 0.0) * 0.55
        p *= 0.55 + 0.45 * (self.health / 100.0)
        p *= max(0.35, 1.0 - 0.18 * len(self.injuries))
        if self.age > 58:
            p *= 0.88
        if self.sick:
            p *= 0.8
        return max(0.15, p)

    def door_strength(self) -> float:
        """Прочность двери для стадии осады «Дверь» (GDD 16)."""
        return 1.0 + 1.6 * self.shelter.get("дверь", 0)

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
    empty: List[EmptyFlat] = field(default_factory=list)

    # погода и инфраструктура
    outside: float = -8.0
    heating: bool = True
    power_on: bool = True
    water_on: bool = True
    network: float = 1.0

    # среда
    scav_richness: float = 1.0
    incidents: int = 0
    first_incident_day: Optional[int] = None
    mods: Dict[str, Any] = field(default_factory=dict)   # временные модификаторы событий

    # вывод
    journal: Any = None
    stats: Dict[str, Any] = field(default_factory=dict)
    chronicle: List[str] = field(default_factory=list)

    def alive(self) -> List[NPC]:
        return [p for p in self.people.values() if p.alive and not p.exiled]

    def others(self, npc: NPC) -> List[NPC]:
        return [p for p in self.alive() if p.id != npc.id]

    def get(self, npc_id: str) -> Optional[NPC]:
        return self.people.get(npc_id)

    def floor_gap(self, a: NPC, b: NPC) -> int:
        return abs(a.floor - b.floor)

    def room_temp(self, npc: NPC, burning: Optional[bool] = None) -> float:
        """Температура в квартире (GDD 15: утепление, буржуйка, обогреватель)."""
        b = self.B
        # гость греется хозяйской печкой — в этом весь смысл съезжаться
        if npc.living_with:
            host = self.people.get(npc.living_with)
            if host and host.alive and not host.exiled:
                return self.room_temp(host, burning)
        if burning is None:
            burning = npc.burning
        t = self.outside + b["дом_базовый_прогрев"]
        if self.heating:
            t += b["отопление_градусов"]
        t += npc.shelter.get("утепление", 0) * b["утепление_градус_за_уровень"]
        if burning and npc.shelter.get("буржуйка"):
            t += b["буржуйка_градусов"]
        if self.power_on and npc.shelter.get("обогреватель"):
            t += b["обогреватель_градусов"]
        return t

    def bump(self, key: str, n: int = 1):
        self.stats[key] = self.stats.get(key, 0) + n

    def note(self, text: str):
        """Строка в хронику дома — то, что попадёт в финальный отчёт."""
        self.chronicle.append(f"день {self.day:>2}: {text}")
