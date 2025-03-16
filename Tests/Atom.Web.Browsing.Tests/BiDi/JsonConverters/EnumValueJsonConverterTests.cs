namespace Atom.Web.Browsing.BiDi.JsonConverters;

using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters.Tests;

[TestFixture]
public class EnumValueJsonConverterTests
{
    [Test]
    public void ShouldSerializeValue()
    {
        string json = JsonSerializer.Serialize(BasicEnum.FirstValue, JsonTestContext.Default.BasicEnum);
        Assert.That(json, Is.EqualTo("\"firstvalue\""));
    }

    [Test]
    public void ShouldSerializeValueWithCustomSerializedValue()
    {
        string json = JsonSerializer.Serialize(BasicEnum.SecondValue, JsonTestContext.Default.BasicEnum);
        Assert.That(json, Is.EqualTo("\"second-value\""));
    }

    [Test]
    public void ShouldDeserializeBasicValue()
    {
        BasicEnum? value = JsonSerializer.Deserialize("\"firstvalue\"", JsonTestContext.Default.BasicEnum);
        Assert.That(value, Is.EqualTo(BasicEnum.FirstValue));
    }

    [Test]
    public void ShouldDeserializeCustomValue()
    {
        BasicEnum? value = JsonSerializer.Deserialize("\"second-value\"", JsonTestContext.Default.BasicEnum);
        Assert.That(value, Is.EqualTo(BasicEnum.SecondValue));
    }

    [Test]
    public void ShouldDeserializeInvalidValueWhenAttributeSet()
    {
        EnumWithDefault? value = JsonSerializer.Deserialize("\"invalid\"", JsonTestContext.Default.EnumWithDefault);
        Assert.That(value, Is.EqualTo(EnumWithDefault.DefaultValue));
    }

    [Test]
    public void DeserializeNonStringValueThrows()
    {
        Assert.That(() => JsonSerializer.Deserialize("1", JsonTestContext.Default.BasicEnum), Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Ошибка десериализации при чтении строкового значения перечисления"));
    }

    [Test]
    public void DeserializeInvalidValueThrows()
    {
        Assert.That(() => JsonSerializer.Deserialize("\"invalid\"", JsonTestContext.Default.BasicEnum), Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Ошибка десериализации: значение 'invalid' недопустимо для типа перечисления Atom.Web.Browsing.BiDi.JsonConverters.BasicEnum"));
    }
}

[JsonConverter(typeof(EnumValueJsonConverter<BasicEnum>))]
public enum BasicEnum
{
    FirstValue,

    [JsonEnumValue("second-value")]
    SecondValue
}

[JsonConverter(typeof(EnumValueJsonConverter<EnumWithDefault>))]
[JsonEnumUnmatchedValue<EnumWithDefault>(DefaultValue)]
public enum EnumWithDefault
{
    [JsonEnumValue("default-value")]
    DefaultValue,

    [JsonEnumValue("non-default-value")]
    NonDefaultValue
}