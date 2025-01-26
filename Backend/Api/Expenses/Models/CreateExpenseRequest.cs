using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Backend.Api.Expenses.Models;

[UsedImplicitly]
public class CreateExpenseRequest
{
    [MaxLength(200)] public required string Description { get; set; }

    [Range(0, (double)decimal.MaxValue)] public required decimal Amount { get; set; }
}