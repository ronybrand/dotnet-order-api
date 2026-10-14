using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentValidation;
using OrderApi.Application.DTOs;
using OrderApi.Domain.Common;

namespace OrderApi.Application.Validators;

internal static partial class CustomerRules
{
    // \z, not $: in .NET "$" also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9./-]{5,20}\z")]
    private static partial Regex TaxIdPattern();

    [GeneratedRegex(@"^[A-Z0-9]{6,9}\z")]
    private static partial Regex PassportPattern();

    private const int MinTaxIdSignificantChars = 5;

    public static void Apply<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string>> name,
        Expression<Func<T, string>> taxId,
        Expression<Func<T, string?>> passportNumber,
        Expression<Func<T, string>> email)
    {
        var getPassportNumber = passportNumber.Compile();
        validator.RuleFor(name).NotEmpty().MaximumLength(FieldLimits.CustomerName);
        validator.RuleFor(taxId).NotEmpty().Matches(TaxIdPattern())
            .Must(id => id is not null && id.Count(char.IsAsciiLetterOrDigit) >= MinTaxIdSignificantChars)
            .WithMessage("'{PropertyName}' must contain at least " + MinTaxIdSignificantChars + " letters or digits.");
        validator.RuleFor(passportNumber).Matches(PassportPattern()).When(request => !string.IsNullOrEmpty(getPassportNumber(request)));
        validator.RuleFor(email).NotEmpty().MaximumLength(FieldLimits.Email).EmailAddress();
    }
}

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator() =>
        CustomerRules.Apply(this, r => r.Name, r => r.TaxId, r => r.PassportNumber, r => r.Email);
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator() =>
        CustomerRules.Apply(this, r => r.Name, r => r.TaxId, r => r.PassportNumber, r => r.Email);
}
