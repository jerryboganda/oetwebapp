using System.Text.RegularExpressions;

namespace OetWithDrHesham.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Regex-based heuristic code chunker that splits source files into
/// semantically meaningful pieces. Supports TypeScript/TSX, C#, JSON,
/// YAML, and Markdown.
///
/// Max chunk size: 2000 characters. Overlap: 200 characters for context continuity.
/// </summary>
public sealed class CodeChunker : ICodeChunker
{
    private const int MaxChunkSize = 2000;
    private const int OverlapSize = 200;

    // TypeScript/TSX patterns
    private static readonly Regex TsClassPattern = new(
        @"^(?:export\s+)?(?:abstract\s+)?class\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TsFunctionPattern = new(
        @"^(?:export\s+)?(?:async\s+)?(?:function\s+(\w+)|(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s*)?\(?)",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TsComponentPattern = new(
        @"^(?:export\s+)?(?:default\s+)?(?:const|function)\s+(\w+).*?(?:=>|{)\s*(?:return\s*)?\(?.*?<",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TsInterfacePattern = new(
        @"^(?:export\s+)?(?:interface|type)\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // C# patterns
    private static readonly Regex CsClassPattern = new(
        @"^\s*(?:public|internal|private|protected)?\s*(?:static\s+)?(?:sealed\s+)?(?:abstract\s+)?(?:partial\s+)?(?:class|record|struct)\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex CsInterfacePattern = new(
        @"^\s*(?:public|internal)?\s*interface\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex CsMethodPattern = new(
        @"^\s*(?:public|internal|private|protected)\s+(?:static\s+)?(?:async\s+)?(?:virtual\s+)?(?:override\s+)?(?:[\w<>\[\]?,\s]+)\s+(\w+)\s*[<(]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Markdown heading pattern
    private static readonly Regex MdHeadingPattern = new(
        @"^(#{1,6})\s+(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public IReadOnlyList<CodeChunk> ChunkFile(string filePath, string content, string language)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<CodeChunk>();

        var lang = (language ?? "").ToLowerInvariant();

        return lang switch
        {
            "typescript" or "tsx" or "javascript" or "jsx" => ChunkTypeScript(content, lang),
            "csharp" or "cs" => ChunkCSharp(content, lang),
            "json" => ChunkJson(content, lang),
            "yaml" or "yml" => ChunkYaml(content, lang),
            "markdown" or "md" => ChunkMarkdown(content, lang),
            _ => ChunkBySize(content, lang, null)
        };
    }

    private IReadOnlyList<CodeChunk> ChunkTypeScript(string content, string language)
    {
        var boundaries = new List<(int lineIndex, string? symbol)>();

        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            Match m;

            m = TsClassPattern.Match(line);
            if (m.Success) { boundaries.Add((i, m.Groups[1].Value)); continue; }

            m = TsInterfacePattern.Match(line);
            if (m.Success) { boundaries.Add((i, m.Groups[1].Value)); continue; }

            m = TsFunctionPattern.Match(line);
            if (m.Success)
            {
                var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                boundaries.Add((i, name));
                continue;
            }
        }

        return BuildChunksFromBoundaries(lines, boundaries, language);
    }

    private IReadOnlyList<CodeChunk> ChunkCSharp(string content, string language)
    {
        var boundaries = new List<(int lineIndex, string? symbol)>();

        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            Match m;

            m = CsClassPattern.Match(line);
            if (m.Success) { boundaries.Add((i, m.Groups[1].Value)); continue; }

            m = CsInterfacePattern.Match(line);
            if (m.Success) { boundaries.Add((i, m.Groups[1].Value)); continue; }

            m = CsMethodPattern.Match(line);
            if (m.Success) { boundaries.Add((i, m.Groups[1].Value)); continue; }
        }

        return BuildChunksFromBoundaries(lines, boundaries, language);
    }

    private IReadOnlyList<CodeChunk> ChunkJson(string content, string language)
    {
        // Chunk by top-level keys
        var lines = content.Split('\n');
        var boundaries = new List<(int lineIndex, string? symbol)>();
        var topLevelKeyPattern = new Regex(@"^\s{0,2}""(\w+)""\s*:", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var m = topLevelKeyPattern.Match(lines[i]);
            if (m.Success)
                boundaries.Add((i, m.Groups[1].Value));
        }

        return BuildChunksFromBoundaries(lines, boundaries, language);
    }

    private IReadOnlyList<CodeChunk> ChunkYaml(string content, string language)
    {
        var lines = content.Split('\n');
        var boundaries = new List<(int lineIndex, string? symbol)>();
        var topLevelKeyPattern = new Regex(@"^(\w[\w-]*)\s*:", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var m = topLevelKeyPattern.Match(lines[i]);
            if (m.Success)
                boundaries.Add((i, m.Groups[1].Value));
        }

        return BuildChunksFromBoundaries(lines, boundaries, language);
    }

    private IReadOnlyList<CodeChunk> ChunkMarkdown(string content, string language)
    {
        var lines = content.Split('\n');
        var boundaries = new List<(int lineIndex, string? symbol)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var m = MdHeadingPattern.Match(lines[i]);
            if (m.Success)
                boundaries.Add((i, m.Groups[2].Value.Trim()));
        }

        return BuildChunksFromBoundaries(lines, boundaries, language);
    }

    private IReadOnlyList<CodeChunk> BuildChunksFromBoundaries(
        string[] lines, List<(int lineIndex, string? symbol)> boundaries, string language)
    {
        if (boundaries.Count == 0)
            return ChunkBySize(string.Join('\n', lines), language, null);

        var chunks = new List<CodeChunk>();

        for (int b = 0; b < boundaries.Count; b++)
        {
            var startLine = boundaries[b].lineIndex;
            var endLine = b + 1 < boundaries.Count
                ? boundaries[b + 1].lineIndex - 1
                : lines.Length - 1;

            var symbol = boundaries[b].symbol;
            var chunkContent = string.Join('\n', lines[startLine..(endLine + 1)]);

            if (chunkContent.Length <= MaxChunkSize)
            {
                chunks.Add(new CodeChunk(chunkContent, startLine + 1, endLine + 1, language, symbol));
            }
            else
            {
                // Split oversized chunks with overlap
                var subChunks = SplitWithOverlap(chunkContent, startLine, language, symbol);
                chunks.AddRange(subChunks);
            }
        }

        // Handle content before the first boundary
        if (boundaries.Count > 0 && boundaries[0].lineIndex > 0)
        {
            var preamble = string.Join('\n', lines[0..boundaries[0].lineIndex]);
            if (!string.IsNullOrWhiteSpace(preamble))
            {
                if (preamble.Length <= MaxChunkSize)
                {
                    chunks.Insert(0, new CodeChunk(preamble, 1, boundaries[0].lineIndex, language, null));
                }
                else
                {
                    var subChunks = SplitWithOverlap(preamble, 0, language, null);
                    chunks.InsertRange(0, subChunks);
                }
            }
        }

        return chunks;
    }

    private static List<CodeChunk> SplitWithOverlap(
        string content, int baseLineOffset, string language, string? symbol)
    {
        var chunks = new List<CodeChunk>();
        var lines = content.Split('\n');
        var currentStart = 0;

        while (currentStart < lines.Length)
        {
            var currentContent = new List<string>();
            int charCount = 0;
            int lineIdx = currentStart;

            while (lineIdx < lines.Length && charCount + lines[lineIdx].Length + 1 <= MaxChunkSize)
            {
                currentContent.Add(lines[lineIdx]);
                charCount += lines[lineIdx].Length + 1;
                lineIdx++;
            }

            // If a single line exceeds max, take it anyway
            if (currentContent.Count == 0 && lineIdx < lines.Length)
            {
                currentContent.Add(lines[lineIdx]);
                lineIdx++;
            }

            var chunkText = string.Join('\n', currentContent);
            chunks.Add(new CodeChunk(
                chunkText,
                baseLineOffset + currentStart + 1,
                baseLineOffset + currentStart + currentContent.Count,
                language,
                symbol));

            // Advance with overlap
            var overlapLines = 0;
            var overlapChars = 0;
            for (int i = currentContent.Count - 1; i >= 0 && overlapChars < OverlapSize; i--)
            {
                overlapChars += currentContent[i].Length + 1;
                overlapLines++;
            }

            var advance = currentContent.Count - overlapLines;
            if (advance <= 0) advance = 1; // Always advance at least one line
            currentStart += advance;
        }

        return chunks;
    }

    private static IReadOnlyList<CodeChunk> ChunkBySize(string content, string language, string? symbol)
    {
        var lines = content.Split('\n');
        var result = SplitWithOverlap(content, 0, language, symbol);
        return result;
    }
}
