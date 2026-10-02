using System.Text.Json.Nodes;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public class ConfigurationSafetyTests
{
    [Fact]
    public void ReviewPreferencesRememberEveryChoicePerTargetAndPreserveOtherSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-review-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string user = Path.Combine(root, "settings.json"), target = Path.Combine(root, "first");
            File.WriteAllText(user, """{"futureSetting":42,"SensitiveValuePatterns":["private"]}""");
            var cfg = DumpConfig.CreateDefault();
            cfg.Style = OutputStyle.MarkdownAi;
            cfg.OutputTarget = OutputTarget.Clipboard;
            cfg.OutputDir = root;
            cfg.ShowReviewBeforeDump = false;
            cfg.LastSelectionMode = DumpSelectionMode.None;
            cfg.RespectGitignore = false;
            cfg.UseDumpToTxtIgnore = false;
            cfg.SecretScan = SecretScanMode.Redact;
            var selection = DumpContentSelection.Empty.WithPathOverrides(new Dictionary<string, bool>
            { ["folder"] = true, ["folder/skip.txt"] = false });
            ConfigStore.SaveReviewPreferences(target, cfg, selection, user);
            ConfigStore.SaveReviewPreferences(Path.Combine(root, "second"), DumpConfig.CreateDefault(), DumpContentSelection.Empty, user);
            var saved = ConfigStore.LoadReviewPreferences(target.ToUpperInvariant(), user)!;
            var restored = DumpConfig.CreateDefault();
            saved.Apply(restored);
            Assert.Equal(cfg.Style, restored.Style);
            Assert.Equal(cfg.OutputTarget, restored.OutputTarget);
            Assert.Equal(root, restored.OutputDir);
            Assert.False(restored.ShowReviewBeforeDump);
            Assert.Equal(DumpSelectionMode.None, restored.LastSelectionMode);
            Assert.False(restored.RespectGitignore);
            Assert.False(restored.UseDumpToTxtIgnore);
            Assert.Equal(SecretScanMode.Redact, restored.SecretScan);
            Assert.True(saved.PathOverrides["folder"]);
            Assert.False(saved.PathOverrides["folder/skip.txt"]);
            Assert.Null(ConfigStore.LoadReviewPreferences(Path.Combine(root, "third"), user));
            var document = JsonNode.Parse(File.ReadAllText(user))!;
            Assert.Equal(42, document["futureSetting"]!.GetValue<int>());
            Assert.Equal("private", document["SensitiveValuePatterns"]![0]!.GetValue<string>());
            Assert.Null(document["SecretScan"]);
        }
        finally { Directory.Delete(root, true); }
    }

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
