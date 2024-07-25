using System.Text.Json;
using PackageDeployer.Core.Models;

namespace PackageDeployer.Core;

public static class ConfigUtil
{
    public static Config LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return new Config();
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<Config>(json);
    }

    public static void SaveConfig(string configPath, Config config)
    {
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(configPath, json);
    }
}