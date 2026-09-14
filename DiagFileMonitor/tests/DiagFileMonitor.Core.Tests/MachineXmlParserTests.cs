using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class MachineXmlParserTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "diagxml", Guid.NewGuid().ToString("N"));

    private string WriteXml(string xml)
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "machine.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void ReadsStandardSchema()
    {
        var path = WriteXml("""
            <?xml version="1.0" encoding="utf-8"?>
            <Machine>
              <MachineType>CoffeeRoaster-5000</MachineType>
              <SerialNumber>SN-00123</SerialNumber>
              <Customer>Wellington Roasters Ltd</Customer>
              <Version>2.4.1</Version>
            </Machine>
            """);

        var info = MachineXmlParser.Parse(path);

        Assert.Equal("CoffeeRoaster-5000", info.MachineType);
        Assert.Equal("SN-00123", info.SerialNumber);
        Assert.Equal("Wellington Roasters Ltd", info.Customer);
        Assert.Equal("2.4.1", info.Version);
    }

    [Fact]
    public void ReadsAliasElementNames()
    {
        var path = WriteXml("""
            <Machine>
              <Model>Grinder-200</Model>
              <Serial>SN-99</Serial>
              <Client>Auckland Cafe</Client>
              <FirmwareVersion>1.0.3</FirmwareVersion>
            </Machine>
            """);

        var info = MachineXmlParser.Parse(path);

        Assert.Equal("Grinder-200", info.MachineType);
        Assert.Equal("SN-99", info.SerialNumber);
        Assert.Equal("Auckland Cafe", info.Customer);
        Assert.Equal("1.0.3", info.Version);
    }

    [Fact]
    public void ReadsValuesHeldAsAttributes()
    {
        var path = WriteXml("""<Machine MachineType="Roaster-1" SerialNumber="SN-7" Customer="Hamilton Beans" Version="3.0" />""");

        var info = MachineXmlParser.Parse(path);

        Assert.Equal("Roaster-1", info.MachineType);
        Assert.Equal("SN-7", info.SerialNumber);
        Assert.Equal("Hamilton Beans", info.Customer);
        Assert.Equal("3.0", info.Version);
    }

    [Fact]
    public void MatchesElementNamesCaseInsensitively()
    {
        var path = WriteXml("""
            <machine>
              <serialnumber>SN-42</serialnumber>
            </machine>
            """);

        Assert.Equal("SN-42", MachineXmlParser.Parse(path).SerialNumber);
    }

    [Fact]
    public void ReturnsNullForMissingFields()
    {
        var path = WriteXml("<Machine><SerialNumber>SN-1</SerialNumber></Machine>");

        var info = MachineXmlParser.Parse(path);

        Assert.Equal("SN-1", info.SerialNumber);
        Assert.Null(info.Customer);
        Assert.Null(info.MachineType);
        Assert.Null(info.Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
