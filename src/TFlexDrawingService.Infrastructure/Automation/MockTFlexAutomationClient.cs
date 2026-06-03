using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Infrastructure.Automation;

public sealed class MockTFlexAutomationClient(ILogger<MockTFlexAutomationClient> logger) : ITFlexAutomationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<GeneratedFile>> GenerateAsync(
        TFlexGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(request.ResultDirectory);

        var format = request.OutputFormat.Trim().TrimStart('.').ToLowerInvariant();
        var fileName = $"{Sanitize(request.Template.Code)}_{request.Job.Id}.{format}";
        var resultPath = Path.Combine(request.ResultDirectory, fileName);

        if (format == "pdf")
        {
            await File.WriteAllBytesAsync(resultPath, BuildPdfPlaceholder(request), cancellationToken);
        }
        else
        {
            await File.WriteAllTextAsync(resultPath, BuildPlaceholderText(request), Encoding.UTF8, cancellationToken);
        }

        var fileInfo = new FileInfo(resultPath);
        logger.LogInformation(
            "Mock T-FLEX adapter generated {FileName} for job {JobId}. Replace this adapter with COM/SDK integration on Windows.",
            fileName,
            request.Job.Id);

        return
        [
            new GeneratedFile
            {
                JobId = request.Job.Id,
                FileName = fileName,
                Format = format,
                Path = resultPath,
                SizeBytes = fileInfo.Length
            }
        ];
    }

    private static byte[] BuildPdfPlaceholder(TFlexGenerationRequest request)
    {
        var lines = new[]
        {
            "Mock T-FLEX generation result",
            $"Job: {request.Job.Id}",
            $"Template: {request.Template.Code}",
            $"Output format: {request.OutputFormat}",
            "TODO: Replace MockTFlexAutomationClient with real T-FLEX CAD automation."
        };

        var contentStream = new StringBuilder();
        contentStream.AppendLine("BT");
        contentStream.AppendLine("/F1 12 Tf");
        contentStream.AppendLine("72 760 Td");
        foreach (var line in lines)
        {
            contentStream.Append('(').Append(EscapePdfText(line)).AppendLine(") Tj");
            contentStream.AppendLine("0 -18 Td");
        }
        contentStream.AppendLine("ET");

        var streamText = contentStream.ToString();
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n",
            $"4 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(streamText)} >>\nstream\n{streamText}endstream\nendobj\n",
            "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"
        };

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n");
        var offsets = new List<long>();
        foreach (var item in objects)
        {
            offsets.Add(output.Position);
            WriteAscii(output, item);
        }

        var xrefPosition = output.Position;
        WriteAscii(output, "xref\n");
        WriteAscii(output, $"0 {objects.Length + 1}\n");
        WriteAscii(output, "0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(output, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\n");
        WriteAscii(output, $"startxref\n{xrefPosition}\n%%EOF\n");
        return output.ToArray();
    }

    private static string BuildPlaceholderText(TFlexGenerationRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Mock T-FLEX generation result");
        builder.AppendLine($"Job: {request.Job.Id}");
        builder.AppendLine($"Template: {request.Template.Code}");
        builder.AppendLine($"Template copy: {request.TemplateCopyPath}");
        builder.AppendLine($"Output format: {request.OutputFormat}");
        builder.AppendLine();
        builder.AppendLine("Input parameters:");
        builder.AppendLine(JsonSerializer.Serialize(request.Parameters, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("TODO: Replace MockTFlexAutomationClient with real T-FLEX CAD COM/SDK automation.");
        builder.AppendLine("The real adapter must open the copied template, set variables, rebuild, export, and close the document.");
        return builder.ToString();
    }

    private static string EscapePdfText(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static void WriteAscii(Stream stream, string value)
    {
        stream.Write(Encoding.ASCII.GetBytes(value));
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "drawing" : clean;
    }
}
