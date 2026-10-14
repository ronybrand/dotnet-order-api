using FluentValidation;
using OrderApi.Application.DTOs;
using OrderApi.Application.Services;
using OrderApi.Domain.Common;

namespace OrderApi.Application.Validators;

public sealed class ItemRequestValidator : AbstractValidator<ItemRequest>
{
    public const int MaxDescriptionLength = FieldLimits.ItemDescription;

    public ItemRequestValidator()
    {
        RuleFor(i => i.Description).NotEmpty().MaximumLength(MaxDescriptionLength);
        RuleFor(i => i.UnitPrice)
            .GreaterThan(0)
            .LessThanOrEqualTo(FieldLimits.MaxUnitPrice)
            .Must(price => decimal.Round(price, 2) == price)
            .WithMessage("'{PropertyName}' must have at most 2 decimal places.");
        RuleFor(i => i.Quantity).InclusiveBetween(1, FieldLimits.MaxQuantity);
    }
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(r => r.CustomerId).NotEmpty();
        RuleFor(r => r.Items)
            .Must(items => items is null || items.Count <= OrderService.MaxItemsPerOrder)
            .WithMessage($"An order can have at most {OrderService.MaxItemsPerOrder} items.");
        RuleFor(r => r.Items)
            .Must(items => items is null || items.All(item => item is not null))
            .WithMessage("'{PropertyName}' must not contain null elements.");
        RuleForEach(r => r.Items).SetValidator(new ItemRequestValidator())
            .When(r => r.Items is null || r.Items.All(item => item is not null));
    }
}

public sealed class UpdateItemQuantityRequestValidator : AbstractValidator<UpdateItemQuantityRequest>
{
    public UpdateItemQuantityRequestValidator() => RuleFor(r => r.Quantity).InclusiveBetween(1, FieldLimits.MaxQuantity);
}
