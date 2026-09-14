using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// Covers the real Spida machine.xml shape: a large settings document with the identifying
/// fields as direct children of the root near the end, plus a combined Title line.
/// Values here are made up; the structure mirrors a real file.
/// </summary>
public class SpidaMachineXmlTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spidaxml", Guid.NewGuid().ToString("N"));

    private string Write(string xml)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, $"machine{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private const string SpidaShaped = """
        <?xml version="1.0" encoding="utf-8"?>
        <Machine xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <JobSettings>
            <JobPath1>C:\Data\SpidaJobs</JobPath1>
          </JobSettings>
          <MemberRoleActions>
            <RoleActions>
              <Role>Stud</Role>
              <Name>Stud</Name>
            </RoleActions>
            <RoleActions>
              <Role>Nog</Role>
              <Name>Nog</Name>
            </RoleActions>
          </MemberRoleActions>
          <ServerStationID>1</ServerStationID>
          <MachineModel>Apollo</MachineModel>
          <MachineName>Spida Saw</MachineName>
          <SiteName>Example Housing Limited</SiteName>
          <SerialNumber>M99999-1</SerialNumber>
          <AORNumber />
          <SiteLocation>Hamilton</SiteLocation>
          <Title>Spida SDN, V2.5.0.0, M99999-1, Example Housing Limited, Apollo</Title>
        </Machine>
        """;

    [Fact]
    public void ReadsEveryFieldFromARealShapedFile()
    {
        var info = MachineXmlParser.Parse(Write(SpidaShaped));

        Assert.Equal("M99999-1", info.SerialNumber);
        Assert.Equal("Example Housing Limited", info.Customer);
        Assert.Equal("Apollo", info.MachineType);
        Assert.Equal("Spida Saw", info.MachineName);
        Assert.Equal("Hamilton", info.SiteLocation);
        Assert.Equal("Spida SDN", info.SoftwareName);
        Assert.Equal("V2.5.0.0", info.Version);
    }

    [Fact]
    public void NestedRoleNamesAreNotMistakenForTheMachineName()
    {
        // The document is full of RoleActions/Name elements; none of them is the machine name.
        var info = MachineXmlParser.Parse(Write(SpidaShaped));

        Assert.Equal("Spida Saw", info.MachineName);
        Assert.NotEqual("Stud", info.MachineName);
    }

    [Fact]
    public void EmptyElementsAreTreatedAsMissing()
    {
        // AORNumber is self-closing in the sample and must not become a value.
        var info = MachineXmlParser.Parse(Write(SpidaShaped));

        Assert.Equal("M99999-1", info.SerialNumber);
    }

    [Fact]
    public void FallsBackToTheTitleLineWhenElementsAreAbsent()
    {
        var info = MachineXmlParser.Parse(Write("""
            <Machine>
              <Title>Spida SDN, V2.5.0.0, M12345-2, Some Builder Co, Nexus</Title>
            </Machine>
            """));

        Assert.Equal("M12345-2", info.SerialNumber);
        Assert.Equal("Some Builder Co", info.Customer);
        Assert.Equal("Nexus", info.MachineType);
        Assert.Equal("Spida SDN", info.SoftwareName);
        Assert.Equal("V2.5.0.0", info.Version);
    }

    [Fact]
    public void VersionComesFromTheTitleEvenWhenEverythingElseIsAnElement()
    {
        // There is no Version element in a Spida file - Title is the only source.
        var info = MachineXmlParser.Parse(Write(SpidaShaped));

        Assert.Equal("V2.5.0.0", info.Version);
    }

    [Fact]
    public void ElementsWinOverTheTitleWhenTheyDisagree()
    {
        var info = MachineXmlParser.Parse(Write("""
            <Machine>
              <MachineModel>Apollo</MachineModel>
              <SiteName>Correct Customer Ltd</SiteName>
              <SerialNumber>M11111-1</SerialNumber>
              <Title>Spida SDN, V2.5.0.0, M-STALE, Stale Customer, StaleModel</Title>
            </Machine>
            """));

        Assert.Equal("M11111-1", info.SerialNumber);
        Assert.Equal("Correct Customer Ltd", info.Customer);
        Assert.Equal("Apollo", info.MachineType);
    }

    [Fact]
    public void ACommaInTheCustomerNameDoesNotShiftTheModel()
    {
        var info = MachineXmlParser.Parse(Write("""
            <Machine>
              <Title>Spida SDN, V2.5.0.0, M22222-1, Smith, Jones and Co Limited, Apollo</Title>
            </Machine>
            """));

        Assert.Equal("M22222-1", info.SerialNumber);
        Assert.Equal("Smith, Jones and Co Limited", info.Customer);
        Assert.Equal("Apollo", info.MachineType);
        Assert.Equal("V2.5.0.0", info.Version);
    }

    [Fact]
    public void HandlesAShortOrMissingTitleWithoutFailing()
    {
        var shortTitle = MachineXmlParser.Parse(Write("<Machine><Title>Spida SDN, V2.5.0.0</Title></Machine>"));
        Assert.Equal("Spida SDN", shortTitle.SoftwareName);
        Assert.Equal("V2.5.0.0", shortTitle.Version);
        Assert.Null(shortTitle.SerialNumber);

        var noTitle = MachineXmlParser.Parse(Write("<Machine><SerialNumber>M1</SerialNumber></Machine>"));
        Assert.Equal("M1", noTitle.SerialNumber);
        Assert.Null(noTitle.Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
