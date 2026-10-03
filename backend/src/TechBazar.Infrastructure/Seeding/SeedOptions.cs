namespace TechBazar.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Run the seeder at API start-up (Development only by default).</summary>
    public bool Enabled { get; set; }
    /// <summary>Apply pending EF migrations at start-up before seeding.</summary>
    public bool ApplyMigrations { get; set; }
    public string AdminEmail { get; set; } = "admin@techbazar.bd";
    public string AdminFullName { get; set; } = "TechBazar Admin";
    /// <summary>Supply via user-secrets (Seed:AdminPassword). When empty no admin account is created.</summary>
    public string? AdminPassword { get; set; }
}
