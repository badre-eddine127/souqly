using FluentValidation;
using Souqly.Catalog.Domain.Products;

namespace Souqly.Catalog.Application.Products;

public sealed class ProductRequestValidator : AbstractValidator<ProductRequest>
{
    public ProductRequestValidator()
    {
        RuleFor(r => r.CategoryId).GreaterThan(0);
        RuleFor(r => r.Sku).NotEmpty().MaximumLength(Product.SkuMaxLength).Matches("^[A-Za-z0-9-]+$")
            .WithMessage("Le SKU ne contient que des lettres, chiffres et tirets.");
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(r => r.Price).NotNull().SetValidator(new MoneyDtoValidator());
    }
}

public sealed class MoneyDtoValidator : AbstractValidator<MoneyDto>
{
    public MoneyDtoValidator()
    {
        RuleFor(m => m.Currency).Equal(MoneyDto.Mad).WithMessage("Seule la devise MAD est acceptée.");
        RuleFor(m => m.Amount).NotEmpty().Matches(@"^\d{1,8}(\.\d{1,2})?$")
            .WithMessage("Le montant est une chaîne décimale avec au plus 2 décimales, par exemple \"1200.00\".");
    }
}
