using System.Diagnostics;
using System.Globalization;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>What the Media Library settings show about ffmpeg: whether it is set up and which version runs.</summary>
/// <param name="Configured"><c>FFMPEG_PATH</c> is set.</param>
/// <param name="Available">It ran and answered <c>-version</c>.</param>
public sealed record FfmpegInfo(bool Configured, bool Available, string? Path, string? Version, string? Problem);

/// <summary>
/// The ffmpeg executable from <c>FFMPEG_PATH</c> (.docs/features/media-storage.md#optimization): probed once, and used to
/// compress MP4 uploads to H.264/AAC at the configured resolution and quality. The arguments are passed as a list - no
/// shell, nothing user-typed in them.
/// </summary>
public sealed class Ffmpeg
{
    /// <summary>A compression that takes longer than this is stopped (the upload is kept).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromHours(2);

    private readonly string? _path;
    private readonly ILogger<Ffmpeg> _logger;
    private readonly Lazy<Task<FfmpegInfo>> _info;

    public Ffmpeg(AppEnvironment env, ILogger<Ffmpeg> logger)
    {
        _path = string.IsNullOrWhiteSpace(env.FfmpegPath) ? null : env.FfmpegPath;
        _logger = logger;
        _info = new Lazy<Task<FfmpegInfo>>(ProbeAsync);
    }

    public Task<FfmpegInfo> GetInfoAsync() => _info.Value;

    /// <summary>
    /// The ffmpeg arguments: the first video and (if any) audio stream, H.264 at <paramref name="crf"/>, scaled down so
    /// the short side is at most <paramref name="resolution"/> (never up, dimensions kept even), AAC 128 kbit/s, and
    /// the index at the front so playback starts before the whole file has loaded.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string input, string output, int resolution, int crf)
    {
        var r = resolution.ToString(CultureInfo.InvariantCulture);
        var scale = $"scale='if(gte(iw,ih),-2,trunc(min({r},iw)/2)*2)':'if(gte(iw,ih),trunc(min({r},ih)/2)*2,-2)'";
        return
        [
            "-hide_banner", "-nostdin", "-y", "-i", input,
            "-map", "0:v:0", "-map", "0:a:0?",
            "-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(CultureInfo.InvariantCulture),
            "-vf", scale, "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k",
            "-movflags", "+faststart", output,
        ];
    }

    /// <summary>Runs one compression; returns null on success, else what went wrong (the end of ffmpeg's output).</summary>
    public async Task<string?> CompressAsync(string input, string output, int resolution, int crf, CancellationToken ct)
    {
        var (exitCode, log) = await RunAsync(Arguments(input, output, resolution, crf), Timeout, ct);
        return exitCode == 0 ? null : $"ffmpeg exited with {exitCode}: {log}";
    }

    private async Task<FfmpegInfo> ProbeAsync()
    {
        if (_path is null)
        {
            return new FfmpegInfo(false, false, null, null, null);
        }

        try
        {
            var (exitCode, log) = await RunAsync(["-hide_banner", "-version"], TimeSpan.FromSeconds(15), CancellationToken.None);
            var first = log.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
            var copyright = first.IndexOf(" Copyright", StringComparison.Ordinal);
            var version = copyright > 0 ? first[..copyright] : first;
            return exitCode == 0
                ? new FfmpegInfo(true, true, _path, version, null)
                : new FfmpegInfo(true, false, _path, null, $"ffmpeg -version exited with {exitCode}.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or TimeoutException)
        {
            _logger.LogWarning(ex, "FFMPEG_PATH {Path} could not be started.", _path);
            return new FfmpegInfo(true, false, _path, null, $"FFMPEG_PATH could not be started ({ex.GetType().Name}).");
        }
    }

    /// <summary>Runs ffmpeg; returns the exit code and the last part of its output (it writes everything to stderr).</summary>
    private async Task<(int ExitCode, string Log)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(_path!)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        var tail = new Queue<string>();
        void Keep(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > 20)
                {
                    tail.Dequeue();
                }
            }
        }

        process.ErrorDataReceived += (_, e) => Keep(e.Data);
        process.OutputDataReceived += (_, e) => Keep(e.Data);
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"ffmpeg ran longer than {timeout}.");
        }

        lock (tail)
        {
            return (process.ExitCode, string.Join('\n', tail));
        }
    }
}

