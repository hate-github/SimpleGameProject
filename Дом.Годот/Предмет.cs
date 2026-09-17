// Предмет: то, к чему подходят и жмут E.
//
// Замок, коробка, верстак, выключатель, лестница в яму. Всё, что делает
// дверь квартиры, делает и предмет: говорит, что он такое, и что-то
// делает по E. Разница одна: дверь отвечает на вопрос дома, а предмет —
// действие самого героя, как карман.
//
// Это область, а не тело: луч видит её (`RayCast3D.CollideWithAreas`),
// а ходить она не мешает. Мешает ходить сама вещь — у неё своя форма.

using Godot;

namespace Дом.Годот;

public partial class Предмет : Area3D
{
    /// <summary>Что написать под прицелом.</summary>
    public System.Func<string> Подсказка { get; set; } = () => "";

    /// <summary>Что сделать по E.</summary>
    public System.Action Действие { get; set; } = () => { };

    /// <summary>Видит ли его луч. Выключенный предмет не заслоняет
    /// того, что за ним: замок, например, пока ворота открыты.</summary>
    public bool Активен
    {
        get => CollisionLayer != 0;
        set => CollisionLayer = value ? 1u : 0u;
    }

    /// <summary>
    /// Поставить область размером с габарит вещи.
    ///
    /// `запас` — насколько раздуть её в метрах: замок и выключатель
    /// мельче пальца, и попасть в них прицелом без запаса нельзя.
    /// </summary>
    public static Предмет Вокруг(Node3D куда, Aabb габарит, float запас,
                                 string имя, Vector3? вынос = null)
    {
        var размер = габарит.Size + Vector3.One * запас;
        var п = new Предмет
        {
            Name = имя,
            Position = габарит.GetCenter() + (вынос ?? Vector3.Zero),
        };
        п.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = размер },
        });
        куда.AddChild(п);
        return п;
    }
}
