namespace Atom.Web.Browsing.BiDi.TestUtilities;

using System.Threading.Tasks;
using Protocol;
using Atom.Web.Browsing.BiDi;
using System.Text.Json.Serialization.Metadata;

public class TestTransport : Transport
{
    private TimeSpan messageProcessingDelay = TimeSpan.Zero;

    public TestTransport(Connection connection) : base(connection)
    {
    }

    public long LastTestCommandId => this.LastCommandId;

    public bool ReturnCustomValue { get; set; }

    public TimeSpan MessageProcessingDelay { get => this.messageProcessingDelay; set => this.messageProcessingDelay = value; }

    public CommandResult? CustomReturnValue { get; set; }

    public override async Task<Command> SendCommandAsync<TParams, TResult>(TParams commandData, JsonTypeInfo<TParams> parametersTypeInfo, JsonTypeInfo<CommandResponseMessage<TResult>> resultTypeInfo)
    {
        if (this.ReturnCustomValue)
        {
            Command returnedCommand = new Command(LastCommandId, commandData, parametersTypeInfo, resultTypeInfo)
            {
                Result = this.CustomReturnValue
            };

            return returnedCommand;
        }

        return await base.SendCommandAsync(commandData, parametersTypeInfo, resultTypeInfo);
    }

    public Connection GetConnection()
    {
        return this.Connection;
    }
}
