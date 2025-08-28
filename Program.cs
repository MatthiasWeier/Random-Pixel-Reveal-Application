using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

// --- IMPORTANT FOR .NET 5/6/7/8+ ---
// This code uses System.Drawing classes. To resolve compilation errors like "'Bitmap' could not be found",
// you must add the System.Drawing.Common NuGet package to your project.
// You can do this by right-clicking your project in Visual Studio -> "Manage NuGet Packages..." -> Search for "System.Drawing.Common" and install it.

namespace PixelRevealVideoGenerator;

internal class Program {
  // --- Reveal Configuration ---
  private const int FollowersThisSession = 1; // Set the number of *new* followers for this run.
  private const int PixelsPerFollower = 5000; // Set how many pixels each follower reveals.

  // --- Video/Audio Configuration ---
  private const int VideoDurationSeconds = 10;
  private const int FramesPerSecond = 30;
  private const int MaxTotalBlipsInVideo = 40; // To avoid tinnitus, this caps the total number of sounds.
  private const string OutputDirectory = "frames";
  private const string OutputVideoFileName = "output.mp4";
  private const string AudioTrackFileName = "audio.wav"; // Temporary sound file
  private const string FollowerTextFileName = "follower_expression.txt"; // Temp file for the follower counter
  private const string PercentTextFileName = "percent_expression.txt"; // Temp file for the percentage counter
  private const int HoldLastFrameSeconds = 30; // How many seconds to freeze the final frame

  // --- Paths (CHANGE THESE) ---
  private const string SourceImageFileName = @"C:\Program Files (x86)\Steam\userdata\313669169\760\remote\730\screenshots\20250704215901_1.jpg";
  private const string FfmpegExecutablePath = @"C:\Users\Matthias\source\repos\YoutubeAutomation2\YoutubeAutomation\bin\Debug\net80\Data\ffmpeg\ffmpeg.exe";

  // --- IMPORTANT: Font Path for Counter Text ---
  private const string FontFilePath = "C:/Windows/Fonts/arial.ttf";

  private static Bitmap? _sourceImage;
  private static Bitmap? _canvasImage;
  private static readonly List<Point> _pixelCoordinates = [];
  private static Random _rng = new();
  private static int _previousFollowers; // Store previous followers for use in CreateVideo

  private static void Main(string[] args) {
    Console.WriteLine("Starting video frame generation...");

    Program._previousFollowers = Program.LoadPreviousFollowerCount();
    var totalFollowers = Program._previousFollowers + Program.FollowersThisSession;
    Console.WriteLine($"Previous followers: {Program._previousFollowers}. New followers this session: {Program.FollowersThisSession}. Total: {totalFollowers}.");

    if (!Program.SetupGenerator()) {
      Console.WriteLine("\nPress any key to exit.");
      Console.ReadKey();
      return;
    }

    Program.GenerateFrames(totalFollowers);
    Console.WriteLine("\nFrame generation complete! Now creating assets...");

    if (Program.GenerateBlipAudioTrack(Program.FollowersThisSession) && Program.CreateVideo()) {
      Console.WriteLine("Cleaning up temporary files...");
      Directory.Delete(Program.OutputDirectory, true);
      File.Delete(Program.AudioTrackFileName);
      if (File.Exists(Program.FollowerTextFileName)) File.Delete(Program.FollowerTextFileName);
      if (File.Exists(Program.PercentTextFileName)) File.Delete(Program.PercentTextFileName);
      Console.WriteLine("Cleanup complete!");

      Program.SaveCurrentFollowerCount(totalFollowers);
    }

    Console.WriteLine("\nAll tasks finished. Press any key to exit.");
    Console.ReadKey();
  }

