# -*- coding: utf-8 -*-
"""Мелкие утилиты: случайность с зерном, зажимы, взвешенный выбор.

Вся случайность в симуляции идёт через один объект Rng, созданный от зерна.
Это значит: одно и то же зерно = один и тот же прогон, до последнего слова.
(GDD 21: «Распределение случайных событий фиксируется зерном жизни».)
"""
import math
import random
import re
import zlib


def clamp(v, lo=0.0, hi=100.0):
    """Зажать значение в границы."""
    return lo if v < lo else (hi if v > hi else v)


def lerp(a, b, t):
    return a + (b - a) * t


def norm(v, lo, hi):
    """Привести значение к 0..1 внутри диапазона."""
    if hi <= lo:
        return 0.0
    return clamp((v - lo) / (hi - lo), 0.0, 1.0)


class Rng:
    """Обёртка над random.Random — чтобы нигде в коде не было глобального рандома."""

    def __init__(self, seed):
        self.seed = seed
        self.r = random.Random(seed)

    def branch(self, tag):
        """Отдельный поток случайности от того же зерна.

        GDD 21: «Распределение случайных событий фиксируется зерном жизни,
        чтобы игрок не чувствовал несправедливости при повторе». С одним общим
        потоком это не выполняется: стоит игроку сделать хоть что-то иначе —
        и весь календарь погоды съезжает, потому что решения жильцов тянут
        числа из той же ленты. Мир получает свою.

        crc32, а не hash(): встроенный хэш строк в Python зависит от
        PYTHONHASHSEED, и зерно перестало бы быть зерном.
        """
        return Rng(zlib.crc32(tag.encode("utf-8")) ^ (self.seed * 2654435761 & 0xFFFFFFFF))

    def chance(self, p):
        return self.r.random() < p

    def uni(self, a, b):
        return self.r.uniform(a, b)

    def rint(self, a, b):
        return self.r.randint(a, b)

    def pick(self, seq):
        seq = list(seq)
        return self.r.choice(seq) if seq else None

    def shuffled(self, seq):
        s = list(seq)
        self.r.shuffle(s)
        return s

    def weighted(self, pairs):
        """pairs: [(объект, вес)] -> объект. Веса не обязаны быть нормированы."""
        pairs = [(o, max(0.0, w)) for o, w in pairs]
        total = sum(w for _, w in pairs)
        if total <= 0:
            return pairs[0][0] if pairs else None
        x = self.r.random() * total
        acc = 0.0
        for o, w in pairs:
            acc += w
            if x <= acc:
                return o
        return pairs[-1][0]

    def softmax_pick(self, options, temp=1.0):
        """options: [(объект, оценка)] -> объект.

        Мягкий выбор: обычно берётся лучшее, но иногда — второе или третье.
        temp (температура) растёт от паники: чем страшнее, тем безрассуднее выбор.
        """
        if not options:
            return None
        temp = max(0.08, temp)
        best = max(s for _, s in options)
        weights = [(o, math.exp((s - best) / temp)) for o, s in options]
        return self.weighted(weights)


# Согласование глаголов по полу — чтобы в логе не было «Лида ходил».
_IRREGULAR = {"зашёл": "зашла", "ушёл": "ушла", "нашёл": "нашла", "слёг": "слегла",
              "умер": "умерла", "мёртв": "мертва", "уверен": "уверена",
              "вынес": "вынесла", "унёс": "унесла", "принёс": "принесла",
              "занёс": "занесла", "отнёс": "отнесла", "привёл": "привела",
              "полез": "полезла", "вышел": "вышла", "дошёл": "дошла",
              "замёрз": "замёрзла", "смог": "смогла"}


_FORM = re.compile(r"\{([^{}|]*)\|([^{}|]*)\}")


def gform(text, sex):
    """Подставить род в строку из данных: «сидел{|а}» -> «сидел» / «сидела».

    Слева мужская форма, справа женская. Так реплики правятся в JSON
    без единой строчки кода.
    """
    return _FORM.sub(lambda m: m.group(2) if sex == "ж" else m.group(1), text)


def vb(sex, word):
    """vb('ж', 'ходил') -> 'ходила', vb('ж', 'проснулся') -> 'проснулась'."""
    if sex != "ж":
        return word
    if word in _IRREGULAR:
        return _IRREGULAR[word]
    if word.endswith("лся"):          # возвратные: -лся -> -лась
        return word[:-2] + "ась"
    return word + "а"
