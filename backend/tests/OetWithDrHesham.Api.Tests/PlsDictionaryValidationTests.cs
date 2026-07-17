using System.Text;
using Microsoft.AspNetCore.Http;
using OetWithDrHesham.Api.Endpoints;
using Xunit;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Regression coverage for the PLS pronunciation-dictionary validation. The
/// production "unexpected server error" on upload was an unhandled
/// InvalidOperationException ("Set XmlReaderSettings.Async to true ...") thrown
/// by XDocument.LoadAsync because the reader settings did not enable Async — it
/// crashed before the file ever reached ElevenLabs. These tests exercise the
/// real validator so that regression cannot return silently.
/// </summary>
public sealed class PlsDictionaryValidationTests
{
    private const string ValidNamespacedPls =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<lexicon version=\"1.0\" xmlns=\"http://www.w3.org/2005/01/pronunciation-lexicon\" alphabet=\"ipa\" xml:lang=\"en-GB\">" +
        "<lexeme><grapheme>dyspnoea</grapheme><phoneme>dɪspˈniːə</phoneme></lexeme>" +
        "</lexicon>";

    [Fact]
    public async Task ValidatePls_AcceptsValidNamespacedLexicon()
    {
        var result = await VoiceDesignAdminEndpoints.ValidatePlsAsync(MakeFile(ValidNamespacedPls), CancellationToken.None);
        Assert.Null(result); // null == valid; previously this threw InvalidOperationException -> 500
    }

    [Fact]
    public async Task ValidatePls_RejectsNonLexiconRoot()
    {
        var xml = "<?xml version=\"1.0\"?><notalexicon><a/></notalexicon>";
        var result = await VoiceDesignAdminEndpoints.ValidatePlsAsync(MakeFile(xml), CancellationToken.None);
        Assert.Equal("dictionary_file_invalid_pls_root", result);
    }

    [Fact]
    public async Task ValidatePls_RejectsMalformedXml()
    {
        var result = await VoiceDesignAdminEndpoints.ValidatePlsAsync(MakeFile("<lexicon><unclosed>"), CancellationToken.None);
        Assert.Equal("dictionary_file_invalid_xml", result);
    }

    private static IFormFile MakeFile(string content, string fileName = "dict.pls")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pls+xml",
        };
    }
}
