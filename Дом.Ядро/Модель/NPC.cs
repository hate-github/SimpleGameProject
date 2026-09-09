// Перенос house/model.py, строки 591–800: состояние жильца.
// Производные величины (power, speed, believed, знаю_про …) — в NPC.Знание.cs
// и NPC.Тело.cs: в Python это один класс, разделённый комментариями-разделами,
// и разделы переезжают файлами.

namespace Дом.Ядро;

public sealed partial class NPC
{
    // --- паспорт ---
    public required string id { get; init; }
    public required string name { get; set; }
    public required string @short { get; set; }
    public required int apt { get; set; }
    public required int floor { get; set; }
    public required int age { get; set; }
    public required string role { get; set; }
    public string sex { get; set; } = "м";
    public string gen { get; set; } = "";       // родительный: «у Лиды»
    public string dat { get; set; } = "";       // дательный: «занёс Лиде»
    public string acc { get; set; } = "";       // винительный: «убил Лиду»
    public string ins { get; set; } = "";       // творительный: «обменял с Лидой»
    public List<string> skills { get; } = new();

    // GDD 12.1: «Ценности: что любит и ненавидит». Метки поступков, на которые
    // этот человек реагирует сильнее прочих — см. social.judge и своя_мерка
    public Ценности values { get; set; } = new();

    // именованные правила поведения: список строк, как умения (см. пунктик)
    public List<string> пунктики { get; } = new();

    // --- черты 0..10 (GDD 12.1) ---
    public Словарь<string, double> traits { get; } = Словари.Числа();

    // --- имущество ---
    public Словарь<string, double> stock { get; } = Словари.Числа();
    public Оружие weapon { get; set; } = Оружие.НЕТ;
    public int одежда { get; set; }             // 0 — куртка, 1 — утеплённая, 2 — тулуп

    // насколько человек свыкся с ЭТИМ оружием: вид -> 0..1. Своё, с которым
    // он вошёл в метель, привычно; чужое, снятое с мёртвого, — нет, и стреляет
    // он из него хуже, пока не привыкнет
    // ключ — само оружие, а не его название: в прототипе здесь член
    // перечисления, и сейв это видит (`{"#вид": "Оружие"}` против голой
    // строки). Снимку всё равно — он всякий ключ сводит к тексту
    public Словарь<Оружие, double> рука { get; } = new();

    public double счёт { get; set; }            // цифровые деньги: их нельзя украсть
    public Словарь<string, double> места { get; } = Словари.Числа();  // что я думаю о местах
    public int dependents { get; set; }
    public string dependent_name { get; set; } = "";
    public string dependent_acc { get; set; } = "";     // «взял Ваню»
    public string dependent_gen { get; set; } = "";     // «для Вани»
    public string dependent_ins { get; set; } = "";     // «ушла с Ваней»

    // Ребёнок — состояние, а не множитель (GDD 12.6). У него своя сытость,
    // своё тепло и своё здоровье; он мёрзнет и болеет первым и может умереть.
    // `dependents` остаётся числом рук, которые он связывает, — на него смотрят
    // два десятка решений; `дети` — то, что с ним на самом деле происходит.
    public List<Ребёнок> дети { get; } = new();

    // --- состояние (GDD 6.1). 100 = хорошо, 0 = критично ---
    public double satiety { get; set; } = 85.0;
    public double hydration { get; set; } = 85.0;
    public double warmth { get; set; } = 80.0;
    public double rest { get; set; } = 90.0;
    public double mood { get; set; } = 65.0;
    public double health { get; set; } = 100.0;

    public double panic { get; set; } = 10.0;
    public double horizon { get; set; } = 10.0; // на сколько дней вперёд нужен запас

    // 1.0 — «жизнь ещё обычная», 0.0 — «всё, началось». Это состояние, а не
    // формула: оно помнит себя, падает от увиденного и отрастает в тихие дни.
    public double normalcy { get; set; } = 1.0;
    // у каждого свой пол и своя скорость — два числа в npcs.json, а не код
    public double нормальность_пол { get; set; } = 0.1;
    public double нормальность_скорость { get; set; } = 1.0;

    public List<string> injuries { get; } = new();
    public string? sick { get; set; }

    // --- отношение к каждому другому жильцу (GDD 12.3) ---
    // что я о нём знаю: оценка запасов и осведомлённость
    public Словарь<string, Сведения> сведения { get; } = Словари.Строкой<Сведения>();
    public Словарь<string, double> hate { get; } = Словари.Числа();
    public Словарь<string, double> trust { get; } = Словари.Числа();

