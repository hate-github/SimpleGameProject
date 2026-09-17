// Что слышно (ГДД 23): ветер, гул генератора, шаги, скрип.
//
// «Звук — основной носитель напряжения». Сначала записанных звуков
// не было ни одного, и ветер с гулом **синтезировались** прямо здесь.
// Теперь ветер — запись вьюги (`звуки/вьюга.mp3`): дома её глушат стены —
// фильтр низких частот на своей шине, — во дворе глушить нечему. Гул
// генератора записи не получил и остаётся синтезом: низкая синусоида
// с гармоникой.
//
// Что здесь настоящее и останется: **откуда берётся громкость.** Ветер
// громче в буран, потому что в календаре стоит буран; гул слышен, только
// если в квартире работает незаглушенный генератор, и тише через этаж.
// Когда придут записанные звуки, они встанут на место синтеза,
// а `Дом.Экран.Подача` не изменится ни строкой.
//
// ----------------------------------------------------------------------
//
// Ветер общий, гул — точка в пространстве. Метель везде: она за всеми
// стенами разом, и приходить ей неоткуда. Гул идёт из конкретной
// квартиры, и в игре от первого лица это половина смысла: поднимаясь
// по лестнице, слышно, **у кого** работает генератор, — а значит, у кого
// есть чем его кормить.
//
// Разделение обязанностей жёсткое и записано в `Голос`: **направление —
// от движка, громкость — от симуляции.** Затухание по расстоянию поэтому
// выключено: гул через этаж тише не оттого, что дальше от уха, а оттого,
// что через перекрытие, и это уже посчитано. Оставить оба — значит
// приглушить вдвое.
//
// Шаги и скрип соседей не играются: записи шагов есть, но у голоса
// «шаги на лестнице» нет места, а шагам без места нечего сказать.
// Свои шаги героя — в `Шаги`.
//
// Отдельно — то, что случилось с прошлого вопроса и было слышно
// (`События`): налёт ломится в дверь, налётчики прорвались внутрь —
// бьётся стекло, кто-то переезжает — двигают мебель. Это не громкость
// из симуляции, а сам факт из потока событий, и звучит он у той двери,
// где было.

using Godot;
using Дом.Ядро;
using Дом.Экран;

namespace Дом.Годот;

public partial class ЗвукUI : Node
{
    private const int ЧАСТОТА = 22050;

    private AudioStreamPlayer _ветер = null!;
    private AudioEffectLowPassFilter _стены = null!;
    private double _ветер_сила;

    private const string ШИНА_ВЕТРА = "Ветер";
    private const float ЧАСТОТА_ДОМА = 650f;      // что пропускают стены
    private const float ЧАСТОТА_ДВОРА = 9000f;

    /// <summary>Гул на квартиру: свой источник, своя фаза, своё место.</summary>
    private sealed class Гудит
    {
        public AudioStreamPlayer3D игрок = null!;
        public AudioStreamGeneratorPlayback? поток;
        public double сила;
        public double фаза;
    }

    private readonly Dictionary<int, Гудит> _гулы = new();
    private readonly RandomNumberGenerator _шум = new();

    /// <summary>Не пищать: пока громкость ниже этого, канал молчит вовсе.</summary>
    [Export] public float Порог { get; set; } = 0.02f;

    public override void _Ready()
    {
        // своя шина для ветра: на ней стены — фильтр низких частот
        int шина = AudioServer.GetBusIndex(ШИНА_ВЕТРА);
        if (шина < 0)
        {
            AudioServer.AddBus();
            шина = AudioServer.BusCount - 1;
            AudioServer.SetBusName(шина, ШИНА_ВЕТРА);
            AudioServer.SetBusSend(шина, "Master");
            AudioServer.AddBusEffect(шина, new AudioEffectLowPassFilter { CutoffHz = ЧАСТОТА_ДОМА });
        }
        _стены = (AudioEffectLowPassFilter)AudioServer.GetBusEffect(шина, 0);
        _ветер = new AudioStreamPlayer
        {
            Stream = Звуки.Взять("вьюга", петля: true),
            Bus = ШИНА_ВЕТРА,
            VolumeDb = -80f,
        };
        AddChild(_ветер);
        _ветер.Play();
        _шум.Randomize();
    }

    /// <summary>
    /// Отдать узлу то, что слышно сейчас. Зовётся не каждый кадр, а когда
    /// дом остановился на вопросе: громкости меняются с ходом, а не с кадром.
    ///
    /// <paramref name="где"/> переводит номер квартиры в место в мире.
    /// Узел звука сам не знает, где какая дверь, и знать не должен: это
    /// дело `Мир`, а его дело — что и как громко.
    /// </summary>
    public void Обновить(IReadOnlyList<Голос> голоса, Func<int, Vector3?> где)
    {
        _ветер_сила = голоса.Where(г => г.что == Звук.ВЕТЕР)
                            .Select(г => г.громкость).DefaultIfEmpty(0).Max();

        foreach (var г in _гулы.Values)
            г.сила = 0;

        foreach (var г in голоса.Where(x => x.что == Звук.ГУЛ_ГЕНЕРАТОРА))
        {
            if (г.квартира is not int кв)
                continue;
            if (!_гулы.TryGetValue(кв, out var гудит))
                _гулы[кв] = гудит = Завести();
            гудит.сила = г.громкость;
            if (где(кв) is Vector3 место)
                гудит.игрок.Position = место + new Vector3(0, 1.2f, 0);
        }
    }