  private static bool SetupGenerator() {
    if (!File.Exists(Program.FfmpegExecutablePath)) {
      Console.WriteLine($"Error: FFmpeg not found at '{Program.FfmpegExecutablePath}'. Please check the path.");
      return false;
    }

    if (!File.Exists(Program.FontFilePath)) {
      Console.WriteLine("\n--- WARNING! ---");
      Console.WriteLine($"Font file not found at '{Program.FontFilePath}'.");
      Console.WriteLine("The text counter in the video will NOT be generated.");
      Console.WriteLine("Please update the 'FontFilePath' constant in the code.");
      Console.WriteLine("Continuing without text overlay...");
    }

    if (File.Exists(Program.OutputVideoFileName)) {
      Console.WriteLine("Deleting existing output.mp4...");
      File.Delete(Program.OutputVideoFileName);
    }

    if (File.Exists(Program.AudioTrackFileName)) File.Delete(Program.AudioTrackFileName);
    if (File.Exists(Program.FollowerTextFileName)) File.Delete(Program.FollowerTextFileName);
    if (File.Exists(Program.PercentTextFileName)) File.Delete(Program.PercentTextFileName);

    try {
      if (!File.Exists(Program.SourceImageFileName)) {
        Console.WriteLine($"Error: Source image not found at '{Program.SourceImageFileName}'.");
        return false;
      }

      Program._sourceImage = new(Program.SourceImageFileName);
    } catch (Exception ex) {
      Console.WriteLine("Error: Failed to load source image. It might be corrupted or in use.");
      Console.WriteLine($"Details: {ex.Message}");
      return false;
    }

    if (Directory.Exists(Program.OutputDirectory))
      Directory.Delete(Program.OutputDirectory, true);
    Directory.CreateDirectory(Program.OutputDirectory);

    var evenWidth = Program._sourceImage.Width % 2 == 0 ? Program._sourceImage.Width : Program._sourceImage.Width - 1;
    var evenHeight = Program._sourceImage.Height % 2 == 0 ? Program._sourceImage.Height : Program._sourceImage.Height - 1;

    if (evenWidth != Program._sourceImage.Width || evenHeight != Program._sourceImage.Height)
      Console.WriteLine($"Note: Image dimensions adjusted for video encoding: {Program._sourceImage.Width}x{Program._sourceImage.Height} -> {evenWidth}x{evenHeight}");

    Program._canvasImage = new(evenWidth, evenHeight);
    using (var g = Graphics.FromImage(Program._canvasImage))
      g.Clear(Color.Black);

    Program._rng = new(Program.SourceImageFileName.GetHashCode());

    for (var y = 0; y < evenHeight; y++)
    for (var x = 0; x < evenWidth; x++)
      Program._pixelCoordinates.Add(new(x, y));

    Program.Shuffle(Program._pixelCoordinates);
    return true;
  }

  private static void GenerateFrames(int totalFollowerCount) {
    if (Program._canvasImage == null || Program._sourceImage == null) return;

    var totalPixelsInImage = Program._pixelCoordinates.Count;
    var totalPixelsToReveal = Math.Min(totalFollowerCount * Program.PixelsPerFollower, totalPixelsInImage);

    Console.WriteLine($"Revealing {totalPixelsToReveal} of {totalPixelsInImage} total pixels.");

    const int totalFrames = Program.VideoDurationSeconds * Program.FramesPerSecond;
    var pixelsRevealedSoFar = 0;

    for (var frameNumber = 0; frameNumber < totalFrames; frameNumber++) {
      var targetPixelCount = (int)((double)(frameNumber + 1) / totalFrames * totalPixelsToReveal);
      var pixelsToRevealNow = targetPixelCount - pixelsRevealedSoFar;

      if (pixelsToRevealNow > 0) {
        for (var i = 0; i < pixelsToRevealNow; i++)
          if (pixelsRevealedSoFar + i < totalPixelsToReveal) {
            var p = Program._pixelCoordinates[pixelsRevealedSoFar + i];
            var pixelColor = Program._sourceImage.GetPixel(p.X, p.Y);
            Program._canvasImage.SetPixel(p.X, p.Y, pixelColor);
          }

        pixelsRevealedSoFar += pixelsToRevealNow;
      }

      var framePath = Path.Combine(Program.OutputDirectory, $"frame_{frameNumber:D4}.png");
      Program._canvasImage.Save(framePath, ImageFormat.Png);
      Console.Write($"\rGenerating frame {frameNumber + 1} of {totalFrames}... ");
    }
  }

