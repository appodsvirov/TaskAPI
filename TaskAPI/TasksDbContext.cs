using Microsoft.EntityFrameworkCore;

namespace TaskAPI;

public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskItemEntity> Tasks => Set<TaskItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItemEntity>(entity =>
        {
            entity.ToTable("Tasks");
            entity.HasKey(task => task.Id);
            entity.Property(task => task.Title)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(task => task.Priority)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(task => task.CreatedAt)
                .IsRequired();
            entity.Property(task => task.RowVersion)
                .IsRowVersion();
        });
    }
}
