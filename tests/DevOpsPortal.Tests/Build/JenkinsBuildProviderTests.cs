using System.Net;
using System.Text;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Build;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Build;

public class JenkinsBuildProviderTests
{
    private static readonly BuildServer Server = new() { Name = "jenkins-main", BaseUrl = "https://jenkins.example.local", ProviderType = BuildProviderType.Jenkins };

    private static JenkinsBuildProvider CreateSut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<JenkinsBuildProvider>.Instance);

    // ------------------------------------------------------------- IsConfigured

    [Fact]
    public void IsConfigured_WithValidBaseUrl_ReturnsTrue()
    {
        var sut = CreateSut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.True(sut.IsConfigured(Server));
    }

    [Fact]
    public void IsConfigured_WithInvalidBaseUrl_ReturnsFalse()
    {
        var sut = CreateSut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.False(sut.IsConfigured(new BuildServer { Name = "bad", BaseUrl = "not-a-url" }));
    }

    // ------------------------------------------------------------- Trigger

    [Fact]
    public async Task TriggerBuildAsync_WhenHostUnreachable_ReturnsFailNeverThrows()
    {
        var sut = new JenkinsBuildProvider(new HttpClient(), NullLogger<JenkinsBuildProvider>.Instance);
        var unreachable = new BuildServer { Name = "unreachable", BaseUrl = "http://127.0.0.1:1" };

        var result = await sut.TriggerBuildAsync(unreachable, new BuildTriggerRequest("sample-app", "main", "abc123", new Dictionary<string, string>()));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task TriggerBuildAsync_WithoutParameters_UsesPlainBuildEndpoint_AndParsesQueueItemFromLocation()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/crumbIssuer/api/json"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            captured = req;
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri("https://jenkins.example.local/queue/item/42/");
            return response;
        });
        var sut = CreateSut(handler);

        var result = await sut.TriggerBuildAsync(Server, new BuildTriggerRequest("sample-app", null, null, new Dictionary<string, string>()));

        Assert.True(result.Success);
        Assert.Equal("42", result.ProviderQueueItemId);
        Assert.NotNull(captured);
        Assert.EndsWith("/job/sample-app/build", captured!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task TriggerBuildAsync_WithParameters_UsesBuildWithParametersEndpoint()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/crumbIssuer/api/json"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            captured = req;
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri("https://jenkins.example.local/queue/item/7/");
            return response;
        });
        var sut = CreateSut(handler);

        var parameters = new Dictionary<string, string> { ["BRANCH"] = "main", ["COMMIT_SHA"] = "abc123" };
        var result = await sut.TriggerBuildAsync(Server, new BuildTriggerRequest("sample-app", "main", "abc123", parameters));

        Assert.True(result.Success);
        Assert.Equal("7", result.ProviderQueueItemId);
        Assert.Contains("buildWithParameters", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("BRANCH=main", captured.RequestUri.Query);
    }

    [Fact]
    public async Task TriggerBuildAsync_WhenJenkinsReturnsNonSuccessStatus_ReturnsFail()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);

        var result = await sut.TriggerBuildAsync(Server, new BuildTriggerRequest("missing-job", null, null, new Dictionary<string, string>()));

        Assert.False(result.Success);
        Assert.Contains("404", result.ErrorMessage);
    }

    [Fact]
    public async Task TriggerBuildAsync_WhenLocationHeaderMissing_ReturnsFail()
    {
        var handler = new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/crumbIssuer/api/json")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.Created));
        var sut = CreateSut(handler);

        var result = await sut.TriggerBuildAsync(Server, new BuildTriggerRequest("sample-app", null, null, new Dictionary<string, string>()));

        Assert.False(result.Success);
        Assert.Contains("queue item location", result.ErrorMessage);
    }

    // --------------------------------------------------------------- Status

    [Fact]
    public async Task GetBuildStatusAsync_ByBuildNumber_WhileBuilding_ReturnsRunning()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"building":true,"result":null,"url":"https://jenkins.example.local/job/sample-app/5/"}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildStatusAsync(Server, "sample-app", null, 5);

        Assert.True(result.Success);
        Assert.Equal(ProviderBuildLifecycleState.Running, result.State);
        Assert.Equal(5, result.BuildNumber);
    }

    [Fact]
    public async Task GetBuildStatusAsync_ByBuildNumber_WhenResultSuccess_ReturnsSucceeded()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"building":false,"result":"SUCCESS","url":"https://jenkins.example.local/job/sample-app/5/"}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildStatusAsync(Server, "sample-app", null, 5);

        Assert.True(result.Success);
        Assert.Equal(ProviderBuildLifecycleState.Succeeded, result.State);
    }

    [Fact]
    public async Task GetBuildStatusAsync_ByBuildNumber_WhenResultFailure_ReturnsFailed()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"building":false,"result":"FAILURE","url":null}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildStatusAsync(Server, "sample-app", null, 5);

        Assert.True(result.Success);
        Assert.Equal(ProviderBuildLifecycleState.Failed, result.State);
    }

    [Fact]
    public async Task GetBuildStatusAsync_ByQueueItem_StillWaiting_ReturnsQueued()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"cancelled":false,"executable":null}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildStatusAsync(Server, "sample-app", "42", null);

        Assert.True(result.Success);
        Assert.Equal(ProviderBuildLifecycleState.Queued, result.State);
        Assert.Null(result.BuildNumber);
    }

    [Fact]
    public async Task GetBuildStatusAsync_ByQueueItem_WhenCancelled_ReturnsFail()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"cancelled":true,"executable":null}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildStatusAsync(Server, "sample-app", "42", null);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.ErrorMessage);
    }

    [Fact]
    public async Task GetBuildStatusAsync_ByQueueItem_WhenResolved_FollowsUpAndReturnsBuildStatus()
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/queue/item/")
            ? JsonResponse("""{"cancelled":false,"executable":{"number":9,"url":"https://jenkins.example.local/job/sample-app/9/"}}""")
            : JsonResponse("""{"building":false,"result":"SUCCESS","url":"https://jenkins.example.local/job/sample-app/9/"}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildStatusAsync(Server, "sample-app", "42", null);

        Assert.True(result.Success);
        Assert.Equal(9, result.BuildNumber);
        Assert.Equal(ProviderBuildLifecycleState.Succeeded, result.State);
    }

    [Fact]
    public async Task GetBuildStatusAsync_WhenHostUnreachable_ReturnsFailNeverThrows()
    {
        var sut = new JenkinsBuildProvider(new HttpClient(), NullLogger<JenkinsBuildProvider>.Instance);
        var unreachable = new BuildServer { Name = "unreachable", BaseUrl = "http://127.0.0.1:1" };

        var result = await sut.GetBuildStatusAsync(unreachable, "sample-app", null, 1);

        Assert.False(result.Success);
    }

    // ------------------------------------------------------------------ Log

    [Fact]
    public async Task GetBuildLogAsync_WhenBuildComplete_ReturnsLogTextAndIsComplete()
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.EndsWith("consoleText")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("build output line 1\nbuild output line 2") }
            : JsonResponse("""{"building":false}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildLogAsync(Server, "sample-app", 5);

        Assert.True(result.Success);
        Assert.True(result.IsComplete);
        Assert.Contains("build output line 1", result.LogText);
    }

    [Fact]
    public async Task GetBuildLogAsync_WhenConsoleTextRequestFails_ReturnsFail()
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.EndsWith("consoleText")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : JsonResponse("""{"building":false}"""));
        var sut = CreateSut(handler);

        var result = await sut.GetBuildLogAsync(Server, "sample-app", 5);

        Assert.False(result.Success);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
