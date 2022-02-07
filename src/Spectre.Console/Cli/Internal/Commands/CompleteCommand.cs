namespace Spectre.Console.Cli;

[Description("Generates a list of completion options for the given command.")]
[SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes")]
internal sealed class CompleteCommand : Command<CompleteCommand.Settings>
{
    private readonly CommandModel _model;
    private readonly IAnsiConsole _writer;
    private readonly IConfiguration _configuration;

    public CompleteCommand(IConfiguration configuration, CommandModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _writer = configuration.Settings.Console.GetConsole();
        _configuration = configuration;
    }

    public sealed class Settings : CommandSettings
    {
        public Settings(string? commandToComplete)
        {
            CommandToComplete = commandToComplete;
        }

        [CommandArgument(0, "[commandToComplete]")]
        public string? CommandToComplete { get; }
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        foreach (var completion in GetCompletions(_model, settings))
        {
            _writer.WriteLine(completion, Style.Plain);
        }

        return 0;
    }

    private string[] GetCompletions(CommandModel model, Settings settings)
    {
        // Get all command elements and skip the application name at the start.
        var commandElements = settings.CommandToComplete?.Split(' ').Skip(1);

        // Return early if the only thing we got was "".
        if (commandElements == null ||
           (commandElements.Count() == 1 &&
            string.IsNullOrEmpty(commandElements.First())))
        {
            return model.Commands.Where(cmd => !cmd.IsHidden)
                                 .Select(c => c.Name)
                                 .ToArray();
        }

        // Parse the command elements to get an abstract syntax tree and some context
        CommandTreeParserResult? parsedResult = null;
        var parser = new CommandTreeParser(model, _configuration.Settings);
        var context = string.Empty;
        var partialElement = string.Empty;
        try
        {
            parsedResult = parser.Parse(commandElements);
            context = commandElements.Last();
            if (string.IsNullOrEmpty(context))
            {
                // Because we support netstandard2.0, we can't use SkipLast, since it's not supported. Also negative indexes are a no go.
                // There probably is a more elegant way to get this result that I just can't see now.
                // context = commandElements.SkipLast(1).Last();
                context = commandElements.ToArray()[commandElements.Count() - 2];
            }
        }
        catch (CommandParseException)
        {
            // Assume that it's because the last commandElement was not complete, and omit that one.
            var strippedCommandElements = commandElements.Take(commandElements.Count() - 1);
            if (strippedCommandElements.Any())
            {
                parsedResult = parser.Parse(strippedCommandElements);
                context = strippedCommandElements.Last();
                partialElement = commandElements.Last();
            }
        }

        // Return command options based on our current context, filtered on any partial element we found.
        // If partial element = "", the StartsWith will return all options.
        CommandInfo parent;
        if (parsedResult?.Tree == null)
        {
            return model.Commands.Where(cmd => !cmd.IsHidden)
                                 .Select(c => c.Name)
                                 .Where(n => n.StartsWith(partialElement))
                                 .ToArray();
        }
        else
        {
            // The Tree does not natively support walking or visiting, so we need to search it manually.
            parent = FindContextInTree(parsedResult.Tree, context);
        }

        return parent.Children.Where(cmd => !cmd.IsHidden)
                              .Select(c => c.Name)
                              .Where(n => n.StartsWith(partialElement))
                              .ToArray();
    }

    private static CommandInfo FindContextInTree(CommandTree tree, string context)
    {
        // This needs to become a recursive function, but for the simpler situations this would work.
        var commandInfo = tree.Command;

        if (commandInfo.Name != context)
        {
            commandInfo = tree.Command.Children.FirstOrDefault(c => c.Name == context);
        }

        return commandInfo;
    }
}