    // четвёртая шкала: насколько человек опасается вот этого соседа (GDD 12.3).
    // К ненависти она не сводится — бояться можно того, на кого не злишься
    // вовсе: он просто вышел на площадку с ружьём
    public Словарь<string, double> страх { get; } = Словари.Числа();

    // пятая шкала, и она не мнение, а история: сколько раз говорили, носили,
    // возвращали, ночевали под одной крышей. Она у каждого своя и делится
    // на всех, поэтому у того, к кому ходят пятеро, она размазана,
    // а у того, кто ходит к одному, — собрана
    public Словарь<string, double> близость { get; } = Словари.Числа();

    // Обида: вес неулаженного счёта к этому соседу, 0..100. Главное в ней —
    // она не проходит от времени. Гасят её только поступками — и не до нуля:
    // у неё есть дно, ниже которого она не опускается никогда
    public Словарь<string, double> счёты { get; } = Словари.Числа();

    // А это те, кого он не простит ни за что: у него вынесли квартиру, его
    // выставили на мороз из собственных стен
    public HashSet<string> не_прощу { get; } = new(StringComparer.Ordinal);

    // каким сосед КАЖЕТСЯ: сыт ли, цел ли, не мёрзнет ли. В подъезде без света
    // шкаф чужой не виден, а лицо видно каждый день
    public Словарь<string, Взгляд> вид { get; } = Словари.Строкой<Взгляд>();

    // чью смерть человек считает делом дней. Метка на паре, а не на человеке
    public HashSet<string> не_жилец { get; } = new(StringComparer.Ordinal);

    // и те, кто не вышел на его крик. Не вражда и не обида — знание
    public HashSet<string> не_вышли { get; } = new(StringComparer.Ordinal);

    // --- социальное ---
    public string? group { get; set; }
    public string? living_with { get; set; }    // к кому переехал
    public HashSet<string> guests { get; } = new(StringComparer.Ordinal);
    public HashSet<string> allies { get; } = new(StringComparer.Ordinal);
    public Словарь<string, int> favors { get; } = Словари.Строкой<int>();
    public Словарь<string, double> дал { get; } = Словари.Числа();   // кому и сколько я отдал
    public HashSet<int> ключи { get; } = new();                       // от каких квартир
    // от каких кладовых. Отдельным полем: там номера квартир числами
    public HashSet<string> ключи_кладовых { get; } = new(StringComparer.Ordinal);
    public Словарь<string, ОпытПросьб> asking { get; } = Словари.Строкой<ОпытПросьб>();

    // --- слово (GDD 14) ---
    public List<Ложь> врал { get; } = new();            // что я кому наговорил
    public List<Обещание> обещал { get; } = new();      // и что обещал
    public Словарь<string, double> не_верю { get; } = Словари.Числа();  // кто мне уже врал

    // --- о чьей смерти он знает (GDD 12.3, 13) ---
    // Факт и знание о факте — разные вещи. Смерть тихая, во сне, была
    // невозможна не потому, что не бывает, а потому, что её некому было бы
    // не заметить
    public HashSet<string> знает_о_смерти { get; } = new(StringComparer.Ordinal);

    // --- служебное ---
    public bool alive { get; set; } = true;
    public string? cause { get; set; }
    public int? died_day { get; set; }
    public bool exiled { get; set; }
    // ушёл к пункту обогрева. Ушедший не выжил и не погиб: дом не знает,
    // дошёл он или замёрз на объездной, и не узнает никогда
    public bool ушёл { get; set; }
    // знает ли он, куда идти. Объявление на двери — вещь, а не знание
    public bool знает_пункт { get; set; }
    // цель, которая живёт дольше одного дня (house/замысел.py). Не приказ,
    // а перевес: подкрашивает оценки, спит, пока человеку не до неё
    public Замысел? замысел { get; set; }

    public double time_left { get; set; } = 16.0;
    public double подъём { get; set; } = 8.0;   // во сколько он встаёт (GDD 12.1)
    public Словарь<string, string> привычки { get; } = Словари.Строкой<string>();  // отрезок -> занятие
    public double slept { get; set; } = 8.0;
    public Ночь tonight { get; set; } = Ночь.СПАТЬ;
    public bool away { get; set; }
    public bool burning { get; set; }
    public List<Память> memory { get; } = new();

