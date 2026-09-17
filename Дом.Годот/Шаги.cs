// Шаги героя: по чему идёт — так и звучит (записи и разметка поверхностей —
// в `Звуки.cs`).

using Godot;

namespace Дом.Годот;

/// <summary>Шаги героя: одна нога — один источник, чтобы долгий
/// снежный шаг не обрывался следующим.</summary>
public partial class Шаги : Node
{
    private readonly AudioStreamPlayer[] _ноги = new AudioStreamPlayer[2];
    private AudioStreamPlayer _скрип = null!;
    private int _нога;
    private readonly RandomNumberGenerator _кость = new();

    // где в записи удар — с этого места и играем
    private const float УДАР_ПОЛ = 0.44f;
    private const float УДАР_СНЕГ_1 = 0.40f;
    private const float УДАР_СНЕГ_2 = 0.20f;
    private const float УДАР_СКРИП = 0.08f;

    /// <summary>Громкость шагов целиком, в децибелах.</summary>
    public float Громкость { get; set; } = 0f;

    public override void _Ready()
    {
        for (int i = 0; i < _ноги.Length; i++)
        {
            _ноги[i] = new AudioStreamPlayer { Name = $"нога{i}" };
            AddChild(_ноги[i]);
        }
        _скрип = new AudioStreamPlayer { Name = "скрип", Stream = Звуки.Взять("шаги_скрип") };
        AddChild(_скрип);
        _кость.Randomize();
    }

    public void Шагнуть(string поверхность)
    {
        var нога = _ноги[_нога];
        _нога = (_нога + 1) % _ноги.Length;
        switch (поверхность)
        {
            case Поверхность.СНЕГ:
                bool первый = _нога == 0;
                Играть(нога, Звуки.Взять(первый ? "шаги_снег_1" : "шаги_снег_2"),
                       первый ? УДАР_СНЕГ_1 : УДАР_СНЕГ_2, -2f, 0.92f, 1.08f);
                break;
            case Поверхность.ДЕРЕВО:
                // старые доски: шаг глуше, и через раз-другой скрипят
                Играть(нога, Звуки.Взять("шаги_пол"), УДАР_ПОЛ, -9f, 0.78f, 0.9f);
                if (!_скрип.Playing && _кость.Randf() < 0.3f)
                    Играть(_скрип, _скрип.Stream, УДАР_СКРИП, -11f, 0.9f, 1.1f);
                break;
            default:
                Играть(нога, Звуки.Взять("шаги_пол"), УДАР_ПОЛ, -5f, 0.92f, 1.08f);
                break;
        }
    }

    private void Играть(AudioStreamPlayer кем, AudioStream? что, float откуда,
                        float громкость, float высота_от, float высота_до)
    {
        if (что is null)
            return;
        кем.Stream = что;
        кем.VolumeDb = громкость + Громкость;
        кем.PitchScale = _кость.RandfRange(высота_от, высота_до);
        кем.Play(откуда);
    }
}
