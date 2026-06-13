using FluentValidation;
using MediatR;
using ValidationException = Zentory.Application.Exceptions.ValidationException;
using ValidationError     = Zentory.Application.Exceptions.ValidationError;

namespace Zentory.Application.Common.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 cancellationToken)
    {
        if (!_validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
        {
            var errors = failures.Select(f => new ValidationError(f.PropertyName, f.ErrorMessage));
            throw new ValidationException(errors);
        }

        return await next();
    }
}
