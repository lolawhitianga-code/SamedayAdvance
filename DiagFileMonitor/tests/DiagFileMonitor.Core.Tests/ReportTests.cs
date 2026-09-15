using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Reports;

namespace DiagFileMonitor.Core.Tests;

public class ReportRenderingTests
{
    private static ReportModel Sample(params ReportBlock[] blocks)
    {
        var report = new ReportModel
        {
            Title = "Fault Benchmarking - PlatePresentSwitch",
            Subtitle = "RakingWallExtruderV3DG fleet",
            Period = new ReportPeriod(new DateTime(2026, 8, 15), new DateTime(2026, 9, 14), "Last 30 days"),
            Footer = "Spida Machinery - internal diagnostics."
        };

        var section = new ReportSection { Title = "Fault log" };
        section.Blocks.AddRange(blocks);
        report.Sections.Add(section);
        return report;
    }

    [Fact]
    public void TheReportIsSelfContained()
    {
        // The whole point of a single file is that it opens on a factory PC with no internet.
        // A stylesheet, font or chart library pulled over https renders as nothing there.
        var html = new ReportHtmlRenderer().Render(Sample(new TextBlock { Text = "Something happened." }));

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("fetch(", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void WebFontsAreOptInOnly()
    {
        var withFonts = new ReportHtmlRenderer(new ReportHtmlOptions { UseWebFonts = true })
            .Render(Sample(new TextBlock { Text = "x" }));

        Assert.Contains("fonts.googleapis.com", withFonts);
    }

    [Fact]
    public void ThePeriodIsInTheHeaderSoATotalCannotReadAsAllTime()
    {
        var html = new ReportHtmlRenderer().Render(Sample(new TextBlock { Text = "x" }));

        Assert.Contains("Last 30 days", html);
        Assert.Contains("15 Aug - 14 Sep 2026", html);
        Assert.Contains("30 days", html);
    }

    [Fact]
    public void InternalReportsAreMarked()
    {
        Assert.Contains("INTERNAL USE ONLY", new ReportHtmlRenderer().Render(Sample()));

        var customerFacing = new ReportModel { Title = "x", InternalUseOnly = false };
        Assert.DoesNotContain("INTERNAL USE ONLY", new ReportHtmlRenderer().Render(customerFacing));
    }

    [Fact]
    public void ContentIsEscaped()
    {
        var html = new ReportHtmlRenderer().Render(Sample(new TextBlock
        {
            Text = "Operator wrote <script>alert(1)</script> & meant it"
        }));

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
    }

    [Fact]
    public void AnEmptyTableSaysSoRatherThanRenderingNothing()
    {
        var html = new ReportHtmlRenderer().Render(Sample(new TableBlock
        {
            Columns = { new ReportColumn("Timestamp") },
            EmptyText = "No occurrences in the period."
        }));

        Assert.Contains("No occurrences in the period.", html);
        Assert.DoesNotContain("<table>", html);
    }

    [Fact]
    public void ChartsAreDrawnInlineWithEveryBarLabelled()
    {
        var chart = new BarChartBlock
        {
            Bars =
            {
                new BarChartBar("M21036", "Waihi Mitre 10", 14, BarTone.Bad),
                new BarChartBar("M21844", "PlaceMakers", 5, BarTone.Warning),
                new BarChartBar("DGM20771", "TrussTech", 0, BarTone.Absent)
            }
        };

        var html = new ReportHtmlRenderer().Render(Sample(chart));

        Assert.Contains("<svg", html);
        // Identity never rests on colour alone: each bar carries its own value and name.
        foreach (var bar in chart.Bars)
        {
            Assert.Contains(bar.Label, html);
            Assert.Contains(bar.SubLabel, html);
        }
        // A machine with nothing recorded still appears, rather than being dropped.
        Assert.Contains("DGM20771: 0", html);
    }

    [Fact]
    public void ABarPastTheAxisTopKeepsItsRealValue()
    {
        // Scaling to the 97th percentile means a burst can overflow the axis. It is drawn full
        // height, but the number printed on it is the true one.
        var chart = new BarChartBlock
        {
            Bars = Enumerable.Range(0, 9)
                .Select(i => new BarChartBar($"M{i}", "site", i == 8 ? 500 : 2))
                .ToList()
        };

        var html = new ReportHtmlRenderer().Render(Sample(chart));

        Assert.Contains(">500<", html);
    }
}

public class FaultBenchmarkReportTests
{
    private static FaultOccurrence Glitch(string serial, string site, DateTime when, double recoverySeconds) => new()
    {
        Kind = FaultKind.PlateSensorGlitch,
        WhenUtc = when,
        SerialNumber = serial,
        MachineType = "RakingWallExtruderV3DG",
        Site = site,
        Signal = "PlatePresentSwitch 192.168.250.1-4.2 (fixed)",
        Detail = "Only the fixed side dropped, with no plate clamp output change nearby.",
        RecoveredAfter = TimeSpan.FromSeconds(recoverySeconds),
        Step = 1298,
        Confidence = Confidence.Confirmed
    };

    private static FleetScanResult Scan(params FaultOccurrence[] occurrences)
    {
        var machines = occurrences
            .Select(o => o.SerialNumber)
            .Distinct()
            .Select(s => new ScannedMachine
            {
                SerialNumber = s,
                MachineType = "RakingWallExtruderV3DG",
                Site = occurrences.First(o => o.SerialNumber == s).Site,
                BundlesRead = 1
            })
            .ToList();

        return new FleetScanResult
        {
            Occurrences = occurrences.OrderByDescending(o => o.WhenUtc).ToList(),
            Machines = machines
        };
    }

    private static ReportPeriod Period => new(new DateTime(2026, 8, 15), new DateTime(2026, 9, 14), "Last 30 days");

    [Fact]
    public void EveryOccurrenceIsListed()
    {
        // The house rule is the complete table first, pattern analysis after - never a count
        // standing in for the list.
        var scan = Scan(
            Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 9, 12, 14, 47, 3), 1.4),
            Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 9, 10, 9, 15, 41), 1.2),
            Glitch("M21844", "PlaceMakers Auckland", new DateTime(2026, 9, 11, 13, 22, 9), 1.6));

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(scan, Period, "PlatePresentSwitch"));

        // The timestamp is split over two lines in the cell, date then time.
        Assert.Contains("2026-09-12", html);
        Assert.Contains("14:47:03", html);
        Assert.Contains("2026-09-11", html);
        Assert.Contains("13:22:09", html);
        Assert.Contains("2026-09-10", html);
        Assert.Contains("09:15:41", html);
    }

    [Fact]
    public void OccurrencesAreNewestFirst()
    {
        var scan = Scan(
            Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 8, 20), 1.4),
            Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 9, 12), 1.2));

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(scan, Period, "x"));

        Assert.True(html.IndexOf("2026-09-12", StringComparison.Ordinal)
                    < html.IndexOf("2026-08-20", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRowCarriesItsConfidence()
    {
        var scan = Scan(Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 9, 12), 1.4) with
        {
            Confidence = Confidence.Unconfirmed
        });

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(scan, Period, "x"));

        Assert.Contains("Unconfirmed", html);
    }

    [Fact]
    public void AMachineWithNothingRecordedStillAppears()
    {
        // A comparison that quietly drops a machine is not a comparison. Zero is a finding.
        var scan = Scan(Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 9, 12), 1.4));

        var withClearMachine = new FleetScanResult
        {
            Occurrences = scan.Occurrences,
            Machines = scan.Machines
                .Append(new ScannedMachine
                {
                    SerialNumber = "DGM20771",
                    MachineType = "RakingWallExtruderV3DG",
                    Site = "TrussTech",
                    BundlesRead = 3
                })
                .ToList()
        };

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(withClearMachine, Period, "x"));

        Assert.Contains("DGM20771", html);
        Assert.Contains("TrussTech", html);
    }

    [Fact]
    public void AMachineWithNoBundlesReadsAsPendingNotZero()
    {
        var scan = new FleetScanResult
        {
            Machines = new[]
            {
                new ScannedMachine { SerialNumber = "M21817", Site = "PlaceMakers Auckland", BundlesRead = 0 }
            }
        };

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(scan, Period, "x"));

        Assert.Contains("pending", html);
    }

    [Fact]
    public void OneMachineAloneIsNotCalledAFleetPattern()
    {
        var scan = Scan(Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 9, 12), 1.4));
        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(scan, Period, "x"));

        Assert.Contains("shows this in the period covered", html);
        Assert.Contains("own problem", html);
    }

    [Fact]
    public void AClearOutlierIsCalledOut()
    {
        var occurrences = Enumerable.Range(0, 9)
            .Select(i => Glitch("M21036", "Waihi Mitre 10", new DateTime(2026, 9, 1).AddDays(i), 1.4))
            .Append(Glitch("M21844", "PlaceMakers Auckland", new DateTime(2026, 9, 3), 1.6))
            .ToArray();

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(Scan(occurrences), Period, "x"));

        Assert.Contains("9x the rate", html);
    }

    [Fact]
    public void AnIdentityDisagreementIsPrintedNotResolvedQuietly()
    {
        var scan = new FleetScanResult
        {
            Machines = new[]
            {
                new ScannedMachine
                {
                    SerialNumber = "M21036",
                    Site = "Waihi Mitre 10",
                    BundlesRead = 1,
                    Disagreement = "M21036: the documents disagree about the machine type."
                }
            }
        };

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(scan, Period, "x"));

        Assert.Contains("the documents disagree about the machine type", html);
        Assert.Contains("Machine identity comes from each bundle", html);
        Assert.Contains("own Machine.xml, not from the site", html);
    }

    [Fact]
    public void BundlesThatCouldNotBeReadAreDeclared()
    {
        var scan = new FleetScanResult
        {
            Machines = Array.Empty<ScannedMachine>(),
            Skipped = new[] { "M21036SupportFile.szip: unpacked files are no longer on disk." }
        };

        var html = new ReportHtmlRenderer().Render(FaultBenchmarkReport.Build(scan, Period, "x"));

        Assert.Contains("Not counted:", html);
        Assert.Contains("no longer on disk", html);
    }
}

