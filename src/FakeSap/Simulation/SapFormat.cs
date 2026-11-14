using System.Globalization;

namespace FakeSap.Simulation;

// SAP DATS / TIMS formats. The simulated systems run in UTC.
internal static class SapFormat
{
    public static string Date(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset value) =>
        value.UtcDateTime.ToString("HHmmss", CultureInfo.InvariantCulture);
}
