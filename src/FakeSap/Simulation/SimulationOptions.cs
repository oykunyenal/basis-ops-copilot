using System.ComponentModel.DataAnnotations;

namespace FakeSap.Simulation;

public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    // How often new dumps, locks and job runs are generated. 0 disables the generator.
    [Range(0, 3600)]
    public int EventIntervalSeconds { get; set; } = 30;
}
