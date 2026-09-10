using DevOpsPortal.Application.Services;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class ComposeFileAnalyzerTests
{
    // Mirrors the shape of the real legacy compose pattern (base runtime image +
    // named volume bound to a host publish dir via driver_opts, external shared
    // network, extra_hosts, list-form entrypoint/environment) but with generic,
    // non-client-specific names — never hardcode a real tenant's application here.
    private const string SampleCompose = """
        services:
          SampleApi:
            image: mcr.microsoft.com/dotnet/aspnet:8.0
            container_name: SampleApi
            restart: always
            ports:
              - "5555:8080"
            volumes:
              - SampleApi-volume:/app
              - /mnt/data/sample-apps/Logs/sampleapi:/app/Logs/sampleapi
            working_dir: /app
            entrypoint: ["dotnet", "SampleApi.dll"]
            environment:
              - TZ=Asia/Karachi
              - DB_PASSWORD=hunter2
            extra_hosts:
              - "internal-db:192.168.10.9"
            networks:
              - shared

        volumes:
          SampleApi-volume:
            driver: local
            driver_opts:
              type: none
              o: bind
              device: /mnt/data/sample-apps/sampleapi/publish

        networks:
          shared:
            external: true
            name: sample-internal-network
        """;

    private readonly ComposeFileAnalyzer _sut = new();

    [Fact]
    public void Analyze_ExtractsServiceBasics()
    {
        var result = _sut.Analyze(SampleCompose);

        var service = Assert.Single(result.Services);
        Assert.Equal("SampleApi", service.ServiceName);
        Assert.Equal("mcr.microsoft.com/dotnet/aspnet:8.0", service.Image);
        Assert.Equal("SampleApi", service.ContainerName);
        Assert.Equal("/app", service.WorkingDir);
        Assert.Equal("always", service.RestartPolicy);
        Assert.Equal(["dotnet", "SampleApi.dll"], service.Entrypoint);
        Assert.Contains("5555:8080", service.Ports);
    }

    [Fact]
    public void Analyze_ResolvesNamedVolumeToItsHostBindDevice()
    {
        var result = _sut.Analyze(SampleCompose);
        var service = result.Services.Single();

        var appVolume = service.Volumes.Single(v => v.Target == "/app");
        Assert.True(appVolume.IsBindMount);
        Assert.Equal("/mnt/data/sample-apps/sampleapi/publish", appVolume.Source);
        Assert.True(appVolume.LooksLikePublishBinding);

        var logsVolume = service.Volumes.Single(v => v.Target == "/app/Logs/sampleapi");
        Assert.True(logsVolume.IsBindMount);
        Assert.Equal("/mnt/data/sample-apps/Logs/sampleapi", logsVolume.Source);
    }

    [Fact]
    public void Analyze_DetectsExternalNetwork()
    {
        var result = _sut.Analyze(SampleCompose);
        var service = result.Services.Single();

        Assert.Contains("shared", service.Networks);
        Assert.True(service.UsesExternalNetwork);
    }

    [Fact]
    public void Analyze_RedactsSecretLookingEnvironmentValues()
    {
        var result = _sut.Analyze(SampleCompose);
        var service = result.Services.Single();

        Assert.Contains("TZ=Asia/Karachi", service.EnvironmentEntries);
        Assert.Contains("DB_PASSWORD=***REDACTED***", service.EnvironmentEntries);
        Assert.DoesNotContain(service.EnvironmentEntries, e => e.Contains("hunter2"));
    }

    [Fact]
    public void Analyze_CapturesExtraHosts()
    {
        var result = _sut.Analyze(SampleCompose);
        Assert.Contains("internal-db:192.168.10.9", result.Services.Single().ExtraHosts);
    }

    [Fact]
    public void Analyze_EmptyInput_ReturnsWarningNoThrow()
    {
        var result = _sut.Analyze("");
        Assert.Empty(result.Services);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Analyze_InvalidYaml_ReturnsWarningNoThrow()
    {
        var result = _sut.Analyze("services: [this is not: valid: yaml: at all");
        Assert.Empty(result.Services);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Analyze_MissingServicesSection_ReturnsWarning()
    {
        var result = _sut.Analyze("volumes:\n  foo:\n");
        Assert.Empty(result.Services);
        Assert.Contains(result.Warnings, w => w.Contains("services"));
    }

    [Fact]
    public void Analyze_ServiceWithoutContainerNameOrImage_WarnsButStillReturnsService()
    {
        var result = _sut.Analyze("services:\n  bare:\n    ports:\n      - \"80:80\"\n");

        var service = Assert.Single(result.Services);
        Assert.Null(service.ContainerName);
        Assert.Null(service.Image);
        Assert.Contains(result.Warnings, w => w.Contains("container_name"));
        Assert.Contains(result.Warnings, w => w.Contains("image"));
    }
}
