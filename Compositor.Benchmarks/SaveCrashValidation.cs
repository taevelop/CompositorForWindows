using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class SaveCrashValidation
{
    public static void Run(string[] args)
    {
        if (args[0] == "--save-crash-child")
        {
            var stop = Enum.Parse<SaveCheckpoint>(args[2]);
            var next = Fixture(true);
            ProjectStore.SaveInternal(next, next.Layers[0].Id, args[1], null, checkpoint =>
            {
                if (checkpoint != stop) return;
                Console.WriteLine("READY"); Console.Out.Flush();
                // Parent kills this process. Neither catch nor finally may execute.
                using var barrier = new ManualResetEvent(false);
                barrier.WaitOne();
            });
            throw new InvalidOperationException("The requested crash checkpoint was not reached.");
        }
        if (args[0] == "--save-crash-verify")
        {
            Verify(args[1], Enum.Parse<SaveCheckpoint>(args[2]), bool.Parse(args[3]));
            Console.WriteLine("VERIFIED"); return;
        }
        if (args[0] != "--save-crash") throw new ArgumentException("Unknown crash validation command.");
        string report = Path.GetFullPath(args.Length > 1 ? args[1] : "save-crash-validation.json");
        File.Delete(report); // Do not leave a stale successful report after a failed rerun.
        string root = Path.Combine(Path.GetDirectoryName(report)!, "save-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var results = new List<object>();
        foreach (bool existing in new[] { false, true })
        foreach (var stage in Enum.GetValues<SaveCheckpoint>())
        {
            string folder = Path.Combine(root, $"{(existing ? "overwrite" : "new")}-{stage}");
            Directory.CreateDirectory(folder);
            string project = Path.Combine(folder, "document.comp");
            if (existing)
            {
                var original = Fixture(false);
                ProjectStore.Save(original, original.Layers[0].Id, project);
                File.WriteAllText(Path.Combine(folder, "original-hashes.json"), JsonSerializer.Serialize(Hashes(project)));
            }
            using (var child = Start("--save-crash-child", project, stage.ToString()))
            {
                try
                {
                    string? ready = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                    Require(ready == "READY", "Writer did not reach checkpoint: " + ready);
                    child.Kill(entireProcessTree: true);
                    Require(child.WaitForExit(10000), "Writer did not terminate.");
                    Require(child.ExitCode != 0, "Writer exited normally instead of being killed.");
                }
                finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
            }
            // A separate process, with no in-memory document, opens and recovers the files.
            using (var verifier = Start("--save-crash-verify", project, stage.ToString(), existing.ToString()))
            {
                var output = verifier.StandardOutput.ReadToEndAsync();
                var error = verifier.StandardError.ReadToEndAsync();
                try
                {
                    Require(verifier.WaitForExit(30000), "Restart verification timed out.");
                    Require(verifier.ExitCode == 0 && output.GetAwaiter().GetResult().Contains("VERIFIED"),
                        "Restart verification failed: " + error.GetAwaiter().GetResult());
                }
                finally { if (!verifier.HasExited) { verifier.Kill(entireProcessTree: true); verifier.WaitForExit(); } }
            }
            results.Add(new { existing, stage = stage.ToString(), passed = true, project });
            Console.WriteLine($"PASS {(existing ? "overwrite" : "new")} {stage}");
        }
        File.WriteAllText(report, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
            description = "Real child-process kill during production ProjectStore saves, followed by fresh-process verification. Not power-loss or physical-disk fault testing. Artifacts retained.",
            passed = true, results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        // Support invocation through either the apphost or dotnet <assembly>.
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(SaveCrashValidation).Assembly.Location);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException("Could not start verification process.");
    }

    private static void Verify(string project, SaveCheckpoint stage, bool existing)
    {
        bool published = stage == SaveCheckpoint.Published;
        bool originalMoved = existing && stage is SaveCheckpoint.BeforePublish or SaveCheckpoint.Published;
        string backup = project + ".recovery";
        Require(Directory.Exists(project) == (published || (existing && !originalMoved)), "Unexpected destination state.");
        Require(Directory.Exists(backup) == originalMoved, "Unexpected recovery state.");
        if (Directory.Exists(project)) AssertProject(ProjectStore.Load(project), published);
        if (existing)
        {
            string preserved = originalMoved ? backup : project;
            string baseline = File.ReadAllText(Path.Combine(Path.GetDirectoryName(project)!, "original-hashes.json"));
            Require(JsonSerializer.Serialize(Hashes(preserved)) == baseline, "Original bytes changed.");
            if (originalMoved)
            {
                AssertProject(ProjectStore.LoadRecovery(backup), false);
                bool rejected = false;
                try { ProjectStore.Save(Fixture(true), Fixture(true).Layers[0].Id, project); }
                catch (IOException) { rejected = true; }
                Require(rejected, "Existing recovery must block overwriting.");
                var recovered = ProjectStore.LoadRecovery(backup);
                var session = new EditorSession(recovered.Document);
                session.Load(recovered.Document, recovered.ActiveLayerId, recovered: true);
                Require(session.IsModified, "Recovery must require saving.");
                string saved = Path.Combine(Path.GetDirectoryName(project)!, "recovered.comp");
                ProjectStore.Save(session.Document, session.ActiveLayerId, saved);
                AssertProject(ProjectStore.Load(saved), false);
                Require(JsonSerializer.Serialize(Hashes(backup)) == baseline, "Recovery source changed.");
            }
        }
        string[] staging = Directory.GetDirectories(Path.GetDirectoryName(project)!, "document.comp.staging-*");
        Require(staging.Length == (published ? 0 : 1), "Unexpected staging state.");
        if (!published && stage != SaveCheckpoint.AssetWritten)
        {
            AssertProject(ProjectStore.Load(staging[0]), true);
        }
        else if (stage == SaveCheckpoint.AssetWritten)
        {
            bool rejected = false;
            try { ProjectStore.Load(staging[0]); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Incomplete staging must not load.");
        }
        // The killed writer's OS lock must have been released.
        using var handle = new FileStream(project + ".write-lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static Document Fixture(bool changed)
    {
        var bytes = new byte[512 * 512 * 4];
        for (int p = 0; p < bytes.Length; p += 4)
        { bytes[p] = changed ? (byte)201 : (byte)32; bytes[p + 1] = (byte)(p / 4 % 251); bytes[p + 2] = 70; bytes[p + 3] = 255; }
        var layer = new Layer(Guid.Parse("11111111-1111-1111-1111-111111111111"), changed ? "Edited" : "Original",
            Raster.FromRgba(512, 512, bytes), new(7, 11, 490, 480, 17), Opacity: .65, Blend: BlendMode.Multiply);
        return new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 512, 512, 144, [layer]);
    }

    private static void AssertProject(LoadedProject loaded, bool changed)
    {
        var expected = Fixture(changed); var actual = loaded.Document;
        Require(actual.Id == expected.Id && actual.Width == expected.Width && actual.Height == expected.Height &&
            actual.Resolution == expected.Resolution && actual.Layers.Length == 1 && loaded.ActiveLayerId == expected.Layers[0].Id,
            "Document metadata mismatch.");
        var a = actual.Layers[0]; var e = expected.Layers[0];
        Require((a with { Pixels = e.Pixels }) == e && a.Pixels.ToRgba().SequenceEqual(e.Pixels.ToRgba()), "Layer mismatch.");
    }

    private static SortedDictionary<string, string> Hashes(string path) => new(Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .ToDictionary(file => Path.GetRelativePath(path, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))), StringComparer.Ordinal);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
