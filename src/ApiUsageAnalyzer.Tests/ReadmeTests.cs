using Spectre.Console.Cli;
using Spectre.Console.Testing;
using System.Text.RegularExpressions;

namespace ApiUsageAnalyzer.Tests;

public class ReadmeTests
{
    [Test]
    public void Command_line_arguments_section_is_up_to_date()
    {
        var console = new TestConsole();

        // This is the exact number of characters which will not cause GitHub.com to horizontally scroll the code block
        // when the readme is rendered on the home page of the repo.
        console.Profile.Width = 100;

        var app = new CommandApp<CliCommand>();
        app.Configure(c => c.SetApplicationName(typeof(CliCommand).Assembly.GetName().Name!));
        app.Configure(c => c.ConfigureConsole(console));
        app.Run(["--help"]);

        var helpOutput = console.Output.TrimLines().Trim();

        var expectedReadmeCodeBlock = Regex.Replace(helpOutput, @"\ADESCRIPTION:(\r?\n[^\r\n]+)*(\r?\n){2}", "", RegexOptions.IgnoreCase);

        var readmeContents = File.ReadAllText(Path.Join(TestUtils.DetectSolutionDirectory(), "Readme.md"));
        var actualReadmeCodeBlock = Regex.Match(readmeContents, @"^## Command-line arguments(?:\s*\n)+```\s*\n(?<contents>.*)\s*\n```", RegexOptions.Singleline | RegexOptions.Multiline).Groups["contents"].Value;

        if (actualReadmeCodeBlock.NormalizeLineEndings() != expectedReadmeCodeBlock.NormalizeLineEndings())
        {
            Assert.Fail($"""
                Update the ‘Command-line arguments’ section in Readme.md with the following exact contents:
                -----
                {expectedReadmeCodeBlock}
                -----
                """);
        }
    }
}
