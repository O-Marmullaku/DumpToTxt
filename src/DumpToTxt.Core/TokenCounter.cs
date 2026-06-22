using System.Collections.Concurrent;
using Microsoft.ML.Tokenizers;

namespace DumpToTxt.Core;

/// <summary>The BPE encoding used for token counting.</summary>
public enum TokenEncoding
{
    /// <summary>GPT-4o / o-series era (current). The default; closest sane proxy for the Claude era too.</summary>
    O200kBase,
    /// <summary>Legacy gpt-4 / gpt-3.5-turbo / text-embedding era.</summary>
    Cl100kBase,
}

/// <summary>
/// Counts BPE tokens with <c>Microsoft.ML.Tokenizers</c> (Tiktoken, embedded vocab). The engine uses this
/// to compute per-file/total token counts into the <see cref="DumpModel"/> so every formatter shares one
/// source of truth. Tokenizer instances are cached per encoding (creating one parses the vocab once).
/// Pure compute — no IO, no network.
/// </summary>
public static class TokenCounter
{
    private static readonly ConcurrentDictionary<TokenEncoding, TiktokenTokenizer> Cache = new();

    /// <summary>The canonical tiktoken encoding name (e.g. "o200k_base"), as used in output/labels.</summary>
    public static string EncodingName(TokenEncoding enc) => enc switch
    {
        TokenEncoding.Cl100kBase => "cl100k_base",
        _ => "o200k_base",
    };

    private static TiktokenTokenizer Get(TokenEncoding enc) =>
        Cache.GetOrAdd(enc, e => TiktokenTokenizer.CreateForEncoding(EncodingName(e)));

    /// <summary>Token count of <paramref name="text"/> under <paramref name="enc"/>. Empty text ⇒ 0.</summary>
    public static int Count(string text, TokenEncoding enc) =>
        string.IsNullOrEmpty(text) ? 0 : Get(enc).CountTokens(text);
}
