#:property PublishAot=false

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

if (args.Length == 0 || args is ["--help"] or ["-h"])
{
	Console.WriteLine("""
		Usage: dotnet run --file tools/ef.cs -- [--ef-version VERSION] <EF arguments>

		Examples (from the repository root):
		  dotnet tool restore
		  dotnet run --file tools/ef.cs -- migrations list
		  dotnet run --file tools/ef.cs -- migrations has-pending-model-changes
		  dotnet run --file tools/ef.cs -- migrations add MigrationName
		  dotnet run --file tools/ef.cs -- database update
		  dotnet run --file tools/ef.cs -- --ef-version 10.0.5 migrations list

		Defaults: ZPI4M.Database, startup ZPI4M.Api, context AppDbContext.
		Explicit --project/-p, --startup-project/-s and --context/-c replace defaults.
		EF runs in the API directory; the context factory loads containers/local/.env outside Docker.
		migrations list checks the database; has-pending-model-changes checks the model.
		database update changes the configured database. No migration runs by default.
		Use a subcommand's --help to see EF options. Exit codes are passed through.
		""");
	return 0;
}

var forwarded = new List<string>();
string? version = null;
for (var index = 0; index < args.Length; index++)
{
	if (args[index] == "--")
	{
		forwarded.AddRange(args[index..]);
		break;
	}
	if (args[index] == "--ef-version")
	{
		if (version is not null || ++index >= args.Length ||
			string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith('-'))
		{
			Console.Error.WriteLine("Specify --ef-version once, followed by a tool version.");
			return 2;
		}
		version = args[index];
	}
	else
	{
		forwarded.Add(args[index]);
	}
}

if (forwarded.Count == 0)
{
	Console.Error.WriteLine("An EF command is required. Use --help for examples.");
	return 2;
}

var root = Path.GetDirectoryName(Path.GetDirectoryName(SourcePath()))!;
var api = Path.Combine(root, "backend", "ZPI4M.Backend", "src", "ZPI4M.Api");
var database = Path.Combine(root, "backend", "ZPI4M.Backend", "src", "ZPI4M.Database");
// Options after -- belong to the startup application, not the EF CLI.
var separator = forwarded.IndexOf("--");
if (separator < 0)
	separator = forwarded.Count;
var efOptions = forwarded.Take(separator).ToArray();
foreach (var (option, alias, value) in new[]
{
	("--project", "-p", database),
	("--startup-project", "-s", api),
	("--context", "-c", "AppDbContext")
})
{
	if (!efOptions.Any(argument => argument == option || argument == alias || argument.StartsWith(option + "=")))
	{
		forwarded.Insert(separator++, option);
		forwarded.Insert(separator++, value);
	}
}

var start = new ProcessStartInfo("dotnet")
{
	WorkingDirectory = api,
	UseShellExecute = false
};
if (version is null)
{
	start.ArgumentList.Add("tool");
	start.ArgumentList.Add("run");
	start.ArgumentList.Add("dotnet-ef");
}
else
{
	start.ArgumentList.Add("tool");
	start.ArgumentList.Add("exec");
	start.ArgumentList.Add("dotnet-ef@" + version);
	start.ArgumentList.Add("--yes");
}
start.ArgumentList.Add("--");
foreach (var argument in forwarded)
	start.ArgumentList.Add(argument);

try
{
	using var process = Process.Start(start)!;
	await process.WaitForExitAsync();
	return process.ExitCode;
}
catch (Win32Exception)
{
	Console.Error.WriteLine("Unable to start dotnet. Install the .NET 10 SDK and ensure dotnet is on PATH.");
	return 1;
}

static string SourcePath([CallerFilePath] string path = "") => path;
