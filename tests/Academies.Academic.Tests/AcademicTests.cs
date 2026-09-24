using Academies.Academic.Application;
using Academies.Academic.Domain;
using Academies.Academic.Infrastructure.Persistence;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Tests;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Academies.Academic.Tests;

public sealed class AcademicTests : IAsyncLifetime
{
    private const long Academy = 3, Admin = 1, TeacherA = 10, TeacherB = 11, StudentA = 20, StudentB = 21, ParentA = 30, ParentB = 31;

    private ServiceHarness<AcademicDbContext> _h = null!;
    private long _courseId, _groupId;

    public async ValueTask InitializeAsync()
    {
        _h = new ServiceHarness<AcademicDbContext>(s =>
        {
            s.AddScoped<IAcademicDbContext>(sp => sp.GetRequiredService<AcademicDbContext>());
            s.AddSingleton<IMeetingLinkGenerator, TestMeetings>();
            s.AddSingleton<ICertificateRenderer, TestRenderer>();
            s.AddAcademicApplication();
        });

        await _h.SeedAsync(async db =>
        {
            db.People.AddRange(
                PeopleSeed.Person(Academy, Admin, "Admin", Roles.Admin),
                PeopleSeed.Person(Academy, TeacherA, "Teacher A", Roles.Teacher),
                PeopleSeed.Person(Academy, TeacherB, "Teacher B", Roles.Teacher),
                PeopleSeed.Person(Academy, StudentA, "Student A", Roles.Student),
                PeopleSeed.Person(Academy, StudentB, "Student B", Roles.Student),
                PeopleSeed.Person(Academy, ParentA, "Parent A", Roles.Parent),
                PeopleSeed.Person(Academy, ParentB, "Parent B", Roles.Parent));
            db.Students.AddRange(
                new Student { AcademyId = Academy, UserId = StudentA, ParentUserId = ParentA, EnrollmentDate = new DateOnly(2026, 1, 1) },
                new Student { AcademyId = Academy, UserId = StudentB, ParentUserId = ParentB, EnrollmentDate = new DateOnly(2026, 1, 1) });
            db.Teachers.AddRange(new Teacher { AcademyId = Academy, UserId = TeacherA }, new Teacher { AcademyId = Academy, UserId = TeacherB });

            var course = new Course { AcademyId = Academy, Name = "Quran" };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            _courseId = course.Id;

            var group = new Group { AcademyId = Academy, Name = "G1", CourseId = course.Id, TeacherUserId = TeacherA };
            group.Students.Add(new GroupStudent { AcademyId = Academy, StudentUserId = StudentA });
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            _groupId = group.Id;
        });

        _h.User.As(Admin, Academy, Roles.Admin);
    }

    public async ValueTask DisposeAsync() => await _h.DisposeAsync();

    private SaveSessionRequest Slot(DateTime start, long teacher = TeacherA, long? group = null, int weeks = 1) =>
        new("Lesson", _courseId, group ?? _groupId, teacher, start, 60, SessionType.Offline, "Room 1", null, false, null, weeks);

    [Fact]
    public async Task Teacher_cannot_be_double_booked()   // US-025
    {
        var start = new DateTime(2026, 9, 20, 16, 0, 0, DateTimeKind.Utc);
        await _h.RunAsync<ISessionService>(s => s.CreateAsync(Slot(start)));

        await Should.ThrowAsync<ConflictException>(() =>
            _h.RunAsync<ISessionService>(s => s.CreateAsync(Slot(start.AddMinutes(30), group: null) with { GroupId = null })));

        // The next hour, or another teacher at the same time, is fine.
        await _h.RunAsync<ISessionService>(s => s.CreateAsync(Slot(start.AddHours(1))));
        await _h.RunAsync<ISessionService>(s => s.CreateAsync(Slot(start, teacher: TeacherB) with { GroupId = null }));
    }

    [Fact]
    public async Task Weekly_repeat_creates_one_session_per_week()
    {
        var created = await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s =>
            s.CreateAsync(Slot(new DateTime(2026, 10, 1, 16, 0, 0, DateTimeKind.Utc), weeks: 4)));

