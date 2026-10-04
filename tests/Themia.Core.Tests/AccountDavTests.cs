using Themia.Core.Accounts;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// DAV discovery and multistatus reading, driven by a scripted server: each URL answers with a fixed
/// reply, and anything else is a 404 — so a test fails loudly if discovery asks somewhere new.
/// </summary>
public class AccountDavTests
{
    private const string Base = "https://cloud.example";

    private sealed class FakeServer
    {
        private readonly Dictionary<string, (int Status, string Body, string? MovedTo)> _routes = new(StringComparer.Ordinal);

        public List<(string Url, int Depth)> Asked { get; } = [];

        public FakeServer At(string path, int status, string body = "", string? movedTo = null)
        {
            _routes[Base + path] = (status, body, movedTo);
            return this;
        }

        /// <summary>Follows redirects itself, the way the app's HTTP layer does, and reports the final URL.</summary>
        public Task<DavReply> Propfind(Uri url, string body, int depth, CancellationToken cancellation)
        {
            var current = url;

            for (var hop = 0; hop < 5; hop++)
            {
                Asked.Add((current.AbsoluteUri, depth));

                if (!_routes.TryGetValue(current.AbsoluteUri, out var route))
                {
                    return Task.FromResult(new DavReply(404, string.Empty, current));
                }

                if (route.MovedTo is { } moved)
                {
                    current = new Uri(current, moved);
                    continue;
                }

                return Task.FromResult(new DavReply(route.Status, route.Body, current));
            }

            return Task.FromResult(new DavReply(508, string.Empty, current));
        }
    }

    private static Task<DavDiscoveryResult> Discover(FakeServer server, string entered, DavKind kind)
        => DavDiscovery.DiscoverAsync(new Uri(entered), kind, server.Propfind, CancellationToken.None);

    [Fact]
    public async Task S517_nextcloud_root_401_then_well_known_discovers_the_calendar()
    {
        // Issue #71 in the classic app: the server root refuses a basic-auth PROPFIND with 401 even
        // though the DAV context signs in fine; the .well-known fallback finds it.
        var server = new FakeServer()
            .At("/", 401)
            .At("/.well-known/caldav", 301, movedTo: "/remote.php/dav/")
            .At("/remote.php/dav/", 207, """
                <d:multistatus xmlns:d="DAV:"><d:response>
                  <d:href>/remote.php/dav/</d:href>
                  <d:propstat><d:prop>
                    <d:resourcetype><d:collection/></d:resourcetype>
                    <d:current-user-principal><d:href>/remote.php/dav/principals/users/alice/</d:href></d:current-user-principal>
                  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat>
                </d:response></d:multistatus>
                """)
            .At("/remote.php/dav/principals/users/alice/", 207, """
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:response>
                  <d:href>/remote.php/dav/principals/users/alice/</d:href>
                  <d:propstat><d:prop>
                    <c:calendar-home-set><d:href>/remote.php/dav/calendars/alice/</d:href></c:calendar-home-set>
                  </d:prop></d:propstat>
                </d:response></d:multistatus>
                """)
            .At("/remote.php/dav/calendars/alice/", 207, """
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
                  <d:response><d:href>/remote.php/dav/calendars/alice/</d:href>
                    <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat></d:response>
                  <d:response><d:href>/remote.php/dav/calendars/alice/tasks/</d:href>
                    <d:propstat><d:prop>
                      <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                      <c:supported-calendar-component-set><c:comp name="VTODO"/></c:supported-calendar-component-set>
                    </d:prop></d:propstat></d:response>
                  <d:response><d:href>/remote.php/dav/calendars/alice/personal/</d:href>
                    <d:propstat><d:prop>
                      <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                      <c:supported-calendar-component-set><c:comp name="VEVENT"/></c:supported-calendar-component-set>
                    </d:prop></d:propstat></d:response>
                </d:multistatus>
                """);

        var result = await Discover(server, Base + "/", DavKind.Calendar);

        Assert.Equal(DavDiscoveryOutcome.Found, result.Outcome);
        Assert.Equal(Base + "/remote.php/dav/calendars/alice/personal/", result.Collection?.AbsoluteUri);
    }

    [Fact]
    public async Task S517_an_exact_collection_url_is_used_verbatim_in_one_request()
    {
        var server = new FakeServer().At("/dav/calendars/alice/personal/", 207, """
            <multistatus xmlns="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav"><response>
              <href>/dav/calendars/alice/personal/</href>
              <propstat><prop><resourcetype><collection/><C:calendar/></resourcetype></prop></propstat>
            </response></multistatus>
            """);

        var entered = Base + "/dav/calendars/alice/personal/";
        var result = await Discover(server, entered, DavKind.Calendar);

        Assert.Equal(DavDiscoveryOutcome.Found, result.Outcome);
        Assert.Equal(entered, result.Collection?.AbsoluteUri);
        Assert.Single(server.Asked);
    }

