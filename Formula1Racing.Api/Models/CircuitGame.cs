namespace Formula1Racing.Api.Models;

public class CircuitGame
{
    public required string CircuitId { get; set; }
    public required string Game { get; set; }
    public DateOnly? Date { get; set; }
    public string? GrandPrix { get; set; }
    public Circuit Circuit { get; set; } = null!;
}