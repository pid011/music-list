using Google.Apis.Services;
using Google.Apis.YouTube.v3;

using MusicList;

using Serilog;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

const string ApikeyFileName = "api-key.txt";
const string ExtractFileName = "playlist.csv";
const string ChannelId = "UC7gy0ee1jeNO11HievGQJzA";
string[] IncludeKeywords = { "OST" };
string[] ExcludeKeywords = { "인터뷰", "Inst" };

static string LoadApiKey()
{
    var directory = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
    var filePath = Path.Combine(directory, ApikeyFileName);

    return File.ReadAllText(filePath).Trim();
}

static async Task<List<string>> RequestVideos(YouTubeService service, IEnumerable<string> includes, IEnumerable<string> excludes)
{
    var request = service.Search.List("id,snippet");
    request.ChannelId = ChannelId;
    request.Type = "video";
    request.MaxResults = 50;
    request.Order = SearchResource.ListRequest.OrderEnum.Date;

    var videos = new List<string>();
    while (true)
    {
        var (items, nextPageToken) = await RequestAsync(request, includes, excludes);
        videos.AddRange(items);

        if (nextPageToken is null) break;
        request.PageToken = nextPageToken;

        await Task.Delay(50);
    }

    return videos;

    static async Task<(IEnumerable<string> Items, string NextPageToken)> RequestAsync(SearchResource.ListRequest request, IEnumerable<string> includes, IEnumerable<string> excludes)
    {
        var response = await request.ExecuteAsync();

        var items = response.Items
            .Where(item => includes.FirstOrDefault(k => item.Snippet.Title.Contains(k)) is not null)
            .Where(item => excludes.FirstOrDefault(k => item.Snippet.Title.Contains(k)) is null)
            .Select(item => item.Id.VideoId);

        return (items, response.NextPageToken);
    }
}

static async Task<List<VideoDetail>> RequestVideoDetails(YouTubeService service, IReadOnlyList<string> videos)
{
    var request = service.Videos.List("id,snippet,contentDetails");
    request.MaxResults = 50;

    var videoDetails = new List<VideoDetail>();
    int startIndex = 0;
    while (true)
    {
        startIndex = await Request(request, videos, videoDetails, startIndex);
        if (startIndex >= videos.Count) break;

        //await Task.Delay(100);
    }
    return videoDetails;

    async Task<int> Request(
        VideosResource.ListRequest request, IReadOnlyList<string> videos, IList<VideoDetail> details, int startIndex)
    {
        var index = startIndex;
        var inputId = new StringBuilder();
        for (int i = 0; i < 50; i++)
        {
            if (index >= videos.Count) break;
            inputId.Append($",{videos[index]}");
            ++index;
        }

        if (inputId.Length == 0) return index;

        var inputIdStr = inputId.Remove(0, 1).ToString();
        request.Id = inputIdStr;
        var response = await request.ExecuteAsync();

        foreach (var item in response.Items)
        {
            var duration = ConvertDuration(item.ContentDetails.Duration);

            var detail = new VideoDetail
            {
                Title = item.Snippet.Title,
                Id = item.Id,
                ThumbnailUrl = item.Snippet.Thumbnails.Default__.Url,
                Duration = duration
            };
            details.Add(detail);
        }
        return index;
    }
}

static async Task ExtractToCsv(IReadOnlyList<VideoDetail> videoDetails, string path)
{
    using FileStream fs = new(path, FileMode.Create, FileAccess.Write);
    using StreamWriter writer = new(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // utf-8-bom

    StringBuilder builder = new StringBuilder()
        .AppendJoin(',', "title", "id", "thumbnailUrl", "duration")
        .AppendLine();

    for (int i = 0; i < videoDetails.Count; i++)
    {
        var detail = videoDetails[i];
        builder = builder
            .AppendJoin(',',
                GetCSVString(detail.Title),
                GetCSVString(detail.Id),
                GetCSVString(detail.ThumbnailUrl),
                GetCSVString($"{detail.Duration.Minutes}:{detail.Duration.Seconds}"));

        if (i < videoDetails.Count - 1) builder.AppendLine();
    }

    await writer.WriteAsync(builder);

    static string GetCSVString<T>(T obj) => obj is null ? "\"---\"" : $"\"{obj}\"";
}

static TimeSpan ConvertDuration(string duration)
{
    var capture = Regex.Match(duration, "^PT(?:([0-9]+)M)?(?:([0-9]+)S)?$");
    if (capture.Groups.Count != 3)
    {
        throw new ArgumentException($"Duration 파싱에 실패했습니다. -> [{duration}]");
    }

    int min = int.Parse(capture.Groups[1].Value == string.Empty ? "0" : capture.Groups[1].Value);
    int sec = int.Parse(capture.Groups[2].Value == string.Empty ? "0" : capture.Groups[2].Value);

    return new TimeSpan(0, min, sec);
}

// -------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var apiKey = LoadApiKey();

var youtubeService = new YouTubeService(new BaseClientService.Initializer()
{
    ApiKey = apiKey,
    ApplicationName = "NecordMusicList"
});

Log.Information("Requesting...");
var videos = await RequestVideos(youtubeService, IncludeKeywords, ExcludeKeywords);
// var videos = new List<string> { "SckyzZhiZko" }; // TEST
var videoDetails = await RequestVideoDetails(youtubeService, videos);
Log.Information($"Video Count: {videoDetails.Count}");

await ExtractToCsv(videoDetails, ExtractFileName);
Log.Information("Done.");
Log.Information($"Extracted csv file: {ExtractFileName}");

//ConvertDuration("PT123M12S");
//ConvertDuration("PT12S");
//ConvertDuration("PT123M");
