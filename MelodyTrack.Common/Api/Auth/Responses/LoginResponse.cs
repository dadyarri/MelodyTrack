using MelodyTrack.Common.Data.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Common.Api.Auth.Responses;

public class LoginResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required UserRoles Role { get; set; }
}