        created.Count.ShouldBe(4);
        created.Select(c => c.StartsAtUtc.Day).ShouldBe([1, 8, 15, 22]);
    }

    [Fact]
    public async Task Teachers_schedule_only_for_themselves()
    {
        _h.User.As(TeacherA, Academy, Roles.Teacher);
        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _h.RunAsync<ISessionService>(s => s.CreateAsync(Slot(new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc), teacher: TeacherB) with { GroupId = null })));
    }

    [Fact]
    public async Task Attendance_publishes_events_and_awards_points_then_session_completes()   // US-026, US-042
    {
        var session = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s =>
            s.CreateAsync(Slot(new DateTime(2026, 9, 14, 16, 0, 0, DateTimeKind.Utc))))).Single();

        _h.User.As(TeacherA, Academy, Roles.Teacher);
        var roster = await _h.RunAsync<ISessionService, IReadOnlyList<RosterItemDto>>(s =>
            s.RecordAttendanceAsync(session.Id, new RecordAttendanceRequest([new AttendanceItem(StudentA, AttendanceStatus.Present, null)])));
        roster.Single().AttendanceStatus.ShouldBe("Present");

        var recorded = _h.Events.OfType<AttendanceRecorded>().Single();
        recorded.ParentUserId.ShouldBe(ParentA);

        var points = await _h.RunAsync<IGamificationService, StudentPointsDto>(g => g.GetAsync(StudentA));
        points.Total.ShouldBe(GamificationRules.Present);

        // Changing Present to Absent removes the award rather than stacking another.
        await _h.RunAsync<ISessionService>(s =>
            s.RecordAttendanceAsync(session.Id, new RecordAttendanceRequest([new AttendanceItem(StudentA, AttendanceStatus.Absent, "sick")])));
        (await _h.RunAsync<IGamificationService, StudentPointsDto>(g => g.GetAsync(StudentA))).Total.ShouldBe(0);

        var completed = await _h.RunAsync<ISessionService, SessionDto>(s => s.CompleteAsync(session.Id));
        completed.Status.ShouldBe("Completed");
        _h.Events.OfType<SessionCompleted>().ShouldHaveSingleItem().TeacherUserId.ShouldBe(TeacherA);
    }

    [Fact]
    public async Task Future_sessions_cannot_be_completed()
    {
        var session = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s =>
            s.CreateAsync(Slot(new DateTime(2026, 12, 1, 16, 0, 0, DateTimeKind.Utc))))).Single();
        await Should.ThrowAsync<BusinessRuleException>(() => _h.RunAsync<ISessionService>(s => s.CompleteAsync(session.Id)));
    }

    [Fact]
    public async Task Feedback_is_visible_to_the_student_and_their_parent_only()   // US-027
    {
        var session = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s =>
            s.CreateAsync(Slot(new DateTime(2026, 9, 14, 16, 0, 0, DateTimeKind.Utc))))).Single();
        await _h.RunAsync<ISessionService>(s => s.SaveFeedbackAsync(session.Id, new SaveFeedbackRequest([new FeedbackItem(StudentA, 5, "Excellent")])));

        _h.User.As(ParentA, Academy, Roles.Parent);
        (await _h.RunAsync<ISessionService, IReadOnlyList<FeedbackDto>>(s => s.StudentFeedbackAsync(StudentA, 10))).Single().Rating.ShouldBe(5);

        _h.User.As(StudentA, Academy, Roles.Student);
        (await _h.RunAsync<ISessionService, IReadOnlyList<FeedbackDto>>(s => s.StudentFeedbackAsync(StudentA, 10))).ShouldHaveSingleItem();

        _h.User.As(ParentB, Academy, Roles.Parent);
        await Should.ThrowAsync<ForbiddenAccessException>(() => _h.RunAsync<ISessionService>(s => s.StudentFeedbackAsync(StudentA, 10)));

        _h.User.As(TeacherB, Academy, Roles.Teacher);
        await Should.ThrowAsync<ForbiddenAccessException>(() => _h.RunAsync<ISessionService>(s => s.StudentFeedbackAsync(StudentA, 10)));
    }

    [Fact]
    public async Task Role_dashboards_show_only_own_data()   // US-028, US-040
    {
        await _h.RunAsync<ISessionService>(s => s.CreateAsync(Slot(_h.Clock.Now.UtcDateTime.AddDays(1))));

        _h.User.As(TeacherA, Academy, Roles.Teacher);
        var teacher = await _h.RunAsync<IOverviewService, MyOverviewDto>(o => o.MineAsync());
        teacher.Teacher!.Students.Select(s => s.UserId).ShouldBe([StudentA]);
        teacher.Teacher.Upcoming.ShouldHaveSingleItem();

        _h.User.As(ParentA, Academy, Roles.Parent);
        var parent = await _h.RunAsync<IOverviewService, MyOverviewDto>(o => o.MineAsync());
        parent.Children!.Select(c => c.UserId).ShouldBe([StudentA]);
        parent.Children![0].Upcoming.ShouldHaveSingleItem();

        _h.User.As(ParentB, Academy, Roles.Parent);
        (await _h.RunAsync<IOverviewService, MyOverviewDto>(o => o.MineAsync())).Children!.Single().Upcoming.ShouldBeEmpty();
    }

    [Fact]
    public async Task New_users_get_role_profiles()   // US-020
    {
        _h.User.As(0, Academy);
        await _h.RunAsync<IProfileSync>(p => p.SyncAsync(Academy, 99, [Roles.Teacher, Roles.Supervisor]));

        _h.User.As(Admin, Academy, Roles.Admin);
        await _h.SeedAsync(db =>
        {
            db.Teachers.Any(t => t.UserId == 99).ShouldBeTrue();
            db.Supervisors.Any(s => s.UserId == 99).ShouldBeTrue();
            db.Students.Any(s => s.UserId == 99).ShouldBeFalse();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Work_schedule_rejects_shift_ending_before_it_starts()   // US-021
    {
        var validator = new SaveWorkScheduleValidator();
        var bad = new SaveWorkScheduleRequest([new WorkDayDto(DayOfWeek.Sunday, true, new TimeOnly(17, 0), new TimeOnly(9, 0))]);
        (await validator.ValidateAsync(bad, TestContext.Current.CancellationToken)).IsValid.ShouldBeFalse();

        var none = new SaveWorkScheduleRequest([new WorkDayDto(DayOfWeek.Sunday, false, null, null)]);
        (await validator.ValidateAsync(none, TestContext.Current.CancellationToken)).IsValid.ShouldBeFalse();

        var good = new SaveWorkScheduleRequest([new WorkDayDto(DayOfWeek.Sunday, true, new TimeOnly(9, 0), new TimeOnly(17, 0))]);
        (await validator.ValidateAsync(good, TestContext.Current.CancellationToken)).IsValid.ShouldBeTrue();
    }

    private sealed class TestMeetings : IMeetingLinkGenerator
    {
        public string Generate(long academyId, string title, DateTime startsAtUtc) => $"https://meet.test/{academyId}";
    }

    private sealed class TestRenderer : ICertificateRenderer
    {
        public byte[] Render(CertificateDocument certificate) => [1, 2, 3];
    }
}
