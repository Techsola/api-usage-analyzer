# API Usage Analyzer

Produces a report of API usage across code repositories, with version and target framework statistics, listing unused/removed APIs, and links to each usage. The output file format is [KDL](https://kdl.dev) for ease of human browsing and easily clickable links when viewing. The file is rewritten in real time as the analysis proceeds.

See: [Prerelease status](#prerelease-status), [How to use](#how-to-use), [Command-line arguments](#command-line-arguments)

Sample output:

```kdl
assembly SomeLibrary {
  current-api-source {
    repo "https://somewhere/SomeLibrary"
    branch main
    commit b7e651d52c9b19e6d6e0ca6b4389bc06ac3e7fde
  }
  stats {
    unused-api-count 4_000
    removed-api-count 66
    used-api-count 749
    version "1.0.2.0" {
      repo "https://somewhere/ConsumingRepo1" { tfm net462 }
    }
    version "2.0.0.0" {
      repo "https://somewhere/ConsumingRepo2" { tfm net8.0 }
      repo "https://somewhere/ConsumingRepo3" { tfm net48 }
    }
    version "3.0.0.0" {
      repo "https://somewhere/ConsumingRepo2" { tfm net48; tfm net8.0-windows; tfm net9.0-windows }
    }
  }

  unused-apis {
    api "SomeLibrary.AverageAccumulator.AverageAccumulator(double initialTotal, double initialCount)" url="https://somewhere/SomeLibrary?version=GBmain&path=src/SomeLibrary/AverageAccumulator.cs&line=13&lineEnd=17&lineStartColumn=9&lineEndColumn=10" { tfm net48; tfm net8.0 }
    api "SomeLibrary.AverageAccumulator.Count.get" url="https://somewhere/SomeLibrary?version=GBmain&path=src/SomeLibrary/AverageAccumulator.cs&line=51&lineEnd=51&lineStartColumn=29&lineEndColumn=37" { tfm net48; tfm net8.0 }
    api "SomeLibrary.AverageAccumulator.implicit operator double?(SomeLibrary.AverageAccumulator accumulator)" url="https://somewhere/SomeLibrary?version=GBmain&path=src/SomeLibrary/AverageAccumulator.cs&line=53&lineEnd=56&lineStartColumn=9&lineEndColumn=10" { tfm net48; tfm net8.0 }
  }

  removed-apis {
    api "SomeLibrary.IAsyncDisposable" {
      version "2.0.0.0" {
        repo "https://somewhere/ConsumingRepo1" {
          reference "https://somewhere/ConsumingRepo1?version=GBmain&path=src/ConsumingProject1/AsyncDisposable.cs&line=12&lineEnd=12&lineStartColumn=5&lineEndColumn=112" { tfm net48 }
          reference "https://somewhere/ConsumingRepo1?version=GBmain&path=src/ConsumingProject1/AsyncDisposable.cs&line=6&lineEnd=6&lineStartColumn=39&lineEndColumn=64" { tfm net48 }
          // ...
        }
        repo "https://somewhere/ConsumingRepo2" {
          reference "https://somewhere/ConsumingRepo2?version=GBmain&path=src/ConsumingProject2/BatchReporting/Domain/MailingClaim.cs&line=5&lineEnd=5&lineStartColumn=40&lineEndColumn=56" { tfm net48 }
          reference "https://somewhere/ConsumingRepo2?version=GBmain&path=src/ConsumingProject2/BatchReporting/Domain/MailingClaimLifetime.cs&line=5&lineEnd=5&lineStartColumn=50&lineEndColumn=66" { tfm net48 }
          // ...
        }
      }
    }
    // ...
  }

  api "SomeLibrary.AverageAccumulator" url="https://somewhere/SomeLibrary?version=GBmain&path=src/SomeLibrary/AverageAccumulator.cs&line=7&lineEnd=57&lineStartColumn=5&lineEndColumn=6" {
    version "2.0.0.0" {
      repo "https://somewhere/ConsumingRepo2" {
        reference "https://somewhere/ConsumingRepo2?version=GBmain&path=src/ConsumingProject2/PerfDataEntry/ReportHandler.cs&line=447&lineEnd=447&lineStartColumn=90&lineEndColumn=107" { tfm net48 }
        reference "https://somewhere/ConsumingRepo2?version=GBmain&path=src/ConsumingProject2/PerfDataEntry/ReportHandler.cs&line=451&lineEnd=451&lineStartColumn=90&lineEndColumn=107" { tfm net48 }
        // ...
      }
    }
  }
  api "SomeLibrary.AverageAccumulator.Add(double value, double weight)" url="https://somewhere/SomeLibrary?version=GBmain&path=src/SomeLibrary/AverageAccumulator.cs&line=35&lineEnd=47&lineStartColumn=9&lineEndColumn=10" {
    version "2.0.0.0" {
      repo "https://somewhere/ConsumingRepo2" {
        reference "https://somewhere/ConsumingRepo2?version=GBmain&path=src/ConsumingProject2/PerfDataEntry/DataEntrySummaryCalculator.cs&line=135&lineEnd=135&lineStartColumn=40&lineEndColumn=43" { tfm net48 }
        reference "https://somewhere/ConsumingRepo2?version=GBmain&path=src/ConsumingProject2/PerfDataEntry/DataEntrySummaryCalculator.cs&line=92&lineEnd=92&lineStartColumn=56&lineEndColumn=59" { tfm net48 }
        // ...
      }
    }
  }
  // ...
}
```

## Prerelease status

You may hit a NotImplementedException if there's syntax that the tool hasn't seen before. If you don't mind getting a little grease on your hands, this will pose no obstacle to you, and we would be delighted if you're inspired to share back as a pull request. Issues are welcome too.

## How to use

1. Clone this repository (example: `git clone https://github.com/Techsola/api-usage-analyzer`)
2. Navigate to the `src\ApiUsageAnalyzer` subdirectory (example: `cd api-usage-analyzer\src\ApiUsageAnalyzer`)
3. Type `dotnet run` followed by the arguments below. `--open-in-vs-code` is recommended.
4. Wait a bit as repositories are cloned, restored, and analyzed.
5. Peruse the `<assembly-name>.kdl` file in the working directory.

## Command-line arguments

```
USAGE:
    ApiUsageAnalyzer <assembly-name> [OPTIONS]

ARGUMENTS:
    <assembly-name>    The name of the assembly defining the APIs whose usage will be reported

OPTIONS:
    -h, --help                              Prints help information
        --azdo <URLS>                       The Azure DevOps project collection URLs from which
                                            repositories are discovered. Repeat --azdo to specify
                                            more than one URL. Currently, only Git repositories and
                                            only the Azure DevOps platform are supported
        --defining-repo-name <NAME>         If specified, only repositories with this name will be
                                            searched for the project that defines the API assembly
        --defining-repo-url <URL>           The remote URL of an repository containing the project
                                            that defines the API assembly. The repository's current
                                            HEAD will be used to define the set of available APIs in
                                            the API assembly which will be used for the
                                            unused/removed API comparison
        --repo-package-id-filter <REGEX>    If specified, repositories will be skipped unless they
                                            contain a project with a direct reference to a NuGet
                                            package with an ID that matches this regular expression
        --open-in-vs-code                   If specified, the output file will automatically open in
                                            VS Code. VS Code can monitor the live updates to the
                                            results
```
