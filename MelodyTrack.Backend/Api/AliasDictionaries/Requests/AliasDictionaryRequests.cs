using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.AliasDictionaries.Requests;

public class GetAliasDictionaryRequest
{
    [FromRoute]
    public string Dictionary { get; set; } = string.Empty;

    [FromQuery]
    public string? Search { get; set; }
}

public class CreateAliasDictionaryEntryRequest : IValidatableRequest
{
    [Required(ErrorMessage = "Укажите значение.")]
    [StringLength(60, ErrorMessage = "Значение не должно быть длиннее 60 символов.")]
    public string Value { get; set; } = string.Empty;
}

public class UpdateAliasDictionaryEntryRequest : CreateAliasDictionaryEntryRequest
{
    public bool IsEnabled { get; set; }
}

public class AliasDictionaryEntryRouteRequest
{
    [FromRoute]
    public string Dictionary { get; set; } = string.Empty;

    [FromRoute]
    public Ulid Id { get; set; }
}
