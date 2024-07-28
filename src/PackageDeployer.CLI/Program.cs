using System.Diagnostics;
using System.Globalization;
using Octokit;
using PackageDeployer.Core;
using PackageDeployer.Core.Models;
using Sharprompt;
using Spectre.Console;
using LibGit2Sharp;
using PackageDeployer.Services.GitService;
using Language = PackageDeployer.Lang.GlobalStrings;
using Repository = PackageDeployer.Core.Models.Repository;
using Branch = PackageDeployer.Core.Models.Branch;
using Project = PackageDeployer.Core.Models.Project;

namespace PackageDeployer.CLI;

internal class Program
{
    private static bool _run = true;
    private static string _buildOutputPath;
    private static string _repositoriesPath;
    private static string _configPath;
    private static Config _config;

    public static async Task Main(string[] args)
    {
        Setup();
        Greetings();
        while (_run)
        {
            await MainMenu();
        }
    }

    private static void Setup()
    {
        Language.Culture = CultureInfo.GetCultureInfo(CultureInfo.CurrentCulture.ToString());
        var exePath = AppDomain.CurrentDomain.BaseDirectory;
        _configPath = Path.Combine(exePath, "config.json");
        _buildOutputPath = Path.Combine(exePath, "build");
        _repositoriesPath = Path.Combine(exePath, "repositories");
        if (!Directory.Exists(_repositoriesPath))
        {
            Directory.CreateDirectory(_repositoriesPath);
        }

        _config = ConfigUtil.LoadConfig(_configPath);
    }

    private static void Greetings()
    {
        AnsiConsole.Write(new FigletText("Package Deployer (CL EDITION)").Centered().Color(Color.Blue));
        Console.WriteLine();
    }

    private static async Task MainMenu()
    {
        Console.Clear();
        var options = BuildMainMenuOptions();
        var selectedOption = Prompt.Select("Select an option", options, null, null,
            kvp => $"{kvp.Value}");
        switch (selectedOption.Key)
        {
            case 0:
                MainMenuExit();
                break;
            case 1:
                break;
            case 2:
                break;
            case 3:
                await HandleNewRepositoryAsync();
                break;
            default:
                MainMenuExit();
                break;
        }
    }

    private static List<KeyValuePair<ushort, string>> BuildMainMenuOptions()
    {
        var options = new List<KeyValuePair<ushort, string>>();
        if (_config.Repositories.Count != 0)
        {
            options.AddRange([
                new KeyValuePair<ushort, string>(1, "Publish exisiting repository"),
                new KeyValuePair<ushort, string>(2, "Change repository configuration")
            ]);
        }

        options.AddRange([
            new KeyValuePair<ushort, string>(3, "Add new repository"),
            new KeyValuePair<ushort, string>(0, "Exit"),
        ]);
        return options;
    }

    private static async Task MainLoop()
    {
        try
        {
            var options = BuildPublishMenuOptions(_config);
            var selectedOption = Prompt.Select(Language.Prompt_SelectOrCreateRepositoryConfig, options, null, null,
                kvp => $"{kvp.Value}");
            switch (selectedOption.Key)
            {
                case var id when id == Guid.Empty:
                    MainMenuExit();
                    return;
                case var id when id == Guid.Parse("99999999-9999-9999-9999-999999999999"):
                    await HandleNewRepositoryAsync();
                    break;
                default:
                    await HandleExistingRepositoryAsync(selectedOption);
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Language.Error_MainLoop} {ex.Message}[/]");
        }
    }

    private static async Task HandleNewRepositoryAsync()
    {
        try
        {
            var repository = CreateNewRepository();
            //await ProcessRepositoryAsync(repoParts, token, repoName);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Language.Error_HandleNewRepository}: {ex.Message}[/]");
            AnsiConsole.MarkupLine(Language.Prompt_PressToFinish);
            Console.ReadKey();
        }
    }