public class MechanicalChangeCaseTests
{
    private static ReportPeriod Period => new(new DateTime(2026, 8, 15), new DateTime(2026, 9, 14), "Last 30 days");

    private static FleetScanResult Scan(int events, params string[] serials)
    {
        var occurrences = new List<FaultOccurrence>();
        for (var i = 0; i < events; i++)
        {
            occurrences.Add(new FaultOccurrence
            {
                Kind = FaultKind.PlateSensorGlitch,
                WhenUtc = new DateTime(2026, 9, 1).AddDays(i % 10),
                SerialNumber = serials[i % serials.Length],
                MachineType = "RakingWallExtruderV3DG",
                Signal = "PlatePresentSwitch 4.2",
                Detail = "Only the fixed side dropped, with no plate clamp output change nearby.",
                Confidence = Confidence.Confirmed
            });
        }

        return new FleetScanResult
        {
            Occurrences = occurrences,
            Machines = serials.Select(s => new ScannedMachine
            {
                SerialNumber = s,
                MachineType = "RakingWallExtruderV3DG",
                BundlesRead = 1
            }).ToList()
        };
    }

    [Fact]
    public void NoDowntimeFigureIsInventedWhenNobodySuppliedOne()
    {
        var report = MechanicalChangeCaseReport.Build(Scan(22, "M21036", "M21844"), Period, "x", new ChangeCaseInputs());
        var html = new ReportHtmlRenderer().Render(report);

        Assert.Contains("No downtime figure:", html);
        Assert.Contains("rather than an invented one", html);
    }

