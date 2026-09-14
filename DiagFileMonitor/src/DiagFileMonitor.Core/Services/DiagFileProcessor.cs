using System.IO.Compression;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>Unpacks one diagnostic zip, reads machine.xml, indexes the extracted files, and persists the result.</summary>
public class DiagFileProcessor
{
    private readonly string _extractRootPath;
    private readonly DiagFileRepository _repository;
    private readonly bool _fileNameTimesAreUtc;

    private static readonly Dictionary<string, LogFileKind> KnownLogFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["changelog.txt"] = LogFileKind.ChangeLog,
        ["machinelog.txt"] = LogFileKind.MachineLog,
        ["errorlog.txt"] = LogFileKind.ErrorLog,
        ["supportinfo.txt"] = LogFileKind.SupportInfo
    };

    public DiagFileProcessor(string extractRootPath, DiagFileRepository repository, bool fileNameTimesAreUtc = true)
    {
        _extractRootPath = extractRootPath;
        _repository = repository;
        _fileNameTimesAreUtc = fileNameTimesAreUtc;
        Directory.CreateDirectory(_extractRootPath);
    }

    /// <summary>Whether this bundle has been imported before, so a re-scan can skip it.</summary>
    public async Task<bool> IsAlreadyStoredAsync(string zipPath)
    {
        try
        {
            var info = new FileInfo(zipPath);
            if (!info.Exists) return false;

            return await _repository.ExistsAsync(
                info.Name, info.Length, DiagFileNameDate.ArrivedUtc(zipPath, _fileNameTimesAreUtc));
        }
        catch (IOException)
        {
            // If we cannot tell, let it through rather than silently dropping a bundle.
            return false;
        }
    }

    public async Task<DiagnosticFile> ProcessAsync(string zipPath, CancellationToken token = default)
    {
        var fileInfo = new FileInfo(zipPath);
        var diagFile = new DiagnosticFile
        {
            OriginalFileName = fileInfo.Name,
            SourcePath = zipPath,
            FileSizeBytes = fileInfo.Length,
            // The name carries when the machine produced the bundle; the file date only
            // says when it was last copied about.
            ArrivedAtUtc = DiagFileNameDate.ArrivedUtc(zipPath, _fileNameTimesAreUtc),
            Status = ProcessingStatus.Pending
        };

        try
        {
            var extractDir = CreateUniqueExtractDir(fileInfo.Name);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            diagFile.ExtractedPath = extractDir;

            var machineXmlPath = Directory
                .EnumerateFiles(extractDir, "machine.xml", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (machineXmlPath is not null)
            {
                var info = MachineXmlParser.Parse(machineXmlPath);
                diagFile.MachineType = info.MachineType;
                diagFile.MachineName = info.MachineName;
                diagFile.SerialNumber = info.SerialNumber;
                diagFile.Customer = info.Customer;
                diagFile.SiteLocation = info.SiteLocation;
                diagFile.SoftwareName = info.SoftwareName;
                diagFile.Version = info.Version;
            }
            else
            {
                SimpleLogger.Info($"No machine.xml found in '{zipPath}'.");
            }

            var supportInfoPath = Directory
                .EnumerateFiles(extractDir, "supportinfo.txt", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (supportInfoPath is not null)
            {
                var support = SupportInfoParser.ParseFile(supportInfoPath);
                diagFile.SupportPanel = support.Panel;
                diagFile.SupportMembers = support.Members;
                diagFile.SupportIssue = support.Issue;
            }

            foreach (var extractedFile in Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(extractedFile);
                var kind = KnownLogFiles.TryGetValue(name, out var knownKind) ? knownKind : LogFileKind.Other;

                diagFile.LogFiles.Add(new ExtractedLogFile
                {
                    FileName = name,
                    FullPath = extractedFile,
                    SizeBytes = new FileInfo(extractedFile).Length,
                    Kind = kind
                });
            }

            if (string.IsNullOrWhiteSpace(diagFile.SerialNumber))
            {
                diagFile.Status = ProcessingStatus.Error;
                diagFile.ErrorMessage = "machine.xml was missing or did not contain a serial number.";
            }
            else
            {
                diagFile.Status = ProcessingStatus.Processed;
            }
        }
        catch (Exception ex)
        {
            diagFile.Status = ProcessingStatus.Error;
            diagFile.ErrorMessage = ex.Message;
            SimpleLogger.Error($"Error processing '{zipPath}'", ex);
        }
        finally
        {
            diagFile.ProcessedAtUtc = DateTime.UtcNow;
        }

        await _repository.AddAsync(diagFile);
        return diagFile;
    }

    private string CreateUniqueExtractDir(string zipFileName)
    {
        var baseName = SanitizeForPath(Path.GetFileNameWithoutExtension(zipFileName));
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");

        // Two bundles can land inside the same millisecond, so keep trying until we get a new folder.
        var dir = Path.Combine(_extractRootPath, $"{stamp}_{baseName}");
        var attempt = 1;
        while (Directory.Exists(dir))
        {
            dir = Path.Combine(_extractRootPath, $"{stamp}_{baseName}_{attempt++}");
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SanitizeForPath(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
