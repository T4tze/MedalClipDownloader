using System;
using System.IO;
using System.Threading;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Threading.Tasks;

class Program
{
    static string X_Authentication = "";
    static string downloadDirectory = "Clips"; // Default download directory
    static long totalBytesDownloaded = 0;
    static double totalElapsedTime = 0;

    static async Task Main(string[] args)
    {
        Console.Clear();
        Console.Title = "Medal.tv Clip Downloader";
        bool exitProgram = false;

        while (!exitProgram)
        {
            string[] options = new string[] { "[1] Download All Profile Clips", "[2] Download a Clip", "[3] Set Download Directory", "[4] Exit" };
            PrintMenu(options);
            string choice = Console.ReadKey().KeyChar.ToString();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    await DownloadAllClips();
                    break;
                case "2":
                    await DownloadClip();
                    break;
                case "3":
                    SetDownloadDirectory();
                    break;
                case "4":
                    exitProgram = true;
                    Console.WriteLine("Exiting the program. Goodbye!");
                    break;
                default:
                    Console.WriteLine("Invalid choice. Please select a valid option.");
                    break;
            }
        }
    }

    static void SetDownloadDirectory()
    {
        Console.Clear();
        PrintMenu(new string[] { "Enter the directory where clips should be downloaded:" });
        string directory = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(directory))
        {
            downloadDirectory = directory;
            Console.WriteLine($"Download directory set to: {downloadDirectory}");
        }
        else
        {
            Console.WriteLine("Invalid directory. Using default: Clips");
        }
        Thread.Sleep(1000);
    }

    static async Task DownloadAllClips()
    {
        while (true)
        {
            Console.Clear();
            PrintMenu(new string[] { "Enter the profile link (or type 'back' to return to the main menu):" });
            string profileLink = Console.ReadLine();
            if (profileLink.ToLower() == "back") return;

            PrintMenu(new string[] { "Enter your X-Authentication Token (or type 'back' to return to the main menu):" });
            X_Authentication = Console.ReadLine();
            if (X_Authentication.ToLower() == "back") return;

            if (string.IsNullOrWhiteSpace(profileLink) || string.IsNullOrWhiteSpace(X_Authentication))
            {
                Console.WriteLine("Profile link or authentication token cannot be empty. Please try again.");
                continue;
            }

            try
            {
                await ProcessProfileClips(profileLink);
                break;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                Console.WriteLine("An error occurred. Please check your inputs and try again.");
            }
        }
    }

    static async Task ProcessProfileClips(string profileLink)
    {
        var client = new HttpClient();
        string html = await client.GetStringAsync(profileLink);

        var regex = new Regex(@"(?<=""userId"":"")\d+");
        var match = regex.Match(html);

        if (!match.Success)
        {
            throw new Exception("Invalid profile link or user ID not found.");
        }

        string userId = match.Value;
        long offset = 0;
        bool finished = false;

        while (!finished)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                Headers =
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0" },
                    { "X-Authentication", X_Authentication },
                },
            };
            request.RequestUri = new Uri($"https://medal.tv/api/content?userId={userId}&offset={offset}&sortBy=publishedAt&sortDirection=DESC");

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to fetch clips. HTTP Status: {response.StatusCode}. Reason: {response.ReasonPhrase}");
            }

            var body = await response.Content.ReadAsStringAsync();
            var json = JArray.Parse(body);

            if (json.Count == 0)
            {
                finished = true;
                break;
            }

            Console.WriteLine($"\nClips remaining in batch: {json.Count}");

            foreach (var clip in json)
            {
                var contentUrl1080p = clip["contentUrl1080p"]?.ToString();
                var contentUrl720p = clip["contentUrl720p"]?.ToString();
                var contentUrl480p = clip["contentUrl480p"]?.ToString();
                var videoLengthSeconds = clip["videoLengthSeconds"]?.ToString();

                string contentId = clip["contentId"]?.ToString();
                string contentTitle = clip["contentTitle"]?.ToString().Replace(" ", "_").Replace(@"""", "");

                if (string.IsNullOrWhiteSpace(contentTitle) || string.IsNullOrWhiteSpace(contentId))
                {
                    Console.WriteLine("Clip metadata is incomplete. Skipping...");
                    continue;
                }

                if (contentTitle.Contains("Instant_Screenshot") && videoLengthSeconds == "1")
                {
                    contentUrl1080p = clip["thumbnail1080p"]?.ToString();
                    contentUrl720p = clip["thumbnail720p"]?.ToString();
                    contentUrl480p = clip["thumbnail480p"]?.ToString();
                }

                if (await DownloadURL(contentUrl1080p, contentTitle, contentId, "1080p", offset + 1, json.Count))
                    continue;
                else if (await DownloadURL(contentUrl720p, contentTitle, contentId, "720p", offset + 1, json.Count))
                    continue;
                else if (await DownloadURL(contentUrl480p, contentTitle, contentId, "480p", offset + 1, json.Count))
                    continue;
            }

            offset += json.Count;
        }
    }

    static async Task DownloadClip()
    {
        while (true)
        {
            Console.Clear();
            PrintMenu(new string[] { "Enter the clip link (or type 'back' to return to the main menu):" });
            string clipLink = Console.ReadLine();
            if (clipLink.ToLower() == "back") return;

            if (string.IsNullOrWhiteSpace(clipLink))
            {
                Console.WriteLine("Clip link cannot be empty. Please try again.");
                continue;
            }

            try
            {
                await ProcessClipDownload(clipLink);
                break;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                Console.WriteLine("An error occurred. Please check your clip link and try again.");
            }
        }
    }

    static async Task ProcessClipDownload(string clipLink)
    {
        var client = new HttpClient();
        string html = await client.GetStringAsync(clipLink);

        var regex = new Regex(@"(?<=var hydrationData=)[\s\S]*?(?=</script>)");
        var match = regex.Match(html);

        if (!match.Success)
        {
            throw new Exception("Invalid clip link or hydration data not found.");
        }

        regex = new Regex(@"(?<=""contentId"":"")[\s\S]*?(?="")");
        var cidmatch = regex.Match(match.Value);

        if (!cidmatch.Success)
        {
            throw new Exception("Clip ID not found in the provided link.");
        }

        string contentId = cidmatch.Value;
        string json = match.Value;
        JObject jjson = JObject.Parse(json);
        JObject clips = JObject.Parse(jjson["clips"]?.ToString() ?? "{}");
        JObject clip = JObject.Parse(clips[contentId]?.ToString() ?? "{}");

        string contentUrl1080p = clip["contentUrl1080p"]?.ToString();
        string contentUrl720p = clip["contentUrl720p"]?.ToString();
        string contentUrl480p = clip["contentUrl480p"]?.ToString();
        string contentTitle = clip["contentTitle"]?.ToString().Replace(" ", "_").Replace(@"""", "");

        if (string.IsNullOrWhiteSpace(contentTitle) || string.IsNullOrWhiteSpace(contentId))
        {
            throw new Exception("Clip metadata is incomplete. Cannot proceed with download.");
        }

        if (await DownloadURL(contentUrl1080p, contentTitle, contentId, "1080p", 1, 1)) return;
        if (await DownloadURL(contentUrl720p, contentTitle, contentId, "720p", 1, 1)) return;
        if (await DownloadURL(contentUrl480p, contentTitle, contentId, "480p", 1, 1)) return;

        throw new Exception("Failed to download the clip in all available qualities.");
    }

    static async Task<bool> DownloadURL(string url, string contentTitle, string contentId, string quality, long index, long max)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine($"URL for {contentTitle} ({quality}) is invalid. Skipping...");
            return false;
        }

        if (!Directory.Exists(downloadDirectory))
            Directory.CreateDirectory(downloadDirectory);

        try
        {
            string fileextension = url.Split('.').Last();
            fileextension = fileextension.Substring(0, fileextension.IndexOf("?"));
            string filePath = $"{downloadDirectory}/{contentTitle}_{contentId}_{quality}.{fileextension}";

            var client = new HttpClient();
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to download {contentTitle} ({quality}). HTTP Status: {response.StatusCode}. Reason: {response.ReasonPhrase}");
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                var progress = new Progress<(long bytesDownloaded, double speed)>(report =>
                {
                    var (bytesDownloaded, speed) = report;
                    if (canReportProgress)
                    {
                        Console.Write($"\rDownloading: {contentTitle} ({quality}) to {filePath} - {bytesDownloaded}/{totalBytes} bytes ({(bytesDownloaded / (double)totalBytes) * 100:0.00}%) - Speed: {speed:0.00} MB/s                          ");
                    }
                    else
                    {
                        Console.Write($"\rDownloading: {contentTitle} ({quality}) to {filePath} - {bytesDownloaded} bytes - Speed: {speed:0.00} MB/s                                                                                              ");
                    }
                });

                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var stopwatch = Stopwatch.StartNew();
                    await CopyToAsync(stream, fileStream, 81920, progress);
                    stopwatch.Stop();

                    totalBytesDownloaded += totalBytes;
                    totalElapsedTime += stopwatch.Elapsed.TotalSeconds;
                }
            }
            Console.WriteLine($"\nDownloaded Successfully: {contentTitle} ({quality}) to {filePath}");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"\nFailed to download {contentTitle} ({quality}). Reason: {e.Message}");
            return false;
        }
    }

    static async Task CopyToAsync(Stream source, Stream destination, int bufferSize, IProgress<(long, double)> progress = null)
    {
        var buffer = new byte[bufferSize];
        long totalBytesRead = 0;
        int bytesRead;
        var stopwatch = Stopwatch.StartNew();

        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) != 0)
        {
            await destination.WriteAsync(buffer, 0, bytesRead);
            totalBytesRead += bytesRead;
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            var speed = (totalBytesRead / 1024d / 1024d) / elapsedSeconds;
            progress?.Report((totalBytesRead, speed));
        }
    }

    static void PrintMenu(string[] options)
    {
        string menu = "";
        int longestoption = 0;

        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].Length > longestoption)
                longestoption = options[i].Length;
        }
        menu += "┌";
        for (int i = 0; i < longestoption + 2; i++)
        {
            menu += "─";
        }
        menu += "┐\n";
        for (int i = 0; i < options.Length; i++)
        {
            menu += "│ ";
            menu += options[i];
            for (int j = 0; j < longestoption - options[i].Length; j++)
            {
                menu += " ";
            }
            menu += " │\n";
        }
        menu += "├";
        for (int i = 0; i < longestoption + 2; i++)
        {
            menu += "─";
        }
        menu += "┘\n";
        menu += "└[»] ";
        Console.Write(menu);
    }
}