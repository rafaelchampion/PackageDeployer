using System.Diagnostics;

namespace PackageDeployer.Services.GitService;

public class GitService
{
    public string RepositoryName { get; set; }
    public string RepositoriesPath { get; set; }

    private string RepositoryLocalPath => $"{RepositoriesPath}/{RepositoryName}";

    public string RepositoryUsername { get; set; }
    public string RepositoryToken { get; set; }
    public bool Success { get; private set; } = true;
    public List<string> Errors { get; set; } = [];
    private ProcessStartInfo GitProcess;

    public GitService(
        string repositoryName,
        string repositoriesPath,
        string repositoryUsername,
        string repositoryToken)
    {
        RepositoryName = repositoryName;
        RepositoriesPath = repositoriesPath;
        RepositoryUsername = repositoryUsername;
        RepositoryToken = repositoryToken;
        GitProcess = new ProcessStartInfo
        {
            WorkingDirectory = RepositoryLocalPath,
            FileName = $"git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    public void InitializeRepository()
    {
        GitProcess.WorkingDirectory = RepositoriesPath;
        GitProcess.Arguments =
            $"clone https://{RepositoryUsername}:{RepositoryToken}@github.com/{RepositoryName} --verbose";
        var localProcess = Process.Start(GitProcess);
        using (var processErrors = localProcess.StandardError)
        {
            var errors = "";
            errors = processErrors.ReadToEnd();
            Errors = ExtractErrors(errors);
            var workingTreeErrors = Errors.Where(x => x.Contains("unable to checkout working tree")).ToList();
            if (workingTreeErrors.Count != 0)
            {
                Errors.RemoveAll(x=> workingTreeErrors.Contains(x));
                CheckoutDefault();
            }
            if (Errors.Count != 0)
            {
                Success = false;
            }
        }
        try
        {
            localProcess.WaitForExit();
        }
        catch (Exception e)
        {
            Success = false;
            Errors.Add(e.Message);
        }
    }

    public void CloneRepository()
    {
        GitProcess.Arguments = $"clone";
        var localProcess = Process.Start(GitProcess);
        localProcess.WaitForExit();
    }

    public void PullRepository()
    {
        GitProcess.Arguments = $"pull";
        var localProcess = Process.Start(GitProcess);
        localProcess.WaitForExit();
    }

    public void CheckoutDefault()
    {
        GitProcess.Arguments = $"restore --source=HEAD :/";
        var localProcess = Process.Start(GitProcess);
        localProcess.WaitForExit();
    }

    public List<string> ListRemoteBranches()
    {
        GitProcess.Arguments = $"branch -r";
        var localProcess = Process.Start(GitProcess);
        var branches = new List<string>();
        localProcess.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null)
            {
                branches.Add(args.Data);
            }
        };
        localProcess.WaitForExit();
        return branches;
    }

    public void CheckoutBranch(string branchName)
    {
        GitProcess.Arguments = $"checkout -t -b {branchName}";
        var localProcess = Process.Start(GitProcess);
        localProcess.WaitForExit();
    }

    private List<string> ExtractErrors(string input)
    {
        var errorLines = new List<string>();
        using var reader = new StringReader(input);
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains("fatal:"))
            {
                errorLines.Add(line);
            }
        }

        return errorLines;
    }
}