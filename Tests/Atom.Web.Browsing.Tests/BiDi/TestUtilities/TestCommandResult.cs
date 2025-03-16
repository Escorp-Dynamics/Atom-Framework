namespace Atom.Web.Browsing.BiDi.TestUtilities;

using System.Text.Json.Serialization;

public class TestCommandResult : CommandResult
{
    private bool isError;

    [JsonIgnore]
    public override bool IsError { get => isError; }

    public string? Value { get; set; }

    [JsonPropertyName("elapsed")]
    public double? ElapsedMilliseconds { get; set; }

    public void SetIsErrorValue(bool isError) => this.isError = isError;
}