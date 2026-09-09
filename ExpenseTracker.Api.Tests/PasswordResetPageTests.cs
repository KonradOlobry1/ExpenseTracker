using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// The statically rendered account pages, driven the way a browser drives them: fetch the form,
/// post back every field it rendered.
/// </summary>
/// <remarks>
/// These exist because of a specific regression that shipped. Blazor's SSR form binding builds
/// each input's <c>name</c> attribute from the text of its bind expression and matches posted
/// fields back to the property carrying <c>[SupplyParameterFromForm]</c>. Break that
/// correlation and the page still renders perfectly, still validates, still looks right in a
/// screenshot — and every submit posts empty. A test that only asserted 200 on the GET would
/// have been green throughout.
///
/// So each of these posts a real form and asserts on the effect, never on the markup.
/// </remarks>
public class PasswordResetPageTests(PasswordResetFactory factory) : IClassFixture<PasswordResetFactory>
{
    private const string OriginalPassword = "Passw0rd!";
    private const string NewPassword = "N3wPassw0rd!";

    [Fact]
    public async Task The_forgot_password_form_actually_posts_the_address()
    {
        // The binding test. If the field names stopped correlating, Email would arrive empty,
        // no account would match, and no token would ever be issued — while the page carried
        // on looking entirely healthy.
        var (client, email) = await RegisterAsync();

        var response = await PostFormAsync(client, "/account/forgot-password",
            new() { ["Input.Email"] = email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(factory.Sender.TokenFor(email));
    }

    [Fact]
    public async Task The_forgot_password_page_says_the_same_thing_for_an_unknown_address()
    {
        var client = factory.CreateClient();
        var unknown = $"nobody-{Guid.NewGuid():N}@test.local";

        var response = await PostFormAsync(client, "/account/forgot-password",
            new() { ["Input.Email"] = unknown });
        var html = await response.Content.ReadAsStringAsync();

        // Asserted on structure rather than on the wording: the form is gone, replaced by the
        // same confirmation panel a real address gets. Paired with the test above — which
        // proves a real address does issue a token — that is the property worth pinning: the
        // page looks identical either way, only the invisible half differs.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Input.Email", html);
        Assert.False(factory.Sender.SentAnythingTo(unknown));
    }

    [Fact]
    public async Task The_reset_form_actually_changes_the_password()
    {
        // Posts the form exactly as the browser would, including the hidden token field the
        // page rendered, then proves the change by signing in with the new password.
        var (client, email) = await RegisterAsync();
        await PostFormAsync(client, "/account/forgot-password", new() { ["Input.Email"] = email });
        var token = factory.Sender.TokenFor(email)!;

        var response = await PostFormAsync(client, $"/account/reset-password?token={token}",
            new()
            {
                ["Input.NewPassword"] = NewPassword,
                ["Input.ConfirmPassword"] = NewPassword,
            });

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = NewPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task The_reset_page_carries_the_token_through_the_post_itself()
    {
        // The hidden field, not the query string. If the token only survived because the
        // browser happened to repost to the same URL, this would still pass — so the assertion
        // is that the rendered form contains the token as a field of its own.
        var (client, email) = await RegisterAsync();
        await PostFormAsync(client, "/account/forgot-password", new() { ["Input.Email"] = email });
        var token = factory.Sender.TokenFor(email)!;

        var html = await client.GetStringAsync($"/account/reset-password?token={token}");
        var fields = FormFields(html);

        Assert.Equal(token, fields.GetValueOrDefault("Input.Token"));
    }

    [Fact]
    public async Task A_dead_link_says_so_and_renders_no_form()
    {
        // Better than letting someone type a new password twice before being told the link
        // expired an hour ago.
        var html = await factory.CreateClient()
            .GetStringAsync("/account/reset-password?token=not-a-real-token");

        Assert.Contains("This link is invalid or has expired", html);
        Assert.DoesNotContain("Input.NewPassword", html);
    }

    [Fact]
    public async Task A_spent_link_is_dead_on_the_second_visit()
    {
        var (client, email) = await RegisterAsync();
        await PostFormAsync(client, "/account/forgot-password", new() { ["Input.Email"] = email });
        var token = factory.Sender.TokenFor(email)!;

        await PostFormAsync(client, $"/account/reset-password?token={token}",
            new()
            {
                ["Input.NewPassword"] = NewPassword,
                ["Input.ConfirmPassword"] = NewPassword,
            });

        var html = await client.GetStringAsync($"/account/reset-password?token={token}");

        Assert.Contains("This link is invalid or has expired", html);
    }

    [Fact]
    public async Task Mismatched_confirmation_is_refused_and_keeps_the_link_alive()
    {
        var (client, email) = await RegisterAsync();
        await PostFormAsync(client, "/account/forgot-password", new() { ["Input.Email"] = email });
        var token = factory.Sender.TokenFor(email)!;

        var mismatch = await PostFormAsync(client, $"/account/reset-password?token={token}",
            new()
            {
                ["Input.NewPassword"] = NewPassword,
                ["Input.ConfirmPassword"] = "SomethingElse1!",
            });
        var mismatchHtml = await mismatch.Content.ReadAsStringAsync();

        // Still usable afterwards: a typo must not cost the user their only link.
        var retry = await PostFormAsync(client, $"/account/reset-password?token={token}",
            new()
            {
                ["Input.NewPassword"] = NewPassword,
                ["Input.ConfirmPassword"] = NewPassword,
            });
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = NewPassword });

        Assert.Contains("do not match", mismatchHtml);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task The_sign_in_page_offers_a_way_in_for_someone_who_forgot()
    {
        // A reset flow nothing links to is a reset flow nobody finds.
        var html = await factory.CreateClient().GetStringAsync("/account/login");

        Assert.Contains("/account/forgot-password", html);
    }

    private async Task<(HttpClient Client, string Email)> RegisterAsync()
    {
        var email = $"page-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = OriginalPassword });
        response.EnsureSuccessStatusCode();

        return (client, email);
    }

    /// <summary>
    /// Fetches the page, replays every field it rendered — antiforgery token and Blazor's own
    /// form handler included — with <paramref name="values"/> layered on top.
    /// </summary>
    /// <remarks>
    /// Replaying whatever the page rendered, rather than hand-listing the fields, is what keeps
    /// this honest: the test never asserts the field names are what it expects, it uses the
    /// ones the page actually produced and then checks the effect.
    /// </remarks>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string url, Dictionary<string, string> values)
    {
        var fields = FormFields(await client.GetStringAsync(url));

        foreach (var (key, value) in values)
            fields[key] = value;

        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static Dictionary<string, string> FormFields(string html)
    {
        var fields = new Dictionary<string, string>();

        foreach (Match tag in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var name = Regex.Match(tag.Value, "name=\"([^\"]*)\"").Groups[1].Value;
            if (string.IsNullOrEmpty(name)) continue;

            var value = Regex.Match(tag.Value, "value=\"([^\"]*)\"").Groups[1].Value;
            fields[name] = WebUtility.HtmlDecode(value);
        }

        return fields;
    }
}
