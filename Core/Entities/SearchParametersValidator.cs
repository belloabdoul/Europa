using FluentValidation;

namespace Core.Entities;

public class SearchParametersValidator : AbstractValidator<SearchParameters>
{
    public SearchParametersValidator()
    {
        RuleFor(searchParameters => searchParameters.Folders).NotEmpty().WithMessage("At least one folder is required");
        RuleFor(searchParameters => searchParameters.FileSearchType).NotNull()
            .WithMessage("The search type is required");
        RuleFor(searchParameters => searchParameters.DegreeOfSimilarity).NotEmpty()
            .WithMessage("The degree of similarity is required").When(searchParameters =>
                searchParameters.FileSearchType == FileSearchType.Images);
        RuleFor(searchParameters => searchParameters.MinSize)
            .LessThanOrEqualTo(searchParameters => searchParameters.MaxSize).WithMessage("Min size must be lower than max size").When(searchParameters =>
                searchParameters.MinSize.HasValue && searchParameters.MaxSize.HasValue);
        RuleFor(searchParameters => searchParameters.MaxSize)
            .GreaterThanOrEqualTo(searchParameters => searchParameters.MinSize).WithMessage("Min size must be lower than max size").When(searchParameters =>
                searchParameters.MinSize.HasValue && searchParameters.MaxSize.HasValue);
    }
}