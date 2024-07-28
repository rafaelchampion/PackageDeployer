using System.Diagnostics;

namespace PackageDeployer.Services.GitService;

public class GitService
{
    public string RepositoryName { get; set; }
    public string RepositoriesPath { get; set; }
    private string RepositoryLocalPath
    {
        get => $"{RepositoriesPath}/{RepositoryName}";
    }
    public string RepositoryUsername { get; set; }
    public string RepositoryToken { get; set; }
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
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    public void InitializeRepository()
    {
        GitProcess.WorkingDirectory = RepositoriesPath;
        GitProcess.Arguments = $"clone https://{RepositoryUsername}:{RepositoryToken}@github.com/{RepositoryName}";
        var localProcess = Process.Start(GitProcess);
        localProcess.WaitForExit();
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
}