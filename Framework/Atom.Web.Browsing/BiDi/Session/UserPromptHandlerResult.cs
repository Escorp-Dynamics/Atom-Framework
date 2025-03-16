namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Describes a set of settings for handling user prompts during test execution.
/// </summary>
public class UserPromptHandlerResult
{
    private readonly UserPromptHandler userPromptHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserPromptHandlerResult"/> class.
    /// </summary>
    /// <param name="userPromptHandler">The <see cref="UserPromptHandler"/> to use in the result.</param>
    internal UserPromptHandlerResult(UserPromptHandler userPromptHandler) => this.userPromptHandler = userPromptHandler;

    /// <summary>
    /// Gets the type of prompt handler for user prompts for which a handler type has not been explicitly set.
    /// </summary>
    public UserPromptHandlerType? Default => userPromptHandler.Default;

    /// <summary>
    /// Gets the type of prompt handler for alert user prompts.
    /// </summary>
    public UserPromptHandlerType? Alert => userPromptHandler.Alert;

    /// <summary>
    /// Gets the type of prompt handler for confirm user prompts.
    /// </summary>
    public UserPromptHandlerType? Confirm => userPromptHandler.Confirm;

    /// <summary>
    /// Gets the type of prompt handler for prompt user prompts.
    /// </summary>
    public UserPromptHandlerType? Prompt => userPromptHandler.Prompt;

    /// <summary>
    /// Gets the type of prompt handler for beforeUnload user prompts.
    /// </summary>
    public UserPromptHandlerType? BeforeUnload => userPromptHandler.BeforeUnload;
}