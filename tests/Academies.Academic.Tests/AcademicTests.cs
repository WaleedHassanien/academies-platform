using Academies.Academic.Application;
using Academies.Academic.Domain;
using Academies.Academic.Infrastructure;
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
            s.AddSingleton<IMeetingLinkGenerator>(new JitsiMeetingLinkGenerator(new MeetingOptions()));
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

    [Fact]
    public async Task Student_page_shows_their_sessions_teachers_and_time_zone()
    {
        // Teacher B also teaches Student A one-to-one (no group).
        await _h.SeedAsync(async db =>
        {
            db.TeacherStudents.Add(new TeacherStudent { AcademyId = Academy, TeacherUserId = TeacherB, StudentUserId = StudentA });
            await db.SaveChangesAsync();
        });
        var groupSession = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s =>
            s.CreateAsync(Slot(new DateTime(2026, 9, 14, 16, 0, 0, DateTimeKind.Utc))))).Single();
        await _h.RunAsync<ISessionService>(s => s.CreateAsync(Slot(new DateTime(2026, 9, 15, 16, 0, 0, DateTimeKind.Utc), teacher: TeacherB) with { GroupId = null }));
        await _h.RunAsync<ISessionService>(s =>
            s.RecordAttendanceAsync(groupSession.Id, new RecordAttendanceRequest([new AttendanceItem(StudentA, AttendanceStatus.Late, null)])));

        var sessions = await _h.RunAsync<ISessionService, IReadOnlyList<StudentSessionDto>>(s => s.StudentSessionsAsync(StudentA));
        sessions.Select(x => x.Session.TeacherUserId).ShouldBe([TeacherB, TeacherA]);   // newest first
        sessions.Single(x => x.Session.Id == groupSession.Id).AttendanceStatus.ShouldBe("Late");
        (await _h.RunAsync<ISessionService, IReadOnlyList<StudentSessionDto>>(s => s.StudentSessionsAsync(StudentB))).ShouldBeEmpty();

        var student = await _h.RunAsync<IProfileService, StudentDto>(p =>
            p.UpdateStudentAsync(StudentA, new UpdateStudentRequest(null, new DateOnly(2026, 1, 1), StudentStatus.Active, "Asia/Riyadh")));
        student.TimeZone.ShouldBe("Asia/Riyadh");
        student.Teachers.Select(t => t.UserId).ShouldBe([TeacherA, TeacherB]);

        // Other students' parents can't open the page.
        _h.User.As(ParentB, Academy, Roles.Parent);
        await Should.ThrowAsync<ForbiddenAccessException>(() => _h.RunAsync<ISessionService>(s => s.StudentSessionsAsync(StudentA)));
    }

    private SaveSessionRequest Solo(DateTime start, long student = StudentB, int minutes = 45) =>
        new("Hifz", _courseId, null, TeacherA, start, minutes, SessionType.Online, null, null, false, null, 1, student);

    [Fact]
    public async Task One_to_one_sessions_link_the_teacher_and_have_a_single_student_roster()
    {
        var session = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s => s.CreateAsync(Solo(new DateTime(2026, 9, 14, 16, 0, 0, DateTimeKind.Utc))))).Single();
        session.StudentUserId.ShouldBe(StudentB);
        session.DurationMinutes.ShouldBe(45);

        (await _h.RunAsync<ISessionService, IReadOnlyList<RosterItemDto>>(s => s.RosterAsync(session.Id))).Select(r => r.StudentUserId).ShouldBe([StudentB]);
        (await _h.RunAsync<IProfileService, StudentDto>(p => p.GetStudentAsync(StudentB))).Teachers.Select(t => t.UserId).ShouldBe([TeacherA]);

        // The student can't be booked twice at the same time, even with another teacher.
        await Should.ThrowAsync<ConflictException>(() => _h.RunAsync<ISessionService>(s =>
            s.CreateAsync(Solo(new DateTime(2026, 9, 14, 16, 30, 0, DateTimeKind.Utc)) with { TeacherUserId = TeacherB })));
    }

    [Fact]
    public async Task Excused_session_is_rescheduled_and_the_ledger_counts_only_what_was_held()
    {
        var future = _h.Clock.Now.UtcDateTime.AddDays(2);
        var session = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s => s.CreateAsync(Solo(future)))).Single();

        // Another student can't excuse it; the student can.
        _h.User.As(StudentA, Academy, Roles.Student);
        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _h.RunAsync<ISessionOutcomeService>(o => o.RequestExcuseAsync(session.Id, new RequestExcuseRequest("sick", null))));
        _h.User.As(StudentB, Academy, Roles.Student);
        var excused = await _h.RunAsync<ISessionOutcomeService, SessionDto>(o => o.RequestExcuseAsync(session.Id, new RequestExcuseRequest("travel", null)));
        excused.Status.ShouldBe("Excused");
        excused.Excuse!.Status.ShouldBe("Pending");

        // A student can't settle it; the admin reschedules it.
        await Should.ThrowAsync<ForbiddenAccessException>(() => _h.RunAsync<ISessionOutcomeService>(o =>
            o.ResolveExcuseAsync(excused.Excuse.Id, new ResolveExcuseRequest(ExcuseResolution.NotCounted, false, null, null))));
        _h.User.As(Admin, Academy, Roles.Admin);
        var resolved = await _h.RunAsync<ISessionOutcomeService, SessionDto>(o =>
            o.ResolveExcuseAsync(excused.Excuse.Id, new ResolveExcuseRequest(ExcuseResolution.Rescheduled, false, future.AddDays(1), null)));
        resolved.Excuse!.Resolution.ShouldBe("Rescheduled");

        var ledger = await _h.RunAsync<ISessionOutcomeService, IReadOnlyList<LedgerSessionDto>>(o =>
            o.LedgerAsync(future.AddDays(-1), future.AddDays(3), null, StudentB));
        ledger.Count.ShouldBe(2);
        ledger[0].Outcome.ShouldBe(SessionOutcomes.Excused);
        ledger[0].Counts.ShouldBeFalse();
        ledger[1].MakeupOfSessionId.ShouldBe(session.Id);
        ledger[1].DurationMinutes.ShouldBe(45);
    }

    [Fact]
    public async Task Supervisor_decides_whether_an_unexcused_absence_counts()
    {
        var past = new DateTime(2026, 9, 10, 16, 0, 0, DateTimeKind.Utc);
        var session = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s => s.CreateAsync(Solo(past)))).Single();
        await _h.RunAsync<ISessionService>(s => s.RecordAttendanceAsync(session.Id, new RecordAttendanceRequest([new AttendanceItem(StudentB, AttendanceStatus.Absent, null)])));
        await _h.RunAsync<ISessionService>(s => s.CompleteAsync(session.Id));

        async Task<LedgerSessionDto> Row() => (await _h.RunAsync<ISessionOutcomeService, IReadOnlyList<LedgerSessionDto>>(o =>
            o.LedgerAsync(past.AddDays(-1), past.AddDays(1), TeacherA, null))).Single();
        (await Row()).Outcome.ShouldBe(SessionOutcomes.AbsentPending);
        (await _h.RunAsync<ISessionOutcomeService, IReadOnlyList<SessionDto>>(o => o.PendingAbsencesAsync())).ShouldHaveSingleItem();

        // The teacher can't decide; the supervisor of that teacher can.
        _h.User.As(TeacherA, Academy, Roles.Teacher);
        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _h.RunAsync<ISessionOutcomeService>(o => o.DecideAbsenceAsync(session.Id, new AbsenceDecisionRequest(true))));

        const long Supervisor = 40;
        await _h.SeedAsync(async db =>
        {
            db.People.Add(PeopleSeed.Person(Academy, Supervisor, "Supervisor", Roles.Supervisor));
            db.SupervisorTeachers.Add(new SupervisorTeacher { AcademyId = Academy, SupervisorUserId = Supervisor, TeacherUserId = TeacherA });
            await db.SaveChangesAsync();
        });
        _h.User.As(Supervisor, Academy, Roles.Supervisor);
        await _h.RunAsync<ISessionOutcomeService>(o => o.DecideAbsenceAsync(session.Id, new AbsenceDecisionRequest(true)));

        var row = await Row();
        row.Outcome.ShouldBe(SessionOutcomes.AbsentCounted);
        row.Counts.ShouldBeTrue();

        var report = await _h.RunAsync<ISessionOutcomeService, SessionReportDto>(o => o.ReportAsync(past.AddDays(-1), past.AddDays(1)));
        report.Totals.AbsentCounted.ShouldBe(1);
        report.ByTeacher.Single().UserId.ShouldBe(TeacherA);
    }

    [Fact]
    public async Task Each_teacher_has_one_private_room_even_at_the_same_hour()
    {
        var start = _h.Clock.Now.UtcDateTime.AddMinutes(20);
        var a = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s => s.CreateAsync(Solo(start, StudentB) with { GenerateMeetingLink = true }))).Single();
        var b = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s =>
            s.CreateAsync(Solo(start, StudentA) with { TeacherUserId = TeacherB, GenerateMeetingLink = true }))).Single();
        var later = (await _h.RunAsync<ISessionService, IReadOnlyList<SessionDto>>(s =>
            s.CreateAsync(Solo(start.AddDays(1), StudentB) with { GenerateMeetingLink = true }))).Single();

        a.MeetingUrl.ShouldNotBe(b.MeetingUrl);   // two teachers at once: two rooms
        later.MeetingUrl.ShouldBe(a.MeetingUrl);  // same teacher: always the same room

        // The teacher opens it as moderator, with their name pre-filled.
        _h.User.As(TeacherA, Academy, Roles.Teacher);
        var teacher = await _h.RunAsync<ISessionService, JoinLinkDto>(s => s.JoinAsync(a.Id));
        teacher.IsModerator.ShouldBeTrue();
        teacher.Url.ShouldStartWith(a.MeetingUrl!);
        teacher.Url.ShouldContain("Teacher%20A");
        (await _h.RunAsync<ISessionService, JoinLinkDto>(s => s.MyRoomAsync())).Url.ShouldStartWith(a.MeetingUrl!);

        // Its student joins from 30 minutes before; tomorrow's session isn't open yet; others can't join.
        _h.User.As(StudentB, Academy, Roles.Student);
        (await _h.RunAsync<ISessionService, JoinLinkDto>(s => s.JoinAsync(a.Id))).IsModerator.ShouldBeFalse();
        await Should.ThrowAsync<BusinessRuleException>(() => _h.RunAsync<ISessionService>(s => s.JoinAsync(later.Id)));
        _h.User.As(StudentA, Academy, Roles.Student);
        await Should.ThrowAsync<ForbiddenAccessException>(() => _h.RunAsync<ISessionService>(s => s.JoinAsync(a.Id)));

        // A supervisor shares the room of a teacher they follow, and only theirs.
        const long Supervisor = 40;
        await _h.SeedAsync(async db =>
        {
            db.People.Add(PeopleSeed.Person(Academy, Supervisor, "Supervisor", Roles.Supervisor));
            db.SupervisorTeachers.Add(new SupervisorTeacher { AcademyId = Academy, SupervisorUserId = Supervisor, TeacherUserId = TeacherA });
            await db.SaveChangesAsync();
        });
        _h.User.As(Supervisor, Academy, Roles.Supervisor);
        var shared = await _h.RunAsync<ISessionService, JoinLinkDto>(s => s.TeacherRoomLinkAsync(TeacherA, 30));
        shared.Url.ShouldStartWith(a.MeetingUrl!);
        shared.ExpiresAtUtc.ShouldBe(_h.Clock.Now.UtcDateTime.AddDays(30));
        await Should.ThrowAsync<ForbiddenAccessException>(() => _h.RunAsync<ISessionService>(s => s.TeacherRoomLinkAsync(TeacherB, 30)));
    }

    [Fact]
    public void JaaS_links_carry_a_signed_token_with_the_teachers_email()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var generator = new JitsiMeetingLinkGenerator(new MeetingOptions
        {
            Provider = "JaaS",
            JaaS = new MeetingOptions.JaasOptions { AppId = "vpaas-magic-cookie-test", KeyId = "vpaas-magic-cookie-test/k1", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() },
        });

        var room = generator.RoomUrl(Academy, TeacherA);
        room.ShouldStartWith("https://8x8.vc/vpaas-magic-cookie-test/academy3-t10-");
        var url = generator.JoinUrl(Academy, TeacherA, new MeetingParticipant(TeacherA, "Teacher A", "a@academy.test", true), DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

        var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(url.Split("?jwt=")[1].Split('#')[0]);
        jwt.Kid.ShouldBe("vpaas-magic-cookie-test/k1");
        jwt.GetClaim("sub").Value.ShouldBe("vpaas-magic-cookie-test");
        jwt.GetClaim("room").Value.ShouldBe(room.Split('/').Last());
        var user = System.Text.Json.JsonDocument.Parse(jwt.GetClaim("context").Value).RootElement.GetProperty("user");
        user.GetProperty("email").GetString().ShouldBe("a@academy.test");
        user.GetProperty("moderator").GetString().ShouldBe("true");
    }

    [Fact]
    public async Task Student_time_zone_must_be_an_iana_zone()
    {
        var validator = new UpdateStudentValidator();
        UpdateStudentRequest With(string? tz) => new(null, new DateOnly(2026, 1, 1), StudentStatus.Active, tz);
        var ct = TestContext.Current.CancellationToken;

        (await validator.ValidateAsync(With("Africa/Cairo"), ct)).IsValid.ShouldBeTrue();
        (await validator.ValidateAsync(With(null), ct)).IsValid.ShouldBeTrue();
        (await validator.ValidateAsync(With("Mars/Olympus"), ct)).IsValid.ShouldBeFalse();
        (await validator.ValidateAsync(With("Egypt Standard Time"), ct)).IsValid.ShouldBeFalse();
    }

    private sealed class TestRenderer : ICertificateRenderer
    {
        public byte[] Render(CertificateDocument certificate) => [1, 2, 3];
    }
}