    [Fact]
    public async Task S517_carddav_walks_principal_to_home_to_the_address_book()
    {
        var server = new FakeServer()
            .At("/dav/", 207, """
                <d:multistatus xmlns:d="DAV:"><d:response><d:href>/dav/</d:href>
                  <d:propstat><d:prop>
                    <d:current-user-principal><d:href>/dav/principals/users/bob/</d:href></d:current-user-principal>
                  </d:prop></d:propstat></d:response></d:multistatus>
                """)
            .At("/dav/principals/users/bob/", 207, """
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav"><d:response>
                  <d:href>/dav/principals/users/bob/</d:href>
                  <d:propstat><d:prop>
                    <c:addressbook-home-set><d:href>/dav/addressbooks/users/bob/</d:href></c:addressbook-home-set>
                  </d:prop></d:propstat></d:response></d:multistatus>
                """)
            .At("/dav/addressbooks/users/bob/", 207, """
                <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav">
                  <d:response><d:href>/dav/addressbooks/users/bob/</d:href>
                    <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat></d:response>
                  <d:response><d:href>/dav/addressbooks/users/bob/contacts/</d:href>
                    <d:propstat><d:prop><d:resourcetype><d:collection/><c:addressbook/></d:resourcetype></d:prop></d:propstat></d:response>
                </d:multistatus>
                """);

        var result = await Discover(server, Base + "/dav/", DavKind.Contacts);

        Assert.Equal(Base + "/dav/addressbooks/users/bob/contacts/", result.Collection?.AbsoluteUri);
        Assert.Contains(server.Asked, a => a.Url.EndsWith("/bob/", StringComparison.Ordinal) && a.Depth == 1);
    }

    [Fact]
    public async Task S517_all_candidates_401_reports_the_credential_failure()
    {
        var server = new FakeServer().At("/", 401).At("/.well-known/caldav", 401);

        var result = await Discover(server, Base + "/", DavKind.Calendar);

        Assert.Equal(DavDiscoveryOutcome.AuthFailed, result.Outcome);
        Assert.Equal(401, result.Status);
        Assert.Contains("401", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task S517_signed_in_but_nothing_there_says_so()
    {
        var server = new FakeServer().At("/", 207, """<d:multistatus xmlns:d="DAV:"><d:response><d:href>/</d:href></d:response></d:multistatus>""");

        var result = await Discover(server, Base + "/", DavKind.Contacts);

        Assert.Equal(DavDiscoveryOutcome.NothingFound, result.Outcome);
        Assert.Contains("address book", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task S517_a_server_error_is_not_reported_as_bad_credentials()
    {
        var server = new FakeServer().At("/", 500).At("/.well-known/caldav", 500);

        var result = await Discover(server, Base + "/", DavKind.Calendar);

        Assert.Equal(DavDiscoveryOutcome.ServerError, result.Outcome);
    }

    [Fact]
    public void S515_responses_are_read_by_local_name_whatever_the_prefix()
    {
        const string xml = """
            <A:multistatus xmlns:A="DAV:" xmlns:B="urn:ietf:params:xml:ns:caldav">
              <A:response><A:href>/c/</A:href><A:propstat><A:prop>
                <A:resourcetype><A:collection/><B:calendar/></A:resourcetype>
                <A:displayname>Work</A:displayname>
              </A:prop></A:propstat></A:response>
            </A:multistatus>
            """;

        Assert.True(DavXml.IsCollection(xml, DavKind.Calendar));
        Assert.False(DavXml.IsCollection(xml, DavKind.Contacts));
        var collection = Assert.Single(DavXml.Collections(xml, DavKind.Calendar));
        Assert.Equal("Work", collection.Name);
    }

    [Fact]
    public void S515_a_property_under_a_404_propstat_is_absent()
    {
        const string xml = """
            <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:response><d:href>/p/</d:href>
              <d:propstat><d:prop><c:calendar-home-set/></d:prop><d:status>HTTP/1.1 404 Not Found</d:status></d:propstat>
            </d:response></d:multistatus>
            """;

        Assert.Null(DavXml.HomeSet(xml, DavKind.Calendar));
    }

    [Theory]
    [InlineData("<html><body>Login</body></html")]
    [InlineData("not xml at all")]
    [InlineData("")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE x [<!ENTITY a "aaaa">]><x>&a;</x>""")]
    public void S515_a_body_that_is_not_a_multistatus_reads_as_nothing(string body)
        => Assert.Empty(DavXml.Responses(body));

    [Fact]
    public void S515_collections_fall_back_to_the_last_segment_for_a_name_and_keep_the_colour()
    {
        const string xml = """
            <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:i="http://apple.com/ns/ical/">
              <d:response><d:href>/cal/my%20family/</d:href><d:propstat><d:prop>
                <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                <i:calendar-color>#FF0000FF</i:calendar-color>
              </d:prop></d:propstat></d:response>
            </d:multistatus>
            """;

        var collection = Assert.Single(DavXml.Collections(xml, DavKind.Calendar));
        Assert.Equal("my family", collection.Name);
        Assert.Equal("#FF0000FF", collection.Color);
    }

    [Theory]
    [InlineData("https://h.example/a/b", "/x/y/", "https://h.example/x/y/")]
    [InlineData("https://h.example/a/b", "https://other.example/z/", "https://other.example/z/")]
    [InlineData("https://h.example/a/b", "c/", "https://h.example/a/c/")]
    [InlineData("https://h.example:8443/a/", "/x/", "https://h.example:8443/x/")]
    public void S516_hrefs_resolve_against_the_reply_url(string reply, string href, string expected)
        => Assert.Equal(expected, DavUrls.Resolve(new Uri(reply), href)?.AbsoluteUri);

    [Fact]
    public void S516_well_known_sits_on_the_origin()
        => Assert.Equal(
            "https://cloud.example:8443/.well-known/carddav",
            DavUrls.WellKnown(new Uri("https://cloud.example:8443/remote.php/dav"), DavKind.Contacts)?.AbsoluteUri);

    [Theory]
    [InlineData("cloud.example", "https://cloud.example/")]
    [InlineData("http://lan-nas:5000/caldav.php", "http://lan-nas:5000/caldav.php")]
    [InlineData("ftp://x.example", null)]
    [InlineData("   ", null)]
    public void S516_typed_addresses_become_urls(string typed, string? expected)
        => Assert.Equal(expected, DavUrls.FromTyped(typed)?.AbsoluteUri);
}
