using System.Diagnostics;
using System.Globalization;
using Octokit;
using PackageDeployer.Core;
using PackageDeployer.Core.Models;
using Sharprompt;
using Spectre.Console;
using Language = PackageDeployer.Lang.GlobalStrings;
using Repository = PackageDeployer.Core.Models.Repository;
using Branch = PackageDeployer.Core.Models.Branch;
using Project = PackageDeployer.Core.Models.Project;

namespace PackageDeployer.CLI;

internal class Program
{
    private static bool _run = true;
    private static string _configPath;
    private static Config _config;

    public static async Task Main(string[] args)
    {
        Setup();
        Greetings();
        while (_run)
        {
            await MainLoop();
        }
    }

    private static void Setup()
    {
        Language.Culture = CultureInfo.GetCultureInfo(CultureInfo.CurrentCulture.ToString());
        var exePath = AppDomain.CurrentDomain.BaseDirectory;
        _configPath = Path.Combine(exePath, "config.json");
        _config = ConfigUtil.LoadConfig(_configPath);
    }

    private static void Greetings()
    {
        AnsiConsole.Write(new FigletText("Package Deployer (CL EDITION)").Centered().Color(Color.Blue));
        Console.WriteLine();
    }

    private static async Task MainLoop()
    {
        try
        {
            var options = BuildMainMenuOptions(_config);
            var selectedOption = Prompt.Select(Language.Prompt_SelectOrCreateRepositoryConfig, options, null, null, kvp => $"{kvp.Value}");
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
            AnsiConsole.MarkupLine($"[red]An error occurred during the main loop: {ex.Message}[/]");
        }
    }