    private static async Task HandleExistingRepositoryAsync(KeyValuePair<Guid, string> selectedOption)
    {
        try
        {
            var repositoryOption = _config.Repositories.FirstOrDefault(x => x.Name == selectedOption.Value);
            if (repositoryOption == null)
            {
                AnsiConsole.MarkupLine($"[red]{Language.Error_RepositoryNotFound}: {selectedOption.Value}[/]");
                return;
            }

            repositoryOption.LastUsed = DateTime.Now;
            ConfigUtil.SaveConfig(_configPath, _config);

            var repoName = LoadExistingRepository(repositoryOption, out var repoParts, out var token);
            await ProcessRepositoryAsync(repoParts, token, repoName);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Language.Error_HandleExistingRepository}: {ex.Message}[/]");
        }
    }

    private static List<KeyValuePair<Guid, string>> BuildPublishMenuOptions(Config repoConfig)
    {
        var keyValuePairs = repoConfig.Repositories
            .OrderByDescending(repository => repository.LastUsed)
            .Select(repository => new KeyValuePair<Guid, string>(repository.Id, repository.Name))
            .ToList();

        keyValuePairs.Add(new KeyValuePair<Guid, string>(Guid.Parse("99999999-9999-9999-9999-999999999999"),
            $"+++ {Language.Option_NewRepository} +++"));
        keyValuePairs.Add(new KeyValuePair<Guid, string>(Guid.Empty, $"--- {Language.Option_Exit} ---"));

        return keyValuePairs;
    }

    private static void MainMenuExit()
    {
        _run = false;
    }

    private static Repository CreateNewRepository()
    {
        string repoName;
        string[] repoParts;
        string username;
        string token;
        var exit = false;
        do
        {
            repoName = AnsiConsole.Ask<string>(Language.Prompt_EnterRepositoryName);
            repoParts = repoName.Split('/');
            if (repoParts.Length == 2) continue;
            AnsiConsole.MarkupLine(Language.Error_InvalidRepositoryFormat);
            var tryAgain = AnsiConsole.Confirm("Try again?");
            exit = !tryAgain;
        } while (repoParts.Length != 2 || exit);

        var credentialsCorrect = true;
        do
        {
            username = AnsiConsole.Ask<string>("Enter the username for the repository");
            token = AnsiConsole.Ask<string>(Language.Prompt_EnterGitHubToken);
            AnsiConsole.MarkupLine($"Username: {username}. Token: {token}");
            credentialsCorrect = AnsiConsole.Confirm("Are the credentials correct?");
        } while (!credentialsCorrect);

        var gitService = new GitService(repoName, _repositoriesPath, username, token);
        AnsiConsole.Status().Start(Language.Info_CloningRepository, ctx =>
        {
            gitService.InitializeRepository();
            ctx.Status(Language.Info_RepositoryClonedSuccessfully);
        });
        if (!gitService.Success)
        {
            throw new Exception($"Error initializing repository: {gitService.Errors.FirstOrDefault()}");
        }

        var repository = new Repository() { Name = repoName, Username = username, GitHubToken = token };
        _config.Repositories.Add(repository);
        ConfigUtil.SaveConfig(_configPath, _config);
        return repository;
    }

    private static string LoadExistingRepository(Repository repository, out string[] repoParts, out string token)
    {
        repoParts = repository.Name.Split('/');
        token = repository.GitHubToken;
        return repository.Name;
    }

    private static async Task ProcessRepositoryAsync(string[] repoParts, string token, string repoName)
    {
        var owner = repoParts[0];
        var repo = repoParts[1];

        var client = new GitHubClient(new ProductHeaderValue("GitHubBranchDownloader"))
        {
            Credentials = new Octokit.Credentials(token)
        };

        try
        {
            var repository = _config.Repositories.First(r => r.Name == repoName);
            var githubBranches = await client.Repository.Branch.GetAll(owner, repo);
            SynchronizeBranches(repository, githubBranches);
            ConfigUtil.SaveConfig(_configPath, _config);
            var branchName = SelectBranch(repository);
            var branch = repository.Branches.First(b => b.Name == branchName);
            branch.LastUsed = DateTime.Now;
            var localRepoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "repositories", repo);
            await CloneOrUpdateRepository(owner, repo, branchName, token, localRepoPath);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Language.Error_ProcessingRepository} {ex.Message}[/]");
        }
    }

    // private static async Task ProcessRepositoryAsync(string[] repoParts, string token, string repoName)
    // {
    //     var owner = repoParts[0];
    //     var repo = repoParts[1];
    //
    //     var client = new GitHubClient(new ProductHeaderValue("GitHubBranchDownloader"))
    //     {
    //         Credentials = new Octokit.Credentials(token)
    //     };
    //
    //     try
    //     {
    //         var repository = _config.Repositories.First(r => r.Name == repoName);
    //         var githubBranches = await client.Repository.Branch.GetAll(owner, repo);
    //         SynchronizeBranches(repository, githubBranches);
    //         ConfigUtil.SaveConfig(_configPath, _config);
    //
    //         var branchName = SelectBranch(repository);
    //         var branch = repository.Branches.First(b => b.Name == branchName);
    //         branch.LastUsed = DateTime.Now;
    //
    //         var localRepoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "repositories", repo);
    //
    //         await CloneOrUpdateRepository(owner, repo, branchName, token, localRepoPath);
    //
    //         SwitchToBranch(localRepoPath, branchName);
    //
    //         var csprojFiles = GetCsprojFiles(localRepoPath);
    //         SynchronizeProjects(branch, csprojFiles);
    //         ConfigUtil.SaveConfig(_configPath, _config);
    //
    //         var projectName = SelectProject(branch);
    //         var project = branch.Projects.First(p => p.Name == projectName);
    //         project.LastUsed = DateTime.Now;
    //
    //         var projectPath = csprojFiles.First(x => RemoveFileExtension(x.Value, ".csproj") == projectName).Key;
    //         var projectFolder = projectPath.Replace(projectName + ".csproj", "");
    //         var publishFolder = GetPublishFolder(branchName, projectName, repoName);
    //
    //         var configuration = AnsiConsole.Confirm(Language.Prompt_UseReleaseConfiguration_) ? "Release" : "Debug";
    //         if (string.IsNullOrWhiteSpace(configuration))
    //         {
    //             configuration = "Release";
    //         }
    //
    //         var isDotNetCoreOrHigher = IsDotNetCoreOrHigher(projectFolder);
    //
    //         PublishProject(projectPath, isDotNetCoreOrHigher, configuration);
    //
    //         CopyBuildOutputToPublishFolder(projectPath, publishFolder);
    //         FinishDeploy();
    //     }
    //     catch (Exception ex)
    //     {
    //         AnsiConsole.MarkupLine($"[red]{Language.Error_ProcessingRepository} {ex.Message}[/]");
    //     }
    // }

    private static void SwitchToBranch(string localRepoPath, string branchName)
    {
        using var repo = new LibGit2Sharp.Repository(localRepoPath);
        var branch = repo.Branches.Select(x => x.FriendlyName).FirstOrDefault(x => x == branchName);
        if (branch != null)
        {
            Commands.Checkout(repo, branch);
        }
    }

    private static async Task<string> CloneOrUpdateRepository(string owner, string repo, string branchName,
        string token, string localRepoPath)
    {
        if (!Directory.Exists(localRepoPath))
        {
            Directory.CreateDirectory(localRepoPath);
        }

        var client = new GitHubClient(new ProductHeaderValue("GitHubBranchDownloader"))
        {
            Credentials = new Octokit.Credentials(token)
        };

        try
        {
            var repoDir = new DirectoryInfo(localRepoPath);

            if (repoDir.Exists && Directory.GetFiles(localRepoPath).Length > 0)
            {
                var remoteCommits =
                    await client.Repository.Commit.GetAll(owner, repo, new CommitRequest { Sha = branchName });
                var localCommits = await GetLocalCommits(localRepoPath, branchName);

                var commitsToUpdate = remoteCommits.Except(localCommits).ToList();
                if (commitsToUpdate.Any())
                {
                    await UpdateRepository(localRepoPath, owner, repo, branchName);
                }
            }
            else
            {
                await CloneRepository(owner, repo, branchName, localRepoPath);
            }

            return localRepoPath;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Language.Error_CloningOrUpdatingRepository} {ex.Message}[/]");
            throw;
        }
    }

    private static async Task CloneRepository(string owner, string repo, string branchName, string localRepoPath)
    {
        AnsiConsole.Status().Start(Language.Info_CloningRepository, ctx =>
        {
            var cloneProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone --branch {branchName} https://github.com/{owner}/{repo}.git {localRepoPath}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            cloneProcess?.WaitForExit();
            ctx.Status(Language.Info_RepositoryClonedSuccessfully);
        });
    }

    private static async Task UpdateRepository(string localRepoPath, string owner, string repo, string branchName)
    {
        AnsiConsole.Status().Start(Language.Info_UpdatingRepository, ctx =>
        {
            var fetchProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"fetch --all",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = localRepoPath
            });

            fetchProcess?.WaitForExit();

            var checkoutProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"checkout {branchName}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = localRepoPath
            });

            checkoutProcess?.WaitForExit();

            var pullProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"pull origin {branchName}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = localRepoPath
            });

            pullProcess?.WaitForExit();
            ctx.Status(Language.Info_RepositoryUpdatedSuccessfully);
        });
    }

    private static async Task<IReadOnlyList<GitHubCommit>> GetLocalCommits(string localRepoPath, string branchName)
    {
        var commitList = new List<GitHubCommit>();

        var commitProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"log {branchName} --pretty=format:\"%H\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = localRepoPath
        });

        if (commitProcess != null)
        {
            using var reader = commitProcess.StandardOutput;
            while (await reader.ReadLineAsync() is { } commitHash)
            {
                commitList.Add(new GitHubCommit("", "", "", "", commitHash, null, null, null, null, null, null, null,
                    null, null, null));
            }
        }

        await commitProcess?.WaitForExitAsync()!;
        return commitList;
    }

    private static string SelectBranch(Repository repository)
    {
        var options = BuildBranchOptions(repository);
        var option = Prompt.Select(Language.Prompt_SelectOrCreateBranchConfig, options, null, null,
            kvp => $"{kvp.Value}");
        return option.Value;
    }

    private static List<KeyValuePair<Guid, string>> BuildBranchOptions(Repository repository)
    {
        var keyValuePairs = repository.Branches
            .OrderByDescending(branch => branch.LastUsed)
            .Select(branch => new KeyValuePair<Guid, string>(branch.Id, branch.Name!))
            .ToList();
        // keyValuePairs.Add(new KeyValuePair<Guid, string>(Guid.Empty, $"--- {Language.Option_Exit} ---"));

        return keyValuePairs;
    }

    private static Dictionary<string, string> GetCsprojFiles(string localRepoPath)
    {
        var csprojFiles = Directory.GetFiles(localRepoPath, "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(csprojFile => csprojFile, Path.GetFileName);

        return csprojFiles!;
    }

    private static string SelectProject(Branch branch)
    {
        var options = BuildProjectOptions(branch);
        var option = Prompt.Select(Language.Prompt_SelectOrCreateProjectConfig, options, null, null,
            kvp => $"{kvp.Value}");
        return option.Value;
    }

    private static List<KeyValuePair<Guid, string>> BuildProjectOptions(Branch branch)
    {
        var keyValuePairs = branch.Projects
            .OrderByDescending(project => project.LastUsed)
            .Select(project => new KeyValuePair<Guid, string>(project.Id, project.Name!))
            .ToList();
        // keyValuePairs.Add(new KeyValuePair<Guid, string>(Guid.Empty, $"--- {Language.Option_Exit} ---"));

        return keyValuePairs;
    }

    private static string RemoveFileExtension(string fileName, string extensionToRemove)
    {
        return fileName.EndsWith(extensionToRemove, StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - extensionToRemove.Length)
            : fileName;
    }

    private static void SynchronizeBranches(Repository repository, IReadOnlyList<Octokit.Branch> githubBranches)
    {
        var githubBranchNames = githubBranches.Select(b => b.Name).ToList();
        foreach (var branch in from branchName in githubBranchNames
                 let branch = repository.Branches.FirstOrDefault(b => b.Name == branchName)
                 where branch == null
                 select new Branch { Name = branchName })
        {
            repository.Branches.Add(branch);
        }

        repository.Branches.RemoveAll(b => !githubBranchNames.Contains(b.Name));
    }

    private static void SynchronizeProjects(Branch branch, Dictionary<string, string> csprojFiles)
    {
        var projectNames = csprojFiles.Values.Select(csprojFile => RemoveFileExtension(csprojFile, ".csproj")).ToList();
        branch.Projects.RemoveAll(p => !projectNames.Contains(p.Name));
        foreach (var csprojFile in csprojFiles.Values)
        {
            var projectName = RemoveFileExtension(csprojFile, ".csproj");
            if (branch.Projects.All(p => p.Name != projectName))
            {
                branch.Projects.Add(new Project { Id = Guid.NewGuid(), Name = projectName });
            }
        }
    }

    private static string GetPublishFolder(string branchName, string projectName, string repoName)
    {
        var projectBranchPublishConfig = (from repository in _config.Repositories
            from branch in repository.Branches
            from project in branch.Projects
            where branch.Name == branchName && project.Name == projectName &&
                  !string.IsNullOrEmpty(project.PublishFolder)
            select project).FirstOrDefault();

        string publishFolder;

        if (projectBranchPublishConfig != null)
        {
            AnsiConsole.MarkupLine($"{Language.Info_PublishFolderFound} '{projectBranchPublishConfig.PublishFolder}'");
            var useExistingFolder = AnsiConsole.Confirm($"{Language.Prompt_WishToUseExistingPublishFolder}");
            if (useExistingFolder)
            {
                publishFolder = projectBranchPublishConfig.PublishFolder;
            }
            else
            {
                publishFolder = AnsiConsole.Ask<string>($"{Language.Prompt_EnterDestinationFolder}:");
                UpdatePublishConfiguration(_config, _configPath, repoName, branchName, projectName, publishFolder);
            }
        }
        else
        {
            publishFolder = AnsiConsole.Ask<string>($"{Language.Prompt_EnterDestinationFolder}:");
            UpdatePublishConfiguration(_config, _configPath, repoName, branchName, projectName, publishFolder);
        }

        if (!Directory.Exists(publishFolder))
        {
            Directory.CreateDirectory(publishFolder);
        }

        AnsiConsole.MarkupLine($"{Language.Info_UsingPublishFolder} {publishFolder}");
        return publishFolder;
    }

    private static bool IsDotNetCoreOrHigher(string projectPath)
    {
        var csprojFile = Directory.GetFiles(projectPath, "*.csproj").FirstOrDefault();
        if (csprojFile == null)
        {
            throw new FileNotFoundException("Project file (.csproj) not found in the specified directory.");
        }

        var lines = File.ReadAllLines(csprojFile);
        return lines.Any(line => line.Contains("<TargetFramework>netcoreapp") || line.Contains("<TargetFramework>net"));
    }

    private static void PublishProject(string projectPath, bool isDotNetCoreOrHigher, string configuration)
    {
        CleanDirectory(_buildOutputPath);
        AnsiConsole.MarkupLine($"[blue]{Language.Info_ProjectBuildStarted}[/]");

        if (isDotNetCoreOrHigher)
        {
            PublishUsingDotNet(projectPath, configuration);
        }
        else
        {
            RestoreNugetPackages(projectPath);
            PublishUsingMsBuild(projectPath, configuration);
        }

        AnsiConsole.MarkupLine($"[green]{Language.Info_ProjectBuiltSuccessfully}[/]");
    }

    private static void CopyBuildOutputToPublishFolder(string projectPath, string publishFolder)
    {
        AnsiConsole.MarkupLine($"[blue]{Language.Info_FileCopyStarted}[/]");
        AnsiConsole.Status().Start(Language.Info_CopyingFilesToThePublishDestinationFolder, ctx =>
        {
            FileCopier.CopyFilesRecursively(new DirectoryInfo(_buildOutputPath), new DirectoryInfo(publishFolder));
            ctx.Status(Language.Info_CopyingFilesToThePublishDestinationFolder);
        });
        AnsiConsole.MarkupLine($"[green]{Language.Info_FilesCopiedSuccessfully}[/]");
    }

    private static void FinishDeploy()
    {
        AnsiConsole.MarkupLine($"[green]{Language.Info_DeploymentFinish}[/]");
        AnsiConsole.MarkupLine($"{Language.Prompt_PressToFinish}");
        Console.ReadKey();
        Console.Clear();
    }

    private static void SavePublishConfiguration(string repoName, string branchName, string projectName,
        string publishFolder)
    {
        var repository = _config.Repositories.FirstOrDefault(x => x.Name == repoName) ??
                         new Repository() { Name = repoName };
        var branch = repository.Branches.FirstOrDefault(x => x.Name == branchName) ??
                     new Branch() { Name = branchName };
        var project = branch.Projects.FirstOrDefault(x => x.Name == projectName) ??
                      new Project() { Name = projectName, PublishFolder = publishFolder };

        branch.Projects.Add(project);
        repository.Branches.Add(branch);
        _config.Repositories.Add(repository);

        ConfigUtil.SaveConfig(_configPath, _config);
    }


    private static void RestoreNugetPackages(string projectPath)
    {
        var solutionFile = Directory.GetFiles(Path.GetDirectoryName(projectPath), "*.sln").FirstOrDefault();
        if (solutionFile == null)
        {
            AnsiConsole.MarkupLine("[red]Solution file (.sln) not found in the project directory.[/]");
            return;
        }

        var nugetExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nuget.exe");
        if (!File.Exists(nugetExePath))
        {
            AnsiConsole.MarkupLine("[red]nuget.exe not found in the project root directory.[/]");
            return;
        }

        var nugetRestoreProcessStartInfo = new ProcessStartInfo
        {
            FileName = nugetExePath,
            Arguments = $"restore \"{solutionFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var nugetRestoreProcess = Process.Start(nugetRestoreProcessStartInfo);
        // nugetRestoreProcess.OutputDataReceived += (sender, args) =>
        // {
        //     if (args.Data != null)
        //     {
        //         AnsiConsole.WriteLine(Markup.Escape(args.Data));
        //     }
        // };
        // nugetRestoreProcess.ErrorDataReceived += (sender, args) =>
        // {
        //     if (args.Data != null)
        //     {
        //         AnsiConsole.WriteLine(Markup.Escape(args.Data));
        //     }
        // };
        // nugetRestoreProcess.BeginOutputReadLine();
        // nugetRestoreProcess.BeginErrorReadLine();
        nugetRestoreProcess.WaitForExit();
    }

    private static void PublishUsingDotNet(string projectPath, string configuration)
    {
        if (!IsDotNetAvailable())
        {
            AnsiConsole.MarkupLine($"[red]{Language.Error_DotNetNotFound}[/]");
            return;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectPath}\" -c {configuration} -o \"{_buildOutputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ExecuteProcess(processStartInfo);
    }

    private static void PublishUsingMsBuild(string projectPath, string configuration)
    {
        if (!IsDotNetAvailable())
        {
            AnsiConsole.MarkupLine($"[red]{Language.Error_DotNetNotFound}[/]");
            return;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"msbuild \"{projectPath}\" -property:Configuration={configuration}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ExecuteProcess(processStartInfo);
    }

    private static void ExecuteProcess(ProcessStartInfo processStartInfo)
    {
        AnsiConsole.Status().Start(Language.Info_BuildingProject, ctx =>
        {
            var publishProcess = Process.Start(processStartInfo);
            // publishProcess.OutputDataReceived += (_, e) =>
            // {
            //     if (e.Data != null)
            //     {
            //         AnsiConsole.MarkupLine(Markup.Escape(e.Data));
            //     }
            // };
            // publishProcess.ErrorDataReceived +=
            //     (_, e) =>
            //     {
            //         if (e.Data != null)
            //         {
            //             AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Data)}[/]");
            //         }
            //     };
            // publishProcess.BeginOutputReadLine();
            // publishProcess.BeginErrorReadLine();
            publishProcess.WaitForExit();

            if (publishProcess.ExitCode != 0)
            {
                throw new Exception(Language.Error_ProjectPublishFailed);
            }

            ctx.Status(Language.Info_ProjectBuiltSuccessfully);
        });
    }

    private static bool IsDotNetAvailable()
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMsBuildAvailable()
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "msbuild",
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }


    private static void CleanDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            var directory = new DirectoryInfo(directoryPath);
            foreach (var file in directory.GetFiles())
            {
                file.Delete();
            }

            foreach (var subDirectory in directory.GetDirectories())
            {
                subDirectory.Delete(true);
            }
        }
        else
        {
            Directory.CreateDirectory(directoryPath);
        }
    }


    private static string CloneRepository(string owner, string repo, string branchName)
    {
        var localRepoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "repositories", repo);

        if (!Directory.Exists(localRepoPath))
        {
            Directory.CreateDirectory(localRepoPath);
        }

        AnsiConsole.Status().Start(Language.Info_CloningRepository, ctx =>
        {
            var cloneProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone --branch {branchName} https://github.com/{owner}/{repo}.git {localRepoPath}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            cloneProcess?.WaitForExit();
            ctx.Status(Language.Info_RepositoryClonedSuccessfully);
        });

        return localRepoPath;
    }

    private static void UpdatePublishConfiguration(Config config, string configPath, string repoName, string branchName,
        string projectName, string publishFolder)
    {
        var repository = config.Repositories.FirstOrDefault(x => x.Name == repoName) ??
                         new Repository { Name = repoName };
        var branch = repository.Branches.FirstOrDefault(x => x.Name == branchName) ?? new Branch { Name = branchName };
        var project = branch.Projects.FirstOrDefault(x => x.Name == projectName) ?? new Project { Name = projectName };

        project.PublishFolder = publishFolder;

        if (!config.Repositories.Contains(repository))
        {
            config.Repositories.Add(repository);
        }

        if (!repository.Branches.Contains(branch))
        {
            repository.Branches.Add(branch);
        }

        if (!branch.Projects.Contains(project))
        {
            branch.Projects.Add(project);
        }

        ConfigUtil.SaveConfig(configPath, config);
    }
}