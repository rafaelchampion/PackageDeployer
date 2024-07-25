namespace PackageDeployer.Core.Models;

public class Repository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public string GitHubToken { get; set; }
    public ICollection<Branch> Branches { get; set; } = [];
}