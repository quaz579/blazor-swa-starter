using System.Net;
using System.Text;
using App.Web.Pages;
using App.Web.Services;
using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Web;

public sealed class ChangePasswordPageTests : TestContext
{
    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private PocAuthState Register(Func<HttpRequestMessage, HttpResponseMessage> handle, out List<string> requests)
    {
        var seen = new List<string>();
        requests = seen;
        var http = new HttpClient(new DelegateHttpMessageHandler((req, _) =>
        {
            seen.Add($"{req.Method} {req.RequestUri!.AbsolutePath}");
            return Task.FromResult(handle(req));
        })) { BaseAddress = new Uri("https://api.test/") };
        var state = new PocAuthState(http, true);
        Services.AddSingleton(state);
        return state;
    }

    private static HttpResponseMessage Route(HttpRequestMessage req, HttpResponseMessage changeResponse, bool signedIn = true) =>
        req.RequestUri!.AbsolutePath switch
        {
            "/api/auth/me" => signedIn
                ? Json(HttpStatusCode.OK, "{\"authenticated\":true,\"username\":\"admin\",\"roles\":[\"user\"]}")
                : Json(HttpStatusCode.OK, "{\"authenticated\":false}"),
            _ => changeResponse,
        };

    private static void Fill(IRenderedComponent<ChangePassword> cut, string current, string next, string confirm)
    {
        cut.Find("[data-testid='change-password-current']").Change(current);
        cut.Find("[data-testid='change-password-new']").Change(next);
        cut.Find("[data-testid='change-password-confirm']").Change(confirm);
    }

    [Fact]
    public void SignedOut_ShowsASignInPromptInsteadOfTheForm()
    {
        Register(req => Route(req, new HttpResponseMessage(HttpStatusCode.NoContent), signedIn: false), out _);

        var cut = RenderComponent<ChangePassword>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='change-password-signin-required']"));
        cut.FindAll("[data-testid='change-password-submit']").Should().BeEmpty();
    }

    [Fact]
    public void SignedIn_SubmittingValidPasswords_ShowsSuccessAndClearsTheFields()
    {
        Register(req => Route(req, new HttpResponseMessage(HttpStatusCode.NoContent)), out var requests);
        var cut = RenderComponent<ChangePassword>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='change-password-submit']"));

        Fill(cut, "Ch@nageM3", "Another-pass-9", "Another-pass-9");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-testid='change-password-success']"));
        requests.Should().Contain("POST /api/auth/change-password");
        cut.Find("[data-testid='change-password-current']").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void MismatchedConfirmation_ShowsAnErrorWithoutCallingTheApi()
    {
        Register(req => Route(req, new HttpResponseMessage(HttpStatusCode.NoContent)), out var requests);
        var cut = RenderComponent<ChangePassword>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='change-password-submit']"));

        Fill(cut, "Ch@nageM3", "Another-pass-9", "Different-pass-9");
        cut.Find("form").Submit();

        cut.Find("[data-testid='change-password-error']").TextContent.Should().Contain("do not match");
        requests.Should().NotContain("POST /api/auth/change-password");
    }

    [Fact]
    public void ServerRejection_ShowsTheServerMessageAndKeepsTheFormUsable()
    {
        Register(req => Route(req, Json(HttpStatusCode.BadRequest, "{\"error\":\"The current password is incorrect.\"}")), out _);
        var cut = RenderComponent<ChangePassword>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='change-password-submit']"));

        Fill(cut, "wrong", "Another-pass-9", "Another-pass-9");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-testid='change-password-error']").TextContent.Should().Contain("current password is incorrect"));
        cut.Find("[data-testid='change-password-submit']").HasAttribute("disabled").Should().BeFalse();
    }
}
