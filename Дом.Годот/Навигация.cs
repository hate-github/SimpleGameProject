// Навигация соседей (задание автора, п. 26): по навигационной сетке Godot,
// а не `position = target`.
//
// Сетка запекается из самого мира, когда он построен: пол, пандусы
// маршей, стены, мебель кладовых (`СЛОЙ_МЕБЕЛИ`). Не запекаются слои,
// которые двигаются или сами ходят: входная створка подъезда
// (`СЛОЙ_ДВЕРЕЙ` — она открывается сама, и сосед в неё проходит) и люди
// (`СЛОЙ_ЛЮДЕЙ`). Запекается в стороне от кадра, на потоке; пока сетки нет,
// соседи ставятся на место, как раньше, — и это видно в логе слежки.
//
// Запекается не весь двор: своя секция, подвал, задний двор с гаражом
// и выход — туда соседи и ходят. Соседние секции глухие, в них не заходят.
//
// Путь спрашивается у сервера навигации (`NavigationServer3D.MapGetPath`)
// один раз на переход и дальше просто проходится по точкам: соседи
// не толкаются друг с другом и с героем — сквозь идущего проходят,
// твёрдым он становится, когда встал.

using Godot;

namespace Дом.Годот;

public partial class Навигация : NavigationRegion3D
{
    /// <summary>Группа, из которой запекается сетка: мир и всё под ним.</summary>
    public const string ГРУППА = "навигация_дома";

    /// <summary>Готова ли сетка: до этого ходить некуда — ставят на место.</summary>
    public bool готова { get; private set; }

    /// <summary>Сколько точек у последнего пути — для слежки.</summary>
    public int точек { get; private set; }

    public static Навигация Завести(Node3D мир, Aabb где, uint слои)
    {
        var сетка = new NavigationMesh
        {
            AgentRadius = 0.3f,
            AgentHeight = 1.7f,
            AgentMaxClimb = 0.3f,
            AgentMaxSlope = 50f,
            CellSize = 0.1f,
            CellHeight = 0.1f,
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometryCollisionMask = слои,
            GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.GroupsWithChildren,
            GeometrySourceGroupName = ГРУППА,
            FilterBakingAabb = где,
        };
        var н = new Навигация { Name = "навигация", NavigationMesh = сетка };
        мир.AddToGroup(ГРУППА);
        мир.AddChild(н);
        return н;
    }

    public override void _Ready()
    {
        // сервер навигации считает клетками своей карты — те же, что у сетки,
        // иначе он ругается и сшивает куски неровно
        var карта = GetWorld3D().NavigationMap;
        NavigationServer3D.MapSetCellSize(карта, NavigationMesh.CellSize);
        NavigationServer3D.MapSetCellHeight(карта, NavigationMesh.CellHeight);
        BakeFinished += Запеклась;
    }

    /// <summary>
    /// Запечь, когда прежний мир уже убран. Мир перестраивается на месте:
    /// старые узлы уходят в конце кадра, и если запечь сразу, в сетку
    /// попадут обе постройки разом. Поэтому — через два кадра.
    /// </summary>
    public async void Запечь()
    {
        готова = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;
        BakeNavigationMesh(onThread: true);
    }

    /// <summary>Сетка запеклась — но карта сервера примет её только
    /// на своей синхронизации, в кадре физики. До того путь пуст.</summary>
    private async void Запеклась()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (!IsInsideTree())
            return;
        готова = true;
        GD.Print($"[навигация] сетка готова: {NavigationMesh.GetPolygonCount()} многоугольников");
    }

    /// <summary>
    /// Путь от точки к точке по сетке — или null, если сетки нет или
    /// дойти нельзя (тогда сосед встаёт на место сразу, как раньше).
    /// </summary>
    public Vector3[]? Путь(Vector3 откуда, Vector3 куда)
    {
        if (!готова)
            return null;
        var карта = GetWorld3D().NavigationMap;
        var путь = NavigationServer3D.MapGetPath(карта, откуда, куда, optimize: true);
        точек = путь.Length;
        if (путь.Length < 2)
            return null;
        // путь оборвался далеко от цели — дойти нельзя
        if (путь[^1].DistanceTo(куда) > 1.2f)
            return null;
        // сетка лежит на две клетки выше пола (проба: 0.2 м над полом первого
        // этажа) — по ней шёл бы человек, парящий над ступенями
        float над_полом = NavigationMesh.CellHeight * 2f;
        for (int i = 0; i < путь.Length; i++)
            путь[i] = путь[i] with { Y = путь[i].Y - над_полом };
        return путь;
    }

    /// <summary>Ближайшая к точке точка сетки — там, где можно стоять.</summary>
    public Vector3 На_сетке(Vector3 где)
        => готова ? NavigationServer3D.MapGetClosestPoint(GetWorld3D().NavigationMap, где) : где;
}
