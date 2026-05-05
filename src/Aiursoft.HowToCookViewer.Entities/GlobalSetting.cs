using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
public class GlobalSetting
{
    [Key]
    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public required string Key { get; set; }

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? Value { get; set; }
}
