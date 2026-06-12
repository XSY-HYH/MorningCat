using YamlDotNet.Serialization;

namespace MorningCat.PPC;

public class PluginConfig
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    [YamlMember(Alias = "author")]
    public string Author { get; set; } = "";

    [YamlMember(Alias = "website")]
    public string Website { get; set; } = "";

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();

    [YamlMember(Alias = "entry")]
    public string Entry { get; set; } = "src/main.py";

    [YamlMember(Alias = "resources")]
    public List<string> Resources { get; set; } = new();

    [YamlMember(Alias = "dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [YamlMember(Alias = "libraryDependencies")]
    public List<string> LibraryDependencies { get; set; } = new();

    public bool HasPythonTag => Tags.Any(t => t.Equals("python", StringComparison.OrdinalIgnoreCase));
}
