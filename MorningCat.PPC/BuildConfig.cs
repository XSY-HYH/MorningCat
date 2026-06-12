using YamlDotNet.Serialization;

namespace MorningCat.PPC;

public class BuildConfig
{
    [YamlMember(Alias = "output")]
    public string Output { get; set; } = "";

    [YamlMember(Alias = "runtime")]
    public RuntimeConfig Runtime { get; set; } = new();

    [YamlMember(Alias = "dependencies")]
    public List<PyDependency> Dependencies { get; set; } = new();

    [YamlMember(Alias = "linkerArgs")]
    public List<string> LinkerArgs { get; set; } = new();

    public string GetOutputName(string fallback)
    {
        return string.IsNullOrWhiteSpace(Output) ? fallback : Output;
    }
}

public class RuntimeConfig
{
    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "";

    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "10.0.300";
}

public class PyDependency
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "";

    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "";
}
