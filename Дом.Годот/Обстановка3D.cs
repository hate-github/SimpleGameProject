// Воздух подъезда (ГДД 23): туман, снег, свет.
//
// «Туман и снег как главные визуальные инструменты» — здесь они настоящие,
// объёмные, а не белая плёнка поверх картинки: туман стоит в воздухе
// и глотает лестницу, снег летит за окном и во дворе.
//
// Всё три числа приходят из `Дом.Экран.Подача` и, значит, из симуляции:
// в буран туман гуще, в затишье виднее, а свет гаснет вместе с домом.
// Ни одно не выставлено на глаз в сцене.
//
// Мягкое освещение из ГДД 23 здесь — это отсутствие солнца: в метель
// за окном нет ни теней, ни направления, только ровный серый свет.

using Godot;
using Дом.Экран;

namespace Дом.Годот;

public partial class Обстановка3D : Node3D
{
	private WorldEnvironment _среда = null!;
	private Godot.Environment _воздух = null!;
	private GpuParticles3D _снег = null!;
	private DirectionalLight3D _небо = null!;

	public override void _Ready()
	{
		_воздух = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color("#20242a"),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color("#5a6068"),
			AmbientLightEnergy = 0.30f,
			FogEnabled = true,
			FogLightColor = new Color("#4a5058"),
			FogDensity = 0.012f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
		};
		_среда = new WorldEnvironment { Environment = _воздух };
		AddChild(_среда);

		// ровный свет без солнца: в метель теней не бывает
		_небо = new DirectionalLight3D
		{
			LightColor = new Color("#8a94a0"),
			LightEnergy = 0.18f,
			RotationDegrees = new Vector3(-62, 34, 0),
			ShadowEnabled = false,
		};
		AddChild(_небо);

		_снег = Снег();
		AddChild(_снег);
	}

	/// <summary>
	/// Снег: частицы, а не шейдер.
	///
	/// На плоском экране снег можно было нарисовать шумом, и так оно
	/// и было сделано в первой, панельной версии. В объёме нельзя:
	/// снег должен лететь мимо, а не по стеклу, — иначе он читается
	/// как грязь на камере.
	/// </summary>
	private static GpuParticles3D Снег()
	{
		var вещество = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(12, 1, 8),
			Direction = new Vector3(0.3f, -1, 0.1f),
			Spread = 12,
			Gravity = new Vector3(0, -1.6f, 0),
			InitialVelocityMin = 0.8f,
			InitialVelocityMax = 1.8f,
			ScaleMin = 0.5f,
			ScaleMax = 1.4f,
		};
		var зерно = new QuadMesh { Size = new Vector2(0.045f, 0.045f) };
		зерно.Material = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 1, 1, 0.85f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
		};
		return new GpuParticles3D
		{
			Amount = 3000,
			Lifetime = 7.0,
			ProcessMaterial = вещество,
			DrawPass1 = зерно,
			Position = new Vector3(0, 9, 0),
			Emitting = true,
			Name = "снег",
		};
	}

	/// <summary>
	/// Держать метель за окном на высоте героя.
	///
	/// Точку считает `Мир.Снаружи`, и это важнее, чем кажется: частицы
	/// ни с чем не сталкиваются, и снег, поставленный «около героя»,
	/// идёт внутри подъезда — сквозь стены, потолок и самого человека.
	/// Снаружи он виден только в окно, и ровно так и задумано (ГДД 23).
	/// </summary>
	public void Следом(Vector3 где) => _снег.Position = где;

	/// <summary>
	/// Он вышел во двор или вернулся.
	///
	/// В подъезде туман глотает дальний конец лестницы. На дворе он
	/// глотает двор: в буран дальше десятка шагов не видно ничего,
	/// и ровно поэтому за сугробами не надо рисовать город. Плотность
	/// та же, что и была, помноженная, — иначе затишье и буран снаружи
	/// перестали бы отличаться друг от друга.
	/// </summary>
	public void НаУлице(bool да)
	{
		if (_улица == да)
			return;
		_улица = да;
		_воздух.FogDensity = _туман * (да ? 3.4f : 1.0f);
		_воздух.AmbientLightEnergy = _свет * (да ? 1.6f : 1.0f);
	}

	private bool _улица;
	private float _туман = 0.012f;
	private float _свет = 0.30f;

	public void Обновить(Обстановка что)
	{
		// туман в буран глотает лестницу, в затишье видно до конца
		// Туман в подъезде съедает дальний конец лестницы, а не руки.
		// Первый заход дал плотность вдесятеро больше нужной, и весь
		// экран был ровно белый — ни стен, ни дверей
		_туман = (float)(0.004 + что.туман * 0.012);
		_воздух.FogDensity = _туман * (_улица ? 3.4f : 1.0f);
		_воздух.FogLightColor = new Color("#3a4048").Lerp(
			new Color("#6a7078"), (float)что.свет);

		// свет: гаснет вместе с домом
		// Нижний предел — не «сколько света в буран», а «сколько нужно,
		// чтобы видеть комнату без окна». Прихожая и коридор внутренние,
		// и при 0.10 в них было черно: играть нельзя, а темнота ничего
		// не рассказывает — она просто мешает.
		_свет = (float)(0.17 + 0.22 * что.свет);
		_воздух.AmbientLightEnergy = _свет * (_улица ? 1.6f : 1.0f);
		_небо.LightEnergy = (float)(0.05 + 0.15 * что.свет);

		_снег.AmountRatio = (float)Mathf.Clamp(что.снег, 0.05, 1.0);
	}
}
