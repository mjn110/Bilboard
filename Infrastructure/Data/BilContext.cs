using Infrastructure.Model;
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
    }
}