    [Fact]
    public void ASuppliedTimePerEventIsScaledFromTheRealPeriod()
    {
        // 20 events over 30 days at 30s each is 10 minutes a month, not "20 x 30s" dressed up
        // as a monthly figure.
        var report = MechanicalChangeCaseReport.Build(
            Scan(20, "M21036", "M21844"), Period, "x",
            new ChangeCaseInputs
            {
                TimePerEvent = TimeSpan.FromSeconds(30),
                TimePerEventSource = "timed on M21036, 12 Sep"
            });

        var html = new ReportHtmlRenderer().Render(report);

        Assert.Contains("10 min", html);
        Assert.Contains("timed on M21036, 12 Sep", html);
        Assert.Contains("only as good as that number", html);
    }

    [Fact]
    public void OneAffectedMachineIsNotPresentedAsADesignProblem()
    {
        var report = MechanicalChangeCaseReport.Build(Scan(5, "M21036"), Period, "x", new ChangeCaseInputs());
        var html = new ReportHtmlRenderer().Render(report);

        Assert.Contains("install, not the design", html);
    }

    [Fact]
    public void SeveralAffectedMachinesMakeTheDesignCase()
    {
        var report = MechanicalChangeCaseReport.Build(Scan(8, "M21036", "M21844", "M21737"), Period, "x",
            new ChangeCaseInputs());
        var html = new ReportHtmlRenderer().Render(report);

        Assert.Contains("design or wiring pattern rather than a single bad unit", html);
    }

