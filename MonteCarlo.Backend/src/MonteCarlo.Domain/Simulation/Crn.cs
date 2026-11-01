namespace MonteCarlo.Domain.Simulation;

/// <summary>
/// Common random numbers: every random draw is a pure function of (seed, iteration, stream, index).
/// Streams are keyed by task / person id, so a baseline run and a what-if run get the same "luck"
/// and the difference between them is caused by the scenario, not by noise.
/// </summary>
internal static class Crn
{
    private static ulong SplitMix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public static double Uniform(int seed, int iteration, ulong stream, int index)
    {
        var x = SplitMix((ulong)(uint)seed * 0xD1B54A32D192ED03UL);
        x = SplitMix(x ^ ((ulong)(uint)iteration * 0x9E3779B97F4A7C15UL));
        x = SplitMix(x ^ stream);
        x = SplitMix(x ^ ((ulong)(uint)index * 0xBF58476D1CE4E5B9UL));
        return ((x >> 11) + 0.5) * (1.0 / (1UL << 53));
    }

    /// <summary>Standard normal via Box-Muller.</summary>
    public static double Normal(int seed, int iteration, ulong stream, int index)
    {
        var u1 = Uniform(seed, iteration, stream, index * 2);
        var u2 = Uniform(seed, iteration, stream, index * 2 + 1);
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public static ulong Stream(Guid id)
    {
        Span<byte> b = stackalloc byte[16];
        id.TryWriteBytes(b);
        return SplitMix(BitConverter.ToUInt64(b[..8]) ^ SplitMix(BitConverter.ToUInt64(b[8..])));
    }

    /// <summary>Deterministic Guid from text (FNV-1a), used for tasks/people created by scenarios.</summary>
    public static Guid DeterministicGuid(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (var ch in text) { h ^= ch; h *= 1099511628211UL; }
        var a = SplitMix(h);
        var b = SplitMix(a);
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], a);
        BitConverter.TryWriteBytes(bytes[8..], b);
        return new Guid(bytes);
    }
}
