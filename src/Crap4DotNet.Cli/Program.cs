using System.CommandLine;
using System.Globalization;
using Crap4DotNet.Cli.Commands;

namespace Crap4DotNet.Cli;

internal sealed class Program
{
    static int Main(string[] args) => Run(args);

    /// <summary>
    /// The parser's exit code when nonzero (an unknown option or command), else the command's.
    /// Numbers parse in the invariant culture, so <c>--epsilon 0.01</c> reads the same on every locale.
    /// </summary>
    internal static int Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var rootCommand = new RootCommand("CRAP metric analysis tool for .NET")
        {
            AnalyzeCommand.Create(),
            DiffCommand.Create(),
            ReportCommand.Create()
        };

        var parserExitCode = rootCommand.Invoke(args);
        return parserExitCode != 0 ? parserExitCode : Environment.ExitCode;
    }
}
