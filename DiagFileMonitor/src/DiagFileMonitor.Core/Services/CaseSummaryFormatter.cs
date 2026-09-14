using System.Text;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>Builds the plain-text block support pastes into a ticket.</summary>
public static class CaseSummaryFormatter
{
    public static string Format(DiagnosticFileSummary file)
    {
        var text = new StringBuilder();
        text.AppendLine($"Diagnostic file: {file.OriginalFileName}");
        text.AppendLine($"Serial number:   {file.SerialNumber}");
        text.AppendLine($"Machine model:   {file.MachineType}");
        text.AppendLine($"Machine name:    {file.MachineName}");
        text.AppendLine($"Customer:        {file.Customer}");
        text.AppendLine($"Site location:   {file.SiteLocation}");
        text.AppendLine($"Software:        {file.SoftwareName}");
        text.AppendLine($"Version:         {file.Version}");
        text.AppendLine($"Arrived:         {file.ArrivedDisplay}");
        text.AppendLine($"Status:          {file.Status}");

        if (!string.IsNullOrWhiteSpace(file.TicketNumber))
        {
            text.AppendLine($"Ticket:          {file.TicketNumber}");
        }

        if (!string.IsNullOrWhiteSpace(file.ErrorMessage))
        {
            text.AppendLine($"Problem:         {file.ErrorMessage}");
        }

        if (!string.IsNullOrWhiteSpace(file.ExtractedPath))
        {
            text.AppendLine($"Unpacked to:     {file.ExtractedPath}");
        }

        if (!string.IsNullOrWhiteSpace(file.Notes))
        {
            text.AppendLine();
            text.AppendLine("Notes:");
            text.AppendLine(file.Notes);
        }

        return text.ToString();
    }
}
