using System;
using System.Collections.Generic;
using System.IO;
using CueSharp;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: convertredumptogdi <cuefile> <outputdir>");
    return 1;
}

string cueFile = args[0];
string outputDir = args[1];

if (!File.Exists(cueFile))
{
    Console.Error.WriteLine($"CUE file not found: {cueFile}");
    return 1;
}

try
{
    Directory.CreateDirectory(outputDir);

    var cueInfo = new FileInfo(cueFile);
    var cueSheet = new CueSheet(cueFile);
    string gdiName = Path.GetFileNameWithoutExtension(cueFile);

    int currentSector = 0;
    var gdiLines = new List<string> { cueSheet.Tracks.Length.ToString() };

    for (int i = 0; i < cueSheet.Tracks.Length; i++)
    {
        Track track = cueSheet.Tracks[i];
        string inputPath = Path.Combine(cueInfo.Directory!.FullName, track.DataFile.Filename);
        bool isAudio = track.TrackDataType == DataType.AUDIO;
        string outputName = $"track{track.TrackNumber}.{(isAudio ? "raw" : "bin")}";
        string outputPath = Path.Combine(outputDir, outputName);

        int sectorAmount;
        if (track.Indices.Length == 1)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            sectorAmount = (int)(new FileInfo(inputPath).Length / 2352);
        }
        else
        {
            int gapOffset = ToFrames(track.Indices[1]);
            sectorAmount = CopyWithOffset(inputPath, outputPath, gapOffset);
            currentSector += gapOffset;
        }

        gdiLines.Add($"{track.TrackNumber} {currentSector} {(isAudio ? 0 : 4)} 2352 {outputName} 0");
        currentSector += sectorAmount;

        if (Array.Exists(track.Comments, c => c.Contains("HIGH-DENSITY AREA")) && currentSector < 45000)
            currentSector = 45000;
    }

    File.WriteAllLines(Path.Combine(outputDir, gdiName + ".gdi"), gdiLines);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int ToFrames(CueSharp.Index idx)
    => idx.Frames + idx.Seconds * 75 + idx.Minutes * 60 * 75;

static int CopyWithOffset(string src, string dst, int frames)
{
    using var inStream  = File.OpenRead(src);
    using var outStream = File.OpenWrite(dst);
    inStream.Position = (long)frames * 2352;
    int sectors = (int)((inStream.Length - inStream.Position) / 2352);
    byte[] buf = new byte[65536];
    int read;
    while ((read = inStream.Read(buf, 0, buf.Length)) > 0)
        outStream.Write(buf, 0, read);
    return sectors;
}
