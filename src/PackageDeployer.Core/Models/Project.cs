namespace PackageDeployer.Core.Models;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public string PublishFolder { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.MinValue;
}