  private static bool GenerateBlipAudioTrack(int followerCount) {
    Console.WriteLine("Generating 'coin' sound effect...");
    if (followerCount <= 0) {
      Console.WriteLine("No new followers, generating silent audio.");
      var silentArgs = $"-f lavfi -i anullsrc=r=44100:cl=mono -t {Program.VideoDurationSeconds} -y {Program.AudioTrackFileName}";
      return Program.RunFfmpegProcess(silentArgs, "generate silent audio");
    }

    var totalBlips = Math.Min(followerCount, Program.MaxTotalBlipsInVideo);
    var blipInterval = (double)Program.VideoDurationSeconds / totalBlips;
    const double toneDuration = 0.2;

    var filterBuilder = new StringBuilder();
    var delayedStreamNames = new List<string>();

    var frequencies = new[] { 1046, 1396, 2093 }; // C6, F6, C7

    for (var i = 0; i < totalBlips; i++) {
      var delayMs = (i * blipInterval * 1000).ToString(CultureInfo.InvariantCulture);
      var sin1 = Math.Sin(Program._rng.NextDouble()).ToString(CultureInfo.InvariantCulture);
      var sin2 = Math.Sin(Program._rng.NextDouble()).ToString(CultureInfo.InvariantCulture);
      var sin3 = Math.Sin(Program._rng.NextDouble()).ToString(CultureInfo.InvariantCulture);
      var toneDurationStr = toneDuration.ToString(CultureInfo.InvariantCulture);

      var aevalsrc = $"aevalsrc='0.4*({sin1}*sin({frequencies[0]}*2*PI*t)+{sin2}*sin({frequencies[1]}*2*PI*t)+{sin3}*sin({frequencies[2]}*2*PI*t))*pow(1-mod(t,{toneDurationStr}), 20)':d={toneDurationStr}[t{i}];";

      filterBuilder.Append(aevalsrc);
      filterBuilder.Append($"[t{i}]adelay={delayMs}|{delayMs}[d{i}];");
      delayedStreamNames.Add($"[d{i}]");
    }

    filterBuilder.Append(string.Concat(delayedStreamNames));
    filterBuilder.Append($"amix=inputs={totalBlips}[a]");

    var arguments = @$"-filter_complex ""{filterBuilder}"" -map ""[a]"" -t {Program.VideoDurationSeconds} -y {Program.AudioTrackFileName}";
    return Program.RunFfmpegProcess(arguments, "generate multi-tone audio track");
  }

