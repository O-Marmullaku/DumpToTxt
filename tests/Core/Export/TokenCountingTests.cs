using System.Text;
using System.Text.Json;
using DumpToTxt.Core;

namespace DumpToTxt.Tests;

/// <summary>token token counting: TokenCounter accuracy, per-file/total/top-N/token-tree surfaced in the
/// non-Classic formatters, the MaxTokens budget warn flag, config round-trip of the new fields, and the
/// guarantee that token counting NEVER changes Classic's (golden-pinned) byte output.</summary>
public class TokenCountingTests
{
    private static readonly UTF8Encoding NoBom = new(false);

    private static string NewTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "dtt-p5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void W(string root, string rel, string content)
    {
        string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, NoBom);
    }

    private static JsonDocument RenderJson(string root, DumpConfig cfg)
    {
        cfg.Style = OutputStyle.Json;
        cfg.OutputTarget = OutputTarget.Stdout;
        return JsonDocument.Parse(new DumpEngine().Run(root, cfg).Text);
    }

    // ---------- TokenCounter ----------

    [Theory]
    [InlineData(TokenEncoding.O200kBase)]
    [InlineData(TokenEncoding.Cl100kBase)]
    public void Count_EmptyIsZero_KnownWordIsOne(TokenEncoding enc)
    {
        Assert.Equal(0, TokenCounter.Count("", enc));
        Assert.Equal(1, TokenCounter.Count("hello", enc));            // a single common word = 1 BPE token
        Assert.True(TokenCounter.Count("public static void Main() {}", enc) > 1);
    }

    [Fact]
    public void EncodingName_MapsToCanonicalTiktokenIds()
    {
        Assert.Equal("o200k_base", TokenCounter.EncodingName(TokenEncoding.O200kBase));
        Assert.Equal("cl100k_base", TokenCounter.EncodingName(TokenEncoding.Cl100kBase));
    }

    // ---------- engine: per-file + total surfaced in JSON ----------

    [Fact]
    public void Json_PerFileAndTotalTokens_SumAndEncodingSurfaced()
    {
        string root = NewTree();
        try
        {
            W(root, "a.cs", "public class A { }\n");
            W(root, "b.cs", "public class B { public int X; public int Y; }\n");
            var doc = RenderJson(root, DumpConfig.CreateDefault());   // default = o200k_base
            var r = doc.RootElement;

            Assert.Equal("o200k_base", r.GetProperty("tokenEncoding").GetString());
            long total = r.GetProperty("totalTokens").GetInt64();
            long sum = r.GetProperty("fileList").EnumerateArray()
                .Sum(f => f.GetProperty("tokens").GetInt64());
            Assert.True(total > 0);
            Assert.Equal(total, sum);                                  // total == sum of per-file
            foreach (var f in r.GetProperty("fileList").EnumerateArray())
                Assert.True(f.GetProperty("tokens").GetInt64() > 0);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Json_TopFilesAndTokenTree_Present()
    {
        string root = NewTree();
        try
        {
            W(root, "small.cs", "class S {}\n");
            W(root, "big/large.cs", string.Concat(Enumerable.Repeat("public int Field = 0;\n", 50)));
            var doc = RenderJson(root, DumpConfig.CreateDefault());
            var r = doc.RootElement;

            Assert.True(r.TryGetProperty("topFilesByTokens", out var top));
            Assert.True(top.GetArrayLength() >= 2);
            // top is sorted desc by tokens -> the large file leads.
            Assert.Equal("big/large.cs", top[0].GetProperty("path").GetString()!.Replace('\\', '/'));
            Assert.True(r.TryGetProperty("tokenTree", out var tree));
            Assert.Contains("tokens", tree.GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TokenTree_ExcludesZeroTokenFiles_ButStructureKeepsThem()
    {
        string root = NewTree();
        try
        {
            W(root, "code.cs", "public class A { public int X; }\n");   // has tokens
            W(root, "empty.cs", "");                                     // 0 tokens
            var r = RenderJson(root, DumpConfig.CreateDefault()).RootElement;
            string tree = r.GetProperty("tokenTree").GetString()!;
            Assert.Contains("code.cs", tree);
            Assert.DoesNotContain("empty.cs", tree);                     // 0-token file omitted from the token tree
            Assert.Contains("empty.cs", r.GetProperty("directoryStructure").GetString()!);  // but kept in structure
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Json_Budget_OverFlagAndMaxTokens()
    {
        string root = NewTree();
        try
        {
            W(root, "a.cs", "public class A { public int X; }\n");
            var cfg = DumpConfig.CreateDefault();
            cfg.MaxTokens = 1;                                         // tiny -> guaranteed over
            var r = RenderJson(root, cfg).RootElement;
            Assert.Equal(1, r.GetProperty("maxTokens").GetInt64());
            Assert.True(r.GetProperty("overBudget").GetBoolean());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Json_NoBudget_OmitsBudgetFields()
    {
        string root = NewTree();
        try
        {
            W(root, "a.cs", "class A {}\n");
            var r = RenderJson(root, DumpConfig.CreateDefault()).RootElement;  // MaxTokens = 0
            Assert.False(r.TryGetProperty("maxTokens", out _));
            Assert.False(r.TryGetProperty("overBudget", out _));
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- config round-trip ----------

    [Fact]
    public void Config_TokenFields_RoundTripThroughSerializeAndResolve()
    {
        var cfg = DumpConfig.CreateDefault();
        cfg.TokenEncoding = TokenEncoding.Cl100kBase;
        cfg.MaxTokens = 5000;
        var back = ConfigStore.Parse(ConfigStore.Serialize(cfg));
        Assert.Equal(TokenEncoding.Cl100kBase, back.TokenEncoding);
        Assert.Equal(5000, back.MaxTokens);

        string root = NewTree();
        try
        {
            File.WriteAllText(Path.Combine(root, ".dumptotxt.json"),
                "{ \"TokenEncoding\": \"Cl100kBase\", \"MaxTokens\": 7000 }");
            string none = Path.Combine(root, "none.json");
            var resolved = ConfigStore.Resolve(root, none, none);
            Assert.Equal(TokenEncoding.Cl100kBase, resolved.TokenEncoding);  // threads through the resolver
            Assert.Equal(7000, resolved.MaxTokens);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- Classic golden safety ----------

    [Fact]
    public void Classic_NeverRendersTokens_AndBudgetDoesNotChangeBytes()
    {
        string root = NewTree();
        try
        {
            W(root, "a.cs", "public class A { }\n");
            var noBudget = DumpConfig.CreateDefault();
            noBudget.Style = OutputStyle.Classic;
            noBudget.OutputTarget = OutputTarget.Stdout;
            string textNoBudget = new DumpEngine().Run(root, noBudget).Text;

            var withBudget = DumpConfig.CreateDefault();
            withBudget.Style = OutputStyle.Classic;
            withBudget.OutputTarget = OutputTarget.Stdout;
            withBudget.MaxTokens = 1;                                  // forces token counting on
            string textWithBudget = new DumpEngine().Run(root, withBudget).Text;

            Assert.DoesNotContain("token", textNoBudget, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OVER BUDGET", textWithBudget);
            Assert.Equal(textNoBudget, textWithBudget);               // computing tokens never alters Classic bytes
        }
        finally { Directory.Delete(root, true); }
    }
}
