using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace rptr.Services.Authorization;

public class AuthService : IAuthService
{
    private readonly DataContext _dbContext;

    public AuthService(DataContext _context)
    {
        _dbContext = _context;
    }
}
