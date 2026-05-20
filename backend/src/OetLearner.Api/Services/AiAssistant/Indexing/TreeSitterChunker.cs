using System.Collections.Generic;

namespace OetLearner.Api.Services.AiAssistant.Indexing;

public sealed class ChunkSpan
{
    public string Language { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string Content { get; init; } = string.Empty;
}

// Splits source files into semantically meaningful spans (function /
// class / block) using tree-sitter parsers. Falls back to a sliding
// window for unsupported languages.
public sealed class TreeSitterChunker
{
    public TreeSitterChunker()
    {
        // TODO Phase 2: lazy-load tree-sitter grammars for TS/TSX/C#/JSON/MD.
    }

    public IReadOnlyList<ChunkSpan> Chunk(string repoRelativePath, string content)
    {
        // TODO Phase 2: pick grammar by extension; fall back to 200-line windows.
        return System.Array.Empty<ChunkSpan>();
    }
}
