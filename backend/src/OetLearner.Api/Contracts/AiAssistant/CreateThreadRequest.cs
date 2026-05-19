using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Contracts.AiAssistant;

public class CreateThreadRequest
{
    [MaxLength(256)]
    public string? Title { get; set; }

    public Guid? ProviderConfigId { get; set; }

    [MaxLength(128)]
    public string? Model { get; set; }
}