    [Fact]
    public void ProposedStepsAndValidationAreCarried()
    {
        var report = MechanicalChangeCaseReport.Build(Scan(8, "M21036", "M21844"), Period, "x",
            new ChangeCaseInputs
            {
                ProposedSteps = new[] { "Check cable routing on the fixed side.", "Trial a shielded lead on M21036." },
                StillNeeded = "Photos of the sensor mount on M21036."
            });

        var html = new ReportHtmlRenderer().Render(report);

        Assert.Contains("Check cable routing on the fixed side.", html);
        Assert.Contains("re-run this report over the same number of days", html);
        Assert.Contains("Photos of the sensor mount on M21036.", html);
    }
}

public class MachineInventoryTests
{
    private readonly MachineInventory _inventory = MachineInventory.FromSupportRecords();

    [Fact]
    public void AddsTheSiteDetailTheLogDoesNotCarry()
    {
        // Machine.xml on the real M20716 bundles says "Carters", not which branch.
        Assert.Equal("Carters Cambridge", _inventory.SiteFor("M20716", "Carters"));
    }

    [Fact]
    public void FallsBackToTheLogForAMachineItHasNeverHeardOf()
    {
        Assert.Equal("Grandeur Housing Limited", _inventory.SiteFor("M99999", "Grandeur Housing Limited"));
    }

    [Theory]
    [InlineData("M21642-1")]
    [InlineData("21642")]
    [InlineData("m21642")]
    public void ALineSuffixOrBareDigitsStillFindTheMachine(string serial)
    {
        Assert.Equal("Grandeur Housing Limited", _inventory.Find(serial)?.Site);
    }

    [Fact]
    public void DigitsSharedByTwoMachinesIdentifyNeither()
    {
        // "1694" is AOR1694's digits. If a second machine ever shares them the digits stop
        // identifying either one, and guessing is worse than saying nothing.
        var ambiguous = new MachineInventory(new[]
        {
            new InventoryEntry { SerialNumber = "AOR1694", Site = "Carters Auckland Line 3" },
            new InventoryEntry { SerialNumber = "M1694", Site = "Somewhere Else" }
        });

        Assert.Null(ambiguous.Find("1694"));
        Assert.Equal("Carters Auckland Line 3", ambiguous.Find("AOR1694")?.Site);
        Assert.Equal("Somewhere Else", ambiguous.Find("M1694")?.Site);
    }

    [Fact]
    public void CartersAucklandLineThreeIsTwoMachinesNotOneWithTwoSerials()
    {
        // AOR1613 and AOR1694 were recorded as a serial number conflict. They are not - the
        // nailer feeds the extruder, and both sit on the same line.
        var nailer = _inventory.Find("AOR1613");
        var extruder = _inventory.Find("AOR1694");

        Assert.Equal("Component Nailer", nailer?.AssetName);
        Assert.Equal("Raking Wall Extruder V1", extruder?.AssetName);
        Assert.Equal("AOR1694", nailer?.FeedsInto);
        Assert.Equal(extruder?.Site, nailer?.Site);
    }

    [Theory]
    [InlineData("AOR1613", true)]
    [InlineData("AOR4156", true)]
    [InlineData("M20716", false)]
    [InlineData("DGM20771", false)]
    [InlineData(null, false)]
    public void TheRetiredSerialStyleIsRecognised(string? serial, bool retired)
    {
        // AOR serials were dropped around 2021, so one means an older build on older electronics.
        Assert.Equal(retired, MachineInventory.IsRetiredSerialStyle(serial));
    }

    [Fact]
    public void AWrongTypeInTheInventoryIsReportedNotApplied()
    {
        var note = _inventory.Disagreement("M20716", "TornadoM500");

        Assert.NotNull(note);
        Assert.Contains("The machine is believed over the inventory.", note);
    }

    [Fact]
    public void ADisputedMachineNeverOverridesTheLog()
    {
        var note = _inventory.Disagreement("M18644", "SomethingElse");

        Assert.NotNull(note);
        Assert.Contains("The log is what this report uses.", note);
    }

    [Fact]
    public void AMatchingTypeRaisesNothing()
    {
        Assert.Null(_inventory.Disagreement("M20716", "RakingWallExtruderV3DG"));
        Assert.Null(_inventory.Disagreement("M21036", "RakingWallExtruderV3DG"));
    }
}

