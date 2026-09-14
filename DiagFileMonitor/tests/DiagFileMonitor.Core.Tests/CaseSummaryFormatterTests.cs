using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class CaseSummaryFormatterTests
{
    private static DiagnosticFileSummary Sample() => new()
    {
        OriginalFileName = "diag_2026.zip",
        SerialNumber = "SN-00123",
        MachineType = "CoffeeRoaster-5000",
        Customer = "Wellington Roasters Ltd",
        Version = "2.4.1",
        Status = "Processed",
        ArrivedAtUtc = new DateTime(2026, 9, 10, 2, 30, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void IncludesTheMachineDetails()
    {
        var text = CaseSummaryFormatter.Format(Sample());

        Assert.Contains("diag_2026.zip", text);
        Assert.Contains("SN-00123", text);
        Assert.Contains("CoffeeRoaster-5000", text);
        Assert.Contains("Wellington Roasters Ltd", text);
        Assert.Contains("2.4.1", text);
        Assert.Contains("Processed", text);
    }

    [Fact]
    public void OmitsOptionalSectionsWhenEmpty()
    {
        var text = CaseSummaryFormatter.Format(Sample());

        Assert.DoesNotContain("Ticket:", text);
        Assert.DoesNotContain("Notes:", text);
        Assert.DoesNotContain("Problem:", text);
    }

    [Fact]
    public void IncludesTicketNotesAndProblemWhenPresent()
    {
        var file = Sample();
        file.TicketNumber = "TICK-42";
        file.Notes = "Customer reports slow preheat.";
        file.ErrorMessage = "machine.xml was missing";
        file.ExtractedPath = @"C:\diag\extract\x";

        var text = CaseSummaryFormatter.Format(file);

        Assert.Contains("TICK-42", text);
        Assert.Contains("Customer reports slow preheat.", text);
        Assert.Contains("machine.xml was missing", text);
        Assert.Contains(@"C:\diag\extract\x", text);
    }
}
