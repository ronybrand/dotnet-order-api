using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentValidation;
using OrderApi.Application.DTOs;

namespace OrderApi.Application.Validators;

internal static partial class CustomerRules
{
    [GeneratedRegex("^[A-Za-z0-9./-]{5,20}$")]
    private static partial Regex TaxIdPattern();

    [GeneratedRegex("^[A-Z0-9]{6,9}$")]
    private static partial Regex PassportPattern();

    public static void Apply<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string>> name,
        Expression<Func<T, string>> taxId,
        Expression<Func<T, string?>> passportNumber,
        Expression<Func<T, string>> email)
    {
        var getPassportNumber = passportNumber.Compile();
        validator.RuleFor(name).NotEmpty();
        validator.RuleFor(taxId).NotEmpty().Matches(TaxIdPattern());
        validator.RuleFor(passportNumber).Matches(PassportPattern()).When(request => !string.IsNullOrEmpty(getPassportNumber(request)));
        validator.RuleFor(email).NotEmpty().EmailAddress();
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