    private static async Task HandleNewRepositoryAsync()
    {
        try
        {
            var repoName = CreateNewRepository(out var repoParts, out var token);
            await ProcessRepositoryAsync(repoParts, token, repoName);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]An error occurred while handling a new repository: {ex.Message}[/]");
        }
    }
    
    private static async Task HandleExistingRepositoryAsync(KeyValuePair<Guid, string> selectedOption)
    {
        try
        {
            var repositoryOption = _config.Repositories.FirstOrDefault(x => x.Name == selectedOption.Value);
            if (repositoryOption == null)
            {
                AnsiConsole.MarkupLine($"[red]Repository not found: {selectedOption.Value}[/]");
                return;
            }
            var repoName = LoadExistingRepository(repositoryOption, out var repoParts, out var token);
            await ProcessRepositoryAsync(repoParts, token, repoName);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]An error occurred while handling an existing repository: {ex.Message}[/]");
        }
    }

    private static List<KeyValuePair<string, string>> GetCsprojFiles(string rootFolder)
    {
        return Directory.GetDirectories(rootFolder, "*", SearchOption.AllDirectories)
            .SelectMany(directory => Directory.GetFiles(directory, "*.csproj"), (directory, file) => new KeyValuePair<string, string>(directory, Path.GetFileName(file)))
            .ToList();
    }

    private static string RemoveFileExtension(string fileName, string extensionToRemove)
    {
        return fileName.EndsWith(extensionToRemove, StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - extensionToRemove.Length)
            : fileName;
    }

    private static List<KeyValuePair<Guid, string>> BuildMainMenuOptions(Config repoConfig)
    {
        var keyValuePairs = repoConfig.Repositories
            .Select(repository => new KeyValuePair<Guid, string>(repository.Id, repository.Name!))
            .ToList();

        keyValuePairs.Add(new KeyValuePair<Guid, string>(Guid.Parse("99999999-9999-9999-9999-999999999999"), Language.Option_NewRepository));
        keyValuePairs.Add(new KeyValuePair<Guid, string>(Guid.Empty, Language.Option_Exit));

        return keyValuePairs;
    }

    private static void MainMenuExit()
    {
        _run = false;
    }

    private static string CreateNewRepository(out string[] repoParts, out string token)
    {
        string repoName;
        do
        {
            repoName = AnsiConsole.Ask<string>(Language.Prompt_EnterRepositoryName);
            repoParts = repoName.Split('/');
            if (repoParts.Length != 2)
            {
                AnsiConsole.MarkupLine(Language.Error_InvalidRepositoryFormat);
            }
        } while (repoParts.Length != 2);

        token = AnsiConsole.Ask<string>(Language.Prompt_EnterGitHubToken);
        var saveToken = AnsiConsole.Confirm(Language.Prompt_WishToSaveConfig);
        if (!saveToken) return repoName;
        _config.Repositories.Add(new Repository() { Name = repoName, GitHubToken = token });
        ConfigUtil.SaveConfig(_configPath, _config);
        return repoName;
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
            Credentials = new Credentials(token)
        };

        try
        {
            var branches = await client.Repository.Branch.GetAll(owner, repo);
            var branchName = SelectBranch(branches);
            var localRepoPath = CloneRepository(owner, repo, branchName);

            var projects = GetCsprojFiles(localRepoPath);
            var projectName = SelectProject(projects);

            var projectPath = projects.First(x => RemoveFileExtension(x.Value, ".csproj") == projectName).Key;
            BuildProject(projectPath);

            var publishFolder = GetPublishFolder(branchName, projectName, repoName);
            CopyBuildOutputToPublishFolder(projectPath, publishFolder);

            AnsiConsole.MarkupLine("[green]Project built and copied successfully.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]An error occurred while processing the repository: {ex.Message}[/]");
        }
    }
    
    private static void CopyBuildOutputToPublishFolder(string projectPath, string publishFolder)
    {
        var buildOutputPath = Path.Combine(projectPath, "bin", "Debug","netstandard2.0");
        AnsiConsole.Status().Start(Language.Info_CopyingFilesToThePublishDestinationFolder, ctx =>
        {
            FileCopier.CopyFilesRecursively(new DirectoryInfo(buildOutputPath), new DirectoryInfo(publishFolder));
            ctx.Status(Language.Info_CopyingFilesToThePublishDestinationFolder);
        });
    }
    
    private static void SavePublishConfiguration(string repoName, string branchName, string projectName, string publishFolder)
    {
        var repository = _config.Repositories.FirstOrDefault(x => x.Name == repoName) ?? new Repository() { Name = repoName };
        var branch = repository.Branches.FirstOrDefault(x => x.Name == branchName) ?? new Branch() { Name = branchName };
        var project = branch.Projects.FirstOrDefault(x => x.Name == projectName) ?? new Project() { Name = projectName, PublishFolder = publishFolder };

        branch.Projects.Add(project);
        repository.Branches.Add(branch);
        _config.Repositories.Add(repository);

        ConfigUtil.SaveConfig(_configPath, _config);
    }
    
    private static string GetPublishFolder(string branchName, string projectName, string repoName)
    {
        var projectBranchPublishConfig = _config.Repositories
            .SelectMany(repo => repo.Branches)
            .SelectMany(branch => branch.Projects)
            .FirstOrDefault(project => project.Name == projectName && !string.IsNullOrEmpty(project.PublishFolder));

        string publishFolder;
        if (projectBranchPublishConfig == null)
        {
            publishFolder = AnsiConsole.Ask<string>($"{Language.Prompt_EnterDestinationFolder}:");
            if (AnsiConsole.Confirm(Language.Prompt_WishToSavePublishConfiguration))
            {
                SavePublishConfiguration(repoName, branchName, projectName, publishFolder);
            }
        }
        else
        {
            publishFolder = projectBranchPublishConfig.PublishFolder;
            AnsiConsole.MarkupLine($"Found publish configuration for project {projectName}, branch {branchName} in folder {publishFolder}");
        }

        if (!Directory.Exists(publishFolder))
        {
            Directory.CreateDirectory(publishFolder);
        }

        return publishFolder;
    }
    
    private static void BuildProject(string projectPath)
    {
        AnsiConsole.Status().Start(Language.Info_BuildingProject, ctx =>
        {
            var buildProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            buildProcess!.WaitForExit();
            ctx.Status(Language.Info_ProjectBuiltSuccessfully);
        });
    }
    
    private static string SelectProject(List<KeyValuePair<string, string>> projects)
    {
        var projectsOptions = projects.Select((project, index) => new KeyValuePair<int, string>(index + 1, RemoveFileExtension(project.Value, ".csproj"))).ToList();
        var selectedProject = Prompt.Select("Select a project", projectsOptions, null, null, kvp => $"{kvp.Key} - {kvp.Value}");
        return selectedProject.Value;
    }
    
    private static string SelectBranch(IReadOnlyList<Octokit.Branch> branches)
    {
        var branchesOptions = branches.Select((branch, index) => new KeyValuePair<int, string>(index + 1, branch.Name)).ToList();
        var selectedBranch = Prompt.Select("Select a branch", branchesOptions, null, null, kvp => $"{kvp.Key} - {kvp.Value}");
        return selectedBranch.Value;
    }
    
    private static string CloneRepository(string owner, string repo, string branchName)
    {
        var localRepoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Publish", repo);

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
}