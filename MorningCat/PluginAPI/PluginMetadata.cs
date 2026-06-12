using System.Collections.Generic;

namespace MorningCat.PluginAPI
{
    public class PluginMetadata
    {
        public string ModuleName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Author { get; set; } = "";
        public string Website { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconBase64 { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> PluginDependencies { get; set; } = new List<string>();
        public List<string> LibraryDependencies { get; set; } = new List<string>();
    }
}
