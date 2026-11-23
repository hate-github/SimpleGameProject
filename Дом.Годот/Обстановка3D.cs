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
		_небо_вещество = new ShaderMaterial { Shader = new Shader { Code = НЕБО } };
		_воздух = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky
			{
				SkyMaterial = _небо_вещество,
				RadianceSize = Sky.RadianceSizeEnum.Size64,
			},
			BackgroundColor = new Color("#20242a"),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color("#5a6068"),
			AmbientLightEnergy = 0.30f,
			FogEnabled = true,
			FogLightColor = new Color("#4a5058"),
			FogDensity = 0.012f,
			FogSkyAffect = 0.3f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
		};
		Небо_заново();
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
		_цвет_тумана = new Color("#3a4048").Lerp(
			new Color("#6a7078"), (float)что.свет);

		// свет: гаснет вместе с домом
		// Нижний предел — не «сколько света в буран», а «сколько нужно,
		// чтобы видеть комнату без окна». Прихожая и коридор внутренние,
		// и при 0.10 в них было черно: играть нельзя, а темнота ничего
		// не рассказывает — она просто мешает.
		_свет = (float)(0.17 + 0.22 * что.свет);
		_воздух.AmbientLightEnergy = _свет * (_улица ? 1.6f : 1.0f);
		_солнце = (float)(0.05 + 0.15 * что.свет);

		_снег.AmountRatio = (float)Mathf.Clamp(что.снег, 0.05, 1.0);

		// небо: в затишье и до метели — облака с просветами, в буран —
		// ровная пелена, и туман застилает его целиком
		_пелена = (float)Mathf.Clamp(что.туман * 0.9 + что.снег * 0.5, 0.0, 1.0);
		_облака = (float)Mathf.Clamp(0.5 + что.снег * 0.7, 0.0, 1.0);
		_воздух.FogSkyAffect = 0.2f + 0.8f * _пелена;
		Небо_заново();
	}

	// ------------------------------------------------------------ небо

	private ShaderMaterial _небо_вещество = null!;
	private float _пелена = 0.15f;      // 0 — чистое небо, 1 — метель стеной
	private float _облака = 0.55f;      // сколько неба закрыто облаками
	private float _ночь;                // 0 — день, 1 — ночь
	private float _час = 12f;
	private float _солнце = 0.18f;      // ровный свет дня — к ночи гаснет
	private Color _цвет_тумана = new("#4a5058");   // днём; ночью — темнее

	/// <summary>
	/// Который час у героя — небо по нему: зимний день короткий, светло
	/// с восьми до пяти, солнце низко на юге, ночью темно и в просветах
	/// звёзды. Зовётся каждый кадр — перекрашивается, только когда
	/// время ушло заметно: небо пересчитывает отражения, и делать это
	/// каждый кадр незачем.
	/// </summary>
	public void Час(double час)
	{
		float ч = (float)(((час % 24) + 24) % 24);
		if (Mathf.Abs(ч - _час) < 0.05f)
			return;
		_час = ч;
		float утро = Mathf.SmoothStep(7.0f, 9.0f, ч);
		float вечер = 1f - Mathf.SmoothStep(16.0f, 18.0f, ч);
		_ночь = 1f - Mathf.Min(утро, вечер);
		Небо_заново();
	}

	/// <summary>Число неба — в шейдер; свет без солнца гаснет к ночи.</summary>
	private void Небо_заново()
	{
		// солнце идёт с востока на запад низко над горизонтом: в полдень
		// градусов двенадцать, на рассвете и закате — у самой земли
		float доля = Mathf.Clamp((_час - 8f) / 9f, 0f, 1f);
		float азимут = Mathf.Lerp(-70f, 70f, доля);
		float высота = Mathf.Max(-4f, Mathf.Sin(доля * Mathf.Pi) * 12f);
		var к_солнцу = new Vector3(Mathf.Sin(Mathf.DegToRad(азимут)), 0, -Mathf.Cos(Mathf.DegToRad(азимут)))
			.Normalized() * Mathf.Cos(Mathf.DegToRad(высота));
		к_солнцу.Y = Mathf.Sin(Mathf.DegToRad(высота));
		_небо_вещество.SetShaderParameter("sun_dir", к_солнцу);
		// закат розовит горизонт, к ночи небо уходит в синюю темноту
		float закат = 1f - Mathf.Abs(доля * 2f - 1f);
		закат = 1f - Mathf.SmoothStep(0.0f, 0.35f, закат);
		_небо_вещество.SetShaderParameter("dusk", закат * (1f - _ночь));
		_небо_вещество.SetShaderParameter("night", _ночь);
		_небо_вещество.SetShaderParameter("cover", _облака);
		_небо_вещество.SetShaderParameter("veil", _пелена);
		// Туман к ночи темнеет вместе с небом, на закате теплеет. Иначе
		// он остаётся дневным серым и ночью высветляет и небо, и двор:
		// первая проба дала в девять вечера сиреневые сумерки.
		var туман = _цвет_тумана.Lerp(new Color("#8a6a62"), закат * (1f - _ночь) * 0.3f);
		_воздух.FogLightColor = туман.Lerp(new Color("#0c1018"), _ночь * 0.8f);
		// горизонт тянется к цвету тумана, чтобы край неба не резал
		_небо_вещество.SetShaderParameter("fog_color", _воздух.FogLightColor);
		if (_небо is not null)
			_небо.LightEnergy = _солнце * (1f - 0.7f * _ночь);
	}

	/// <summary>
	/// Небо — шейдером, без картинки: зимнее, низкое, в облаках. Облака —
	/// шум, спроецированный на купол; солнце — размытое пятно сквозь
	/// пелену; ночью — тёмно-синее, со звёздами в просветах. Ничто
	/// не движется во времени — небо пересчитывается, только когда
	/// меняются погода или час (`Час`).
	/// </summary>
	private const string НЕБО = @"