  private static bool CreateVideo() {
    var videoFilter = new StringBuilder();
    var finalVideoFilterString = "";
    var audioFilterString = ""; // NEW: For padding the audio

    // NEW: Calculate the total output duration
    var totalOutputDuration = VideoDurationSeconds + HoldLastFrameSeconds;

    if (File.Exists(FontFilePath)) {
      var totalFrames = VideoDurationSeconds * FramesPerSecond;

      // --- Follower Counter (logic is unchanged) ---
      var followerCount = $"trunc(((n+1.0)/{totalFrames}) * ({FollowersThisSession} + 0.5))";
      var followerExpr = $"New Followers: %{{eif:min({FollowersThisSession}, {followerCount}):d}}";
      File.WriteAllText(FollowerTextFileName, followerExpr);

      // --- Pixel Counter (logic is unchanged) ---
      var totalFollowers = _previousFollowers + FollowersThisSession;
      var totalPixelsToReveal = Math.Min(totalFollowers * PixelsPerFollower, _pixelCoordinates.Count);
      var pixelCount = $"trunc(((n+1.0)/{totalFrames}) * {totalPixelsToReveal})";
      var percentExpr = $"Total Pixels revealed: %{{eif:{pixelCount}:d}}";
      File.WriteAllText(PercentTextFileName, percentExpr);

      var fontPath = FontFilePath.Replace('\\', '/').Replace(":", "\\:");
      var followerTextPath = Path.GetFullPath(FollowerTextFileName).Replace('\\', '/').Replace(":", "\\:");
      var percentTextPath = Path.GetFullPath(PercentTextFileName).Replace('\\', '/').Replace(":", "\\:");

      // Build the drawtext filter chain
      videoFilter.Append($"drawtext=fontfile='{fontPath}':textfile='{followerTextPath}':reload=1:fontsize=48:fontcolor=white:x=(w-text_w)/2:y=20:box=1:boxcolor=black@0.5:boxborderw=10[txt1];");
      videoFilter.Append($"[txt1]drawtext=fontfile='{fontPath}':textfile='{percentTextPath}':reload=1:fontsize=48:fontcolor=white:x=(w-text_w)/2:y=80:box=1:boxcolor=black@0.5:boxborderw=10");

      // --- NEW: Add the tpad filter to hold the last frame ---
      // We take the existing drawtext filters and append the tpad filter to the end of the chain.
      finalVideoFilterString = $"-vf \"{videoFilter},tpad=stop_mode=clone:stop_duration={HoldLastFrameSeconds}\"";

    } else {
      Console.WriteLine("Skipping text overlay because font file was not found.");
      // --- NEW: If there's no text, the tpad filter is much simpler ---
      finalVideoFilterString = $"-vf \"tpad=stop_mode=clone:stop_duration={HoldLastFrameSeconds}\"";
    }

    // --- NEW: Create an audio filter to pad the audio track with silence ---
    if (HoldLastFrameSeconds > 0) {
      audioFilterString = $"-af apad=pad_dur={HoldLastFrameSeconds}";
    }

    // --- UPDATED: The final FFmpeg command ---
    // It now includes the video and audio filters, and sets the new total duration with -t
    var arguments = @$"-framerate {FramesPerSecond} -i .\{OutputDirectory}\frame_%04d.png -i {AudioTrackFileName} {finalVideoFilterString} {audioFilterString} -t {totalOutputDuration} -c:v libx264 -pix_fmt yuv420p -c:a aac -y {OutputVideoFileName}";

    if (RunFfmpegProcess(arguments, "create the final video")) {
      var fullVideoPath = Path.GetFullPath(OutputVideoFileName);
      Console.WriteLine("\nVideo created successfully!");
      Console.WriteLine($"---> Saved to: {fullVideoPath}");
      return true;
    }

    return false;
  }

  private static bool RunFfmpegProcess(string arguments, string taskDescription) {
    var processStartInfo = new ProcessStartInfo {
      FileName = Program.FfmpegExecutablePath,
      Arguments = arguments,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = new Process { StartInfo = processStartInfo };
    process.Start();
    var errorOutput = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode == 0)
      return true;

    Console.WriteLine($"\nError: FFmpeg failed to {taskDescription}.");
    Console.WriteLine("FFmpeg arguments: " + arguments);
    Console.WriteLine("FFmpeg output:");
    Console.WriteLine(errorOutput);
    return false;
  }

  private static string GetStateFileName() => Path.GetFileName(Program.SourceImageFileName) + ".state";

  private static int LoadPreviousFollowerCount() {
    var stateFile = Program.GetStateFileName();
    if (!File.Exists(stateFile))
      return 0;
    return int.TryParse(File.ReadAllText(stateFile), out var count) ? count : 0;
  }

  private static void SaveCurrentFollowerCount(int totalFollowers) {
    var stateFile = Program.GetStateFileName();
    File.WriteAllText(stateFile, totalFollowers.ToString());
    Console.WriteLine($"Saved current total of {totalFollowers} followers to '{stateFile}'.");
  }

  private static void Shuffle<T>(IList<T> list) {
    var n = list.Count;
    while (n > 1) {
      n--;
      var k = Program._rng.Next(n + 1);
      (list[k], list[n]) = (list[n], list[k]);
    }
  }
}
