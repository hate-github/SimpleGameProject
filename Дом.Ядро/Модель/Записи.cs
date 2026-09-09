// Перенос house/model.py, строки 117–445: записи состояния.
//
// Имена полей оставлены как в прототипе, до буквы: 397 функций из 522
// и почти вся модель названы по-русски, и на эти имена ссылаются планы,
// ADR и замеры (ADR-5). Перевод в PascalCase молча оборвал бы связь кода
// с тем, чем он объясняется.

namespace Дом.Ядро;

/// <summary>
/// Одна строка потока <c>h.события</c>. Ссылки — id, а не объекты: поток
/// переживает и смерть, и переезд, и сохранение состояния.
/// </summary>
public sealed record Событие(
    int день,
    ВидСобытия вид,
    string? кто = null,
    string? кому = null,
    string? что = null,
    double сколько = 0.0,
    int? где = null);          // номер квартиры

/// <summary>
/// Тот, кто на руках (GDD 12.6): своя шкала, короче взрослой.
///
/// Падежи имени ребёнок носит с собой все четыре: он переходит из рук в руки
/// (conflict._orphan, вернуть_детей), и на новом месте «ушла с Ваней» должно
/// читаться так же, как читалось у матери.
/// </summary>
public sealed class Ребёнок
{
    public required string имя { get; set; }
    public required string вин { get; set; }
    public required string род { get; set; }
    public required string твор { get; set; }
    public double сытость { get; set; } = 85.0;
    public double тепло { get; set; } = 80.0;
    public double здоровье { get; set; } = 100.0;
    public string? болен { get; set; }
    public string? у { get; set; }      // у кого ночует сегодня (actions.отдать_на_ночь)
}

/// <summary>Мёртвый в пустой квартире: то, чем он ещё остаётся для дома (GDD 12.2).</summary>
public sealed class Тело
{
    public required string кто { get; set; }    // короткое имя — как называют в журнале
    public required string вин { get; set; }
    public required string падеж { get; set; }  // родительный: «тело Игоря»
    public required int день { get; set; }      // когда умер
    public required double порций { get; set; } // сколько ещё осталось тем, кто переступит
    public bool тронуто { get; set; }
    public int? запах { get; set; }             // день, когда по стояку потянуло
}

/// <summary>Уговор с мастером (services): что, к какому дню, почём и чьи доски.</summary>
public sealed class Заказ
{
    public required string мастер { get; set; }
    public required string что { get; set; }
    public required int готово { get; set; }
    public required double цена { get; set; }
    public required bool материал_заказчика { get; set; }
}

/// <summary>То, чего человек добивается дольше одного дня (house/замысел.py).</summary>
public sealed class Замысел
{
    public required string вид { get; set; }
    public required int с_дня { get; set; }
    public double напор { get; set; }           // насколько далеко зашёл
    public int ходов { get; set; }
    public int спал { get; set; }               // сколько дней подряд нужда мешала
    public string? враг { get; set; }
    public string? жертва { get; set; }
    public string? друг { get; set; }
    public bool сегодня_спит { get; set; }
}

/// <summary>Слово, данное соседу (GDD 14): вернуть, сделать, не делать — со сроком.</summary>
public sealed class Обещание
{
    public required string кому { get; set; }
    public required string вид { get; set; }
    public required string? что { get; set; }
    public required int срок { get; set; }
    public required int день { get; set; }
}

/// <summary>Сказанная неправда: пока её не разоблачили, она работает.</summary>
public sealed class Ложь
{
    public required string кому { get; set; }
    public required string тема { get; set; }   // своё / место / человек
    public required string? что { get; set; }
    public required int день { get; set; }
    public bool раскрыто { get; set; }
}

/// <summary>
/// Что человек сам видел, слышал или учуял — и когда.
///
/// Раньше это была строка «д12:слышал:буржуйка:игорь», которую
/// conflict.suspect разбирал подстроками. Теперь у записи есть день, вид,
/// о ком и что именно.
/// </summary>
public sealed class Память
{
    public required int день { get; set; }
    public required Вид вид { get; set; }       // откуда знает
    public required string кто { get; set; }    // о ком
    public string? что { get; set; }            // что именно: вид шума, ресурс
}

/// <summary>
/// Что человек помнит про этого соседа: просьбы, долги, отказы.
///
/// Это его память о том, как с этим соседом выходило раньше, — и она решает,
/// пойдёт ли он к этой двери снова. Три словаря остались словарями нарочно:
/// у них ключ — не имя поля, а ресурс, пара ресурсов или сосед,
/// и их бывает много.
/// </summary>
public sealed class ОпытПросьб
{
    public double дали { get; set; }            // сколько раз он мне давал
    public double отказали { get; set; }
    public double подряд { get; set; }          // отказов подряд: дважды — и больше не унижаюсь
    public double последняя { get; set; } = -99.0;   // день последней просьбы к нему
    public double я_дал { get; set; } = -99.0;  // день, когда я ему давал (и потому не прошу сам)
    public double должен { get; set; }          // сколько я ему должен
    public string? должен_чем { get; set; }     // взял дрова — верни дрова, а не банку
    public double не_заплатил { get; set; }     // сколько раз не заплатил ему за лечение
    // дни отказов — по видам: у каждого своя память и свой срок обиды
    public double отказ_зов { get; set; } = -99.0;      // не пошёл со мной на вылазку
    public double отказ_ночёвка { get; set; } = -99.0;  // не взял ребёнка на ночь
    public double отказ_переезд { get; set; } = -99.0;  // не пустил к себе
    public double отказ_налёт { get; set; } = -99.0;    // не пошёл со мной к чужой двери
    public double не_впустил { get; set; } = -99.0;     // не открыл мне с аптечкой
    public Словарь<string, double> нет { get; } = Словари.Числа();   // ресурс -> день «у него этого нет»
    public Словарь<string, double> мена { get; } = Словари.Числа();  // «не взял это за то» -> день
    public double мена_подряд { get; set; }     // отказов в мене подряд: трижды — и хватит
    public double мена_закрыт { get; set; } = -99.0;    // день, после которого к нему не ходят вовсе
    public Словарь<string, double> не_поверил { get; } = Словари.Числа();  // о чьей смерти не поверил -> день
}

