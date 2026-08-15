namespace FrameViewAnalyzer.Infrastructure.Stores;

/// <summary>
/// One versioned store file participating in a coordinated two-store commit.
/// Write must atomically replace the file (temp + move) and clean up its own
/// temporary file when the replacement fails, so a failed commit never
/// leaves a partially written destination.
/// </summary>
public interface IStoreDestination
{
    string FilePath { get; }

    /// <summary>Format version this application writes.</summary>
    int ExpectedVersion { get; }

    /// <summary>
    /// Format version of the file on disk, or null when the file is absent
    /// or cannot be read as a versioned document (tolerated as empty).
    /// </summary>
    int? ReadVersion();

    /// <summary>Current file bytes, or null when the file does not exist.</summary>
    byte[]? ReadCurrentBytes();

    /// <summary>Atomically replaces the file with the given bytes.</summary>
    void Write(byte[] bytes);

    /// <summary>Deletes the file (rollback of a store that did not exist).</summary>
    void Delete();
}

/// <summary>
/// Controlled failure of a coordinated two-store commit. The destination
/// files are either untouched or restored to their previous state.
/// </summary>
public class CoordinatedStoreCommitException : Exception
{
    public CoordinatedStoreCommitException(string message)
        : base(message)
    {
    }

    public CoordinatedStoreCommitException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// High-severity variant: the coordinated commit failed AND the automatic
/// restoration of the first store also failed. The two stores may no longer
/// represent the same import outcome; manual inspection is required.
/// </summary>
public sealed class CoordinatedStoreRollbackException : CoordinatedStoreCommitException
{
    public CoordinatedStoreRollbackException(
        string message,
        Exception commitError,
        Exception rollbackError)
        : base(message, commitError) =>
        RollbackError = rollbackError;

    /// <summary>Error raised while attempting to restore the first store.</summary>
    public Exception RollbackError { get; }
}

/// <summary>
/// Coordinates a two-file commit (first store, then second) with staging and
/// rollback:
/// 1. both destinations are version-checked and the first store's current
///    state is read;
/// 2. the complete payloads for BOTH documents must already be serialized
///    before Commit is called, so nothing is replaced until both documents
///    exist in full;
/// 3. the first file is replaced, then the second;
/// 4. if the second replacement fails, the first file is restored
///    byte-for-byte (or deleted when it did not previously exist);
/// 5. if that restoration also fails, a CoordinatedStoreRollbackException
///    (high severity) is thrown instead of the plain commit error.
/// </summary>
public static class StoreCommitTransaction
{
    /// <summary>
    /// Commits the first document, then the second; a failure of the second
    /// rolls the first back. Never reports success for a partial commit.
    /// </summary>
    public static void Commit(
        IStoreDestination first,
        byte[] firstPayload,
        IStoreDestination second,
        byte[] secondPayload)
    {
        EnsureSupportedVersion(first);
        EnsureSupportedVersion(second);

        byte[]? firstOriginal;
        try
        {
            firstOriginal = first.ReadCurrentBytes();
        }
        catch (Exception readError)
        {
            throw new CoordinatedStoreCommitException(
                "The package import was aborted because the current state of "
                + $"'{first.FilePath}' could not be read. Neither store was modified.",
                readError);
        }

        try
        {
            first.Write(firstPayload);
        }
        catch (Exception firstError)
        {
            // Write is atomic: on failure the first file is still its
            // original self and its temporary file was cleaned up.
            throw new CoordinatedStoreCommitException(
                $"The package import failed while saving '{first.FilePath}'. "
                + "Neither store was modified.",
                firstError);
        }

        try
        {
            second.Write(secondPayload);
        }
        catch (Exception secondError)
        {
            Rollback(first, firstOriginal, secondError);
            throw new CoordinatedStoreCommitException(
                $"The package import failed while saving '{second.FilePath}'. "
                + "Both stores were restored to their previous state.",
                secondError);
        }
    }

    private static void Rollback(
        IStoreDestination first,
        byte[]? original,
        Exception commitError)
    {
        try
        {
            if (original is null)
            {
                first.Delete();
            }
            else
            {
                first.Write(original);
            }
        }
        catch (Exception rollbackError)
        {
            throw new CoordinatedStoreRollbackException(
                "CRITICAL PERSISTENCE ERROR: the package import failed while "
                + "saving the second store, and the automatic restoration of "
                + $"'{first.FilePath}' also failed. The stores may not "
                + "represent the same import outcome and require manual "
                + "inspection.",
                commitError,
                rollbackError);
        }
    }

    private static void EnsureSupportedVersion(IStoreDestination destination)
    {
        var version = destination.ReadVersion();
        if (version is not null && version != destination.ExpectedVersion)
        {
            throw new CoordinatedStoreCommitException(
                $"The store at '{destination.FilePath}' uses an unsupported "
                + $"format version {version}. The package import was aborted "
                + "and neither store was modified.");
        }
    }
}
