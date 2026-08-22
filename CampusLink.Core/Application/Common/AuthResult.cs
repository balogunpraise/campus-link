namespace CampusLink.Core.Application.Common;

// Auth failures are expected traffic (wrong password, taken username), not
// exceptional, so the service returns them instead of throwing. ErrorCode maps
// to an HTTP status in the controller; Errors is what the user sees.
public class Result<T>
{
    public bool Succeeded { get; private set; }
    public T Value { get; private set; }
    public AuthErrorCode ErrorCode { get; private set; }
    public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();

    public static Result<T> Success(T value) => new() { Succeeded = true, Value = value };

    public static Result<T> Failure(AuthErrorCode code, params string[] errors) =>
        new() { Succeeded = false, ErrorCode = code, Errors = errors };

    public static Result<T> Failure(AuthErrorCode code, IEnumerable<string> errors) =>
        new() { Succeeded = false, ErrorCode = code, Errors = errors.ToArray() };
}

public enum AuthErrorCode
{
    None = 0,
    ValidationFailed,
    // Deliberately vague: never reveal whether the identifier exists.
    InvalidCredentials,
    AccountLocked,
    EmailNotConfirmed,
    Conflict,
    NotFound,
    InvalidToken
}
