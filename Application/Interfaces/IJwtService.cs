using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Application.Interfaces
{
    public interface IJwtService
    {
        IEnumerable<Claim> ParseClaimsFromJwt(string jwt);
    }
}
