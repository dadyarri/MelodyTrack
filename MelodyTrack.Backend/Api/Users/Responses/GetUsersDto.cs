using Facet;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Users.Responses;

[Facet(typeof(User), Include = [nameof(User.Id), nameof(User.LastName), nameof(User.FirstName)])]
public partial class GetUsersDto;