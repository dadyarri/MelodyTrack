using System.ComponentModel.DataAnnotations;
using FastEndpoints;

namespace Backend.Api.Services.Models;

public class UpdateServicePriceRequest
{
    [Range(0, (double)decimal.MaxValue)]
    public required decimal Price { get; set; }
} 