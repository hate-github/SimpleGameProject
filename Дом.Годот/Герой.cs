// Герой от первого лица (ГДД 23).
//
// Ходит, смотрит, стучит в двери. Ничего не решает: подойти к двери —
// это не ответ на вопрос, а способ его задать. Что случится дальше,
// по-прежнему решает дом.
//
// Тело здесь тоже читается (ГДД 23): замёрзший идёт медленнее, у голодного
// шатается взгляд. Числа для этого приходят из `Дом.Экран.Подача` —
// те же самые, что красят края экрана, и другого источника у них нет.
//
// Мышь захватывается, пока никакой экран не открыт: телефон, карта
// и развёрнутый список вариантов отпускают её обратно, потому что в них
// тыкают курсором. Свёрнутый лист вопроса — не отпускает: при нём ходят.

using Godot;

namespace Дом.Годот;

public partial class Герой : CharacterBody3D
{
	/// <summary>Шаг обычного человека — не бегуна.</summary>
	[Export] public float Шаг { get; set; } = 2.4f;

	[Export] public float Чувствительность { get; set; } = 0.0022f;

	private Camera3D _глаза = null!;
	private double _часы;

	/// <summary>Насколько плохо телу: замедляет и качает взгляд.
	/// Ставится корневым узлом из `Подача.Видно`.</summary>
	public double Дрожь { get; set; }

	/// <summary>Замедление от усталости и холода (ГДД 23).</summary>
	public double Тяжесть { get; set; }

	/// <summary>Ходить и вертеть головой можно, только когда не открыт
	/// ни один экран.</summary>
	public bool Свободен { get; set; } = true;

	/// <summary>На что смотрит — квартира под прицелом или ничего.</summary>
	public int? Перед_дверью { get; private set; }

	/// <summary>Смотрит ли на входную дверь подъезда.</summary>
	public bool Перед_выходом { get; private set; }

	/// <summary>На какую кладовую смотрит — во дворе их пять.</summary>
	public string? Перед_кладовой { get; private set; }

	public Camera3D глаза => _глаза;

	public override void _Ready()
	{
		var форма = new CollisionShape3D
		{
			Shape = new CapsuleShape3D { Height = 1.75f, Radius = 0.3f },
			Position = new Vector3(0, 0.875f, 0),
		};
		AddChild(форма);

		_глаза = new Camera3D
		{
			Position = new Vector3(0, 1.62f, 0),   // рост глаз, не макушки
			Fov = 72,
			Far = 60,
		};
		AddChild(_глаза);

		_луч = new RayCast3D
		{
			TargetPosition = new Vector3(0, 0, -2.2f),  // рука дотянется
			Enabled = true,
			// своя дверь распахнута, и вместо створки в проёме стоит
			// область: тело в нём не пропустило бы человека домой
			CollideWithAreas = true,
		};
		_глаза.AddChild(_луч);

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private RayCast3D _луч = null!;

	/// <summary>Выпрямить взгляд: не вверх и не в пол. Зовётся, когда
	/// человека ставят на новое место, — иначе он смотрит туда же, куда
	/// смотрел в конце прошлой жизни.</summary>
	public void Прямо()
	{
		if (_глаза is not null)
			_глаза.Rotation = Vector3.Zero;
		_с_захвата = 0;
	}

	/// <summary>
	/// Сколько секунд назад захватили мышь.
	///
	/// Первое движение после захвата — не движение: система уводит курсор
	/// в середину окна, и разница приходит сразу на пол-экрана. Взгляд
	/// от этого разворачивает в потолок, и было это видно каждый раз,
	/// когда игра стартовала не по щелчку в окно.
	/// </summary>
	private double _с_захвата;

	private void Следить_за_захватом(double delta)
	{
		bool взята = Input.MouseMode == Input.MouseModeEnum.Captured;
		_с_захвата = взята ? _с_захвата + delta : 0;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventMouseMotion м && Свободен && _с_захвата > 0.25
			&& Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			RotateY(-м.Relative.X * Чувствительность);
			_глаза.RotateX(-м.Relative.Y * Чувствительность);
			_глаза.Rotation = new Vector3(
				Mathf.Clamp(_глаза.Rotation.X, -1.4f, 1.4f), 0, 0);
		}
		else if (e is InputEventKey к && к.Pressed && !к.Echo
				 && к.Keycode == Key.Escape)
		{
			// Escape отпускает мышь — иначе окно не закрыть
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		_часы += delta;
		Следить_за_захватом(delta);

		var v = Velocity;
		v.Y -= 12.0f * (float)delta;                   // тяжесть

		if (Свободен)
		{
			var куда = Vector2.Zero;
			if (Input.IsPhysicalKeyPressed(Key.W)) куда.Y -= 1;
			if (Input.IsPhysicalKeyPressed(Key.S)) куда.Y += 1;
			if (Input.IsPhysicalKeyPressed(Key.A)) куда.X -= 1;
			if (Input.IsPhysicalKeyPressed(Key.D)) куда.X += 1;
			куда = куда.Normalized();

			// замёрзший и усталый идёт медленнее — это и есть «замедление»
			float скорость = Шаг * (float)(1.0 - 0.45 * Тяжесть);
			var шаг = (Transform.Basis * new Vector3(куда.X, 0, куда.Y))
					  .Normalized() * скорость;
			v.X = шаг.X;
			v.Z = шаг.Z;
		}
		else
		{
			v.X = 0;
			v.Z = 0;
		}

		Velocity = v;
		UpDirection = Vector3.Up;
		FloorMaxAngle = Mathf.DegToRad(60);            // по ступеням лезем сами
		MoveAndSlide();

		// взгляд качает от холода и паники: мелко и медленно, не тряской
		float кач = (float)Дрожь * 0.012f;
		_глаза.Position = new Vector3(
			Mathf.Sin((float)_часы * 1.7f) * кач,
			1.62f + Mathf.Sin((float)_часы * 2.3f) * кач,
			0);

		Перед_дверью = null;
		Перед_выходом = false;
		Перед_кладовой = null;
		if (_луч.IsColliding() && _луч.GetCollider() is Node узел)
		{
			string имя = узел.Name.ToString();
			if (имя.StartsWith("дверь", StringComparison.Ordinal)
				&& int.TryParse(имя[5..], out int кв))
				Перед_дверью = кв;
			else if (имя == "выход")
				Перед_выходом = true;
			else if (имя.StartsWith("кладовая", StringComparison.Ordinal))
				Перед_кладовой = имя[8..];
		}
	}
}
