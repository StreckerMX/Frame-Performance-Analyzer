namespace FrameViewAnalyzer.Core.Tests;

public class CaptureIdentityTests
{
    [Fact]
    public void Identity_joins_name_size_and_mtime_nanoseconds()
    {
        var identity = CaptureIdentity.Build("FrameView_Test_Log.csv", 123456, 1723000000000000000);

        Assert.Equal("FrameView_Test_Log.csv|123456|1723000000000000000", identity);
    }

    [Fact]
    public void Identities_differ_when_any_attribute_changes()
    {
        var baseIdentity = CaptureIdentity.Build("a.csv", 100, 200);

        Assert.NotEqual(baseIdentity, CaptureIdentity.Build("b.csv", 100, 200));
        Assert.NotEqual(baseIdentity, CaptureIdentity.Build("a.csv", 101, 200));
        Assert.NotEqual(baseIdentity, CaptureIdentity.Build("a.csv", 100, 201));
    }
}
