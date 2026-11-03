using System.Net;
using System.Text;
using Captly.Core.Shell;
using Captly.Updates;

namespace Captly.Core.Tests;

public class UpdateCheckerTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static async Task<(UpdateCheck Result, FakeHandler Handler)> Check(HttpStatusCode status, string body, string current = "0.1.0")
    {
        var handler = new FakeHandler(status, body);
        using var http = new HttpClient(handler);
        var result = await new GitHubUpdateChecker(http, "owner/repo", current).CheckAsync(CancellationToken.None);
        return (result, handler);
    }

    [Fact]
    public async Task NewerRelease_IsAvailable_WithItsPage()
    {
        var (result, handler) = await Check(HttpStatusCode.OK, """{"tag_name":"v0.2.0","html_url":"https://github.com/owner/repo/releases/tag/v0.2.0"}""");

        Assert.Equal(new UpdateCheck(UpdateOutcome.Available, "0.2.0", "https://github.com/owner/repo/releases/tag/v0.2.0"), result);
        Assert.Equal("https://api.github.com/repos/owner/repo/releases/latest", handler.Request!.RequestUri!.ToString());
        Assert.Contains("Captly", handler.Request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task SameOrOlderRelease_IsUpToDate()
    {
        Assert.Equal(UpdateOutcome.UpToDate, (await Check(HttpStatusCode.OK, """{"tag_name":"v0.1.0"}""")).Result.Outcome);
        Assert.Equal(UpdateOutcome.UpToDate, (await Check(HttpStatusCode.OK, """{"tag_name":"0.0.9"}""")).Result.Outcome);
    }

    [Fact]
    public async Task NoReleasesYet_IsUpToDate() =>
        Assert.Equal(UpdateOutcome.UpToDate, (await Check(HttpStatusCode.NotFound, """{"message":"Not Found"}""")).Result.Outcome);

    [Fact]
    public async Task ServerErrorsAndBadTags_Fail()
    {
        Assert.Equal(UpdateOutcome.Failed, (await Check(HttpStatusCode.Forbidden, "{}")).Result.Outcome);
        Assert.Equal(UpdateOutcome.Failed, (await Check(HttpStatusCode.OK, """{"tag_name":"latest"}""")).Result.Outcome);
    }

    [Theory]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("1.0.0-beta.1", "1.0.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("0.1.0+build.5", "0.1.0", 0)]
    public void Versions_CompareAsSemVer(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(SemanticVersion.Parse(a)!.CompareTo(SemanticVersion.Parse(b))));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2.3.4")]
    [InlineData("1.-2.0")]
    public void BadVersions_DoNotParse(string? text) => Assert.Null(SemanticVersion.Parse(text));
}
