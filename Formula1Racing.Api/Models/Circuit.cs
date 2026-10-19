namespace Formula1Racing.Api.Models;

public class Circuit
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? ImageUrl { get; set; }
    public int? Rating { get; set; }
}