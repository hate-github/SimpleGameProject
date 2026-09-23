// Аниматор (задание автора, п. 21): одно и то же у героя и у соседа.
//
// Что играть, решает `Дом.Экран.Анимации` — движение с предметом,
// `BreakLock(Crowbar)`. Здесь — как играть. Есть у персонажа модель
// с `AnimationPlayer` и в нём клип `BreakLock_Crowbar` или `BreakLock` —
// играет клип. Нет — заглушка: рука (гнездо предмета) и тело двигаются
// по простым формулам — удар, рычаг, шаг, дрожь. Ничего из этого
// не должно пережить приход настоящих анимаций, и не переживёт:
// заглушка включается только когда клипа нет.
//
// Разовые движения (достать, убрать, толкнуть) играются раз и возвращают
// к тому, что шло до них; по концу можно позвать дело — убрать предмет
// из руки, когда рука опустилась.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class Аниматор : Node
{
    /// <summary>Гнездо руки: заглушки двигают его.</summary>
    public Node3D? Рука { get; set; }

    /// <summary>Тело: качается на ходу, клонится от ран. У героя от первого
    /// лица тела нет.</summary>
    public Node3D? Тело { get; set; }

    /// <summary>Клипы модели персонажа — когда автор их сделает.</summary>
    public AnimationPlayer? Проигрыватель { get; set; }

    private Движение _сейчас = new(Анимация.Idle);
    private Движение? _разово;
    private System.Action? _по_концу;
    private double _t, _t_разово;
    private bool _клип;
    private Transform3D? _рука_покой, _тело_покой;

    /// <summary>Что играется сейчас: разовое, если идёт, иначе состояние.</summary>
    public Движение сейчас => _разово ?? _сейчас;

    /// <summary>Играется ли настоящий клип, а не заглушка.</summary>
    public bool клип => _клип;

    private const double РАЗОВО = 0.35;

    /// <summary>Перейти в состояние. То же самое — ничего не делает.</summary>
    public void Играть(Движение д)
    {
        if (д == _сейчас)
            return;
        _сейчас = д;
        _t = 0;
        if (_разово is null)
            _клип = Клип(д);
    }

    /// <summary>Сыграть раз и вернуться; <paramref name="по_концу"/> — когда
    /// кончится (или сразу, если новое разовое перебило старое).</summary>
    public void Разово(Движение д, System.Action? по_концу = null)
    {
        var было = _по_концу;
        _по_концу = null;
        было?.Invoke();
        _разово = д;
        _t_разово = 0;
        _по_концу = по_концу;
        _клип = Клип(д);
    }

    private bool Клип(Движение д)
    {
        if (Проигрыватель is null)
            return false;
        foreach (string имя in Анимации.Клипы(д))
            if (Проигрыватель.HasAnimation(имя))
            {
                Проигрыватель.Play(имя);
                return true;
            }
        return false;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_разово is not null)
        {
            _t_разово += delta;
            if (_t_разово >= РАЗОВО)
            {
                _разово = null;
                var дело = _по_концу;
                _по_концу = null;
                дело?.Invoke();
                _клип = Клип(_сейчас);
            }
        }
        if (Рука is not null)
        {
            _рука_покой ??= Рука.Transform;
            var (сдвиг, поворот) = _клип ? (Vector3.Zero, Vector3.Zero) : Рукой();
            var покой = _рука_покой.Value;
            Рука.Transform = new Transform3D(
                покой.Basis * Basis.FromEuler(поворот * (Mathf.Pi / 180f)),
                покой.Origin + сдвиг);
        }
        if (Тело is not null)
        {
            _тело_покой ??= Тело.Transform;
            var (сдвиг, наклон) = _клип ? (Vector3.Zero, 0f) : Телом();
            var покой = _тело_покой.Value;
            Тело.Transform = new Transform3D(
                покой.Basis * new Basis(Vector3.Forward, наклон * (Mathf.Pi / 180f)),
                покой.Origin + сдвиг);
        }
    }

    /// <summary>Заглушка руки: сдвиг (м) и поворот (градусы) от покоя.</summary>
    private (Vector3 сдвиг, Vector3 поворот) Рукой()
    {
        float t = (float)_t;
        if (_разово is { } р)
        {
            float к = (float)System.Math.Clamp(_t_разово / РАЗОВО, 0, 1);
            return р.что switch
            {
                // рука поднимается снизу — достал
                Анимация.Equip => (new Vector3(0, -0.35f * (1 - к), 0.05f * (1 - к)), new Vector3(-40 * (1 - к), 0, 0)),
                // опускается вниз — убрал
                Анимация.Unequip => (new Vector3(0, -0.35f * к, 0.05f * к), new Vector3(-40 * к, 0, 0)),
                // толкнул вперёд и обратно
                _ => (new Vector3(0, 0, -0.12f * Mathf.Sin(к * Mathf.Pi)), Vector3.Zero),
            };
        }
        return _сейчас.что switch
        {
            Анимация.Idle => (new Vector3(0, 0.004f * Mathf.Sin(t * 1.3f), 0), Vector3.Zero),
            Анимация.Walk => (new Vector3(0.01f * Mathf.Sin(t * 4f), 0.02f * Mathf.Abs(Mathf.Sin(t * 4f)), 0), Vector3.Zero),
            Анимация.Run => (new Vector3(0.02f * Mathf.Sin(t * 7f), 0.04f * Mathf.Abs(Mathf.Sin(t * 7f)), 0), new Vector3(8, 0, 0)),
            Анимация.Interact => (new Vector3(0, 0, -0.08f * Mathf.Abs(Mathf.Sin(t * 3f))), Vector3.Zero),
            // полез внутрь и обратно
            Анимация.Loot => (new Vector3(0, -0.15f * Mathf.Abs(Mathf.Sin(t * 2.6f)), -0.06f), new Vector3(-25, 0, 0)),
            // замах и удар
            Анимация.Attack => (Vector3.Zero, new Vector3(Удар(t, 0.7f, -70, 40), 0, 0)),
            // по замку: молоток бьёт, болторез и отмычка — жмут и крутят
            Анимация.BreakLock when _сейчас.ассет is "BoltCutter" or "Lockpick"
                => (new Vector3(0, 0, -0.05f), new Vector3(0, 0, 18 * Mathf.Sin(t * 5f))),
            Анимация.BreakLock => (new Vector3(0, 0, -0.04f), new Vector3(Удар(t, 0.75f, -50, 30), 0, 0)),
            // рычаг: налёг — отпустил
            Анимация.UseTool => (new Vector3(0, 0, -0.05f), new Vector3(0, 0, 25 * Mathf.Sin(t * 4.5f))),
            Анимация.Injured => (new Vector3(0, -0.05f + 0.01f * Mathf.Sin(t * 1.1f), 0), new Vector3(-10, 0, 0)),
            // рвётся из рук: мелко и часто
            Анимация.Restrained => (new Vector3(0.015f * Mathf.Sin(t * 23f), -0.12f + 0.01f * Mathf.Sin(t * 17f), 0.08f), new Vector3(20, 0, 0)),
            // Equip и Unequip — разовые, их заглушка выше
            _ => (Vector3.Zero, Vector3.Zero),
        };
    }

    /// <summary>Заглушка тела: сдвиг и наклон вбок (градусы).</summary>
    private (Vector3 сдвиг, float наклон) Телом()
    {
        float t = (float)_t;
        return _сейчас.что switch
        {
            Анимация.Walk => (new Vector3(0, 0.03f * Mathf.Abs(Mathf.Sin(t * 4f)), 0), 2 * Mathf.Sin(t * 4f)),
            Анимация.Run => (new Vector3(0, 0.05f * Mathf.Abs(Mathf.Sin(t * 7f)), 0), 3 * Mathf.Sin(t * 7f)),
            Анимация.Injured => (new Vector3(0, -0.04f, 0), 8f),
            Анимация.Restrained => (new Vector3(0.02f * Mathf.Sin(t * 19f), 0, 0), 4 * Mathf.Sin(t * 13f)),
            Анимация.Attack => (Vector3.Zero, 3 * Mathf.Sin(t * 9f)),
            Анимация.BreakLock or Анимация.UseTool or Анимация.Loot => (new Vector3(0, -0.03f, 0), 0f),
            _ => (new Vector3(0, 0.006f * Mathf.Sin(t * 1.3f), 0), 0f),
        };
    }

    /// <summary>Удар: медленный замах и быстрый удар, по кругу.</summary>
    private static float Удар(float t, float период, float замах, float удар)
    {
        float ф = (t % период) / период;
        return ф < 0.7f ? Mathf.Lerp(0, замах, ф / 0.7f) : Mathf.Lerp(замах, удар, (ф - 0.7f) / 0.3f);
    }
}