public class FleetScanRequestTests
{
    private static DiagnosticFile Bundle(string serial, string type, DateTime arrived) => new()
    {
        SerialNumber = serial,
        MachineType = type,
        ArrivedAtUtc = arrived,
        Status = ProcessingStatus.Processed
    };

    [Fact]
    public void OnlyProcessedBundlesAreCounted()
    {
        var failed = Bundle("M20716", "RakingWallExtruderV3DG", new DateTime(2026, 9, 1));
        failed.Status = ProcessingStatus.Error;

        Assert.False(new FleetScanRequest().Includes(failed));
    }

    [Fact]
    public void ThePeriodBoundsAreHonoured()
    {
        var request = new FleetScanRequest
        {
            FromUtc = new DateTime(2026, 9, 1),
            ToUtc = new DateTime(2026, 9, 10)
        };

        Assert.False(request.Includes(Bundle("M1", "T", new DateTime(2026, 8, 31))));
        Assert.True(request.Includes(Bundle("M1", "T", new DateTime(2026, 9, 5))));
        Assert.False(request.Includes(Bundle("M1", "T", new DateTime(2026, 9, 10))));
    }

    [Fact]
    public void ScopeDefaultsToEverythingRatherThanNothing()
    {
        Assert.True(new FleetScanRequest().Includes(Bundle("M1", "T", DateTime.UtcNow)));
        Assert.True(new FleetScanRequest().Wants(FaultKind.DriveFault));
    }

    [Fact]
    public void NamedSerialsAndTypesNarrowTheScope()
    {
        var request = new FleetScanRequest
        {
            Serials = new[] { "M20716" },
            MachineTypes = new[] { "RakingWallExtruderV3DG" }
        };

        Assert.True(request.Includes(Bundle("M20716", "RakingWallExtruderV3DG", DateTime.UtcNow)));
        Assert.False(request.Includes(Bundle("M20421", "RakingWallExtruderV3DG", DateTime.UtcNow)));
        Assert.False(request.Includes(Bundle("M20716", "TornadoM500", DateTime.UtcNow)));
    }
}

public class ReportServiceTests
{
    [Fact]
    public void TheFileNameSaysWhatItIsAndWhenItEnds()
    {
        var request = new ReportRequest
        {
            Kind = ReportKind.FaultBenchmarking,
            Subject = "PlatePresentSwitch",
            Scope = new FleetScanRequest
            {
                FromUtc = new DateTime(2026, 8, 15),
                ToUtc = new DateTime(2026, 9, 14)
            }
        };

        var generated = new ReportService(new FleetFaultScanner(null!))
            .Render(request, new FleetScanResult());

        Assert.Equal("fault-benchmark-platepresentswitch-2026-09-14.html", generated.SuggestedFileName);
    }

    [Fact]
    public void ThePeriodFallsBackToTheDataRatherThanClaimingAMonthItDidNotRead()
    {
        var scan = new FleetScanResult
        {
            Occurrences = new[]
            {
                new FaultOccurrence { WhenUtc = new DateTime(2026, 9, 1), SerialNumber = "M1" },
                new FaultOccurrence { WhenUtc = new DateTime(2026, 9, 4), SerialNumber = "M1" }
            }
        };

        var generated = new ReportService(new FleetFaultScanner(null!))
            .Render(new ReportRequest { PeriodLabel = "All stored bundles" }, scan);

        Assert.Equal(new DateTime(2026, 9, 1), generated.Model.Period!.FromUtc);
        Assert.Contains("1 Sep - 5 Sep 2026", generated.Html);
    }
}

public class FaultDeduplicationTests
{
    private static DiagnosticFile Bundle(int id, string name, DateTime arrived) => new()
    {
        Id = id,
        OriginalFileName = name,
        SerialNumber = "M20716",
        MachineType = "RakingWallExtruderV3DG",
        Customer = "Carters",
        ArrivedAtUtc = arrived,
        Status = ProcessingStatus.Processed,
        ExtractedPath = null
    };

    [Fact]
    public void ABundleWhoseFilesAreGoneIsDeclaredRatherThanCountedAsClean()
    {
        var scan = new FleetFaultScanner(null!).Scan(
            new[] { Bundle(1, "a.szip", new DateTime(2026, 7, 27)) }, new FleetScanRequest());

        Assert.Single(scan.Skipped);
        Assert.Contains("no longer on disk", scan.Skipped[0]);

        // The machine still appears - it is in scope, we just could not read it.
        Assert.Single(scan.Machines);
        Assert.Equal(0, scan.Machines[0].BundlesRead);
    }

