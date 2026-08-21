using Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Data
{
    public class BilContext : IdentityDbContext<ApplicationUser>
    {
        public BilContext(DbContextOptions<BilContext> options) : base(options)
        {

        }

        public DbSet<Board> Boards { get; set; }

        public DbSet<Component> Components { get; set; }

        public DbSet<Domain.Entities.Attribute> Attributes { get; set; }
    }
}
