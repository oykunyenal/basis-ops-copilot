namespace BasisOps.Application.Abstractions;

/// <summary>Collects field errors at the start of a handler.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public ValidationErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            _errors[field] = messages = [];
        }

        messages.Add(message);
        return this;
    }

    public ValidationErrors Required(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, $"{field} is required.");
        }
        else if (value.Length > maxLength)
        {
            Add(field, $"{field} must be at most {maxLength} characters.");
        }

        return this;
    }

    public Error ToError() => Error.Validation(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
}
