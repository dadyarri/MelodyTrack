using Facet;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Common.Api.Users.Responses;

[Facet(typeof(User), Include = [nameof(User.Id), nameof(User.LastName), nameof(User.FirstName)])]
public partial class GetUsersDto;