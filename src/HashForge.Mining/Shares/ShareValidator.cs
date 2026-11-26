using System.Globalization;
using System.Numerics;
using HashForge.Bitcoin.Primitives;
using HashForge.Domain;
using HashForge.Mining.Jobs;

namespace HashForge.Mining.Shares;

/// <summary>Rebuilds the block header a miner hashed and checks it against the required difficulty.</summary>
public sealed class ShareValidator : IShareValidator
{
    private const int UInt32HexLength = 8;

    public ShareValidation Validate(ShareSubmission submission, MiningJob job)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(job);

        if (!TryReadFields(submission, job, out var fields))
        {
            return ShareValidation.Rejected(ShareOutcome.Malformed);
        }

        var merkleRoot = MerkleBranch.ComputeRoot(job.Coinbase.ComputeTransactionId(fields.Extranonce), job.MerkleBranch);
        var header = BlockSerializer.SerializeHeader(
            new BlockHeader(fields.Version, job.PreviousBlockHash, merkleRoot, fields.Time, job.Bits, fields.Nonce));
        var hash = Sha256d.Hash(header);
        var hashValue = DifficultyMath.HashToInteger(hash);
        var actualDifficulty = DifficultyMath.DifficultyFromTarget(hashValue);

        // A hash that solves the block is never turned away, whatever share difficulty the miner was set.
        var isBlock = hashValue <= job.NetworkTarget;
        var credited = CreditedDifficulty(submission, hashValue) ?? (isBlock ? submission.Difficulty : null);
        if (credited is null)
        {
            return ShareValidation.Rejected(ShareOutcome.LowDifficulty, actualDifficulty);
        }

        // Duplicates are recognised by the header hash, so the same work cannot be credited twice by
        // spelling a field differently. Only valid work is remembered, so junk cannot fill the memory.
        if (!job.TryRegisterSubmission(hash))
        {
            return ShareValidation.Rejected(ShareOutcome.Duplicate, actualDifficulty);
        }

        return new ShareValidation(
            ShareOutcome.Accepted,
            credited.Value,
            actualDifficulty,
            isBlock ? new BlockCandidate(HashHex.ToDisplayHex(hash), SerializeBlock(job, header, fields.Extranonce)) : null);
    }

    private static string SerializeBlock(MiningJob job, byte[] header, byte[] extranonce) =>
        Convert.ToHexStringLower(BlockSerializer.SerializeBlock(
            header,
            job.Coinbase.AssembleForBlock(extranonce),
            [.. job.Template.Transactions.Select(t => Convert.FromHexString(t.Data))]));

    /// <summary>Returns the difficulty the share earns, or null when it meets neither the current nor the previous one.</summary>
    private static double? CreditedDifficulty(ShareSubmission submission, BigInteger hashValue)
    {
        if (hashValue <= DifficultyMath.TargetFromDifficulty(submission.Difficulty))
        {
            return submission.Difficulty;
        }

        return submission.PreviousDifficulty is { } previous && hashValue <= DifficultyMath.TargetFromDifficulty(previous)
            ? previous
            : null;
    }

    private static bool TryReadFields(ShareSubmission submission, MiningJob job, out ShareFields fields)
    {
        fields = default;
        var extranonce2Size = job.Coinbase.ExtranonceSize - submission.Extranonce1.Length;
        if (!TryReadHexBytes(submission.Extranonce2Hex, extranonce2Size, out var extranonce2)
            || !TryReadUInt32(submission.TimeHex, out var time)
            || !TryReadUInt32(submission.NonceHex, out var nonce)
            || !TryReadVersion(submission, job.Template.Version, out var version))
        {
            return false;
        }

        if (time < job.Template.MinimumTime || time > job.Template.CurrentTime + ShareTimeLimits.MaxFutureSeconds)
        {
            return false;
        }

        fields = new ShareFields([.. submission.Extranonce1, .. extranonce2], time, nonce, version);
        return true;
    }

    private static bool TryReadVersion(ShareSubmission submission, uint templateVersion, out uint version)
    {
        version = templateVersion;
        if (submission.VersionBitsHex is null)
        {
            return true;
        }

        // Miners may only change the bits that were negotiated for version rolling.
        if (!TryReadUInt32(submission.VersionBitsHex, out var bits) || (bits & ~submission.VersionMask) != 0)
        {
            return false;
        }

        version = (templateVersion & ~submission.VersionMask) | bits;
        return true;
    }

    private static bool TryReadUInt32(string? hex, out uint value)
    {
        value = 0;
        return hex is { Length: UInt32HexLength }
            && uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadHexBytes(string? hex, int expectedLength, out byte[] bytes)
    {
        bytes = [];
        if (hex is null || expectedLength <= 0 || hex.Length != expectedLength * 2)
        {
            return false;
        }

        bytes = new byte[expectedLength];
        return Convert.FromHexString(hex, bytes, out _, out _) == System.Buffers.OperationStatus.Done;
    }

    private readonly record struct ShareFields(byte[] Extranonce, uint Time, uint Nonce, uint Version);
}