/// <summary>Wakes <see cref="VideoOptimizationService"/> when an upload queued a video (it also looks every minute).</summary>
public sealed class VideoOptimizationQueue
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled.
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Compresses queued videos one at a time in the background (.docs/features/media-storage.md#optimization): a
/// <see cref="MediaOptimization.Pending"/> file is claimed (<see cref="MediaOptimization.Processing"/>, so two instances
/// never take the same one), copied to a temporary file, compressed by <see cref="Ffmpeg"/>, and - when the result is
/// smaller - stored under a new key that replaces the original. A run that never finished (the app stopped) is retried
/// after <see cref="StaleAfter"/>. With compression off or no ffmpeg, queued files go back to "as uploaded".
/// </summary>
public sealed class VideoOptimizationService : BackgroundService
{
    public static readonly TimeSpan StaleAfter = Ffmpeg.Timeout + TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly VideoOptimizationQueue _queue;
    private readonly Ffmpeg _ffmpeg;
    private readonly ILogger<VideoOptimizationService> _logger;

    public VideoOptimizationService(IServiceProvider services, VideoOptimizationQueue queue, Ffmpeg ffmpeg, ILogger<VideoOptimizationService> logger)
    {
        _services = services;
        _queue = queue;
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Work through the queue, then wait for the next upload (or the next minute).
                while (await RunNextAsync(stoppingToken))
                {
                }

                await _queue.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database may not exist yet (pre-install); log and look again later.
                _logger.LogWarning(ex, "Video compression check failed; will retry.");
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Compresses the oldest queued video; false when there was none.</summary>
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
        var now = DateTime.UtcNow;
        var stale = now - StaleAfter;
        await db.MediaFiles
            .Where(m => m.Optimization == MediaOptimization.Processing && m.OptimizationStartedDate < stale)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Optimization, MediaOptimization.Pending), ct);

        var settings = await scope.ServiceProvider.GetRequiredService<MediaSettingsService>().GetAsync(ct);
        if (!settings.CompressVideos || !(await _ffmpeg.GetInfoAsync()).Available)
        {
            await db.MediaFiles
                .Where(m => m.Optimization == MediaOptimization.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Optimization, MediaOptimization.None), ct);
            return false;
        }

        var next = await db.MediaFiles.AsNoTracking()
            .Where(m => m.Optimization == MediaOptimization.Pending)
            .OrderBy(m => m.UploadedDate)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (next is not { } id)
        {
            return false;
        }

        var claimed = await db.MediaFiles
            .Where(m => m.Id == id && m.Optimization == MediaOptimization.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Optimization, MediaOptimization.Processing)
                .SetProperty(m => m.OptimizationStartedDate, now), ct);
        if (claimed == 1)
        {
            await CompressAsync(db, scope.ServiceProvider.GetRequiredService<IFileStorage>(), id, settings, ct);
        }

        return true;
    }

    private async Task CompressAsync(DotNetForgeDbContext db, IFileStorage storage, Guid id, MediaSettings settings, CancellationToken ct)
    {
        var file = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (file is null)
        {
            return;
        }

        var input = Path.Combine(Path.GetTempPath(), $"dotnetforge-video-{id:N}-in.mp4");
        var output = Path.Combine(Path.GetTempPath(), $"dotnetforge-video-{id:N}-out.mp4");
        try
        {
            await using (var stored = await storage.OpenReadAsync(file.RelativePath, ct))
            {
                if (stored is null)
                {
                    await SetStateAsync(db, id, MediaOptimization.Failed, ct);
                    _logger.LogWarning("Video {MediaId}: the stored object {StorageKey} is missing.", id, file.RelativePath);
                    return;
                }

                await using var copy = File.Create(input);
                await stored.Content.CopyToAsync(copy, ct);
            }

            var problem = await _ffmpeg.CompressAsync(input, output, settings.VideoResolution, settings.VideoCrf, ct);
            if (problem is not null)
            {
                await SetStateAsync(db, id, MediaOptimization.Failed, ct);
                _logger.LogWarning("Video {MediaId} ({FileName}) could not be compressed; it stays as uploaded. {Problem}", id, file.FileName, problem);
                return;
            }

            var size = new FileInfo(output).Length;
            if (size == 0 || size >= file.SizeBytes)
            {
                await SetStateAsync(db, id, MediaOptimization.None, ct);
                return;
            }

            var now = DateTime.UtcNow;
            var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.mp4";
            await using (var compressed = File.OpenRead(output))
            {
                await storage.SaveAsync(key, compressed, "video/mp4", ct);
            }

            // The row may have changed while ffmpeg ran (moved, renamed, deleted): update only what compression owns.
            var row = await db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (row is null)
            {
                await storage.DeleteAsync(key, CancellationToken.None);
                return;
            }

            var oldKey = row.RelativePath;
            row.OriginalSizeBytes = row.SizeBytes;
            row.SizeBytes = size;
            row.RelativePath = key;
            row.ContentType = "video/mp4";
            row.Optimization = MediaOptimization.Optimized;
            await db.SaveChangesAsync(ct);
            await TryDeleteAsync(storage, oldKey);
            _logger.LogInformation("Video {MediaId} ({FileName}) compressed from {Before} to {After} bytes.", id, row.FileName, row.OriginalSizeBytes, size);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Video {MediaId} could not be compressed; it stays as uploaded.", id);
            await SetStateAsync(db, id, MediaOptimization.Failed, CancellationToken.None);
        }
        finally
        {
            TryDelete(input);
            TryDelete(output);
        }
    }

    private static Task SetStateAsync(DotNetForgeDbContext db, Guid id, MediaOptimization state, CancellationToken ct) =>
        db.MediaFiles.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.Optimization, state), ct);

    private async Task TryDeleteAsync(IFileStorage storage, string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete the uncompressed video {StorageKey}; it is now orphaned.", key);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temporary file; the OS cleans its temp folder.
        }
    }
}
