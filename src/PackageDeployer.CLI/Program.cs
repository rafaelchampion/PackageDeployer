using System.Diagnostics;
using System.Globalization;
using Octokit;
using PackageDeployer.Core;
using Sharprompt;
using Spectre.Console;
using Language = PackageDeployer.Lang.GlobalStrings;
using Repository = PackageDeployer.Core.Models.Repository;
using Branch = PackageDeployer.Core.Models.Branch;
using Project = PackageDeployer.Core.Models.Project;

Language.Culture = CultureInfo.GetCultureInfo(CultureInfo.CurrentCulture.ToString());
AnsiConsole.Write(new FigletText("Package Deployer (CL EDITION)").Centered().Color(Color.Blue));

var exePath = AppDomain.CurrentDomain.BaseDirectory;
var configPath = Path.Combine(exePath, "config.json");
var config = ConfigUtil.LoadConfig(configPath);

var options = new List<KeyValuePair<int, string>>();
var optionsIndex = 1;
if (config.Repositories.Count > 0)
{
    config.Repositories.ToList().ForEach(x =>
        {
            options.Add(new KeyValuePair<int, string>(optionsIndex, x.Name!.ToString()));
            optionsIndex++;
        }
    );
}

options.Add(new KeyValuePair<int, string>(0, Language.Option_NewRepository));

var selectedOption = Prompt.Select(Language.Prompt_SelectOrCreateRepositoryConfig, options, null, null,
    kvp => $"{kvp.Key} - {kvp.Value}");
var repoName = "";
var token = "";
string[] repoParts = [];
if (selectedOption.Key == 0)
{
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
    if (saveToken)
    {
        config.Repositories.Add(new Repository() { Name = repoName, GitHubToken = token });
        ConfigUtil.SaveConfig(configPath, config);
    }
}
else
{
    var repositoryOption = options.FirstOrDefault(x => x.Key == selectedOption.Key);
    var repositoryConfig = config.Repositories.FirstOrDefault(x => x.Name == repositoryOption.Value);
    repoName = repositoryConfig.Name;
    token = repositoryConfig.GitHubToken;
}


var owner = repoParts[0];
var repo = repoParts[1];

var client = new GitHubClient(new ProductHeaderValue("GitHubBranchDownloader"))
{
    Credentials = new Credentials(token)
};

try
{
    var branches = await client.Repository.Branch.GetAll(owner, repo);
    var branchesIndex = 1;
    var branchesOptions = new List<KeyValuePair<int, string>>();
    branches.ToList().ForEach(x =>
    {
        branchesOptions.Add(new KeyValuePair<int, string>(branchesIndex, x.Name));
        branchesIndex++;
    });

    var selectedBranch = Prompt.Select("Select a branch", branchesOptions, null, null,
        kvp => $"{kvp.Key} - {kvp.Value}");

    var branchName = selectedBranch.Value;

    var buildPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    var localRepoPath = Path.Combine(buildPath, "Publish", repo);

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

    var projects = GetCsprojFiles(localRepoPath);
    var projectsIndex = 1;
    var projectsOptions = new List<KeyValuePair<int, string>>();
    projects.ToList().ForEach(x =>
    {
        projectsOptions.Add(new KeyValuePair<int, string>(projectsIndex, RemoveFileExtension(x.Value, ".csproj")));
        projectsIndex++;
    });

    var selectedProject = Prompt.Select("Select a project", projectsOptions, null, null,
        kvp => $"{kvp.Key} - {kvp.Value}");

    var projectName = selectedProject.Value;

    var projectPath = projects.FirstOrDefault(x => RemoveFileExtension(x.Value, ".csproj") == projectName).Key;

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

    var publishFolder = "";

    var projectBranchPublishConfig = (from repository in config.Repositories
        from branch in repository.Branches
        from project in branch.Projects
        where branch.Name == branchName && project.Name == projectName &&
              !string.IsNullOrEmpty(project.PublishFolder)
        select project).FirstOrDefault();

    if (projectBranchPublishConfig == null)
    {
        publishFolder = AnsiConsole.Ask<string>($"{Language.Prompt_EnterDestinationFolder}:");
        var savePublishFolder = AnsiConsole.Confirm(Language.Prompt_WishToSavePublishConfiguration);
        if (savePublishFolder)
        {
            var repository = config.Repositories.FirstOrDefault(x => x.Name == repoName) ?? new Repository()
            {
                Name = repoName
            };
            var branch = repository.Branches.FirstOrDefault(x => x.Name == branchName);
            if (branch == null)
            {
                branch = new Branch()
                {
                    Name = branchName
                };
                repository.Branches.Add(branch);
            }

            var project = branch.Projects.FirstOrDefault(x => x.Name == projectName);
            if (project == null)
            {
                project = new Project()
                {
                    Name = projectName,
                    PublishFolder = publishFolder
                };
                branch.Projects.Add(project);
            }

            var index = config.Repositories.ToList().FindIndex(x => x.Name == repository.Name);
            config.Repositories.ToList()[index] = repository;
            ConfigUtil.SaveConfig(configPath, config);
        }
    }
    else
    {
        publishFolder = projectBranchPublishConfig.PublishFolder;
        AnsiConsole.MarkupLine(
            $"Encontrada configuração de publicação do projeto {projectName}, branch {branchName} na pasta {publishFolder}");
    }

    if (!Directory.Exists(publishFolder))
    {
        Directory.CreateDirectory(publishFolder);
    }

    var buildOutputPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Debug");
    AnsiConsole.Status().Start(Language.Info_CopyingFilesToThePublishDestinationFolder, ctx =>
    {
        FileCopier.CopyFilesRecursively(new DirectoryInfo(buildOutputPath), new DirectoryInfo(publishFolder));
        ctx.Status(Language.Info_CopyingFilesToThePublishDestinationFolder);
    });

    AnsiConsole.MarkupLine("[green]Project built and copied successfully.[/]");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]An error occurred: {ex.Message}[/]");
}

static List<KeyValuePair<string, string>> GetCsprojFiles(string rootFolder)
{
    return (from directory in Directory.GetDirectories(rootFolder, "*", SearchOption.AllDirectories)
        from file in Directory.GetFiles(directory, "*.csproj")
        select new KeyValuePair<string, string>(directory, Path.GetFileName(file))).ToList();
}

static string RemoveFileExtension(string fileName, string extensionToRemove)
{
    return fileName.EndsWith(extensionToRemove, StringComparison.OrdinalIgnoreCase)
        ? fileName.Substring(0, fileName.Length - extensionToRemove.Length)
        : fileName;
}