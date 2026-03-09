namespace Textzy.Api.DTOs;

public class UpsertContactRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TagsCsv { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public Guid? GroupId { get; set; }
    public Guid? SegmentId { get; set; }
}
