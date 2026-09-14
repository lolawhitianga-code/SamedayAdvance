using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class LogSearchServiceTests
{
    private readonly LogSearchService _search = new();

    [Fact]
    public async Task FindsATermInsideAnExtractedLog()
    {
        using var env = new TestEnvironment();
        var files = TestEnvironment.SampleBundle();
        files["errorlog.txt"] = "all good\nE-4021 thermostat fault\nend of log";
        await env.Processor.ProcessAsync(env.CreateZip("a.zip", files));

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "E-4021");

        var hit = Assert.Single(result.Hits);
        Assert.Equal("errorlog.txt", hit.LogFileName);
        Assert.Equal(2, hit.LineNumber);
        Assert.Equal("E-4021 thermostat fault", hit.Line);
        Assert.Equal("SN-00123", hit.SerialNumber);
        Assert.Equal(1, result.BundlesMatched);
    }

    [Fact]
    public async Task MatchesCaseInsensitively()
    {
        using var env = new TestEnvironment();
        var files = TestEnvironment.SampleBundle();
        files["errorlog.txt"] = "Thermostat Fault";
        await env.Processor.ProcessAsync(env.CreateZip("a.zip", files));

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "thermostat fault");

        Assert.Single(result.Hits);
    }

    [Fact]
    public async Task SearchesAcrossEveryStoredBundle()
    {
        using var env = new TestEnvironment();

        var first = TestEnvironment.SampleBundle(serial: "SN-1");
        first["errorlog.txt"] = "E-4021 here";
        await env.Processor.ProcessAsync(env.CreateZip("a.zip", first));

        var second = TestEnvironment.SampleBundle(serial: "SN-2");
        second["errorlog.txt"] = "nothing to see";
        await env.Processor.ProcessAsync(env.CreateZip("b.zip", second));

        var third = TestEnvironment.SampleBundle(serial: "SN-3");
        third["machinelog.txt"] = "step failed with E-4021";
        await env.Processor.ProcessAsync(env.CreateZip("c.zip", third));

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "E-4021");

        Assert.Equal(3, result.BundlesSearched);
        Assert.Equal(2, result.BundlesMatched);
        Assert.Equal(new[] { "SN-1", "SN-3" }, result.Hits.Select(h => h.SerialNumber).OrderBy(s => s));
    }

    [Fact]
    public async Task ReturnsNothingForABlankTerm()
    {
        using var env = new TestEnvironment();
        await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "   ");

        Assert.Empty(result.Hits);
    }

    [Fact]
    public async Task CapsHitsPerBundleSoOneNoisyLogCannotFloodResults()
    {
        using var env = new TestEnvironment();
        var files = TestEnvironment.SampleBundle();
        files["errorlog.txt"] = string.Join("\n", Enumerable.Repeat("E-4021 repeated fault", 50));
        await env.Processor.ProcessAsync(env.CreateZip("a.zip", files));

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "E-4021", hitsPerBundle: 3);

        Assert.Equal(3, result.Hits.Count);
    }

    [Fact]
    public async Task StopsAtTheOverallLimitAndSaysSo()
    {
        using var env = new TestEnvironment();
        for (var i = 0; i < 5; i++)
        {
            var files = TestEnvironment.SampleBundle(serial: $"SN-{i}");
            files["errorlog.txt"] = "E-4021\nE-4021\nE-4021";
            await env.Processor.ProcessAsync(env.CreateZip($"b{i}.zip", files));
        }

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "E-4021", totalHitLimit: 4);

        Assert.True(result.TruncatedAtLimit);
        Assert.Equal(4, result.Hits.Count);
    }

    [Fact]
    public async Task ReportsLogsThatAreNoLongerOnDisk()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));

        // Simulate the extracted folder being cleaned up while the database row remains.
        Directory.Delete(file.ExtractedPath!, recursive: true);

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "anything");

        Assert.Empty(result.Hits);
        Assert.True(result.MissingFiles > 0);
    }

    [Fact]
    public async Task SkipsFilesThatAreNotTextLike()
    {
        using var env = new TestEnvironment();
        var files = TestEnvironment.SampleBundle();
        files["capture.bin"] = "E-4021 inside a binary-looking file";
        await env.Processor.ProcessAsync(env.CreateZip("a.zip", files));

        var result = await _search.SearchAsync(await env.Repository.GetAllAsync(), "E-4021");

        Assert.Empty(result.Hits);
    }
}
