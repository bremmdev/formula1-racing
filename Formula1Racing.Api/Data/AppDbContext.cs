using Formula1Racing.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Formula1Racing.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Circuit> Circuits => Set<Circuit>();
    public DbSet<CircuitGame> CircuitGames => Set<CircuitGame>();
    public DbSet<Laptime> Laptimes => Set<Laptime>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Rating is a whole number from 1 to 5; typeof rejects values like 3.5
        modelBuilder.Entity<Circuit>()
            .ToTable(t => t.HasCheckConstraint("ck_circuits_rating",
                "rating IS NULL OR (typeof(rating) = 'integer' AND rating BETWEEN 1 AND 5)"));

        // Composite key for circuit_games
        modelBuilder.Entity<CircuitGame>()
            .HasKey(cg => new { cg.CircuitId, cg.Game });

        // Two races in one game never share a date
        modelBuilder.Entity<CircuitGame>()
            .HasIndex(cg => new { cg.Game, cg.Date })
            .IsUnique();

        // A circuit is on a game's calendar with both a date and a Grand Prix, or off it with neither
        modelBuilder.Entity<CircuitGame>()
            .ToTable(t => t.HasCheckConstraint("ck_circuit_games_date_grand_prix",
                "(date IS NULL) = (grand_prix IS NULL)"));

        // Laptime has a FK that points to the circuit_games composite key
        modelBuilder.Entity<Laptime>()
            .HasOne(l => l.CircuitGame)
            .WithMany()
            .HasForeignKey(l => new { l.CircuitId, l.Game })
            .OnDelete(DeleteBehavior.NoAction);

        // Plain INTEGER PRIMARY KEY instead of AUTOINCREMENT
        modelBuilder.Entity<Laptime>()
            .Property(l => l.Id)
            .Metadata.SetValueGenerationStrategy(SqliteValueGenerationStrategy.None);

        // Weather is stored as 'dry' or 'wet'
        modelBuilder.Entity<Laptime>()
            .Property(l => l.Weather)
            .HasConversion(w => w.ToString().ToLowerInvariant(), s => Enum.Parse<Weather>(s, true));

        modelBuilder.Entity<Laptime>()
            .ToTable(t => t.HasCheckConstraint("ck_laptimes_weather", "weather IN ('dry', 'wet')"));

        // Finds the fastest lap per circuit, game and weather without scanning every lap
        modelBuilder.Entity<Laptime>()
            .HasIndex(l => new { l.CircuitId, l.Game, l.Weather, l.Time })
            .HasDatabaseName("idx_laptimes_record");
    }
}
