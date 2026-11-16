// Short-lived, single-use tickets for opening a WebSocket.
//
// The problem being solved: browsers cannot attach an "Authorization: Bearer
// ..." header to a WebSocket handshake. The obvious workaround is to put the
// JWT in the query string (?token=...), and that is what this app used to
// do - but a URL is the least private part of a request. It is written to
// the web server's access log, to every reverse proxy and load balancer in
// front of it, and into the browser's own history. A token that grants
// access for a full day should not be sitting in any of those.
//
// So instead: the client asks for a ticket over a normal authenticated HTTPS
// request (header, not URL), and puts THAT in the WebSocket URL. A ticket is
//   - random, so it cannot be guessed,
//   - good for 30 seconds, so a leaked log line is worthless by the time
//     anyone reads it,
//   - single-use, so replaying it does nothing.
//
// Note what a ticket is NOT: it is not a second kind of login. It only ever
// stands in for a token the caller already proved they had.
using System.Security.Cryptography;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace IncidentFlow.Api.Security;

public interface IWebSocketTicketStore
{
    // Issues a ticket for an already-authenticated user, identified by the
    // same thing the JWT carries: their email.
    Task<string> IssueAsync(string email);

    // Exchanges a ticket for the email it was issued to, and destroys it.
    // Returns null if the ticket is unknown, expired, or already used.
    Task<string?> RedeemAsync(string? ticket);
}

public static class WebSocketTicket
{
    // Half a minute is plenty: the client asks for a ticket and immediately
    // opens the socket. Anything longer only widens the window in which a
    // copied ticket still works.
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    // 32 bytes from a cryptographic random source, as hex. Hex rather than
    // Base64 so it is URL-safe without any escaping to get wrong.
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string RedisKey(string ticket) => $"wsticket:{ticket}";
}

// The real implementation. Tickets live in Redis rather than in this
// process's memory for the same reason events do: with more than one backend
// replica, the request that issues a ticket and the socket that redeems it
// can easily land on different machines.
public class RedisWebSocketTicketStore(IConnectionMultiplexer redis) : IWebSocketTicketStore
{
    public async Task<string> IssueAsync(string email)
    {
        var ticket = WebSocketTicket.Generate();

        // Redis expires the key for us, so there is no cleanup to forget.
        await redis.GetDatabase().StringSetAsync(
            WebSocketTicket.RedisKey(ticket), email, WebSocketTicket.Lifetime
        );

        return ticket;
    }

    public async Task<string?> RedeemAsync(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;

        // StringGetDeleteAsync is Redis's GETDEL: read and delete in ONE
        // atomic command. Doing it as a get followed by a delete would leave
        // a window in which two sockets could redeem the same ticket, which
        // is exactly the thing "single-use" is supposed to rule out.
        var email = await redis.GetDatabase().StringGetDeleteAsync(WebSocketTicket.RedisKey(ticket));

        return email.IsNullOrEmpty ? null : email.ToString();
    }
}

// An in-memory stand-in, used by the tests (which run without a Redis
// server). Same contract, same single-use guarantee - just scoped to one
// process, which is all a test needs.
public class InMemoryWebSocketTicketStore : IWebSocketTicketStore
{
    private readonly ConcurrentDictionary<string, (string Email, DateTime ExpiresAt)> _tickets = new();

    public Task<string> IssueAsync(string email)
    {
        var ticket = WebSocketTicket.Generate();
        _tickets[ticket] = (email, DateTime.UtcNow.Add(WebSocketTicket.Lifetime));

        // Nothing expires these for us, so take the opportunity to sweep.
        // Without this, a long-running process would hold every ticket ever
        // issued and never used.
        foreach (var (key, value) in _tickets)
        {
            if (value.ExpiresAt <= DateTime.UtcNow) _tickets.TryRemove(key, out _);
        }

        return Task.FromResult(ticket);
    }

    public Task<string?> RedeemAsync(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return Task.FromResult<string?>(null);

        // TryRemove is the atomic read-and-delete that makes this single-use.
        if (!_tickets.TryRemove(ticket, out var entry)) return Task.FromResult<string?>(null);

        // Found, but stale - treat an expired ticket exactly like a wrong one.
        return Task.FromResult(entry.ExpiresAt > DateTime.UtcNow ? entry.Email : null);
    }
}
