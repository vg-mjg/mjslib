using System.Collections.Generic;
using BepInEx.Configuration;

namespace Mjslib
{
    public sealed class ConfigEntryInfo
    {
        internal ConfigEntryInfo(
            string mod, string section, string key, string type, string description,
            object? defaultValue, double? min, double? max, IReadOnlyList<string>? choices,
            string source, ConfigEntryBase entry)
        {
            Mod = mod;
            Section = section;
            Key = key;
            Type = type;
            Description = description;
            DefaultValue = defaultValue;
            Min = min;
            Max = max;
            Choices = choices;
            Source = source;
            Entry = entry;
        }

        public string Mod { get; }

        public string Section { get; }

        public string Key { get; }

        public string Type { get; }

        public string Description { get; }

        public object? DefaultValue { get; }

        public double? Min { get; }

        public double? Max { get; }

        public IReadOnlyList<string>? Choices { get; }

        public string Source { get; }

        public ConfigEntryBase Entry { get; }

        public object? CurrentValue => Entry.BoxedValue;
    }
}
