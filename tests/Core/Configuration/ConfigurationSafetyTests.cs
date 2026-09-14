using System.Text.Json.Nodes;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public class ConfigurationSafetyTests
{
    [Fact]
    public void NullConfigurationIsNotAnEmptyPrivacyOverlay()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-config-null-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, ConfigStore.FolderConfigName);
            File.WriteAllText(path, "null");
            var error = Assert.Throws<InvalidDataException>(() => ConfigStore.Resolve(root, "", ""));
            Assert.Contains(path, error.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MalformedFolderPrivacyConfigurationCannotBeSilentlyIgnored()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-config-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, ConfigStore.FolderConfigName);
            File.WriteAllText(path, """{"SensitiveValuePatterns":["private-value"],broken}""");
            var error = Assert.Throws<InvalidDataException>(() => ConfigStore.Resolve(root, "", ""));
            Assert.Contains(path, error.Message);
            Assert.DoesNotContain("private-value", error.ToString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SettingsSavePreservesUnknownFieldsAndLeavesCorruptFileUntouched()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string user = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(user, """{"futureSetting":{"enabled":true}}""");
            ConfigStore.SaveTo(user, DumpConfig.CreateDefault());
            Assert.True(JsonNode.Parse(File.ReadAllText(user))!["futureSetting"]!["enabled"]!.GetValue<bool>());
            File.WriteAllText(user, "{broken");
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => ConfigStore.SaveTo(user, DumpConfig.CreateDefault()));
            Assert.Equal("{broken", File.ReadAllText(user));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RunPreferencesPreserveSparseUserAndInheritedSafety()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string machine = Path.Combine(root, "machine.json"), user = Path.Combine(root, "user.json");
            File.WriteAllText(machine, """{"SecretScan":"Redact","MaxTotalSizeBytes":500,"SensitiveValuePatterns":["private"]}""");
            File.WriteAllText(user, """{"Theme":"Blue","futureSetting":42}""");
            var run = ConfigStore.Resolve(root, machine, user);
            run.Style = OutputStyle.Json;
            ConfigStore.SaveRunPreferences(run, user);
            var back = ConfigStore.Resolve(root, machine, user);
            Assert.Equal(SecretScanMode.Redact, back.SecretScan);
            Assert.Equal(500, back.MaxTotalSizeBytes);
            Assert.Equal(new[] { "private" }, back.SensitiveValuePatterns);
            Assert.Equal(42, JsonNode.Parse(File.ReadAllText(user))!["futureSetting"]!.GetValue<int>());
            Assert.Null(JsonNode.Parse(File.ReadAllText(user))!["SecretScan"]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitNullOutputDirectoryOverridesMachineDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string machine = Path.Combine(root, "machine.json"), user = Path.Combine(root, "user.json");
            File.WriteAllText(machine, """{"OutputDir":"C:\\exports"}""");
            File.WriteAllText(user, """{"OutputDir":null}""");
            Assert.Null(ConfigStore.Resolve(root, machine, user).OutputDir);
        }
        finally { Directory.Delete(root, true); }
    }
}
