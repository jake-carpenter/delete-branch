#!/usr/bin/env dotnet-script
#r "nuget: Spectre.Console, 0.41.0"
#r "nuget: CliWrap, 3.3.3"

using CliWrap;
using CliWrap.EventStream;
using Spectre.Console;

// Script
var workingDirectory = ParseWorkingDirectory(Args[0]);
var branchesResult = await GetBranches(workingDirectory);
if (!branchesResult.Success)
{
  AnsiConsole.MarkupLine($"[red]Error fetching branches[/]");
  AnsiConsole.MarkupLine($"[red]{branchesResult.Reason}[/]");
  return;
}

var deleteResult = await DeletionPrompt(workingDirectory, PromptSelection(branchesResult.Value));
if (!deleteResult.Success)
{
  AnsiConsole.MarkupLine($"\n\u2757  [bold red]{deleteResult.Reason}[/]");
}

return deleteResult.Value;

// Functions
string ParseWorkingDirectory(string path)
{
  var dir = new DirectoryInfo(path);

  if (!dir.Exists)
    throw new ArgumentException($"Invalid path {path}");

  return dir.FullName;
}

async Task<Result<List<string>>> GetBranches(string workingDirectory)
{
  var safeBranches = new[] { "master", "main", "develop", "dev" };
  var result = await ExecuteGitBranchList(workingDirectory);
  if (!result.Success)
    return result;

  var branches = result.Value
    .Select(branch => branch.Replace("origin/", string.Empty).Trim())
    .Where(branch => !safeBranches.Contains(branch, StringComparer.OrdinalIgnoreCase))
    .ToList();

  return new Result<List<string>>(true, null, branches);
}

async Task<Result<List<string>>> ExecuteGitBranchList(string path)
{
  var branches = new List<string>();
  var errorBuilder = new StringBuilder();

  var result = await Cli.Wrap("git")
    .WithWorkingDirectory(path)
    .WithArguments("branch -r")
    .WithValidation(CommandResultValidation.None)
    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(errorBuilder))
    .WithStandardOutputPipe(PipeTarget.ToDelegate(value =>
    {
      if (value.Contains("origin/HEAD ->"))
        return;

      branches.Add(value.Trim());
    }))
    .ExecuteAsync();

  return new Result<List<string>>(result.ExitCode == 0, errorBuilder.ToString(), branches);
}

List<string> PromptSelection(List<string> branches)
{
  AnsiConsole.WriteLine();

  return AnsiConsole.Prompt(
    new MultiSelectionPrompt<string>()
      .Title("[bold magenta]Select branches to delete:[/]")
      .PageSize(10)
      .AddChoices(branches)
  );
}

async Task<Result<int>> DeletionPrompt(string path, List<string> branches)
{
  var code = 0;

  await AnsiConsole.Status()
    .StartAsync($"Deleting {branches.Count} branches...", async ctx =>
    {
      foreach (var branch in branches)
      {
        AnsiConsole.MarkupLine($"\u23F1  [grey]{branch}[/]");
        var result = await Delete(path, branch);
        var newLine = result.Success
          ? $"[green]\u2705[/]  [grey]{branch}[/]"
          : $"[red]\u274C[/]  [grey]{branch}[/]";

        Console.SetCursorPosition(0, Console.CursorTop - 1);
        AnsiConsole.MarkupLine(newLine);

        if (!result.Success)
        {
          code = 1;
          break;
        }
      }
    });

  return new Result<int>(code == 0, $"Error deleting one or more branches", code);
}

async Task<Result<int>> Delete(string path, string branch)
{
  const string template = "push --delete origin {0}";
  var errorBuilder = new StringBuilder();
  var result = await Cli.Wrap("git")
      .WithWorkingDirectory(path)
      .WithValidation(CommandResultValidation.None)
      .WithArguments(string.Format(template, branch))
      .WithStandardErrorPipe(PipeTarget.ToStringBuilder(errorBuilder))
      .ExecuteAsync();

  return new Result<int>(result.ExitCode == 0, errorBuilder.ToString(), result.ExitCode);
}

record Result<T>(bool Success, string Reason, T Value);