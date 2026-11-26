using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PptRemoteServer;

internal enum AuthResult
{
    Ok,
    BadLength,
    BadTag,
    Replay,
    LockedOut
}

/// <summary>
/// PIN-based packet signing. Packet = [command][counter u32 big-endian][8-byte tag] (13 bytes),
/// tag = first 8 bytes of HMAC-SHA256(key, command + counter), key = SHA-256("PPTRemote-v1:" + PIN).
/// See docs/protocol.md.
/// </summary>
internal static class Auth
{
    public const int PacketLength = 13;
    public const int TagLength = 8;
    private const string Salt = "PPTRemote-v1:";

    public static byte[] DeriveKey(string pin) => SHA256.HashData(Encoding.UTF8.GetBytes(Salt + pin));

    public static byte[] Sign(byte[] key, byte command, uint counter)
    {
        var packet = new byte[PacketLength];
        packet[0] = command;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(1, 4), counter);
        var mac = HMACSHA256.HashData(key, packet.AsSpan(0, 5));
        Array.Copy(mac, 0, packet, 5, TagLength);
        return packet;
    }

    public static AuthResult Verify(
        byte[] key, ReadOnlySpan<byte> packet, uint lastCounter, out byte command, out uint counter)
    {
        command = 0;
        counter = 0;
        if (packet.Length != PacketLength)
        {
            return AuthResult.BadLength;
        }

        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(key, packet[..5], mac);
        if (!CryptographicOperations.FixedTimeEquals(mac[..TagLength], packet.Slice(5, TagLength)))
        {
            return AuthResult.BadTag;
        }

        command = packet[0];
        counter = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(1, 4));
        return counter > lastCounter ? AuthResult.Ok : AuthResult.Replay;
    }

    /// <summary>Checks the code against a packet computed independently (PIN 123456, command 1, counter 1767225600).</summary>
    public static bool SelfTest()
    {
        const string expected = "016955B900F4450A08DC1A3B76";
        var key = DeriveKey("123456");
        var packet = Sign(key, 0x01, 1767225600);
        return string.Equals(Convert.ToHexString(packet), expected, StringComparison.OrdinalIgnoreCase)
            && Verify(key, packet, 0, out var command, out var counter) == AuthResult.Ok
            && command == 0x01
            && counter == 1767225600;
    }
}

/// <summary>Holds the current PIN key and last accepted counter, and locks out PIN guessing.</summary>
internal sealed class AuthState
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LockoutTime = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly Queue<DateTime> _failures = new();
    private byte[] _key = Array.Empty<byte>();
    private uint _lastCounter;
    private bool _dirty;
    private DateTime _lockedUntil = DateTime.MinValue;

    public void SetPin(string pin, uint lastCounter)
    {
        lock (_lock)
        {
            _key = Auth.DeriveKey(pin);
            _lastCounter = lastCounter;
            _dirty = false;
            _failures.Clear();
            _lockedUntil = DateTime.MinValue;
        }
    }

    public AuthResult Check(byte[] packet, out byte command)
    {
        lock (_lock)
        {
            command = 0;
            var now = DateTime.UtcNow;
            if (now < _lockedUntil)
            {
                return AuthResult.LockedOut;
            }

            var result = Auth.Verify(_key, packet, _lastCounter, out command, out var counter);
            if (result == AuthResult.Ok)
            {
                _lastCounter = counter;
                _dirty = true;
                _failures.Clear();
                return result;
            }

            // Wrong tags count towards a lockout; benign replays (duplicates) don't.
            if (result is AuthResult.BadTag or AuthResult.BadLength)
            {
                _failures.Enqueue(now);
                while (_failures.Count > 0 && now - _failures.Peek() > FailureWindow)
                {
                    _failures.Dequeue();
                }
                if (_failures.Count >= MaxFailures)
                {
                    _lockedUntil = now + LockoutTime;
                    _failures.Clear();
                }
            }
            return result;
        }
    }

    /// <summary>True (once) if the counter moved since the last call, so it can be saved.</summary>
    public bool TryTakeDirty(out uint counter)
    {
        lock (_lock)
        {
            counter = _lastCounter;
            var wasDirty = _dirty;
            _dirty = false;
            return wasDirty;
        }
    }
}
