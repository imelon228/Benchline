using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Benchline.Application;
using Benchline.Contracts;
using Benchline.Domain;
using Benchline.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Benchline.Integration.Tests;

public sealed class WorkshopApiTests : IAsyncLifetime
{
    private const string DemoPassword = "Integration-only-password!";
    private readonly string _folder = Directory.CreateTempSubdirectory("benchline-tests-").FullName;
    private WebApplicationFactory<Program> _host = null!;

    public Task InitializeAsync()
    {
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Workshop"] = $"Data Source={Path.Combine(_folder, "test.db")};Pooling=False",
                    ["Workshop:KeyPath"] = Path.Combine(_folder, "keys"),
                    ["Workshop:DemoPassword"] = DemoPassword
                }));
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task RecoveryChangesMemberReservationsAndCreatesAnAuditEntry()
    {
        using var member = await SignIn("member");
        using var technician = await SignIn("tech");
        using var coordinator = await SignIn("coord");

        var initial = await Read<BoardDto>(member, "/api/board");
        var primary = initial.Stations.Single(station => station.Name == "Laser Cutter 01");
        var backup = initial.Stations.Single(station => station.Name == "Laser Cutter 02");
        var original = initial.Bookings.Single(booking =>
            initial.Slots.Single(slot => slot.Id == booking.SlotId).StationId == primary.Id);

        using var disabled = await Write(technician, HttpMethod.Put, $"/api/stations/{primary.Id}",
            new AvailabilityRequest(initial.Revision, false));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        using var techPlan = await technician.GetAsync($"/api/recovery/{primary.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, techPlan.StatusCode);
        var outage = await Read<BoardDto>(member, "/api/board");
        Assert.Equal("Displaced", outage.Bookings.Single(booking => booking.Id == original.Id).Status);

        var plan = await Read<PlanDto>(coordinator, $"/api/recovery/{primary.Id}");
        Assert.Equal(2, plan.Moves.Length);
        Assert.Equal(0, plan.Unmatched);
        using var published = await Write(coordinator, HttpMethod.Post, $"/api/recovery/{primary.Id}",
            new PublishRequest(plan.Revision, plan.Moves));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var final = await Read<BoardDto>(member, "/api/board");
        var recovered = final.Bookings.Single(booking => booking.Id == original.Id);
        Assert.Equal("Reserved", recovered.Status);
        Assert.Equal(backup.Id, final.Slots.Single(slot => slot.Id == recovered.SlotId).StationId);

        var audit = await Read<AuditDto[]>(coordinator, "/api/audit?limit=1");
        Assert.Equal("Recovery published", Assert.Single(audit).Event);
        var csv = await coordinator.GetStringAsync("/api/audit.csv");
        Assert.Contains("Recovery published", csv);

        using var stale = await Write(coordinator, HttpMethod.Post, $"/api/recovery/{primary.Id}",
            new PublishRequest(plan.Revision, plan.Moves));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task WritesRequireAuthenticationAndAntiforgery()
    {
        using var anonymous = CreateClient();
        using var response = await anonymous.GetAsync("/api/board");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var member = await SignIn("member");
        using var unprotected = await member.PostAsJsonAsync("/api/bookings",
            new ReserveRequest(1, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.BadRequest, unprotected.StatusCode);

        var board = await Read<BoardDto>(member, "/api/board");
        var station = board.Stations.First();
        using var forbidden = await Write(member, HttpMethod.Put, $"/api/stations/{station.Id}",
            new AvailabilityRequest(board.Revision, false));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task CompetingReservationsProduceOneBookingAndOneConflict()
    {
        using var one = await SignIn("member");
        using var two = await SignIn("member2");
        var board = await Read<BoardDto>(one, "/api/board");
        var slot = board.Slots.First(item => item.Available);
        var first = new ReserveRequest(board.Revision, Guid.NewGuid(), slot.Id, slot.End);
        var second = first with { BookingId = Guid.NewGuid() };

        var responses = await Task.WhenAll(Write(one, HttpMethod.Post, "/api/bookings", first),
            Write(two, HttpMethod.Post, "/api/bookings", second));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Contains(responses, item => item.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, item => item.StatusCode == HttpStatusCode.Conflict);

        var firstBoard = await Read<BoardDto>(one, "/api/board");
        var secondBoard = await Read<BoardDto>(two, "/api/board");
        Assert.Single(firstBoard.Bookings.Concat(secondBoard.Bookings), item => item.SlotId == slot.Id);
    }

    [Fact]
    public async Task WorkshopRoutesDistinguishAnonymousAndWrongRoles()
    {
        using var anonymous = CreateClient();
        using var member = await SignIn("member");
        using var technician = await SignIn("tech");
        using var coordinator = await SignIn("coord");
        var missing = Guid.NewGuid();
        var board = await Read<BoardDto>(member, "/api/board");

        (HttpMethod Method, string Path, object? Body, HttpStatusCode Member, HttpStatusCode Technician, HttpStatusCode Coordinator)[] routes =
        [
            (HttpMethod.Get, "/api/board", null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK),
            (HttpMethod.Post, "/api/bookings", new ReserveRequest(board.Revision, missing, missing, DateTimeOffset.UtcNow),
                HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            (HttpMethod.Post, $"/api/bookings/{missing}/cancel", new RevisionRequest(board.Revision),
                HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            (HttpMethod.Put, $"/api/stations/{missing}", new AvailabilityRequest(board.Revision, false),
                HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound),
            (HttpMethod.Get, $"/api/recovery/{missing}", null,
                HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.NotFound),
            (HttpMethod.Post, $"/api/recovery/{missing}", new PublishRequest(board.Revision, [new(missing, missing)]),
                HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.NotFound),
            (HttpMethod.Get, "/api/audit", null, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK),
            (HttpMethod.Get, "/api/audit.csv", null, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK)
        ];

        foreach (var (method, path, body, memberStatus, techStatus, coordStatus) in routes)
        {
            await Check(anonymous, HttpStatusCode.Unauthorized);
            await Check(member, memberStatus);
            await Check(technician, techStatus);
            await Check(coordinator, coordStatus);

            async Task Check(HttpClient client, HttpStatusCode expected)
            {
                using var response = body is null ? await client.GetAsync(path) : await Write(client, method, path, body);
                Assert.Equal(expected, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task ChangedPlanDoesNotMutateBookingsOrAudit()
    {
        using var member = await SignIn("member");
        using var coordinator = await SignIn("coord");
        var board = await Read<BoardDto>(member, "/api/board");
        var station = board.Stations.Single(item => item.Name == "Laser Cutter 01");
        using var disabled = await Write(coordinator, HttpMethod.Put, $"/api/stations/{station.Id}",
            new AvailabilityRequest(board.Revision, false));
        disabled.EnsureSuccessStatusCode();

        var plan = await Read<PlanDto>(coordinator, $"/api/recovery/{station.Id}");
        var displaced = await Read<BoardDto>(member, "/api/board");
        var beforeAudit = await Read<AuditDto[]>(coordinator, "/api/audit");

        using var rejected = await Write(coordinator, HttpMethod.Post, $"/api/recovery/{station.Id}",
            new PublishRequest(plan.Revision, [plan.Moves[0] with { SlotId = Guid.NewGuid() }]));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("changed-plan", problem.GetProperty("code").GetString());

        var after = await Read<BoardDto>(member, "/api/board");
        Assert.Equal(displaced.Revision, after.Revision);
        Assert.Equal(displaced.Bookings, after.Bookings);
        Assert.Equal(beforeAudit, await Read<AuditDto[]>(coordinator, "/api/audit"));

        var newest = Assert.Single(await Read<AuditDto[]>(coordinator, "/api/audit?limit=0"));
        var earlier = Assert.Single(await Read<AuditDto[]>(coordinator, $"/api/audit?limit=1&before={newest.Revision}"));
        Assert.True(earlier.Revision < newest.Revision);

        using var csv = await coordinator.GetAsync("/api/audit.csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("benchline-audit.csv", csv.Content.Headers.ContentDisposition?.FileName);
        var lines = (await csv.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Revision,Recorded UTC,Account,Event,Details,Recorded Astana time", lines[0]);
        Assert.StartsWith($"{newest.Revision},", lines[1]);
        Assert.StartsWith($"{earlier.Revision},", lines[2]);
        var values = lines[1].Split(',');
        var utc = DateTimeOffset.Parse(values[1].Trim('"'), CultureInfo.InvariantCulture);
        var astana = DateTimeOffset.Parse(values[^1].Trim('"'), CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, utc.Offset);
        Assert.Equal(TimeSpan.FromHours(5), astana.Offset);
        Assert.Equal(utc.UtcTicks, astana.UtcTicks);
    }

    [Fact]
    public async Task StartupMigratesAnEmptyDatabaseAndLimitsSignInAttempts()
    {
        using var client = CreateClient();
        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
        Assert.True(File.Exists(Path.Combine(_folder, "test.db")));

        for (var attempt = 0; attempt < 15; attempt++)
        {
            using var invalid = await Write(client, HttpMethod.Post, "/api/session", new SignInRequest("member", "wrong"));
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }

        using var limited = await Write(client, HttpMethod.Post, "/api/session", new SignInRequest("member", DemoPassword));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task DemoRestartsKeepAUsableScheduleAndRenewAnExpiredOne()
    {
        var start = new DateTimeOffset(2026, 1, 8, 10, 30, 0, TimeSpan.Zero);
        BoardDto? previous = null;
        foreach (var hours in new[] { 0, 1, 12, 72 })
        {
            var now = start.AddHours(hours);
            await using var restart = _host.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedClock(now))));
            using var member = await SignIn("member", restart);
            using var member2 = await SignIn("member2", restart);
            using var technician = await SignIn("tech", restart);
            using var coordinator = await SignIn("coord", restart);
            var board = await Read<BoardDto>(member, "/api/board");
            var secondBoard = await Read<BoardDto>(member2, "/api/board");
            Assert.Equal(now, board.AsOf);
            Assert.Equal(4, board.Stations.Length);
            Assert.All(board.Stations, station =>
            {
                Assert.True(station.Online);
                Assert.Contains(board.Slots, slot => slot.StationId == station.Id && slot.Available && slot.Start > now);
            });
            var primary = board.Stations.Single(station => station.Name == "Laser Cutter 01");
            var backup = board.Stations.Single(station => station.Name == "Laser Cutter 02");
            var printer = board.Stations.Single(station => station.Name == "Prusa MK4");
            var slots = board.Slots.ToDictionary(slot => slot.Id);
            Assert.Contains(board.Bookings, booking => booking.Status == "Reserved" && slots[booking.SlotId].StationId == printer.Id
                && slots[booking.SlotId].Start > now);
            var laserBookings = new[] { board, secondBoard }.Select(view =>
                Assert.Single(view.Bookings, booking => booking.Status == "Reserved" && slots[booking.SlotId].StationId == primary.Id))
                .ToDictionary(booking => booking.Id);
            Assert.All(laserBookings.Values, booking => Assert.True(slots[booking.SlotId].Start > now));

            if (previous is not null)
            {
                Assert.Equal(previous.Stations.Select(station => station.Id), board.Stations.Select(station => station.Id));
                if (hours == 1)
                {
                    Assert.Equal(previous.Revision, board.Revision);
                    Assert.Equal(previous.Slots.Select(slot => slot.Id), board.Slots.Select(slot => slot.Id));
                    Assert.Equal(previous.Bookings, board.Bookings);
                }
                else
                {
                    Assert.Equal(previous.Revision + 1, board.Revision);
                    Assert.Empty(previous.Slots.Select(slot => slot.Id).Intersect(slots.Keys));
                    Assert.Equal(now.AddMinutes(-30).AddHours(2), board.Slots.Min(slot => slot.Start));
                }
            }
            previous = board;

            // Check the actual planner without changing the persisted scenario between restarts.
            using var scope = restart.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            var schedule = await repository.Read(CancellationToken.None);
            var withoutPrimary = ScheduleRules.Execute(schedule, new Actor("tech", Role.Technician),
                new ScheduleCommand.SetAvailability(schedule.Revision, primary.Id, false), now);
            var change = Assert.IsType<Result<ScheduleChange>.Ok>(withoutPrimary);
            var plan = RecoveryPlanner.Build(change.Value.Schedule,
                schedule.Stations.Single(station => station.Id == primary.Id), now);
            Assert.Equal(2, plan.Moves.Length);
            Assert.Empty(plan.Unmatched);
            Assert.All(plan.Moves, move =>
            {
                var replacement = slots[move.SlotId];
                Assert.Equal(backup.Id, replacement.StationId);
                Assert.True(replacement.Start > now);
                Assert.True(replacement.End <= laserBookings[move.BookingId].Deadline);
            });
        }
    }

    [Theory]
    [InlineData("Demo", true)]
    [InlineData("Production", false)]
    public async Task OnlyDemoResponsesAdvertiseDemoAccounts(string environment, bool showDemo)
    {
        await using var host = _host.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(showDemo, response.Headers.TryGetValues(DemoSignIn.Header, out var values));
        if (showDemo)
            Assert.Equal("configured", Assert.Single(values!));
    }

    private HttpClient CreateClient() => _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task DemoStartupLocalizesExistingAccountsAndStationsInPlace()
    {
        var now = new DateTimeOffset(2026, 11, 16, 10, 30, 0, TimeSpan.Zero);
        void SetClock(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedClock(now)));
        BoardDto before;
        Dictionary<string, string> passwords;
        await using (var older = _host.WithWebHostBuilder(SetClock))
        {
            using var member = await SignIn("member", older);
            before = await Read<BoardDto>(member, "/api/board");
            using var scope = older.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkshopDb>();
            var accounts = await db.Accounts.AsNoTracking().ToArrayAsync();
            passwords = accounts.ToDictionary(account => account.Id, account => account.PasswordHash);
            db.Accounts.UpdateRange(accounts.Select(account => account with { Name = $"Previous {account.UserName}" }));
            var stations = await db.Stations.AsNoTracking().ToArrayAsync();
            db.Stations.UpdateRange(stations.Select(station => station with { Location = $"Previous {station.Name} space" }));
            await db.SaveChangesAsync();
        }

        await using (var production = _host.WithWebHostBuilder(builder =>
        {
            SetClock(builder);
            builder.UseEnvironment("Production");
        }))
        {
            using var scope = production.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkshopDb>();
            Assert.All(await db.Accounts.ToArrayAsync(), account => Assert.StartsWith("Previous ", account.Name));
            Assert.All(await db.Stations.ToArrayAsync(), station => Assert.StartsWith("Previous ", station.Location));
        }

        await using var demo = _host.WithWebHostBuilder(SetClock);
        foreach (var (username, name, role) in new[]
        {
            ("member", "Aidos Serikov", "Member"), ("member2", "Aruzhan Nurgaliyeva", "Member"),
            ("tech", "Nurlan Tolegenov", "Technician"), ("coord", "Dana Kassymova", "Coordinator")
        })
        {
            using var client = await SignIn(username, demo);
            Assert.Equal(new SessionDto(name, role), await Read<SessionDto>(client, "/api/session"));
        }

        using var memberAfter = await SignIn("member", demo);
        var after = await Read<BoardDto>(memberAfter, "/api/board");
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Slots, after.Slots);
        Assert.Equal(before.Bookings, after.Bookings);
        Assert.Equal(before.Stations.Select(station => station.Id), after.Stations.Select(station => station.Id));
        Assert.Equal(new[] { "FabLab", "Coworking Zone", "Open Space", "Library" },
            after.Stations.Select(station => station.Location));

        using var updatedScope = demo.Services.CreateScope();
        var updated = updatedScope.ServiceProvider.GetRequiredService<WorkshopDb>();
        Assert.All(await updated.Accounts.ToArrayAsync(), account =>
        {
            Assert.Equal(passwords[account.Id], account.PasswordHash);
            Assert.Equal(DemoCatalog.DisplayName(account.Id), account.Name);
        });
    }

    private async Task<HttpClient> SignIn(string username, WebApplicationFactory<Program>? host = null)
    {
        var client = host?.CreateClient() ?? CreateClient();
        using var response = await Write(client, HttpMethod.Post, "/api/session", new SignInRequest(username, DemoPassword));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<T> Read<T>(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<T>(path))!;

    private static async Task<HttpResponseMessage> Write<T>(HttpClient client, HttpMethod method, string path, T input)
    {
        var token = await Read<TokenDto>(client, "/api/token");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-Workshop-Token", token.Token);
        return await client.SendAsync(request);
    }
}
