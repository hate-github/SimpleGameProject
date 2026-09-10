// Что слышно (ГДД 23): ветер, гул генератора, шаги, скрип.
//
// «Звук — основной носитель напряжения». Записанных звуков у нас нет
// ни одного, и ждать художника, чтобы услышать хоть что-то, — значит
// не услышать ничего до самого конца. Поэтому ветер и гул **синтезируются**
// прямо здесь: ветер — отфильтрованный шум, гул — низкая синусоида
// с гармоникой. Это не музыка и не замена работе звукорежиссёра; это
// способ проверить, что громкость идёт из симуляции, а не из настроения
// программиста.
//
// Что здесь настоящее и останется: **откуда берётся громкость.** Ветер
// громче в буран, потому что в календаре стоит буран; гул слышен, только
// если у соседа за стеной работает незаглушенный генератор, и тише через
// этаж. Когда придут записанные звуки, они встанут на место синтеза,
// а `Дом.Экран.Подача` не изменится ни строкой.
//
// Шаги и скрип не синтезируются: короткий удар шумом на них не похож
// ни капли, и лучше промолчать, чем изобразить. Они приходят в `Голоса`,
// узел их видит и пока не играет — место занято честно и пусто.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class ЗвукUI : Node
{
    private const int ЧАСТОТА = 22050;

    private AudioStreamPlayer _ветер = null!;
    private AudioStreamPlayer _гул = null!;
    private AudioStreamGeneratorPlayback? _ветер_поток;
    private AudioStreamGeneratorPlayback? _гул_поток;

    private double _ветер_сила;
    private double _гул_сила;
    private double _фаза;
    private double _низ;                 // однополюсный фильтр для ветра
    private readonly RandomNumberGenerator _шум = new();

    /// <summary>Не пищать: пока громкость ниже этого, канал молчит вовсе.</summary>
    [Export] public float Порог { get; set; } = 0.02f;

    public override void _Ready()
    {
        _ветер = Канал();
        _гул = Канал();
        _шум.Randomize();
    }

    private AudioStreamPlayer Канал()
    {
        var поток = new AudioStreamGenerator
        {
            MixRate = ЧАСТОТА,
            BufferLength = 0.25f,
        };
        var игрок = new AudioStreamPlayer { Stream = поток, Autoplay = false };
        AddChild(игрок);
        игрок.Play();
        return игрок;
    }

    /// <summary>Отдать узлу то, что слышно сейчас. Зовётся не каждый кадр,
    /// а когда дом остановился на вопросе: громкости меняются с ходом,
    /// а не с кадром.</summary>
    public void Обновить(IReadOnlyList<Голос> голоса)
    {
        _ветер_сила = голоса.Where(г => г.что == Звук.ВЕТЕР)
                            .Select(г => г.громкость).DefaultIfEmpty(0).Max();
        // гулов может быть несколько — слышен самый близкий
        _гул_сила = голоса.Where(г => г.что == Звук.ГУЛ_ГЕНЕРАТОРА)
                          .Select(г => г.громкость).DefaultIfEmpty(0).Max();
    }

    public override void _Process(double delta)
    {
        _ветер_поток ??= _ветер.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _гул_поток ??= _гул.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        Ветер();
        Гул();
    }

    /// <summary>
    /// Ветер: белый шум, пропущенный через простой низкочастотный фильтр.
    ///
    /// Без фильтра это шипение, с ним — гул за окном. Коэффициент подобран
    /// на слух в том смысле, что его слышал только автор кода; звукорежиссёр
    /// заменит это целиком.
    /// </summary>
    private void Ветер()
    {
        if (_ветер_поток is null)
            return;
        int сколько = _ветер_поток.GetFramesAvailable();
        if (сколько <= 0)
            return;
        float громко = (float)_ветер_сила;
        if (громко < Порог)
        {
            Тишина(_ветер_поток, сколько);
            return;
        }
        for (int i = 0; i < сколько; i++)
        {
            double бел = _шум.RandfRange(-1.0f, 1.0f);
            _низ += (бел - _низ) * 0.06;          // низкочастотный фильтр
            float v = (float)(_низ * громко * 0.35);
            _ветер_поток.PushFrame(new Vector2(v, v));
        }
    }

    /// <summary>Гул: низкая синусоида плюс октава. Именно гул, а не тон:
    /// одна чистая частота звучит как сигнал тревоги, а не как движок
    /// за стеной.</summary>
    private void Гул()
    {
        if (_гул_поток is null)
            return;
        int сколько = _гул_поток.GetFramesAvailable();
        if (сколько <= 0)
            return;
        float громко = (float)_гул_сила;
        if (громко < Порог)
        {
            Тишина(_гул_поток, сколько);
            return;
        }
        for (int i = 0; i < сколько; i++)
        {
            _фаза += 1.0 / ЧАСТОТА;
            double t = _фаза * Mathf.Tau;
            double v = Mathf.Sin((float)(t * 52.0)) * 0.7
                     + Mathf.Sin((float)(t * 104.0)) * 0.25
                     + _шум.RandfRange(-0.05f, 0.05f);
            float s = (float)(v * громко * 0.22);
            _гул_поток.PushFrame(new Vector2(s, s));
        }
    }

    private static void Тишина(AudioStreamGeneratorPlayback поток, int сколько)
    {
        for (int i = 0; i < сколько; i++)
            поток.PushFrame(Vector2.Zero);
    }
}
