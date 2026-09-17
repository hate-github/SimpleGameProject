// Пиксели: мир рисуется крупным зерном, как на старой приставке.
//
// Не уменьшенный вьюпорт, а шейдер поверх: он берёт уже нарисованный кадр,
// собирает его в квадраты и сжимает цвета до нескольких ступеней на канал
// с мелкой сеткой дизеринга. Лежит самым первым среди панелей, поэтому под
// него попадает только мир: подсказки, лист вопроса и края экрана
// (`ПодачаUI`) рисуются поверх и остаются чёткими.
//
// Шейдер написан латиницей: кириллицу шейдеры Godot не принимают.

using Godot;

namespace Дом.Годот;

public partial class ПиксельUI : ColorRect
{
    /// <summary>Сторона пикселя в точках экрана. 1 — без зерна.</summary>
    public int Размер { get; set; } = 3;

    /// <summary>Оттенков на канал. 0 — цвета как есть.</summary>
    public int Оттенков { get; set; } = 24;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // Не SetAnchorsPreset: тот в дереве сохраняет прежний прямоугольник,
        // а он нулевой — и фильтр оставался размером с точку
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Material = new ShaderMaterial { Shader = new Shader { Code = КОД } };
        Обновить();
    }

    /// <summary>Применить размер и оттенки. Фильтр, который ничего
    /// не меняет, не рисуется вовсе.</summary>
    public void Обновить()
    {
        if (Material is not ShaderMaterial м)
            return;
        м.SetShaderParameter("pixel", (float)System.Math.Max(1, Размер));
        м.SetShaderParameter("levels", (float)System.Math.Max(0, Оттенков));
        Visible = Размер > 1 || Оттенков > 0;
    }

    private const string КОД = """
        shader_type canvas_item;

        uniform sampler2D screen : hint_screen_texture, filter_nearest;
        uniform float pixel = 3.0;
        uniform float levels = 24.0;

        const float BAYER[16] = float[16](
            0.0, 8.0, 2.0, 10.0,
            12.0, 4.0, 14.0, 6.0,
            3.0, 11.0, 1.0, 9.0,
            15.0, 7.0, 13.0, 5.0);

        void fragment() {
            vec2 size = vec2(textureSize(screen, 0));
            vec2 cell = floor(FRAGCOORD.xy / pixel);
            vec2 uv = (cell + 0.5) * pixel / size;
            vec3 c = texture(screen, uv).rgb;
            if (levels > 0.0) {
                int i = int(mod(cell.x, 4.0)) + int(mod(cell.y, 4.0)) * 4;
                float d = (BAYER[i] / 16.0 - 0.5) / levels;
                c = floor((c + d) * levels + 0.5) / levels;
            }
            COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
        }
        """;
}
