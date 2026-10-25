#:property TargetFramework=net10.0
#:property PublishAot=false
#:property Nullable=enable

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

const string Marker = "<!-- sourcegen-ioc-generator-benchmark -->";

var commentDirectory = RequiredEnvironmentVariable("COMMENT_DIR");
var headSha = RequiredEnvironmentVariable("HEAD_SHA");
var headRepository = Environment.GetEnvironmentVariable("HEAD_REPOSITORY") ?? "";
var runUrl = RequiredEnvironmentVariable("RUN_URL");
var token = RequiredEnvironmentVariable("GITHUB_TOKEN");
var (owner, repository) = ParseRepository(RequiredEnvironmentVariable("GITHUB_REPOSITORY"));
var pullRequestNumber = ReadPullRequestNumber(Path.Combine(commentDirectory, "pr-number.txt"));

using var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SourceGen-Benchmark-Commenter", "1.0"));
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

var pullRequestPath = $"repos/{owner}/{repository}/pulls/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}";
using(var pullRequest = await GetJsonAsync(client, pullRequestPath))
{
    var head = pullRequest.RootElement.GetProperty("head");
    var pullRequestHeadSha = head.GetProperty("sha").GetString();
    var pullRequestHeadRepository = head.TryGetProperty("repo", out var pullRequestHead)
        && pullRequestHead.ValueKind is JsonValueKind.Object
        && pullRequestHead.TryGetProperty("full_name", out var fullName)
            ? fullName.GetString()
            : null;

    if(!string.Equals(pullRequestHeadSha, headSha, StringComparison.Ordinal)
        || !string.Equals(pullRequestHeadRepository, headRepository, StringComparison.Ordinal))
    {
        WriteCommand("notice", $"Pull request #{pullRequestNumber} is not at {headSha} from {headRepository}; skipping the report.");
        return 0;
    }
}

var report = (await File.ReadAllTextAsync(Path.Combine(commentDirectory, "comparison.md"))).Trim();
const int reportLimit = 60000;
if(report.Length > reportLimit)
{
    report = $"{report[..reportLimit]}\n\n_The report was truncated. See the workflow run for the full results._";
}

var body = $"{Marker}\n{report}\n\n[Workflow run]({runUrl}) for {headSha}.";
try
{
    var existingCommentId = await FindExistingCommentAsync(client, owner, repository, pullRequestNumber);
    var endpoint = existingCommentId is { } commentId
        ? $"repos/{owner}/{repository}/issues/comments/{commentId.ToString(CultureInfo.InvariantCulture)}"
        : $"repos/{owner}/{repository}/issues/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}/comments";
    using var content = new StringContent(JsonSerializer.Serialize(new { body }), Encoding.UTF8, "application/json");
    using var request = new HttpRequestMessage(existingCommentId is null ? HttpMethod.Post : HttpMethod.Patch, endpoint)
    {
        Content = content,
    };
    using var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
}
catch(HttpRequestException exception) when(exception.StatusCode is HttpStatusCode.Forbidden)
{
    WriteCommand("warning", $"Could not comment on pull request #{pullRequestNumber}: {exception.Message}");
}

return 0;

static string RequiredEnvironmentVariable(string name)
    => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"The '{name}' environment variable is required.");

static (string Owner, string Repository) ParseRepository(string repository)
{
    var parts = repository.Split('/');
    return parts is [var owner, var name] && owner.Length > 0 && name.Length > 0
        ? (owner, name)
        : throw new FormatException($"The GITHUB_REPOSITORY value '{repository}' is invalid.");
}

static long ReadPullRequestNumber(string path)
{
    var number = File.ReadAllText(path).Trim();
    var isValid = number.Length is > 0 and <= 10
        && number[0] is >= '1' and <= '9'
        && number.All(static character => character is >= '0' and <= '9');
    return isValid && long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
        ? result
        : throw new FormatException("The benchmark artifact does not contain a valid pull request number.");
}

static async Task<JsonDocument> GetJsonAsync(HttpClient client, string endpoint)
{
    using var response = await client.GetAsync(endpoint);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync();
    return await JsonDocument.ParseAsync(stream);
}

static async Task<long?> FindExistingCommentAsync(HttpClient client, string owner, string repository, long pullRequestNumber)
{
    for(var page = 1; ; page++)
    {
        var endpoint = $"repos/{owner}/{repository}/issues/{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}/comments?per_page=100&page={page.ToString(CultureInfo.InvariantCulture)}";
        using var comments = await GetJsonAsync(client, endpoint);
        var entries = comments.RootElement;
        foreach(var comment in entries.EnumerateArray())
        {
            var isActionsBot = comment.TryGetProperty("user", out var user)
                && user.TryGetProperty("login", out var login)
                && login.GetString() is "github-actions[bot]";
            var hasMarker = comment.TryGetProperty("body", out var commentBody)
                && commentBody.ValueKind is JsonValueKind.String
                && commentBody.GetString()?.StartsWith(Marker, StringComparison.Ordinal) is true;
            if(isActionsBot && hasMarker)
            {
                return comment.GetProperty("id").GetInt64();
            }
        }

        if(entries.GetArrayLength() < 100)
        {
            return null;
        }
    }
}

static void WriteCommand(string level, string message)
{
    var escapedMessage = message.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
    Console.WriteLine($"::{level}::{escapedMessage}");
}
