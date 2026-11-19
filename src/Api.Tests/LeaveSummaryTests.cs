using System.Net;
using System.Net.Http.Json;
using Api.Data;
using Api.Models;
using Api.Services;

namespace Api.Tests;

/// <summary>LeaveService.DaysTakenThisYearAsync: only approved leave in this year counts.</summary>
public class LeaveSummaryServiceTests
{
    // Employee 1 has no seed leave, so each test controls everything that is counted.
    const int Employee = 1;
    static readonly int Year = DateTime.UtcNow.Year;
    static readonly DateOnly Monday = Seed.MondayOnOrAfter(new DateOnly(Year, 6, 1));

    static async Task<decimal?> DaysTakenWith(params LeaveRequest[] requests)
    {
        using var db = TestDb.Create();
        foreach (var r in requests) r.EmployeeId = Employee;
        db.LeaveRequests.AddRange(requests);
        await db.SaveChangesAsync();
        return await new LeaveService(db).DaysTakenThisYearAsync(Employee);
    }

    static LeaveRequest MonToWed(LeaveStatus status) =>
        new() { StartDate = Monday, EndDate = Monday.AddDays(2), Status = status, Reason = "Trip" };

    [Fact]
    public async Task Approved_leave_counts()
    {
        Assert.Equal(3m, await DaysTakenWith(MonToWed(LeaveStatus.Approved)));
    }

    [Fact]
    public async Task Pending_leave_does_not_count()
    {
        Assert.Equal(0m, await DaysTakenWith(MonToWed(LeaveStatus.Pending)));
    }

    [Fact]
    public async Task Rejected_leave_does_not_count()
    {
        Assert.Equal(0m, await DaysTakenWith(MonToWed(LeaveStatus.Rejected)));
    }

    [Fact]
    public async Task Approved_half_day_counts_half()
    {
        var halfDay = new LeaveRequest { StartDate = Monday, EndDate = Monday, HalfDay = HalfDay.AM, Status = LeaveStatus.Approved, Reason = "Clinic" };
        Assert.Equal(0.5m, await DaysTakenWith(halfDay));
    }

    [Fact]
    public async Task Approved_leave_in_another_year_does_not_count()
    {
        var lastYear = Seed.MondayOnOrAfter(new DateOnly(Year - 1, 6, 1));
        var old = new LeaveRequest { StartDate = lastYear, EndDate = lastYear.AddDays(2), Status = LeaveStatus.Approved, Reason = "Old trip" };
        Assert.Equal(0m, await DaysTakenWith(old));
    }

    [Fact]
    public async Task Unknown_employee_is_null()
    {
        using var db = TestDb.Create();
        Assert.Null(await new LeaveService(db).DaysTakenThisYearAsync(999));
    }
}

/// <summary>GET /employees/{id}/summary over HTTP: same access rules as the balance.</summary>
public class LeaveSummaryApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record Summary(int EmployeeId, decimal DaysTakenThisYear);

    [Fact]
    public async Task Employee_sees_their_own_summary()
    {
        var farah = await api.SignedInAs(People.Farah);
        var s = await farah.GetFromJsonAsync<Summary>("/employees/6/summary"); // seed: 3 approved days this year
        Assert.Equal(6, s!.EmployeeId);
        Assert.Equal(3m, s.DaysTakenThisYear);
    }

    [Fact]
    public async Task Another_employee_gets_403()
    {
        var gopal = await api.SignedInAs(People.Gopal);
        Assert.Equal(HttpStatusCode.Forbidden, (await gopal.GetAsync("/employees/6/summary")).StatusCode);
    }

    [Fact]
    public async Task Unknown_employee_gets_404()
    {
        var hr = await api.SignedInAs(People.Dina); // HR may see everyone, so the only reason to fail is "not found"
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync("/employees/999/summary")).StatusCode);
    }

    [Fact]
    public async Task Summary_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/employees/6/summary")).StatusCode);
    }
}
