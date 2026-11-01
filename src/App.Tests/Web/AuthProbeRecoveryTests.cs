using System.Net;
using System.Text;
using App.Web.Layout;
using App.Web.Pages;
using App.Web.Services;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Web;

public sealed class AuthProbeRecoveryTests : TestContext
{
    [Theory]
    [InlineData(typeof(NavMenu), "nav-user")]
    [InlineData(typeof(Login), "login-signed-in")]
    [InlineData(typeof(ChangePassword), "change-password-submit")]
    public async Task InitializeAsync_TwoTimeouts_ShowsRetryAndRecoversToSignedIn(Type component, string signedInTestId)
    {
        var clock = new ProbeTimeProvider();
        var calls = 0;
        using var http = DelegateHttpMessageHandler.CreateClient((_, token) =>
        {
            if (++calls > 2)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"authenticated\":true,\"username\":\"admin\",\"roles\":[]}", Encoding.UTF8, "application/json")
                });
            }
            var pending = new TaskCompletionSource<HttpResponseMessage>();
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        });
        var state = new PocAuthState(http, true, timeProvider: clock);
        Services.AddSingleton(state);
        var cut = RenderComponent<DynamicComponent>(p => p.Add(x => x.Type, component));
        var initialization = state.InitializeAsync();

        await RenderCompletion.AfterAsync(cut, () => cut.FindAll("[data-testid=auth-probe-failed]").Count == 1, async () =>
        {
            (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(25));
            (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(1));
            (await clock.FireNextAsync()).Should().Be(TimeSpan.FromSeconds(25));
            await initialization;
        });

        state.ProbeFailed.Should().BeTrue();
        calls.Should().Be(2);
        cut.Find("[data-testid=auth-probe-failed]").TextContent.Should().Contain("Couldn't check sign-in");
        cut.FindAll("[data-testid=nav-login], [data-testid=login-submit], [data-testid=change-password-signin-required]").Should().BeEmpty();

        await RenderCompletion.AfterAsync(cut, () => cut.FindAll($"[data-testid={signedInTestId}]").Count == 1,
            () => cut.Find("[data-testid=auth-probe-retry]").ClickAsync(new MouseEventArgs()));

        calls.Should().Be(3);
        state.User!.Username.Should().Be("admin");
        state.ProbeFailed.Should().BeFalse();
        cut.FindAll("[data-testid=auth-probe-failed]").Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(NavMenu))]
    [InlineData(typeof(Login))]
    [InlineData(typeof(ChangePassword))]
    public void InitializeAsync_AuthDisabled_ProbesNothingAndShowsNoAuthUi(Type component)
    {
        var calls = 0;
        using var http = DelegateHttpMessageHandler.CreateClient((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("Disabled auth must not probe.");
        });
        Services.AddSingleton(new PocAuthState(http, false, timeProvider: new ProbeTimeProvider()));

        var cut = RenderComponent<DynamicComponent>(p => p.Add(x => x.Type, component));

        calls.Should().Be(0);
        cut.FindAll("[data-testid=auth-probe-failed], [data-testid=auth-probe-retry], [data-testid=nav-login], [data-testid=login-page], [data-testid=change-password-page]").Should().BeEmpty();
    }
}
