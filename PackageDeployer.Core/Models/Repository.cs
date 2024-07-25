namespace PackageDeployer.Core.Models;

public class Repository
{
    public string Name { get; set; }
    public string GitHubToken { get; set; }
    public ICollection<Branch> Branches { get; set; } = [];
}