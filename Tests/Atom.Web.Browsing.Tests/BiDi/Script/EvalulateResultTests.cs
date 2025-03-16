namespace Atom.Web.Browsing.BiDi.Script;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class EvaluateResultTests
{
    [Test]
    public void TestCanDeserializeScriptEvaluateResultSuccess()
    {
        string json = """
                      {
                        "type": "success",
                        "realm": "myRealm",
                        "result": {
                          "type": "string",
                          "value": "myResult"
                        }
                      }
                      """;
        EvaluateResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<EvaluateResultSuccess>());
        EvaluateResultSuccess successResult = (EvaluateResultSuccess)result!;
        Assert.Multiple(() =>
        {
            Assert.That(successResult.RealmId, Is.EqualTo("myRealm"));
            Assert.That(successResult.Result.Type, Is.EqualTo("string"));
            Assert.That(successResult.Result.HasValue);
            Assert.That(successResult.Result.ValueAs<string>(), Is.EqualTo("myResult"));
        });
    }

    [Test]
    public void TestCanDeserializeScriptEvaluateResultException()
    {
        string json = """
                      {
                        "type": "exception",
                        "realm": "myRealm",
                        "exceptionDetails": {
                          "text": "exception thrown",
                          "lineNumber": 1,
                          "columnNumber": 5,
                          "stackTrace": {
                            "callFrames": [] 
                          },
                          "exception": {
                            "type": "string",
                            "value": "exception value"
                          }
                        }
                      }
                      """;
        EvaluateResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<EvaluateResultException>());
        EvaluateResultException successResult = (EvaluateResultException)result!;
        Assert.Multiple(() =>
        {
            Assert.That(successResult.RealmId, Is.EqualTo("myRealm"));
            Assert.That(successResult.ExceptionDetails.Text, Is.EqualTo("exception thrown"));
            Assert.That(successResult.ExceptionDetails.LineNumber, Is.EqualTo(1));
            Assert.That(successResult.ExceptionDetails.ColumnNumber, Is.EqualTo(5));
            Assert.That(successResult.ExceptionDetails.StackTrace.CallFrames, Has.Count.EqualTo(0));
            Assert.That(successResult.ExceptionDetails.Exception.ValueAs<string>(), Is.EqualTo("exception value"));
        });
    }

    [Test]
    public void TestDeserializeScriptEvaluateResultWithInvalidTypePropertyValueThrows()
    {
        string json = """
                     {
                       "type": "invalid",
                       "realm": "myRealm",
                       "noWoman": "noCry"
                     }
                     """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult), Throws.InstanceOf<JsonException>().With.Message.Contains("неизвестный тип 'invalid' для результата скрипта"));
    }

    [Test]
    public void TestDeserializeScriptEvaluateResultWithMissingTypePropertyThrows()
    {
        string json = """
                     {
                       "realm": "myRealm",
                       "noWoman": "noCry"
                     }
                     """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult), Throws.InstanceOf<JsonException>().With.Message.Contains("Ответ скрипта должен содержать свойство 'type'"));
    }

    [Test]
    public void TestDeserializeScriptEvaluateResultWithInvalidTypePropertyObjectThrows()
    {
        string json = """
                     {
                       "type": {
                         "noWoman": "noCry"
                       },
                       "realm": "myRealm",
                       "noWoman": "noCry"
                     }
                     """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult), Throws.InstanceOf<JsonException>().With.Message.Contains("Свойство 'type' в ответе скрипта должно быть строкой"));
    }

    [Test]
    public void TestDeserializeScriptEvaluateResultWithMissingRealmValueThrows()
    {
        string json = """
                      {
                        "type": "success",
                        "result": {
                          "type": "string",
                          "value": "myResult"
                        }
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializeScriptEvaluateResultWithInvalidRealmValueTypeThrows()
    {
        string json = """
                      {
                        "type": "success",
                        "realm": {
                          "noWoman": "noCry"
                        },
                        "result": {
                          "type": "string",
                          "value": "myResult"
                        }
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializeScriptResultWithNonObjectThrows()
    {
        string json = @"[ ""invalid script result"" ]";
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestCannotSerialize()
    {
        // NOTE: ScriptEvaluateResult and subclasses do not provide a way to instantiate
        // one directly using a constructor, so we will deserialize one from JSON.
        string json = """
                      {
                        "type": "success",
                        "realm": "myRealm",
                        "result": {
                          "type": "string",
                          "value": "myResult"
                        }
                      }
                      """;
        EvaluateResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.EvaluateResult);
        Assert.That(() => JsonSerializer.Serialize(result), Throws.InstanceOf<InvalidOperationException>());
    }
}
