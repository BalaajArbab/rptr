namespace rptr.Models;

public class Feedback
{
    public int Id { get; set; }
    public int UserId { get; init; }
    public required string Message { get; init; }
    public string? IpAddress { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public User User { get; init; } = null!;
}