    [Fact]
    public void TheSiteComesFromTheInventoryWhereTheLogOnlyHasTheCompany()
    {
        // The real M20716 bundles say "Carters" in Machine.xml, not which branch.
        var scan = new FleetFaultScanner(null!).Scan(
            new[] { Bundle(1, "a.szip", new DateTime(2026, 7, 27)) }, new FleetScanRequest());

        Assert.Equal("Carters Cambridge", scan.Machines[0].Site);
    }
}

public class ReportPeriodTests
{
    [Fact]
    public void APeriodInsideOneYearNamesItOnce()
    {
        var period = new ReportPeriod(new DateTime(2026, 8, 15), new DateTime(2026, 9, 14), "Last 30 days");

        Assert.Equal("15 Aug - 14 Sep 2026 (30 days)", period.Describe());
    }

    [Fact]
    public void APeriodCrossingAYearSaysSo()
    {
        // "28 Feb - 29 Jul 2026" reads as five months when it was really sixteen.
        var period = new ReportPeriod(new DateTime(2025, 2, 28), new DateTime(2026, 7, 29), "All stored bundles");

        Assert.Equal("28 Feb 2025 - 29 Jul 2026 (516 days)", period.Describe());
    }
}

public class FaultDeduplicationAcrossBundlesTests
{
    /// <summary>Two exports of the same machine, taken minutes apart, carrying the same history.</summary>
    private static Dictionary<string, string> BundleWithErrors() => new()
    {
        ["Machine.xml"] = """
            <?xml version="1.0" encoding="utf-8"?>
            <Machine>
              <Title>Spida SDN, V2.4.0.0, M20716, Carters, RakingWallExtruderV3DG</Title>
            </Machine>
            """,
        ["Logs/ErrLog.txt"] = """
            Date/Time: 27/07/2026 10:56:13 am
            ===========================================================================================

            Title: SDN
            Message: Object reference not set to an instance of an object.
            Source: SDN
            Method: Void ControlWallExtruderPLC()
            StackTrace:    at SDN.ClsWallExtruderPLC.ControlWallExtruderPLC()
            Additional Info:
            """
    };

    [Fact]
    public async Task TheSameFaultInTwoOverlappingExportsIsCountedOnce()
    {
        // Every export carries the machine's recent history, not just the moment it was raised.
        // Two bundles taken seconds apart would otherwise double every figure in the report.
        using var env = new TestEnvironment();

        await env.Processor.ProcessAsync(env.CreateZip("10-15-10 PM . M20716SupportFile.szip", BundleWithErrors()));
        await env.Processor.ProcessAsync(env.CreateZip("10-15-28 PM . M20716SupportFile.szip", BundleWithErrors()));

        var scan = await new FleetFaultScanner(env.Repository).ScanAsync(new FleetScanRequest
        {
            Kinds = new[] { FaultKind.SoftwareError }
        });

        Assert.Equal(2, scan.Machines[0].BundlesRead);
        Assert.Single(scan.Occurrences);
        Assert.Equal("Object reference not set to an instance of an object.", scan.Occurrences[0].Signal);
    }

    [Fact]
    public async Task TheErrorSaysWhereInTheSoftwareItCameFromNotJustTheProductName()
    {
        // Title is "SDN" on every entry in a real ErrLog, so it carries no information.
        using var env = new TestEnvironment();
        await env.Processor.ProcessAsync(env.CreateZip("a.szip", BundleWithErrors()));

        var scan = await new FleetFaultScanner(env.Repository).ScanAsync(new FleetScanRequest
        {
            Kinds = new[] { FaultKind.SoftwareError }
        });

        Assert.DoesNotContain("SDN", scan.Occurrences[0].Signal);
        Assert.Contains("SDN.ClsWallExtruderPLC.ControlWallExtruderPLC()", scan.Occurrences[0].Detail);
    }

    [Fact]
    public async Task MixedCaseFileNamesAreFoundTheSameWayWindowsFindsThem()
    {
        // The real export writes Machine.xml and SupportInfo.txt in mixed case. A filename glob
        // matches case insensitively on Windows but not on Linux, so the match is made in code.
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.szip", BundleWithErrors()));

        Assert.Equal("M20716", file.SerialNumber);
        Assert.Equal("RakingWallExtruderV3DG", file.MachineType);
    }
}