    // состояние, которое раньше лежало в stats строковыми ключами (аудит §4)
    public double часы_работы { get; set; }             // сбрасывается утром
    public int день_вылазки { get; set; } = -99;        // когда его видели с пакетами
    public int день_разговора { get; set; } = -99;      // в дверях или в чате
    public int день_беседы { get; set; } = -99;         // одиночество меряется этим
    public int день_известия_о_смерти { get; set; } = -99;
    public int дежурил_ночь { get; set; } = -99;
    public int караулил { get; set; } = -99;
    public int отдых_день { get; set; } = -99;
    public int отдых_раз { get; set; }
    public int переехал_день { get; set; } = -99;
    public double переезд_умысел { get; set; }          // 0..1 — греться или на шкаф
    public double снёс_дров { get; set; }
    public int? дорога_закрыта { get; set; }            // узнал, что дороги нет
    public int угар_признак { get; set; } = -99;        // просыпался с тяжёлой головой
    public int слышал_про_угар { get; set; } = -99;
    public string? зов_куда { get; set; }               // куда звал на вылазку
    public bool раскрыт { get; set; }                   // дом знает, что он ел человечину
    public bool людоед { get; set; }                    // сам переступил: брал тело
    public int под_подозрением { get; set; }            // обобрал хозяина, ходил с ножом
    public bool дошёл { get; set; }                     // ушёл к пункту и дошёл
    public HashSet<string> переступил { get; } = new(StringComparer.Ordinal);

    // счётчики, которые читает поведение, а не только отчёт
    public int поймали { get; set; }                    // сколько раз ловили на чужом
    public int обокрали { get; set; }                   // сколько раз обокрали его
    // расправы, о которых он знает, лежат в `memory` вместе со всем прочим,
    // что он знает, и стареют вместе с ним (`знает_расправ`)
    public int возвращался { get; set; }                // с полпути к пункту обогрева
    public int ночей_дежурства { get; set; }
    public int замыслов { get; set; }
    public Словарь<string, int> stats { get; } = Словари.Строкой<int>();

    // ссылка на дом: нужна, чтобы npc.shelter означал «стены, в которых он
    // сейчас». В снимок не входит и в сравнение жильцов — тоже
    internal House? _h { get; set; }

    // кто выбирает за него из оценённых вариантов (decision.py): NPC — мягкий
    // выбор, игрок — человек, проверка — скрипт. Не состояние: в снимок
    // не входит. Тип появится вместе со швом на этапе 1д
    public object? решающий { get; set; }

    // ---------- в каких он стенах ----------
    /// <summary>
    /// Убежище той квартиры, в которой человек сейчас живёт: своей — или
    /// хозяйской, если он переехал. Свойство, а не поле: так все сорок мест
    /// в коде, которые спрашивают «а есть ли у него буржуйка», сами собой
    /// стали спрашивать про правильные стены.
    /// </summary>
    public Словарь<string, double> shelter
        => _h is not null ? _h.where(this).shelter : Словари.Числа();

    /// <summary>«Ценю работу»: метка из данных (GDD 12.1, «Ценности»).</summary>
    public bool ценит(string tag) => values.ценит.Contains(tag, StringComparer.Ordinal);

    /// <summary>«Не терплю воровство»: тормоз для своей руки и мерка для чужой.</summary>
    public bool не_терпит(string tag)
        => values.не_терпит.Contains(tag, StringComparer.Ordinal);

    /// <summary>Жив и в доме: не умер и не изгнан. Одна проверка вместо
    /// шестидесяти `alive and not exiled` по всему дому.</summary>
    public bool здесь() => alive && !exiled;

    public void bump(string key, int n = 1) => stats[key] = stats.Взять(key, 0) + n;

    /// <summary>Утро: часы, печка, отлучка и ночное решение сбрасываются.</summary>
    public void новый_день(Баланс b)
    {
        time_left = b["часов_бодрствования"];
        burning = false;
        away = false;
        // без сброса в поле висело вчерашнее решение, и вор прикидывал шанс
        // кражи по позавчерашнему дежурству жертвы
        tonight = Ночь.СПАТЬ;
        часы_работы = 0.0;
    }
}

/// <summary>
/// Ценности жильца (GDD 12.1). В прототипе это словарь из трёх ключей
/// («описание», «ценит», «не_терпит»); здесь — запись с теми же тремя
/// полями: содержимое то же, а опечатка в ключе перестала быть возможной.
/// </summary>
public sealed class Ценности
{
    public string описание { get; set; } = "";
    public List<string> ценит { get; } = new();
    public List<string> не_терпит { get; } = new();
}
