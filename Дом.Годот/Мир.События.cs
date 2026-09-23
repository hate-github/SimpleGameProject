// Мир за стенами дома: здания и то, что с ними делает летопись
// (задание автора, п. 9; `Дом.Ядро.Летопись`, `data/мир.json`).
//
// Место события — узел мира. Соседние подъезды — группы `секция0`
// и `секция1` из `локация.tscn` (их коробки мир и так строит); далёкие
// здания — гнёзда `здание_<имя>` во дворе той же сцены: коробка-силуэт
// размером с масштаб гнезда, за валом сугробов, в тумане. Двигать их
// мышью в редакторе, как и всё во дворе.
//
// Что с местом — решает не узел, а летопись: `СостояниеМира` на утро
// дня. Узел только красит: горит — зарево, дым и рыжие окна; сгорело —
// копоть, чёрные окна и тонкий дым; разрушено — копоть и половина
// высоты. Цело — как построено: новая жизнь начинается с первого дня,
// и мир возвращается к целому, потому что свёртка на первый день пуста.

using Godot;
using Дом.Ядро;

namespace Дом.Годот;

public partial class Мир
{
    // место → его меши и то, какими они были построены
    private readonly Dictionary<string, List<MeshInstance3D>> _места_мира = new(StringComparer.Ordinal);
    private readonly Dictionary<MeshInstance3D, (Color цвет, float высота)> _было = new();
    private readonly Dictionary<string, Пожар> _пожары = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _здания = new(StringComparer.Ordinal);
    private readonly HashSet<MeshInstance3D> _окна = new();

    private static readonly Color КОПОТЬ_СТЕН = new("#1c1814");
    private static readonly Color ЧЁРНОЕ_ОКНО = new(0.02f, 0.02f, 0.02f, 0.9f);
    private static readonly Color ЗАРЕВО = new("#ff7a2a");

    /// <summary>Какие места мира построены — для проверки и подсказок.</summary>
    public IReadOnlyCollection<string> Места_мира => _места_мира.Keys;

    /// <summary>Запомнить меш как часть места: коробки соседнего подъезда,
    /// силуэт далёкого здания. Окно горит заревом и чернеет, стена коптится.</summary>
    private void К_месту(string место, MeshInstance3D? м, bool окно = false)
    {
        if (м is null)
            return;
        if (!_места_мира.TryGetValue(место, out var с))
            _места_мира[место] = с = new List<MeshInstance3D>();
        с.Add(м);
        if (окно)
            _окна.Add(м);
    }

    /// <summary>Новая постройка мира: всё прежнее — забыть.</summary>
    private void Забыть_места()
    {
        _места_мира.Clear();
        _было.Clear();
        _пожары.Clear();
        _здания.Clear();
        _окна.Clear();
    }

    /// <summary>
    /// Далёкие здания — по гнёздам `здание_<имя>`: коробка размером
    /// с масштаб гнезда, стоит на земле. Твёрдости у неё нет — она за валом,
    /// дойти до неё нельзя.
    /// </summary>
    private void Здания()
    {
        _здания.Clear();
        foreach (var (имя, где) in Гнёзда("здание_"))
        {
            string место = имя["здание_".Length..];
            var р = где.Basis.Scale;
            if (р.X <= 0.1f || р.Y <= 0.1f || р.Z <= 0.1f)
            {
                GD.PrintErr($"локация.tscn: у «{имя}» масштаб {р} — не здание");
                continue;
            }
            var узел = new Node3D
            {
                Name = имя,
                Position = где.Origin,
                Rotation = new Vector3(0, где.Basis.Orthonormalized().GetEuler().Y, 0),
            };
            AddChild(узел);
            var м = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = р },
                MaterialOverride = Материал(new Color("#34332f"), 0.95f),
                Position = new Vector3(0, р.Y / 2, 0),
            };
            узел.AddChild(м);
            // окна — тёмные полосы по этажам: издали здание читается
            // зданием, а не коробкой
            int этажей = Math.Max(1, (int)(р.Y / ЭТАЖ));
            for (int э = 0; э < этажей; э++)
            {
                var полоса = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(р.X * 0.9f, 0.9f, р.Z + 0.06f) },
                    MaterialOverride = Стекло_далёкое(),
                    Position = new Vector3(0, ЭТАЖ * э + 1.5f, 0),
                };
                узел.AddChild(полоса);
                К_месту(место, полоса, окно: true);
            }
            К_месту(место, м);
            _здания[место] = узел;
        }
    }

    private static StandardMaterial3D Стекло_далёкое() => new()
    {
        AlbedoColor = new Color("#1d2126"),
        Roughness = 0.4f,
    };

    /// <summary>
    /// Показать мир на это утро. Каждое место красится заново от того,
    /// каким было построено, — так мир и возвращается к целому.
    /// </summary>
    public void Мир_на(СостояниеМира с)
    {
        foreach (var (место, меши) in _места_мира)
        {
            var з = с.Здание(место);
            foreach (var м in меши)
            {
                if (м.MaterialOverride is not StandardMaterial3D мат)
                    continue;
                if (!_было.TryGetValue(м, out var было))
                {
                    было = (мат.AlbedoColor, м.Scale.Y);
                    _было[м] = было;
                }
                bool окно = _окна.Contains(м);
                мат.EmissionEnabled = false;
                мат.AlbedoColor = з switch
                {
                    Здание.ГОРИТ => окно ? ЗАРЕВО : было.цвет.Lerp(КОПОТЬ_СТЕН, 0.45f),
                    Здание.СГОРЕЛО or Здание.РАЗРУШЕНО => окно ? ЧЁРНОЕ_ОКНО : КОПОТЬ_СТЕН,
                    _ => было.цвет,
                };
                if (з == Здание.ГОРИТ && окно)
                {
                    мат.EmissionEnabled = true;
                    мат.Emission = ЗАРЕВО;
                    мат.EmissionEnergyMultiplier = 1.6f;
                }
            }
            // разрушенное здание — половина высоты: стены рухнули
            if (_здания.TryGetValue(место, out var здание))
                здание.Scale = new Vector3(1, з == Здание.РАЗРУШЕНО ? 0.45f : 1f, 1);
            Огонь(место, з);
        }
    }

    /// <summary>Зарево и дым над местом: горит — сильно, сгорело или рухнуло —
    /// тонкая струйка, цело — ничего.</summary>
    private void Огонь(string место, Здание з)
    {
        if (!_пожары.TryGetValue(место, out var п))
        {
            if (з == Здание.ЦЕЛО || Рамка(место) is not Aabb р)
                return;
            п = new Пожар { Name = $"пожар_{место}" };
            AddChild(п);
            п.Поставить(р);
            _пожары[место] = п;
        }
        п.Как(з switch
        {
            Здание.ГОРИТ => Пожар.Сила.ГОРИТ,
            Здание.СГОРЕЛО or Здание.РАЗРУШЕНО => Пожар.Сила.ТЛЕЕТ,
            _ => Пожар.Сила.НЕТ,
        });
    }

    /// <summary>Рамка места в мире — по всем его мешам.</summary>
    private Aabb? Рамка(string место)
    {
        if (!_места_мира.TryGetValue(место, out var меши) || меши.Count == 0)
            return null;
        Aabb? р = null;
        foreach (var м in меши)
        {
            if (!м.IsInsideTree())
                continue;
            var в_мире = м.GlobalTransform * м.GetAabb();
            р = р is Aabb а ? а.Merge(в_мире) : в_мире;
        }
        return р;
    }
}
