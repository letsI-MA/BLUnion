namespace BLUnion.Models;

public enum AvailabilityTag
{
    Morning,
    Afternoon,
    Evening,
    Weekend,
    Flexible,
}

public static class AvailabilityTagExtensions
{
    public static string ToWireValue(this AvailabilityTag tag) => tag switch
    {
        AvailabilityTag.Morning => "morning",
        AvailabilityTag.Afternoon => "afternoon",
        AvailabilityTag.Evening => "evening",
        AvailabilityTag.Weekend => "weekend",
        AvailabilityTag.Flexible => "flexible",
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null),
    };

    public static AvailabilityTag? FromWireValue(string wireValue) => wireValue switch
    {
        "morning" => AvailabilityTag.Morning,
        "afternoon" => AvailabilityTag.Afternoon,
        "evening" => AvailabilityTag.Evening,
        "weekend" => AvailabilityTag.Weekend,
        "flexible" => AvailabilityTag.Flexible,
        _ => null,
    };
}
