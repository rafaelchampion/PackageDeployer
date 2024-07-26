namespace PackageDeployer.Core.Models;

public class Repository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public string GitHubToken { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.MinValue;
    public List<Branch> Branches { get; set; } = [];
    
    public override bool Equals(object obj)
    {
        return obj is Repository repository && Name == repository.Name;
    }
    
    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}