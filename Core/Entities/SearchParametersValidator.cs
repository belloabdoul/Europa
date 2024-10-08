﻿using FluentValidation;

namespace Core.Entities;

public class SearchParametersValidator : AbstractValidator<SearchParameters>
{
    public SearchParametersValidator()
    {
        RuleFor(searchParameters => searchParameters.Folders).NotEmpty().WithMessage("At least one folder is required")
            .OverridePropertyName(LowerCaseFirstLetter(nameof(SearchParameters.Folders)));

        RuleFor(searchParameters => searchParameters.FileSearchType).NotNull()
            .WithMessage("The search type is required")
            .OverridePropertyName(LowerCaseFirstLetter(nameof(SearchParameters.FileSearchType)));

        RuleFor(searchParameters => searchParameters.DegreeOfSimilarity).NotEmpty()
            .WithMessage("The degree of similarity is required").When(searchParameters =>
                searchParameters.FileSearchType == FileSearchType.Images)
            .OverridePropertyName(LowerCaseFirstLetter(nameof(SearchParameters.DegreeOfSimilarity)));

        RuleFor(searchParameters => searchParameters.MinSize)
            .LessThanOrEqualTo(searchParameters => searchParameters.MaxSize)
            .WithMessage("Min size must be lower than max size").When(searchParameters =>
                searchParameters.MinSize.HasValue && searchParameters.MaxSize.HasValue)
            .OverridePropertyName(LowerCaseFirstLetter(nameof(SearchParameters.MinSize)));

        RuleFor(searchParameters => searchParameters.MaxSize)
            .GreaterThanOrEqualTo(searchParameters => searchParameters.MinSize)
            .WithMessage("Min size must be lower than max size").When(searchParameters =>
                searchParameters.MinSize.HasValue && searchParameters.MaxSize.HasValue)
            .OverridePropertyName(LowerCaseFirstLetter(nameof(SearchParameters.MaxSize)));
    }

    private static string LowerCaseFirstLetter(ReadOnlySpan<char> propertyName)
    {
        return string.Concat(new Span<char>([char.ToLower(propertyName[0])]), propertyName[1..]);
    }
}