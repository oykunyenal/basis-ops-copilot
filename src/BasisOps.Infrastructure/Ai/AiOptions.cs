using System.ComponentModel.DataAnnotations;

namespace BasisOps.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>When false, no LLM is called and every report is rule-based.</summary>
    public bool Enabled { get; set; }

    /// <summary>Local Ollama server. Data never leaves the machine.</summary>
    [Required]
    public Uri Endpoint { get; set; } = new("http://localhost:11434");

    [Required]
    public string Model { get; set; } = "qwen3:8b";

    /// <summary>
    /// Upper limit for one LLM call. Generous on purpose: the first call of the day usually has to load
    /// the model into memory. When it is exceeded the caller falls back to the rule-based output.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Language of the daily summary written for people. Prompts themselves are in English.</summary>
    [Required]
    public string SummaryLanguage { get; set; } = "Turkish";

    /// <summary>The daily summary prompt lists at most this many findings (most severe first).</summary>
    [Range(1, 500)]
    public int MaxFindingsInSummary { get; set; } = 40;
}
