using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class ContentSchemaValidatorTests
{
    // ── ValidateDetailJson — common ─────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void ValidateDetailJson_empty_input_is_invalid(string input)
    {
        var r = ContentSchemaValidator.ValidateDetailJson("writing", input);
        Assert.False(r.IsValid);
        Assert.Contains("DetailJson is empty.", r.Errors);
    }

    [Fact]
    public void ValidateDetailJson_invalid_json_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("writing", "{not json");
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.StartsWith("DetailJson is not valid JSON"));
    }

    [Fact]
    public void ValidateDetailJson_array_root_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("writing", "[1,2,3]");
        Assert.False(r.IsValid);
        Assert.Contains("DetailJson must be a JSON object.", r.Errors);
    }

    [Fact]
    public void ValidateDetailJson_unknown_subtest_passes_when_object_root()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("unknown", "{\"any\":1}");
        Assert.True(r.IsValid);
    }

    // ── Writing ─────────────────────────────────────────────────────────────

    private const string WritingValid = """
    {
      "caseNotes": {
        "patientName": "Jane Doe",
        "diagnosis": "Asthma",
        "history": "10y",
        "treatment": "Inhaler"
      },
      "taskInstructions": "Write referral",
      "writingType": "referral",
      "wordLimit": 200,
      "timeLimit": 45
    }
    """;

    [Fact]
    public void ValidateDetailJson_writing_full_payload_is_valid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("writing", WritingValid);
        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void ValidateDetailJson_writing_missing_caseNotes_object_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson(
            "writing",
            "{\"taskInstructions\":\"x\",\"writingType\":\"y\",\"wordLimit\":1,\"timeLimit\":1}");
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("caseNotes"));
    }

    [Fact]
    public void ValidateDetailJson_writing_caseNotes_field_blank_string_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("writing", """
        {
          "caseNotes": { "patientName": "   ", "diagnosis": "x", "history": "x", "treatment": "x" },
          "taskInstructions": "x", "writingType": "x", "wordLimit": 1, "timeLimit": 1
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("caseNotes.patientName"));
    }

    [Fact]
    public void ValidateDetailJson_writing_non_integer_word_limit_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("writing", """
        {
          "caseNotes": { "patientName": "p", "diagnosis": "d", "history": "h", "treatment": "t" },
          "taskInstructions": "x", "writingType": "x", "wordLimit": "not-a-number", "timeLimit": 1
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("wordLimit"));
    }

    [Fact]
    public void ValidateDetailJson_writing_subtest_is_case_insensitive()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("WRITING", WritingValid);
        Assert.True(r.IsValid);
    }

    // ── Speaking ────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateDetailJson_speaking_full_payload_is_valid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("speaking", """
        {
          "roleCard": {
            "setting": "clinic",
            "candidateRole": "doctor",
            "patientRole": "patient",
            "taskObjectives": ["a","b"]
          }
        }
        """);
        Assert.True(r.IsValid);
    }

    [Fact]
    public void ValidateDetailJson_speaking_missing_roleCard_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("speaking", "{\"x\":1}");
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("roleCard"));
    }

    [Fact]
    public void ValidateDetailJson_speaking_missing_taskObjectives_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("speaking", """
        {
          "roleCard": {
            "setting": "clinic",
            "candidateRole": "doctor",
            "patientRole": "patient"
          }
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("taskObjectives"));
    }

    // ── Reading ─────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateDetailJson_reading_full_payload_is_valid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("reading", """
        {
          "part": "A",
          "passages": [{ "id": "p1", "text": "lorem" }],
          "questions": [{ "id": "q1", "questionText": "?", "questionType": "mcq" }]
        }
        """);
        Assert.True(r.IsValid);
    }

    [Fact]
    public void ValidateDetailJson_reading_missing_part_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("reading", """
        {
          "passages": [{ "id": "p1", "text": "lorem" }],
          "questions": [{ "id": "q1", "questionText": "?", "questionType": "mcq" }]
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("'part'"));
    }

    [Fact]
    public void ValidateDetailJson_reading_empty_passages_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("reading", """
        {
          "part": "A",
          "passages": [],
          "questions": [{ "id": "q1", "questionText": "?", "questionType": "mcq" }]
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("passages"));
    }

    [Fact]
    public void ValidateDetailJson_reading_question_missing_id_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("reading", """
        {
          "part": "A",
          "passages": [{ "id": "p1", "text": "lorem" }],
          "questions": [{ "questionText": "?", "questionType": "mcq" }]
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("questions[].id"));
    }

    // ── Listening ───────────────────────────────────────────────────────────

    [Fact]
    public void ValidateDetailJson_listening_full_payload_is_valid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("listening", """
        {
          "part": "A",
          "audioSegments": [{ "id": "s1" }],
          "questions": [{ "id": "q1", "questionText": "?", "questionType": "fill" }]
        }
        """);
        Assert.True(r.IsValid);
    }

    [Fact]
    public void ValidateDetailJson_listening_empty_audioSegments_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("listening", """
        {
          "part": "A",
          "audioSegments": [],
          "questions": [{ "id": "q1", "questionText": "?", "questionType": "fill" }]
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("audioSegments"));
    }

    [Fact]
    public void ValidateDetailJson_listening_missing_questions_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateDetailJson("listening", """
        {
          "part": "A",
          "audioSegments": [{ "id": "s1" }]
        }
        """);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("questions"));
    }

    // ── ValidateModelAnswerJson ─────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void ValidateModelAnswerJson_empty_is_valid_because_optional(string input)
    {
        var r = ContentSchemaValidator.ValidateModelAnswerJson("writing", input);
        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void ValidateModelAnswerJson_invalid_json_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateModelAnswerJson("writing", "{nope");
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.StartsWith("ModelAnswerJson is not valid JSON"));
    }

    [Fact]
    public void ValidateModelAnswerJson_array_root_is_invalid()
    {
        var r = ContentSchemaValidator.ValidateModelAnswerJson("writing", "[1,2,3]");
        Assert.False(r.IsValid);
        Assert.Contains("ModelAnswerJson must be a JSON object.", r.Errors);
    }

    [Fact]
    public void ValidateModelAnswerJson_object_root_is_valid()
    {
        var r = ContentSchemaValidator.ValidateModelAnswerJson("writing", "{\"answer\":\"text\"}");
        Assert.True(r.IsValid);
    }
}
