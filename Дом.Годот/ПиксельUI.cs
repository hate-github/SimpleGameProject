// Пиксели: мир рисуется крупным зерном, как на старой приставке.
//
// Не уменьшенный вьюпорт, а шейдер поверх: он берёт уже нарисованный кадр,
// собирает его в квадраты и сжимает цвета до нескольких ступеней на канал
// с мелкой сеткой дизеринга. Лежит самым первым среди панелей, поэтому под
// него попадает только мир: подсказки, лист вопроса и края экрана
// (`ПодачаUI`) рисуются поверх и остаются чёткими.
//
// Зерно задаётся **строками кадра**, а не точками экрана. Первая версия
// брала три точки — и на экране 1920×1080 это 360 строк: зерно было,
// но на пёстрых текстурах его не было видно. Двести строк — это пять точек
// на таком экране и четыре на окне в 800, и приставка узнаётся сразу.
// Автору это показалось слишком крупным: по умолчанию теперь 320 строк —
// три точки на 1080.
//
// Сетка дизеринга — вторая половина «зернистости»: при шестнадцати
// ступенях она покрывала кадр ровной точечной рябью, и мелкое зерно
// её не убирало. Поэтому у неё своя сила (`Дизеринг`): единица — полная
// сетка, как было, ноль — голые ступени цвета.
//
// Шейдер написан латиницей: кириллицу шейдеры Godot не принимают.

using Godot;

namespace Дом.Годот;

public partial class ПиксельUI : ColorRect
{
    /// <summary>Строк кадра по вертикали. 0 — без зерна.</summary>
    public int Строк { get; set; } = 320;

    /// <summary>Оттенков на канал. 0 — цвета как есть.</summary>
    public int Оттенков { get; set; } = 16;

    /// <summary>Сила сетки дизеринга: 1 — полная, 0 — без неё.</summary>
    public float Дизеринг { get; set; } = 0.35f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // Не SetAnchorsPreset: тот в дереве сохраняет прежний прямоугольник,
        // а он нулевой — и фильтр оставался размером с точку
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Material = new ShaderMaterial { Shader = new Shader { Code = КОД } };
        Обновить();
    }

    /// <summary>Применить строки и оттенки. Фильтр, который ничего
    /// не меняет, не рисуется вовсе.</summary>
    public void Обновить()
    {
        if (Material is not ShaderMaterial м)
            return;
        м.SetShaderParameter("lines", (float)System.Math.Max(0, Строк));
        м.SetShaderParameter("levels", (float)System.Math.Max(0, Оттенков));
        м.SetShaderParameter("dither", System.Math.Clamp(Дизеринг, 0f, 1f));
        Visible = Строк > 0 || Оттенков > 0;
    }

    /// <summary>Сторона зерна в точках при такой высоте кадра — та же,
    /// что считает шейдер. Для лога.</summary>
    public int Зерно(float высота)
        => Строк <= 0 ? 1 : System.Math.Max(1, (int)System.Math.Floor(высота / Строк + 0.5));

    private const string КОД = """
        shader_type canvas_item;

        uniform sampler2D screen : hint_screen_texture, filter_nearest;
        uniform float lines = 320.0;
        uniform float levels = 16.0;
        uniform float dither = 0.35;

        const float BAYER[16] = float[16](
            0.0, 8.0, 2.0, 10.0,
            12.0, 4.0, 14.0, 6.0,
            3.0, 11.0, 1.0, 9.0,
            15.0, 7.0, 13.0, 5.0);

        void fragment() {
            vec2 size = vec2(textureSize(screen, 0));
            float px = lines > 0.0 ? max(1.0, floor(size.y / lines + 0.5)) : 1.0;
            vec2 cell = floor(FRAGCOORD.xy / px);
            vec2 uv = (cell + 0.5) * px / size;
            vec3 c = texture(screen, uv).rgb;
            if (levels > 0.0) {
                int i = int(mod(cell.x, 4.0)) + int(mod(cell.y, 4.0)) * 4;
                float d = (BAYER[i] / 16.0 - 0.5) / levels * dither;
                c = floor((c + d) * levels + 0.5) / levels;
            }
            COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
        }
        """;
}
