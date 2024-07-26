namespace PackageDeployer.Core.Models;

public class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.MinValue;
    public List<Project> Projects { get; set; } = [];
    public string LatestCommitSha { get; set; }
    
    public override bool Equals(object obj)
    {
        return obj is Branch branch && Name == branch.Name;
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}