shader_type sky;

uniform vec3 top_color : source_color = vec3(0.40, 0.48, 0.58);
uniform vec3 horizon_color : source_color = vec3(0.74, 0.77, 0.80);
uniform vec3 ground_color : source_color = vec3(0.50, 0.52, 0.55);
uniform vec3 cloud_color : source_color = vec3(0.78, 0.80, 0.83);
uniform vec3 fog_color : source_color = vec3(0.42, 0.46, 0.50);
uniform vec3 sun_color : source_color = vec3(1.0, 0.86, 0.70);
uniform vec3 sun_dir = vec3(0.0, 0.2, -1.0);
uniform float cover = 0.55;
uniform float veil = 0.15;
uniform float night = 0.0;
uniform float dusk = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
	           mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int k = 0; k < 5; k++) { v += a * noise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
	return v;
}

void sky() {
	vec3 d = EYEDIR;
	float h = d.y;
	vec3 sd = normalize(sun_dir);
	float s = max(dot(d, sd), 0.0);

	vec3 horizon = mix(horizon_color, vec3(0.90, 0.62, 0.52), dusk * 0.8);
	vec3 col = mix(horizon, top_color, smoothstep(0.0, 0.55, h));
	if (h < 0.0)
		col = mix(horizon, ground_color, smoothstep(0.0, 0.12, -h));
	col += sun_color * (pow(s, 6.0) * 0.18 + pow(s, 400.0) * 0.9) * (1.0 - cover * 0.6);

	float clouds = 0.0;
	if (h > 0.0) {
		vec2 uv = d.xz / (h + 0.12) * 1.3;
		float c = fbm(uv);
		clouds = smoothstep(1.0 - cover - 0.1, 1.0 - cover + 0.3, c) * smoothstep(0.0, 0.1, h);
		vec3 cc = cloud_color * (0.78 + 0.3 * fbm(uv * 2.3 + 4.0));
		cc = mix(cc, vec3(0.90, 0.72, 0.62), dusk * 0.35);
		cc += sun_color * pow(s, 5.0) * 0.12;
		col = mix(col, cc, clouds);
	}

	col = mix(col, mix(horizon, fog_color, 0.5), veil);
	col *= mix(1.0, 0.07, night);
	col += vec3(0.01, 0.015, 0.035) * night;
	if (h > 0.0) {
		vec2 st = d.xz / (h + 0.25) * 140.0;
		float star = step(0.9965, hash(floor(st)));
		col += vec3(0.75) * star * night * (1.0 - clouds) * (1.0 - veil) * smoothstep(0.05, 0.3, h);
	}
	COLOR = col;
}
";
}