    private Гудит Завести()
    {
        var игрок = new AudioStreamPlayer3D
        {
            Stream = new AudioStreamGenerator
            {
                MixRate = ЧАСТОТА,
                BufferLength = 0.25f,
            },
            // затухание считает симуляция, а не движок: см. шапку файла
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            Autoplay = false,
        };
        AddChild(игрок);
        игрок.Play();
        return new Гудит { игрок = игрок };
    }

    public override void _Process(double delta)
    {
        Ветер(delta);
        foreach (var г in _гулы.Values)
        {
            г.поток ??= г.игрок.GetStreamPlayback() as AudioStreamGeneratorPlayback;
            Гул(г);
        }
    }

    /// <summary>Он вышел во двор или вернулся: за порогом метель слышно
    /// без стен.</summary>
    public void НаУлице(bool да) => _на_улице = да;

    private bool _на_улице;

    /// <summary>
    /// Ветер: запись вьюги. Громкость считает симуляция (буран громче
    /// метели); здесь только стены — дома запись глушится и тише,
    /// во дворе звучит как есть. Меняется плавно: шаг за порог не щелчок.
    /// </summary>
    private void Ветер(double delta)
    {
        double сила = _ветер_сила * (_на_улице ? 1.0 : 0.6);
        float громкость = сила < Порог ? -80f : Mathf.LinearToDb((float)сила) - 4f;
        float к = (float)System.Math.Min(1.0, delta * 3.0);
        _ветер.VolumeDb = Mathf.Lerp(_ветер.VolumeDb, громкость, к);
        _стены.CutoffHz = Mathf.Lerp(_стены.CutoffHz,
                                     _на_улице ? ЧАСТОТА_ДВОРА : ЧАСТОТА_ДОМА, к);
    }

    /// <summary>
    /// Что случилось с прошлого вопроса и было слышно — звук у той двери,
    /// где было. Налёт ломится в дверь; налётчики прорвались внутрь —
    /// бьётся стекло; кто-то переезжает — двигают мебель.
    /// </summary>
    public void События(IReadOnlyList<Событие> новые, Func<int, Vector3?> где)
    {
        foreach (var с in новые)
        {
            string? звук = с.вид switch
            {
                ВидСобытия.НАЛЁТ => "удар_по_металлу",
                ВидСобытия.ИСХОД when ПРОРВАЛИСЬ.Contains(с.что ?? "") => "стекло",
                ВидСобытия.ПЕРЕЕЗД => "мебель",
                _ => null,
            };
            if (звук is null || с.где is not int кв || где(кв) is not Vector3 место)
                continue;
            var п = Звуки.Точка(this, звук, место + new Vector3(0, 1.2f, 0),
                                размер: 4f, дальше_не: 40f);
            п.Finished += п.QueueFree;
            п.Play();
        }
    }

    /// <summary>Исходы налёта, при которых налётчики оказались внутри.</summary>
    private static readonly HashSet<string> ПРОРВАЛИСЬ = new(StringComparer.Ordinal)
    {
        "ограблен", "занял", "изгнан", "убит",
    };

    /// <summary>Гул: низкая синусоида плюс октава. Именно гул, а не тон:
    /// одна чистая частота звучит как сигнал тревоги, а не как движок
    /// за стеной.</summary>
    private void Гул(Гудит г)
    {
        if (г.поток is null)
            return;
        int сколько = г.поток.GetFramesAvailable();
        if (сколько <= 0)
            return;
        float громко = (float)г.сила;
        if (громко < Порог)
        {
            Тишина(г.поток, сколько);
            return;
        }
        for (int i = 0; i < сколько; i++)
        {
            г.фаза += 1.0 / ЧАСТОТА;
            double t = г.фаза * Mathf.Tau;
            double v = Mathf.Sin((float)(t * 52.0)) * 0.7
                     + Mathf.Sin((float)(t * 104.0)) * 0.25
                     + _шум.RandfRange(-0.05f, 0.05f);
            float s = (float)(v * громко * 0.22);
            г.поток.PushFrame(new Vector2(s, s));
        }
    }

    private static void Тишина(AudioStreamGeneratorPlayback поток, int сколько)
    {
        for (int i = 0; i < сколько; i++)
            поток.PushFrame(Vector2.Zero);
    }
}
