namespace Formula1Racing.Api.Models;

public class Laptime
{
    public int Id { get; set; }
    public required int Time { get; set; }
    public required int Sector1 { get; set; }
    public required int Sector2 { get; set; }
    public required int Sector3 { get; set; }
    public required DateOnly Date { get; set; } // EF Core's SQLite provider stores it as text in yyyy-MM-dd format.
    public required Weather Weather { get; set; }
    public required string Game { get; set; }
    public required string CircuitId { get; set; }
    public Circuit Circuit { get; set; } = null!;
    public CircuitGame CircuitGame { get; set; } = null!;
}

public enum Weather { Dry, Wet }

