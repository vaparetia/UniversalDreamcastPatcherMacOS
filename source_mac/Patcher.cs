using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace UniversalDreamcastPatcher;

public record PatchProgress(int Percent, string Details);

public class PatchException(string message) : Exception(message);

public static class Patcher
{
    // Returns names of any required tools that are missing.
    public static List<string> CheckTools(string toolsDir)
    {
        var missing = new List<string>();
        if (FindTool("buildgdi", toolsDir) == null)
            missing.Add("buildgdi");
        if (FindTool("convertredumptogdi", toolsDir) == null)
            missing.Add("convertredumptogdi");
        if (FindTool("xdelta3", toolsDir) == null)
            missing.Add("xdelta3  (install via: brew install xdelta)");
        return missing;
    }

    public static string ApplyPatch(
        string sourceFile,
        string patchFile,
        string toolsDir,
        IProgress<PatchProgress> progress,
        CancellationToken ct = default)
    {
        string patchFilename = Path.GetFileNameWithoutExtension(patchFile);
        string appBaseFolder = AppContext.BaseDirectory;
        string guid = Guid.NewGuid().ToString();
        string tempCue     = Path.Combine(Path.GetTempPath(), "_UDP_" + guid + "_cue");
        string tempExtract = Path.Combine(Path.GetTempPath(), "_UDP_" + guid + "_extract");
        string tempPatch   = Path.Combine(Path.GetTempPath(), "_UDP_" + guid + "_patch");
        string tempData    = Path.Combine(Path.GetTempPath(), "_UDP_" + guid + "_data");
        string outputDir   = Path.Combine(appBaseFolder, patchFilename + " [GDI]");

        try
        {
            Directory.CreateDirectory(tempExtract);
            Directory.CreateDirectory(tempPatch);
            Directory.CreateDirectory(tempData);

            Report(progress, 5, "Starting...");
            ct.ThrowIfCancellationRequested();

            // ── Convert CUE to GDI if needed ────────────────────────────────
            string gdiFile = sourceFile;
            if (Path.GetExtension(sourceFile).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                Report(progress, 8, "Converting CUE to GDI...");

                string convertTool = FindTool("convertredumptogdi", toolsDir)
                    ?? throw new PatchException("convertredumptogdi is missing from the tools folder.");

                Directory.CreateDirectory(tempCue);
                var (_, cueErr, cueCode) = RunProcess(
                    convertTool, $"\"{sourceFile}\" \"{tempCue}\"");

                if (cueCode != 0)
                    throw new PatchException(
                        "Failed to convert the CUE disc image to GDI format.\n\n" +
                        "The source disc image may be malformed or incompatible.");

                gdiFile = Directory.GetFiles(tempCue, "*.gdi").FirstOrDefault()
                    ?? throw new PatchException(
                        "CUE conversion produced no GDI file. " +
                        "The source disc image may be malformed or incompatible.");
            }

            string buildgdiTool = FindTool("buildgdi", toolsDir)
                ?? throw new PatchException("buildgdi is missing from the tools folder.");

            string xdeltaTool = FindTool("xdelta3", toolsDir)
                ?? throw new PatchException("xdelta3 not found. Install it with: brew install xdelta");

            // ── Extract DCP patch file ───────────────────────────────────────
            Report(progress, 10, "Reading patch file...");
            try
            {
                ZipFile.ExtractToDirectory(patchFile, tempPatch);
            }
            catch (Exception ex)
            {
                throw new PatchException(
                    $"The selected DCP patch file is either corrupt or incompatible.\n\n{ex.Message}");
            }

            // ── Validate the source GDI ──────────────────────────────────────
            Report(progress, 15, "Verifying source GDI...");
            ValidateGdi(gdiFile);

            // ── Extract original GDI if xdelta patches are present ───────────
            var xdeltaFiles = Directory.GetFiles(tempPatch, "*.xdelta", SearchOption.AllDirectories);

            if (xdeltaFiles.Length > 0)
            {
                Report(progress, 20, "Extracting source GDI...");

                var (_, extractErr, extractCode) = RunProcess(
                    buildgdiTool,
                    $"-extract -gdi \"{gdiFile}\" -output \"{tempExtract}\" -ip \"{tempExtract}\"");

                if (extractCode != 0)
                    throw new PatchException(
                        $"Failed to extract the source GDI.\n\nThe source disc image may be malformed or incompatible.");
            }

            // ── Apply xdelta patches ─────────────────────────────────────────
            Report(progress, 55, "Applying patch...");

            foreach (var xdeltaFile in xdeltaFiles)
            {
                string relXdelta   = Path.GetRelativePath(tempPatch, xdeltaFile);
                string relOriginal = relXdelta[..^7]; // strip ".xdelta"

                Report(progress, 55, $"Patching {relOriginal}...");
                ct.ThrowIfCancellationRequested();

                // Find the original file (case-insensitive)
                string? originalFile = FindFileCaseInsensitive(tempExtract, relOriginal);

                if (originalFile == null)
                    throw new PatchException(
                        $"The DCP patch references a file not found in the source disc image:\n\n{relOriginal}");

                string outputFile = Path.Combine(tempData, relOriginal);
                Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

                var (_, xErr, xCode) = RunProcess(
                    xdeltaTool,
                    $"-d -s \"{originalFile}\" \"{xdeltaFile}\" \"{outputFile}\"");

                if (xCode != 0 || !string.IsNullOrEmpty(xErr))
                    throw new PatchException(
                        "The source disc image contains a different version of a file " +
                        "than the patch expects. Patching cannot proceed.");
            }

            // ── Copy non-xdelta, non-bootsector replacement files ────────────
            foreach (var file in Directory.GetFiles(tempPatch, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(tempPatch, file);

                // Skip xdelta patches (already processed above)
                if (rel.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase)) continue;

                // Skip bootsector — its IP.BIN is passed as -ip instead
                if (rel.StartsWith("bootsector" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || rel.Equals("bootsector", StringComparison.OrdinalIgnoreCase)) continue;

                string dest = Path.Combine(tempData, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }

            ct.ThrowIfCancellationRequested();

            // ── Rebuild GDI using buildgdi -rebuild ──────────────────────────
            Report(progress, 70, "Building patched GDI...");

            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
            Directory.CreateDirectory(outputDir);

            string newIpBin = Path.Combine(tempPatch, "bootsector", "IP.BIN");
            string buildArgs = $"-rebuild -gdi \"{gdiFile}\" -data \"{tempData}\" -output \"{outputDir}\"";
            if (File.Exists(newIpBin))
                buildArgs += $" -ip \"{newIpBin}\"";

            var (buildOut, buildErr, buildCode) = RunProcess(buildgdiTool, buildArgs);

            if (buildCode != 0)
                throw new PatchException(
                    $"An error occurred building the patched GDI.\n\n" +
                    $"Try again with a different source disc image.\n\n{buildErr}");

            Report(progress, 100, "Done!");
            return outputDir;
        }
        catch
        {
            TryDeleteDir(tempCue);
            TryDeleteDir(tempExtract);
            TryDeleteDir(tempPatch);
            TryDeleteDir(tempData);
            throw;
        }
        finally
        {
            TryDeleteDir(tempCue);
            TryDeleteDir(tempExtract);
            TryDeleteDir(tempPatch);
            TryDeleteDir(tempData);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void Report(IProgress<PatchProgress> p, int pct, string msg)
        => p.Report(new PatchProgress(pct, msg));

    private static void ValidateGdi(string gdiPath)
    {
        if (!File.Exists(gdiPath))
            throw new PatchException($"GDI file not found:\n{gdiPath}");

        string gdiDir = Path.GetDirectoryName(gdiPath)!;
        var lines = File.ReadAllLines(gdiPath);

        if (lines.Length < 2)
            throw new PatchException("The selected source GDI is malformed or incompatible.");

        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;

            string filename = parts[4];
            string ext = Path.GetExtension(filename).ToLower();

            if (ext != ".bin" && ext != ".iso" && ext != ".raw")
                throw new PatchException(
                    $"The selected source GDI is either malformed or incompatible.\n\n" +
                    $"Unexpected track extension: {ext}");

            if (!File.Exists(Path.Combine(gdiDir, filename)))
                throw new PatchException(
                    $"The selected source GDI is either malformed or incompatible.\n\n" +
                    $"Missing track file: {filename}");
        }
    }

    // Find a file by relative path, trying both exact case and uppercase variant.
    private static string? FindFileCaseInsensitive(string baseDir, string relPath)
    {
        // Exact match
        string exact = Path.Combine(baseDir, relPath);
        if (File.Exists(exact)) return exact;

        // Uppercase match (ISO 9660 filenames are uppercase on Dreamcast)
        string upper = Path.Combine(baseDir, relPath.ToUpper());
        if (File.Exists(upper)) return upper;

        // Full recursive scan for case-insensitive match
        string target = relPath.Replace(Path.DirectorySeparatorChar, '/').ToUpperInvariant();
        foreach (var file in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(baseDir, file)
                             .Replace(Path.DirectorySeparatorChar, '/')
                             .ToUpperInvariant();
            if (rel == target) return file;
        }

        return null;
    }

    private static (string stdout, string stderr, int exitCode) RunProcess(
        string executable, string arguments, string? workingDir = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDir ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (stdout, stderr, process.ExitCode);
    }

    private static string? FindTool(string name, string toolsDir)
    {
        string inTools = Path.Combine(toolsDir, name);
        if (File.Exists(inTools)) return inTools;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var dir in pathEnv.Split(':'))
            {
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