/// <summary>Дом уже всё узнал и ещё не решил: кого судить и с какого дня ждёт.</summary>
public sealed class Приговор
{
    public required string кто { get; set; }
    public required int день { get; set; }
}

/// <summary>Расписание ночей на площадке, принятое собранием (meeting).</summary>
public sealed class Дежурство
{
    public required List<string> очередь { get; set; }
    public required int до { get; set; }
    public required int начало { get; set; }
}

/// <summary>
/// Каким сосед показался в последний раз (GDD 13): три шкалы по лицу, хворь
/// и день встречи. Не правда о нём, а впечатление — с ошибкой, которая тем
/// меньше, чем ближе смотрели. Решение получает это, а не самого соседа.
///
/// Шкала, которую ещё не разглядели, — <c>null</c>: такого соседа считают
/// обычным (сыт 80, цел 100, тепло 80).
/// </summary>
public sealed class Взгляд
{
    public double? сыт { get; set; }
    public double? цел { get; set; }
    public double? тепло { get; set; }
    public bool? хворь { get; set; }            // видел рану или слышал кашель
    public int день { get; set; } = -99;        // когда видел вблизи
    public double подряд { get; set; }          // сколько раз подряд выглядел «не жильцом»

    public double сытость => сыт ?? 80.0;
    public double целость => цел ?? 100.0;
    public double теплота => тепло ?? 80.0;

    /// <summary>Самая провалившаяся из трёх шкал — по ней и судят (NPC.плох).</summary>
    public double худшее() => Math.Min(сытость, Math.Min(целость, теплота));
}

/// <summary>
/// Что человек знает о соседе (GDD 13): оценка его запасов по ресурсам
/// и осведомлённость — насколько он вообще в курсе его дел, 0..100.
/// Не то, каким сосед кажется (<see cref="Взгляд"/>), а то, что о нём
/// известно: слухи, дым из окна, просьбы, подсмотренное.
/// </summary>
public sealed class Сведения
{
    public Словарь<string, double> est { get; } = Словари.Числа();  // ресурс -> сколько, по-моему, у него
    public double aware { get; set; }
}

/// <summary>
/// Как человек себя чувствует в эту минуту — то, с чем он подходит к выбору.
///
/// Не состояние: в снимок не входит, полем <c>NPC</c> не лежит, живёт один
/// ход и считается заново (<c>actions.самочувствие</c>). Три чувства разведены
/// нарочно и остаются разными: <c>отчаяние</c> — «я умираю», считается
/// по запасам и толкает на кражу и налёт; <c>невмоготу</c> — «мне сейчас
/// плохо», холод, боль, ребёнок, и лечится печкой, просьбой, переездом;
/// <c>зову</c> — с чем человек идёт к соседям.
/// </summary>
public sealed record Самочувствие
{
    public required double голод { get; init; }         // 0..1 — насколько пусто в животе
    public required double жажда { get; init; }
    public required double холод { get; init; }
    public required double усталость { get; init; }
    public required double отчаяние { get; init; }      // NPC.desperation() — «я умираю»
    public required double невмоготу { get; init; }     // NPC.невмоготу() — «мне физически плохо»
    public required double зову { get; init; }          // actions.нужда() — с чем идут к соседям
    public required double комната { get; init; }       // градусов в его комнате, если топить
    public required double холод_впереди { get; init; } // куда идёт термометр
    public required double тревога { get; init; }       // своя паника плюс то, что в доме
    public required double еды_дней { get; init; }
    public required double средний_свой { get; init; }  // насколько он вообще близок с домом
    public required double критичный_порог { get; init; }   // ручки для `край`
    public required double нужда_за_критичное { get; init; }

    /// <summary>
    /// Множитель к нужде ниже критического порога.
    ///
    /// Голод и жажда упираются в единицу уже на пятнадцати пунктах, и дальше
    /// сытость три и сытость пятнадцать для решения одно и то же. А это разные
    /// вещи: ниже критического порога начинает сыпаться здоровье, и тело
    /// перестаёт оставлять выбор.
    /// </summary>
    public double край(double значение)
    {
        double порог = критичный_порог;
        return 1.0 + Math.Max(0.0, порог - значение) / порог * нужда_за_критичное;
    }
}

/// <summary>Виды памяти, которые складываются в знание о доме.</summary>
public static class Виды
{
    /// <summary>После этих записей человек знает: в доме крадут.</summary>
    public static readonly IReadOnlyList<Вид> ЗНАЕТ_О_КРАЖАХ =
        new[] { Вид.УКРАЛ, Вид.ПОЙМАЛ_ВОРА, Вид.ПРОПАЖА, Вид.СЛЫШАЛ_О_ВОРЕ };

    /// <summary>…и расправы, о которых он знает: свой страх последствий,
    /// а не счёт дома.</summary>
    public static readonly IReadOnlyList<Вид> РАСПРАВЫ =
        new[] { Вид.ВИДЕЛ_УБИЙСТВО, Вид.ВИДЕЛ_ИЗГНАНИЕ };
}
