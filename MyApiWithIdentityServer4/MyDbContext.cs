using Ars.Common.Core.AspNetCore;
using Ars.Common.EFCore;
using Ars.Common.EFCore.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyApiWithIdentityServer4.Model;

namespace MyApiWithIdentityServer4
{
    public class MyDbContext : ArsDbContext
    {
        public MyDbContext(
            DbContextOptions<MyDbContext> dbContextOptions, 
            IArsSession? arsSession = null, 
            IOptions<DbContextOption>? options = null) 
            : base(dbContextOptions,arsSession,options?.Value)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Student>().HasMany(r => r.Enrollments).WithOne(r => r.Student);
            modelBuilder.Entity<Course>().HasMany(r => r.Enrollments).WithOne(r => r.Course);
        }

        public DbSet<Student> Students { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<Course> Courses { get; set; }
    }
}
