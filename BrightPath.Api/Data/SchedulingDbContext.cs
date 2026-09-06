using BrightPath.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Api.Data;

public class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public DbSet<Tutor> Tutors => Set<Tutor>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonParticipant> LessonParticipants => Set<LessonParticipant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tutor>(entity =>
        {
            entity.HasKey(tutor => tutor.Id);
            entity.Property(tutor => tutor.Id).HasMaxLength(32);
            entity.Property(tutor => tutor.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(student => student.Id);
            entity.Property(student => student.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.HasKey(lesson => lesson.Id);
            entity.Property(lesson => lesson.Room).HasMaxLength(64).IsRequired();
            entity.Property(lesson => lesson.DurationMinutes).IsRequired();
            entity.Property(lesson => lesson.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(lesson => lesson.Tutor).WithMany(tutor => tutor.Lessons)
                .HasForeignKey(lesson => lesson.TutorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LessonParticipant>(entity =>
        {
            entity.HasKey(participant => new { participant.LessonId, participant.StudentId });
            entity.HasOne(participant => participant.Lesson).WithMany(lesson => lesson.Participants)
                .HasForeignKey(participant => participant.LessonId);
            entity.HasOne(participant => participant.Student).WithMany(student => student.LessonParticipants)
                .HasForeignKey(participant => participant.StudentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
