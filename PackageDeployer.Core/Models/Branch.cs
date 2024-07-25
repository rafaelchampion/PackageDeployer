namespace PackageDeployer.Core.Models;

public class Branch
{
    public string Name { get; set; }
    public ICollection<Project> Projects { get; set; } = [];
}