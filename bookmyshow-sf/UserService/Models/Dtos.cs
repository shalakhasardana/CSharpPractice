using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserService.Models
{
    public record RegisterDto(string Email, string Password, string? Phone);
    public record LoginDto(string Email, string Password);
    public record AssignRoleDto(string RoleCode);
    public record CreateRoleDto(string Code, string Name);

    public record UserSummary(long Id, string Email, string? Phone, string Status);
}
