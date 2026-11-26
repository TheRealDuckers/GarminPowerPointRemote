namespace PptRemoteServer;

// Must match garmin/source/Protocol.mc
internal static class Protocol
{
    public static readonly Guid ServiceUuid = new("7d9b0001-5a4c-4f6e-9a3b-2c8f1e0d4b11");
    public static readonly Guid CommandCharacteristicUuid = new("7d9b0002-5a4c-4f6e-9a3b-2c8f1e0d4b11");

    public const byte Hello = 0x00;
    public const byte Next = 0x01;
    public const byte Prev = 0x02;
    public const byte Start = 0x03;
    public const byte End = 0x04;
    public const byte Black = 0x05;

    public static string Describe(byte command) => command switch
    {
        Hello => "Hello",
        Next => "Next slide",
        Prev => "Previous slide",
        Start => "Start slideshow",
        End => "End slideshow",
        Black => "Black screen",
        _ => $"Unknown command 0x{command:X2}"
    };
}
