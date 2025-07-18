using Shouldly;

namespace ApiUsageAnalyzer.Tests;

public class KdlWriterTests
{
    [Test]
    public void Example()
    {
        using var stringWriter = new StringWriter();
        var kdlWriter = new KdlWriter(stringWriter);
        kdlWriter.StartNode("assembly");
        kdlWriter.WriteStringValue("SomeLibrary");
        kdlWriter.StartNode("stats");

        kdlWriter.StartNode("unused-api-count");
        kdlWriter.WriteNumberValue(3);
        kdlWriter.EndNode();

        kdlWriter.StartNode("removed-api-count");
        kdlWriter.WriteNumberValue(5);
        kdlWriter.EndNode();

        kdlWriter.StartNode("used-api-count");
        kdlWriter.WriteNumberValue(10);
        kdlWriter.EndNode();

        kdlWriter.StartNode("version");
        kdlWriter.WriteStringValue("1.2.3");
        kdlWriter.StartNode("repo");
        kdlWriter.WriteStringValue("github.com/example/repo1");

        using (kdlWriter.EnterSingleLineMode())
        {
            kdlWriter.StartNode("tfm");
            kdlWriter.WriteStringValue("netstandard2.0");
            kdlWriter.EndNode();

            kdlWriter.StartNode("tfm");
            kdlWriter.WriteStringValue("net6.0");
            kdlWriter.EndNode();
        }

        kdlWriter.EndNode();
        kdlWriter.EndNode();


        kdlWriter.StartNode("version");
        kdlWriter.WriteStringValue("2.0.0");

        kdlWriter.StartNode("repo");
        kdlWriter.WriteStringValue("github.com/example/repo2");

        using (kdlWriter.EnterSingleLineMode())
        {
            kdlWriter.StartNode("tfm");
            kdlWriter.WriteStringValue("netstandard2.0");
            kdlWriter.EndNode();
        }

        kdlWriter.EndNode();


        kdlWriter.StartNode("repo");
        kdlWriter.WriteStringValue("github.com/example/repo3");

        using (kdlWriter.EnterSingleLineMode())
        {
            kdlWriter.StartNode("tfm");
            kdlWriter.WriteStringValue("net48");
            kdlWriter.EndNode();
        }

        kdlWriter.EndNode();
        kdlWriter.EndNode();
        kdlWriter.EndNode();

        kdlWriter.WriteLine();

        kdlWriter.StartNode("unused-apis");

        kdlWriter.StartNode("api");
        kdlWriter.WriteStringValue("SomeLibrary.SomeUnusedApi");
        kdlWriter.WritePropertyName("url");
        kdlWriter.WriteStringValue("https://example.com/definitions/UnusedMethod");
        kdlWriter.EndNode();

        kdlWriter.StartNode("api");
        kdlWriter.WriteStringValue("SomeLibrary.LegacyThing");
        kdlWriter.WritePropertyName("url");
        kdlWriter.WriteStringValue("https://example.com/definitions/LegacyThing");
        kdlWriter.EndNode();

        kdlWriter.EndNode();

        kdlWriter.EndNode();

        stringWriter.ToString().ShouldBe("""
            assembly SomeLibrary {
              stats {
                unused-api-count 3
                removed-api-count 5
                used-api-count 10
                version "1.2.3" {
                  repo "github.com/example/repo1" { tfm netstandard2.0; tfm net6.0 }
                }
                version "2.0.0" {
                  repo "github.com/example/repo2" { tfm netstandard2.0 }
                  repo "github.com/example/repo3" { tfm net48 }
                }
              }

              unused-apis {
                api SomeLibrary.SomeUnusedApi url="https://example.com/definitions/UnusedMethod"
                api SomeLibrary.LegacyThing url="https://example.com/definitions/LegacyThing"
              }
            }

            """);
    }
}
