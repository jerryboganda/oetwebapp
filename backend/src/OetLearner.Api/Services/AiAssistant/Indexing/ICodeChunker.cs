namespace OetLearner.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Chunks source code files into semantically meaningful pieces for
/// embedding and retrieval.
/// </summary>
public interface ICodeChunker
{
    /// <summary>
    /// Split a source file into chunks, respecting code boundaries where possible.
    /// </summary>
    IReadOnlyList<CodeChunk> ChunkFile(string filePath, string content, string language);
}

/// <summary>
/// A single chunk of source code with metadata.
/// </summary>
public record CodeChunk(
    string Content,
    int StartLine,
    int EndLine,
    string Language,
    string? Symbol);
