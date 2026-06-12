using System;
using System.Collections.Generic;

namespace MorningCat.PluginAPI
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class PluginMetadataAttribute : Attribute
    {
        public string DisplayName { get; set; } = "";
        public string Author { get; set; } = "";
        public string Website { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconBase64 { get; set; } = "";
        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}
