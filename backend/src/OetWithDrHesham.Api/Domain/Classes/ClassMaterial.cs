using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain.Classes;

/// <summary>
/// Visibility of a <see cref="ClassMaterial"/> relative to the session
/// lifecycle. Determines when learners can fetch the asset.
/// </summary>
public enum ClassMaterialVisibility
{
    PreClass = 0,
    DuringClass = 1,
    PostClass = 2,
}

/// <summary>
/// A document, slide deck, link, or other resource attached to a
/// <see cref="LiveClass"/> or a specific <see cref="LiveClassSession"/>.
/// </summary>
/// <remarks>
/// When <see cref="ClassSessionId"/> is null the material applies to every
/// session for the class. Visibility gates learner access at the endpoint
/// layer.
/// </remarks>
[Index(nameof(LiveClassId))]
[Index(nameof(ClassSessionId))]
[Index(nameof(LiveClassId), nameof(ClassSessionId))]
public class ClassMaterial
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LiveClassId { get; set; } = default!;

    [MaxLength(64)]
    public string? ClassSessionId { get; set; }

    [MaxLength(180)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string FileUrl { get; set; } = default!;

    [MaxLength(128)]
    public string? MimeType { get; set; }

    public ClassMaterialVisibility Visibility { get; set; } = ClassMaterialVisibility.PreClass;

    public DateTimeOffset CreatedAt { get; set; }
}
