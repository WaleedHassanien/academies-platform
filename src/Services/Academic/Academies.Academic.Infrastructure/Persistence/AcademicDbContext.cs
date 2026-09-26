using Academies.Academic.Application;
using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Domain;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Infrastructure.Persistence;

/// <summary>Maps only the Academic service's tables in the shared <c>academies</c> database.</summary>
public sealed class AcademicDbContext(DbContextOptions<AcademicDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser), IAcademicDbContext
{
    protected override string ServiceName => AcademicServiceInfo.Name;
    protected override bool HasPeopleDirectory => true;

    public DbSet<Person> People => Set<Person>();

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Supervisor> Supervisors => Set<Supervisor>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<SupervisorTeacher> SupervisorTeachers => Set<SupervisorTeacher>();
    public DbSet<TeacherStudent> TeacherStudents => Set<TeacherStudent>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupStudent> GroupStudents => Set<GroupStudent>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<SessionFeedback> Feedbacks => Set<SessionFeedback>();
    public DbSet<SessionExcuse> Excuses => Set<SessionExcuse>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentSubmission> Submissions => Set<AssignmentSubmission>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<PointEntry> Points => Set<PointEntry>();
    public DbSet<StudentBadge> Badges => Set<StudentBadge>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Student>(e =>
        {
            e.ToTable("Students");
            e.Property(x => x.Level).HasMaxLength(50);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.ParentUserId);
        });
        b.Entity<Teacher>(e =>
        {
            e.ToTable("Teachers");
            e.Property(x => x.Specialization).HasMaxLength(150);
            e.Property(x => x.Bio).HasMaxLength(2000);
            e.HasIndex(x => x.UserId).IsUnique();
        });
        b.Entity<Supervisor>(e =>
        {
            e.ToTable("Supervisors");
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.UserId).IsUnique();
        });
        b.Entity<Parent>(e =>
        {
            e.ToTable("Parents");
            e.Property(x => x.Occupation).HasMaxLength(150);
            e.HasIndex(x => x.UserId).IsUnique();
        });
        b.Entity<WorkSchedule>(e =>
        {
            e.ToTable("WorkSchedules");
            e.Property(x => x.Day).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.SupervisorUserId, x.Day });
        });
        b.Entity<SupervisorTeacher>(e =>
        {
            e.ToTable("SupervisorTeachers");
            e.HasIndex(x => new { x.SupervisorUserId, x.TeacherUserId });
        });
        b.Entity<TeacherStudent>(e =>
        {
            e.ToTable("TeacherStudents");
            e.HasIndex(x => new { x.TeacherUserId, x.StudentUserId });
        });
        b.Entity<Group>(e =>
        {
            e.ToTable("Groups");
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasMany(x => x.Students).WithOne().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TeacherUserId);
        });
        b.Entity<GroupStudent>(e =>
        {
            e.ToTable("GroupStudents");
            e.HasIndex(x => new { x.GroupId, x.StudentUserId });
            e.HasIndex(x => x.StudentUserId);
        });
        b.Entity<Course>(e =>
        {
            e.ToTable("Courses");
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Level).HasMaxLength(50);
        });
        b.Entity<Material>(e =>
        {
            e.ToTable("Materials");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Session>(e =>
        {
            e.ToTable("Sessions");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MeetingUrl).HasMaxLength(500);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TeacherUserId, x.StartsAtUtc });
            e.HasIndex(x => new { x.GroupId, x.StartsAtUtc });
            e.HasIndex(x => new { x.StudentUserId, x.StartsAtUtc });
            e.HasIndex(x => new { x.Status, x.StartsAtUtc });
            e.HasIndex(x => x.MakeupOfSessionId);
        });
        b.Entity<SessionExcuse>(e =>
        {
            e.ToTable("SessionExcuses");
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Resolution).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ResolutionNote).HasMaxLength(1000);
            e.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => new { x.StudentUserId, x.Status });
            e.HasIndex(x => x.Status);
        });
        b.Entity<Attendance>(e =>
        {
            e.ToTable("Attendances");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SessionId, x.StudentUserId });
            e.HasIndex(x => x.StudentUserId);
        });
        b.Entity<SessionFeedback>(e =>
        {
            e.ToTable("SessionFeedbacks");
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SessionId, x.StudentUserId });
            e.HasIndex(x => x.StudentUserId);
        });
        b.Entity<Assignment>(e =>
        {
            e.ToTable("Assignments");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.MaxScore).HasPrecision(8, 2);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AssignmentSubmission>(e =>
        {
            e.ToTable("AssignmentSubmissions");
            e.Property(x => x.Content).HasMaxLength(10000);
            e.Property(x => x.AttachmentUrl).HasMaxLength(1000);
            e.Property(x => x.TeacherFeedback).HasMaxLength(2000);
            e.Property(x => x.Score).HasPrecision(8, 2);
            e.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AssignmentId, x.StudentUserId });
        });
        b.Entity<Certificate>(e =>
        {
            e.ToTable("Certificates");
            e.Property(x => x.Number).HasMaxLength(60);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.StudentUserId, x.CourseId });
        });
        b.Entity<PointEntry>(e =>
        {
            e.ToTable("PointEntries");
            e.Property(x => x.Reason).HasMaxLength(250);
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.HasIndex(x => new { x.SourceType, x.SourceId, x.StudentUserId });
            e.HasIndex(x => x.StudentUserId);
        });
        b.Entity<StudentBadge>(e =>
        {
            e.ToTable("StudentBadges");
            e.Property(x => x.BadgeCode).HasMaxLength(30);
            e.HasIndex(x => new { x.StudentUserId, x.BadgeCode });
        });
    }
}

internal sealed class AcademicDesignTimeFactory() : DesignTimeDbContextFactoryBase<AcademicDbContext>(AcademicServiceInfo.Name)
{
    protected override AcademicDbContext Create(DbContextOptions<AcademicDbContext> options, ICurrentUser currentUser) => new(options, currentUser);
}
