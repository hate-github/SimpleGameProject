// Сохранение наследия (ГДД 24).
//
// Одна жизнь — одно сохранение, автоматическое, перезаписывается при каждом
// сне; загрузить «раньше» нельзя, смерть необратима. Между жизнями остаётся
// **только переносимое** — то есть ровно `Наследие` и ничего больше.
//
// Отсюда устройство файла: в нём нет ни дома, ни соседей, ни дня. Дом
// сохраняется отдельно (`Сохранение.cs`) и живёт ровно одну жизнь;
// наследие переживает все. Смешать их в один файл было бы удобнее на день
// и неверно навсегда: тогда «загрузить прошлую жизнь» стало бы возможным,
// а вся игра держится на том, что нельзя.
//
// Формат — JSON, читаемый глазом. Сейв, который нельзя открыть блокнотом
// и понять, невозможно и починить, когда он однажды сломается.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Дом.Ядро;

public static class Хранение
{
    /// <summary>Версия формата: наследие переживает не только жизни,
    /// но и правки кода, и когда-нибудь его придётся читать старым.</summary>
    public const int ВЕРСИЯ = 1;

    public static JsonObject Сложить(Наследие н)
    {
        var уровни = new JsonObject();
        foreach (var к in н.уровни.Ключи)
            уровни[к.ToString()] = н.уровни[к];

        var записи = new JsonArray();
        foreach (var з in н.записи)
            записи.Add(new JsonObject
            {
                ["жизнь"] = з.жизнь,
                ["день"] = з.день,
                ["вид"] = з.вид.ToString(),
                ["кто"] = з.кто,
                ["где"] = з.где,
                ["текст"] = з.текст,
            });

        var отголоски = new JsonArray();
        foreach (var о in н.отголоски)
            отголоски.Add(new JsonObject
            {
                ["квартира"] = о.квартира,
                ["жизнь"] = о.жизнь,
                ["текст"] = о.текст,
            });

        return new JsonObject
        {
            ["версия"] = ВЕРСИЯ,
            ["очки"] = н.очки,
            ["уровни"] = уровни,
            ["карман"] = н.карман,
            ["рассудок"] = н.рассудок,
            ["жизней"] = н.жизней,
            ["рекорд"] = н.рекорд,
            ["нирвана"] = н.нирвана,
            ["знает_слово"] = н.знает_слово,
            ["записи"] = записи,
            ["отголоски"] = отголоски,
        };
    }

    public static Наследие Разобрать(JsonObject о)
    {
        int версия = о["версия"]?.GetValue<int>() ?? 0;
        if (версия != ВЕРСИЯ)
            throw new InvalidDataException(
                $"наследие версии {версия}, а читаем {ВЕРСИЯ}");

        var н = new Наследие
        {
            очки = (int)Поле(о, "очки").GetValue<int>(),
            карман = Поле(о, "карман").GetValue<int>(),
            рассудок = Поле(о, "рассудок").GetValue<double>(),
            жизней = Поле(о, "жизней").GetValue<int>(),
            рекорд = Поле(о, "рекорд").GetValue<int>(),
            нирвана = Поле(о, "нирвана").GetValue<bool>(),
            знает_слово = Поле(о, "знает_слово").GetValue<bool>(),
        };

        foreach (var п in Поле(о, "уровни").AsObject())
            н.уровни[Enum.Parse<Навык>(п.Key)] = п.Value!.GetValue<int>();

        foreach (var з in Поле(о, "записи").AsArray())
            н.записи.Add(new ЗаписьПамяти(
                Поле(з!.AsObject(), "жизнь").GetValue<int>(),
                Поле(з.AsObject(), "день").GetValue<int>(),
                Enum.Parse<ВидЗаписи>(Поле(з.AsObject(), "вид").GetValue<string>()),
                з["кто"]?.GetValue<string>(),
                з["где"]?.GetValue<int>(),
                Поле(з.AsObject(), "текст").GetValue<string>()));

        foreach (var э in Поле(о, "отголоски").AsArray())
            н.отголоски.Add(new Отголосок(
                Поле(э!.AsObject(), "квартира").GetValue<int>(),
                Поле(э.AsObject(), "жизнь").GetValue<int>(),
                Поле(э.AsObject(), "текст").GetValue<string>()));

        return н;
    }

    /// <summary>
    /// Достать поле или сказать, какого не хватает.
    ///
    /// Не «Object reference not set»: сейв ломается у живого человека
    /// и через полгода, и починить его можно только по имени поля.
    /// Сообщение здесь — часть формата, а не украшение.
    /// </summary>
    private static JsonNode Поле(JsonObject о, string имя)
        => о[имя] ?? throw new InvalidDataException(
               $"в наследии нет поля «{имя}»");

    public static void Записать(Наследие н, string путь)
        => File.WriteAllText(
            путь,
            Сложить(н).ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                    .UnsafeRelaxedJsonEscaping,     // кириллица остаётся читаемой
            }),
            new System.Text.UTF8Encoding(false));

    public static Наследие Прочитать(string путь)
        => Разобрать(JsonNode.Parse(File.ReadAllText(путь))!.AsObject());
}
