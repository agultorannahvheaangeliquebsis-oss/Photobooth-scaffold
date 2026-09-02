using System.Net.Http;
using System.Net.Http.Json;

namespace Photobooth.UI.Tests;

public class RemoteControlServerTests
{
    [Fact]
    public async Task Status_ReturnsWhateverGetStatusReturns()
    {
        using var server = new RemoteControlServer(getStatus: () => "Idle", tryStartNextGuest: () => true);
        server.Start();
        using var client = new HttpClient();

        var response = await client.GetFromJsonAsync<StatusResponse>($"{RemoteControlServer.Url}status");

        Assert.NotNull(response);
        Assert.Equal("Idle", response!.state);
    }

    [Fact]
    public async Task StartNext_CallbackReturnsTrue_Returns200WithOkTrue()
    {
        using var server = new RemoteControlServer(getStatus: () => "Idle", tryStartNextGuest: () => true);
        server.Start();
        using var client = new HttpClient();

        HttpResponseMessage response = await client.PostAsync($"{RemoteControlServer.Url}start-next", content: null);
        var body = await response.Content.ReadFromJsonAsync<StartNextResponse>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.ok);
    }

    [Fact]
    public async Task StartNext_CallbackReturnsFalse_Returns409WithOkFalse()
    {
        using var server = new RemoteControlServer(getStatus: () => "Countdown", tryStartNextGuest: () => false);
        server.Start();
        using var client = new HttpClient();

        HttpResponseMessage response = await client.PostAsync($"{RemoteControlServer.Url}start-next", content: null);
        var body = await response.Content.ReadFromJsonAsync<StartNextResponse>();

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(body!.ok);
    }

    [Fact]
    public async Task UnknownPath_Returns404()
    {
        using var server = new RemoteControlServer(getStatus: () => "Idle", tryStartNextGuest: () => true);
        server.Start();
        using var client = new HttpClient();

        HttpResponseMessage response = await client.GetAsync($"{RemoteControlServer.Url}nope");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private record StatusResponse(string state);
    private record StartNextResponse(bool ok);
}
